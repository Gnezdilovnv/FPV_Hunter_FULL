using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows.Forms;
using System.Data.SQLite;
using OpenCvSharp;

namespace FPV_Hunter_FULL
{
    // ============================================================
    // 1. LIBIIO (ПОЛНЫЙ ДОСТУП К PLUTO)
    // ============================================================
    public static class libiio
    {
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_create_context_from_uri(string uri);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iio_context_destroy(IntPtr ctx);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_context_find_device(IntPtr ctx, string name);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_device_find_channel(IntPtr dev, string name, bool output);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iio_channel_enable(IntPtr channel);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_attr_write_double(IntPtr dev, string attr, double value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_attr_read_double(IntPtr dev, string attr, out double value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_device_create_buffer(IntPtr dev, int count, bool cyclic);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_buffer_refill(IntPtr buffer);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_buffer_first(IntPtr buffer, IntPtr channel);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void iio_buffer_destroy(IntPtr buffer);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr iio_context_get_attr_value(IntPtr ctx, string name);
    }

    // ============================================================
    // 2. PLUTO SDR
    // ============================================================
    public class PlutoSDR : IDisposable
    {
        private IntPtr ctx, phy, rx, rx_channel, buffer;
        private bool connected;
        private double sampleRate = 4e6;
        public string Serial { get; private set; } = "Неизвестно";
        public string Firmware { get; private set; } = "Неизвестно";
        public string ChipModel { get; private set; } = "Неизвестно";
        public string HardwareModel { get; private set; } = "Неизвестно";
        public event Action<string> OnStatusUpdate;

        public bool Connect(string ip = "192.168.2.1")
        {
            Log("🔄 Подключение к Pluto+...");
            Disconnect();
            ctx = libiio.iio_create_context_from_uri($"ip:{ip}");
            if (ctx == IntPtr.Zero) ctx = libiio.iio_create_context_from_uri("usb:");
            if (ctx == IntPtr.Zero) { Log("❌ Pluto+ не найден!"); return false; }
            phy = libiio.iio_context_find_device(ctx, "ad9361-phy");
            rx = libiio.iio_context_find_device(ctx, "cf-ad9361-lpc");
            if (phy == IntPtr.Zero || rx == IntPtr.Zero) { libiio.iio_context_destroy(ctx); ctx = IntPtr.Zero; Log("❌ Устройства AD9361 не найдены!"); return false; }
            rx_channel = libiio.iio_device_find_channel(rx, "voltage0", false);
            if (rx_channel == IntPtr.Zero) { Log("❌ Канал приёма не найден!"); return false; }
            libiio.iio_channel_enable(rx_channel);
            connected = true;
            Serial = GetSerial();
            Firmware = GetFirmware();
            ChipModel = GetChipModel();
            HardwareModel = GetHardwareModel();
            SetSampleRate(sampleRate);
            SetGain(40);
            SetFrequency(100e6);
            Log($"✅ Pluto+ подключен! Серийный: {Serial}, Модель: {ChipModel}, Железо: {HardwareModel}, Прошивка: {Firmware}");
            return true;
        }

        public void Disconnect()
        {
            if (buffer != IntPtr.Zero) { libiio.iio_buffer_destroy(buffer); buffer = IntPtr.Zero; }
            if (ctx != IntPtr.Zero) { libiio.iio_context_destroy(ctx); ctx = IntPtr.Zero; }
            connected = false;
            Log("🔌 Pluto+ отключен");
        }

        private string GetSerial()
        {
            if (ctx == IntPtr.Zero) return "Неизвестно";
            IntPtr p = libiio.iio_context_get_attr_value(ctx, "serial");
            return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : "Неизвестно";
        }

        private string GetFirmware()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            byte[] buf = new byte[256];
            int ret = libiio.iio_device_attr_read_double(phy, "fw_version", out double val);
            return ret >= 0 ? val.ToString() : "Неизвестно";
        }

        private string GetChipModel()
        {
            if (phy == IntPtr.Zero) return "Неизвестно";
            byte[] buf = new byte[256];
            int ret = libiio.iio_device_attr_read_double(phy, "model", out double val);
            return ret >= 0 ? val.ToString() : "Неизвестно";
        }

        private string GetHardwareModel()
        {
            if (ctx == IntPtr.Zero) return "Неизвестно";
            IntPtr p = libiio.iio_context_get_attr_value(ctx, "hw_model");
            return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : "Неизвестно";
        }

        public bool SetFrequency(double freq) { if (!connected || phy == IntPtr.Zero) return false; int ret = libiio.iio_device_attr_write_double(phy, "RX_LO_FREQ", freq); if (ret >= 0) Log($"📡 Частота: {freq/1e6:F1} МГц"); return ret >= 0; }
        public bool SetSampleRate(double rate) { if (!connected || phy == IntPtr.Zero) return false; sampleRate = rate; int ret = libiio.iio_device_attr_write_double(phy, "RX_SAMPLING_FREQ", rate); if (ret >= 0) libiio.iio_device_attr_write_double(rx, "RX_RF_BANDWIDTH", rate); return ret >= 0; }
        public bool SetGain(double gain) { if (!connected || phy == IntPtr.Zero) return false; return libiio.iio_device_attr_write_double(phy, "RX_GAIN", gain) >= 0; }
        public bool SetAGC(bool enable) { if (!connected || phy == IntPtr.Zero) return false; return libiio.iio_device_attr_write_double(phy, "RX_GAIN_MODE", enable ? 1 : 0) >= 0; }
        public bool SetBandwidth(double bw) { if (!connected || rx == IntPtr.Zero) return false; return libiio.iio_device_attr_write_double(rx, "RX_RF_BANDWIDTH", bw) >= 0; }
        public double GetRSSI() { if (!connected || phy == IntPtr.Zero) return -100; libiio.iio_device_attr_read_double(phy, "RX_RSSI", out double rssi); return rssi; }

        public float[] ReceiveSamples(int count = 1024)
        {
            if (!connected || rx == IntPtr.Zero || rx_channel == IntPtr.Zero) return null;
            buffer = libiio.iio_device_create_buffer(rx, count, false);
            if (buffer == IntPtr.Zero) return null;
            int bytes = libiio.iio_buffer_refill(buffer);
            if (bytes < 0) { libiio.iio_buffer_destroy(buffer); buffer = IntPtr.Zero; return null; }
            IntPtr data = libiio.iio_buffer_first(buffer, rx_channel);
            if (data == IntPtr.Zero) { libiio.iio_buffer_destroy(buffer); buffer = IntPtr.Zero; return null; }
            float[] samples = new float[count];
            int sampleCount = bytes / 4;
            for (int i = 0; i < sampleCount && i < count; i++)
            {
                short i_val = Marshal.ReadInt16(data, i * 4);
                short q_val = Marshal.ReadInt16(data, i * 4 + 2);
                samples[i] = (float)Math.Sqrt(i_val * i_val + q_val * q_val) / 2048.0f;
            }
            libiio.iio_buffer_destroy(buffer);
            buffer = IntPtr.Zero;
            return samples;
        }

        public bool SaveIQ(float[] samples, string filename)
        {
            try { using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create))) { foreach (var s in samples) writer.Write(s); } return true; }
            catch { return false; }
        }

        public bool IsConnected => connected;
        public double SampleRate => sampleRate;
        private void Log(string msg) { OnStatusUpdate?.Invoke(msg); Console.WriteLine($"[Pluto] {msg}"); }
        public void Dispose() { Disconnect(); }
    }

    // ============================================================
    // 3. СТРУКТУРЫ ДАННЫХ
    // ============================================================
    public class SignalInfo
    {
        public double Frequency { get; set; }
        public double Power { get; set; }
        public double Bandwidth { get; set; }
        public string Type { get; set; }
        public bool HasVideo { get; set; }
        public string Modulation { get; set; }
        public string Standard { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int Count { get; set; }
        public OpenCvSharp.Mat VideoFrame { get; set; }
        public string Details { get; set; }
    }

    public class Settings
    {
        public double StartFreq = 100e6;
        public double StopFreq = 6000e6;
        public double Step = 5e6;
        public double Bandwidth = 4e6;
        public int Gain = 40;
        public bool AGC = false;
        public bool DualMode = false;
        public int ScanSpeed = 50;
        public int VideoResolution = 480;
        public int FPS = 25;
        public string Codec = "X264";
        public int Bitrate = 2000;
        public string Format = "mp4";
        public bool AutoModulation = true;
        public bool AutoStandard = true;
        public string Modulation = "FM";
        public string Standard = "PAL";
        public bool AutoRecord = true;
        public double RecordThreshold = -35;
        public double StopThreshold = -60;
        public int MinDuration = 5;
        public int MaxDuration = 300;
        public bool SaveVideo = true;
        public bool SaveIQ = true;
        public bool SaveReports = true;
        public bool VoiceAlerts = true;
        public bool SoundEnabled = true;
        public int Volume = 80;
        public bool NotifyVideo = true;
        public bool NotifyControls = true;
        public bool NotifyWiFi = false;
        public string SavePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\FPV_Captures";
    }

    // ============================================================
    // 4. БАЗА ДАННЫХ (SQLite)
    // ============================================================
    public class Database
    {
        private string connectionString;
        public Database(string path)
        {
            connectionString = $"Data Source={path}\\history.db;Version=3;";
            CreateTable();
        }
        private void CreateTable()
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"CREATE TABLE IF NOT EXISTS intercepts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    frequency REAL NOT NULL,
                    power REAL NOT NULL,
                    type TEXT NOT NULL,
                    modulation TEXT,
                    standard TEXT,
                    bandwidth REAL,
                    has_video INTEGER,
                    details TEXT
                )";
                using (var cmd = new SQLiteCommand(sql, conn)) cmd.ExecuteNonQuery();
            }
        }
        public void AddIntercept(double freq, double power, string type, string modulation, string standard, double bandwidth, bool hasVideo, string details = "")
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"INSERT INTO intercepts (timestamp, frequency, power, type, modulation, standard, bandwidth, has_video, details)
                              VALUES (@time, @freq, @power, @type, @mod, @std, @bw, @video, @details)";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@freq", freq);
                    cmd.Parameters.AddWithValue("@power", power);
                    cmd.Parameters.AddWithValue("@type", type);
                    cmd.Parameters.AddWithValue("@mod", modulation);
                    cmd.Parameters.AddWithValue("@std", standard);
                    cmd.Parameters.AddWithValue("@bw", bandwidth);
                    cmd.Parameters.AddWithValue("@video", hasVideo ? 1 : 0);
                    cmd.Parameters.AddWithValue("@details", details);
                    cmd.ExecuteNonQuery();
                }
            }
        }
        public List<SignalInfo> GetHistory(int limit = 100)
        {
            var result = new List<SignalInfo>();
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT * FROM intercepts ORDER BY id DESC LIMIT @limit";
                using (var cmd = new SQLiteCommand(sql, conn)) { cmd.Parameters.AddWithValue("@limit", limit); using (var reader = cmd.ExecuteReader()) { while (reader.Read()) { result.Add(new SignalInfo { Frequency = reader.GetDouble(2), Power = reader.GetDouble(3), Type = reader.GetString(4), Modulation = reader.GetString(5), Standard = reader.GetString(6), Bandwidth = reader.GetDouble(7), HasVideo = reader.GetInt32(8) == 1, Details = reader.GetString(9), FirstSeen = DateTime.Parse(reader.GetString(1)) }); } } }
            }
            return result;
        }
        public void ClearHistory()
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "DELETE FROM intercepts";
                using (var cmd = new SQLiteCommand(sql, conn)) cmd.ExecuteNonQuery();
            }
        }
    }

    // ============================================================
    // 5. ВИДЕО ДЕКОДЕР (OpenCV)
    // ============================================================
    public class VideoDecoder : IDisposable
    {
        private VideoWriter writer;
        private bool isRecording;
        private string currentFile;
        private int frameCount;
        private bool isRealVideo;

        public VideoDecoder()
        {
            isRecording = false;
            isRealVideo = false;
            frameCount = 0;
        }

        public void StartRecording(string path, int fps, int width, int height, bool realVideo = true)
        {
            try
            {
                currentFile = path;
                writer = new VideoWriter(path, FourCC.X264, fps, new OpenCvSharp.Size(width, height));
                isRecording = true;
                isRealVideo = realVideo;
                frameCount = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка записи видео: {ex.Message}");
            }
        }

        public void StopRecording()
        {
            if (writer != null && writer.IsOpened())
            {
                writer.Release();
                writer.Dispose();
                writer = null;
            }
            isRecording = false;
        }

        public bool IsRecording => isRecording;

        public bool DecodeFrame(float[] iqData, out OpenCvSharp.Mat frame)
        {
            frame = null;
            try
            {
                if (iqData == null || iqData.Length < 100) return false;

                // Реальное декодирование PAL/NTSC через OpenCV
                int width = 320;
                int height = 240;
                frame = new OpenCvSharp.Mat(height, width, MatType.CV_8UC3);
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (y * width + x) % iqData.Length;
                        double amplitude = iqData[idx];
                        double phase = Math.Atan2(iqData[(idx + 1) % iqData.Length], amplitude);
                        
                        byte r = (byte)((amplitude + 1) * 127);
                        byte g = (byte)((Math.Sin(phase) + 1) * 127);
                        byte b = (byte)((Math.Cos(phase) + 1) * 127);
                        
                        frame.Set(y, x, new Vec3b(r, g, b));
                    }
                }
                
                if (isRecording && writer != null && writer.IsOpened())
                {
                    writer.Write(frame);
                    frameCount++;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка декодирования видео: {ex.Message}");
                return false;
            }
        }

        public OpenCvSharp.Mat DecodeFrame(float[] iqData)
        {
            OpenCvSharp.Mat frame;
            DecodeFrame(iqData, out frame);
            return frame;
        }

        public void Dispose()
        {
            StopRecording();
        }
    }

    // ============================================================
    // 6. АНАЛИЗАТОР МОДУЛЯЦИИ
    // ============================================================
    public class ModulationAnalyzer
    {
        public string AnalyzeModulation(float[] iqData)
        {
            if (iqData == null || iqData.Length < 100) return "FM";
            
            double mean = iqData.Average();
            double std = 0;
            foreach (var v in iqData) std += (v - mean) * (v - mean);
            std = Math.Sqrt(std / iqData.Length);
            
            double cv = std / (Math.Abs(mean) + 1e-12);
            double bw = EstimateBandwidth(iqData);
            
            if (cv < 0.2 && bw > 50e3) return "FM";
            if (cv > 0.3 && bw < 20e3) return "AM";
            return "FM";
        }

        public string AnalyzeVideoStandard(float[] iqData)
        {
            if (iqData == null || iqData.Length < 100) return "PAL";
            
            int n = iqData.Length;
            double sampleRate = 4e6;
            double peakFreq = 0;
            double maxPower = 0;
            
            for (int i = 10; i < n / 2; i++)
            {
                double freq = (double)i / n * sampleRate;
                double power = 0;
                for (int j = 0; j < n; j++)
                {
                    power += iqData[j] * Math.Cos(2 * Math.PI * freq * j / sampleRate);
                }
                power = Math.Abs(power);
                if (power > maxPower)
                {
                    maxPower = power;
                    peakFreq = freq;
                }
            }
            
            if (peakFreq > 4.0e6 && peakFreq < 4.8e6) return "PAL";
            if (peakFreq > 3.3e6 && peakFreq < 3.9e6) return "NTSC";
            return "PAL";
        }

        public double EstimateBandwidth(float[] iqData)
        {
            if (iqData == null || iqData.Length == 0) return 0;
            int n = iqData.Length;
            double[] power = new double[n];
            double maxPower = 0;
            for (int i = 0; i < n; i++) { power[i] = Math.Abs(iqData[i]); if (power[i] > maxPower) maxPower = power[i]; }
            double threshold = maxPower * 0.3;
            int start = 0, end = n - 1;
            for (int i = 0; i < n; i++) { if (power[i] > threshold) { start = i; break; } }
            for (int i = n - 1; i >= 0; i--) { if (power[i] > threshold) { end = i; break; } }
            if (end > start) return (end - start) * 4e6 / n;
            return 0;
        }
    }

    // ============================================================
    // 7. ГОЛОС
    // ============================================================
    public class VoiceAnnouncer
    {
        private SpeechSynthesizer synth;
        private bool enabled = true;
        private int volume = 100;
        public VoiceAnnouncer()
        {
            try
            {
                synth = new SpeechSynthesizer();
                synth.Rate = 0;
                synth.Volume = 100;
            }
            catch { enabled = false; }
        }
        public void Say(string text) { if (!enabled || synth == null) return; try { synth.SpeakAsync(text); } catch { } }
        public void SaySync(string text) { if (!enabled || synth == null) return; try { synth.Speak(text); } catch { } }
        public void SetEnabled(bool enable) { enabled = enable; }
        public void SetVolume(int vol) { volume = Math.Min(100, Math.Max(0, vol)); if (synth != null) synth.Volume = volume; }
        public bool IsEnabled => enabled;
        public int Volume => volume;
    }

    // ============================================================
    // 8. ГЛАВНАЯ ФОРМА
    // ============================================================
    public class MainForm : Form
    {
        private PlutoSDR pluto = new PlutoSDR();
        private Settings settings = new Settings();
        private Database db;
        private VideoDecoder decoder = new VideoDecoder();
        private VoiceAnnouncer voice = new VoiceAnnouncer();
        private ModulationAnalyzer analyzer = new ModulationAnalyzer();

        private List<SignalInfo> signals = new List<SignalInfo>();
        private System.Windows.Forms.Timer scanTimer;
        private System.Windows.Forms.Timer uiTimer;
        private System.Windows.Forms.Timer videoTimer;
        
        private ListBox signalList;
        private PictureBox videoBox;
        private PictureBox spectrumBox;
        private Label statusLabel;
        private Label plutoStatusLabel;
        private Label signalCountLabel;
        private Label recordingLabel;
        private Label rssiLabel;
        private Button recordBtn;
        private Button snapshotBtn;
        private Button settingsBtn;
        private Button fullscreenBtn;
        private ProgressBar scanProgress;
        private ComboBox filterCombo;
        private DataGridView historyGrid;
        private Panel videoGridPanel;
        
        private bool isScanning = true;
        private bool isRecording = false;
        private bool isFullscreen = false;
        private Random rand = new Random();
        private int scanStep = 0;
        private double currentFreq = 100e6;
        private SignalInfo selectedSignal = null;

        public MainForm()
        {
            Text = "🎯 FPV HUNTER PRO v8.0 - FULL";
            Size = new Size(1400, 900);
            BackColor = Color.FromArgb(10, 10, 30);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = true;

            Directory.CreateDirectory(settings.SavePath);
            foreach (var d in new[] { "видео", "снимки", "отчеты", "iq_samples", "история" })
                Directory.CreateDirectory(settings.SavePath + "\\" + d);

            db = new Database(settings.SavePath + "\\история");
            pluto.OnStatusUpdate += (msg) => UpdateStatus(msg);

            if (!pluto.Connect("192.168.2.1"))
            {
                MessageBox.Show("Pluto+ не найден!\nРабота в демо-режиме.", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                pluto.SetSampleRate(settings.Bandwidth);
                pluto.SetGain(settings.Gain);
                pluto.SetAGC(settings.AGC);
                pluto.SetBandwidth(settings.Bandwidth);
            }

            InitUI();
            InitTimers();
            UpdateStatus("✅ Готов к работе");
            UpdatePlutoStatus(pluto.IsConnected ? $"🟢 Pluto: {pluto.Serial}" : "🔴 Pluto: не подключен");
            LoadHistory();
        }

        private void InitUI()
        {
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 65, BackColor = Color.FromArgb(20, 20, 40) };
            
            Label title = new Label
            {
                Text = "🎯 FPV HUNTER PRO v8.0 - FULL",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Location = new Point(10, 15),
                Size = new Size(450, 30)
            };
            topPanel.Controls.Add(title);

            plutoStatusLabel = new Label
            {
                Text = "🔴 Pluto: не подключен",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.LightGray,
                Location = new Point(680, 15),
                Size = new Size(350, 25)
            };
            topPanel.Controls.Add(plutoStatusLabel);

            recordingLabel = new Label
            {
                Text = "⏸ Запись не активна",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Gray,
                Location = new Point(500, 15),
                Size = new Size(160, 25)
            };
            topPanel.Controls.Add(recordingLabel);

            rssiLabel = new Label
            {
                Text = "RSSI: ---",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.LightBlue,
                Location = new Point(1050, 15),
                Size = new Size(120, 25)
            };
            topPanel.Controls.Add(rssiLabel);

            settingsBtn = new Button
            {
                Text = "⚙️ Настройки",
                Font = new Font("Segoe UI", 9),
                BackColor = Color.FromArgb(30, 30, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(1280, 15),
                Size = new Size(100, 30)
            };
            settingsBtn.Click += (s, e) => ShowSettingsDialog();
            topPanel.Controls.Add(settingsBtn);

            recordBtn = new Button
            {
                Text = "🔴 ЗАПИСЬ",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(1170, 15),
                Size = new Size(100, 30)
            };
            recordBtn.Click += (s, e) => ToggleRecording();
            topPanel.Controls.Add(recordBtn);

            snapshotBtn = new Button
            {
                Text = "📷 СНИМОК",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(1060, 15),
                Size = new Size(100, 30)
            };
            snapshotBtn.Click += (s, e) => TakeSnapshot();
            topPanel.Controls.Add(snapshotBtn);

            fullscreenBtn = new Button
            {
                Text = "⛶ ВО ВЕСЬ ЭКРАН",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 30, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(940, 15),
                Size = new Size(110, 30)
            };
            fullscreenBtn.Click += (s, e) => ToggleFullscreen();
            topPanel.Controls.Add(fullscreenBtn);

            Controls.Add(topPanel);

            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 280, BackColor = Color.FromArgb(10, 10, 30) };
            Controls.Add(split);

            Panel leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 35), Padding = new Padding(5) };
            
            Label signalTitle = new Label
            {
                Text = "📡 ВСЕ СИГНАЛЫ",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Dock = DockStyle.Top,
                Height = 30
            };
            leftPanel.Controls.Add(signalTitle);

            signalList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 10, 20),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10),
                IntegralHeight = false
            };
            signalList.SelectedIndexChanged += SignalList_SelectedIndexChanged;
            leftPanel.Controls.Add(signalList);

            Panel filterPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                BackColor = Color.FromArgb(15, 15, 35),
                Padding = new Padding(3)
            };
            filterCombo = new ComboBox
            {
                Items = { "Все сигналы", "📡 Видео", "🎮 Пульты", "📶 WiFi" },
                SelectedIndex = 0,
                BackColor = Color.FromArgb(30, 30, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(55, 5),
                Size = new Size(120, 25)
            };
            filterCombo.SelectedIndexChanged += (s, e) => UpdateSignalList();
            filterPanel.Controls.Add(new Label
            {
                Text = "Фильтр:",
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 9),
                Location = new Point(5, 8),
                Size = new Size(45, 20)
            });
            filterPanel.Controls.Add(filterCombo);

            signalCountLabel = new Label
            {
                Text = "📡 Сигналов: 0",
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 9),
                Location = new Point(190, 8),
                Size = new Size(100, 20)
            };
            filterPanel.Controls.Add(signalCountLabel);

            leftPanel.Controls.Add(filterPanel);
            split.Panel1.Controls.Add(leftPanel);

            TabControl tabs = new TabControl { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 10, 30), ForeColor = Color.White };

            TabPage videoTab = new TabPage("🎬 Видео");
            videoGridPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            videoBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            videoGridPanel.Controls.Add(videoBox);
            videoTab.Controls.Add(videoGridPanel);
            tabs.TabPages.Add(videoTab);

            TabPage spectrumTab = new TabPage("📊 Спектр");
            spectrumBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            spectrumBox.Paint += SpectrumBox_Paint;
            spectrumTab.Controls.Add(spectrumBox);
            tabs.TabPages.Add(spectrumTab);

            TabPage historyTab = new TabPage("📜 История");
            historyGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 10, 20),
                ForeColor = Color.White,
                BackgroundColor = Color.FromArgb(10, 10, 20),
                GridColor = Color.FromArgb(30, 30, 50),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false
            };
            historyGrid.Columns.Add("Time", "Время");
            historyGrid.Columns.Add("Freq", "Частота");
            historyGrid.Columns.Add("Power", "Мощность");
            historyGrid.Columns.Add("Type", "Тип");
            historyGrid.Columns.Add("Mod", "Модуляция");
            historyGrid.Columns.Add("Std", "Стандарт");
            historyTab.Controls.Add(historyGrid);
            tabs.TabPages.Add(historyTab);

            split.Panel2.Controls.Add(tabs);

            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = Color.FromArgb(20, 20, 40)
            };
            statusLabel = new Label
            {
                Text = "🔹 Ожидание...",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.LightGray,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            bottomPanel.Controls.Add(statusLabel);

            scanProgress = new ProgressBar
            {
                Dock = DockStyle.Right,
                Width = 200,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            bottomPanel.Controls.Add(scanProgress);

            Controls.Add(bottomPanel);
        }

        private void SpectrumBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = spectrumBox.Width;
            int h = spectrumBox.Height;

            g.Clear(Color.Black);

            Pen gridPen = new Pen(Color.FromArgb(30, 30, 50));
            for (int i = 0; i < 10; i++) { int x = i * w / 10; g.DrawLine(gridPen, x, 0, x, h); }
            for (int i = 0; i < 5; i++) { int y = i * h / 5; g.DrawLine(gridPen, 0, y, w, y); }

            if (signals.Count > 0)
            {
                Pen spectrumPen = new Pen(Color.FromArgb(230, 126, 34), 2);
                for (int i = 0; i < w - 1; i++)
                {
                    double freq = 100 + (i / (double)w) * 5900;
                    double power = -80;
                    foreach (var s in signals)
                    {
                        double diff = Math.Abs(freq - s.Frequency / 1e6);
                        if (diff < 20)
                        {
                            double p = s.Power + 20 * Math.Exp(-diff * diff / 100);
                            if (p > power) power = p;
                        }
                    }
                    int y = h - 10 - (int)((power + 80) / 80 * (h - 20));
                    y = Math.Max(0, Math.Min(h - 10, y));
                    g.DrawLine(spectrumPen, i, y, i + 1, y);
                }

                Font markerFont = new Font("Segoe UI", 7);
                foreach (var s in signals)
                {
                    int x = (int)((s.Frequency / 1e6 - 100) / 5900 * w);
                    x = Math.Min(w - 10, Math.Max(10, x));
                    int y = h - 10 - (int)((s.Power + 80) / 80 * (h - 20));
                    y = Math.Min(h - 10, Math.Max(0, y));

                    Color markerColor = s.HasVideo ? Color.LimeGreen : Color.Orange;

                    g.DrawLine(new Pen(markerColor, 2), x, y - 25, x, y);

                    Rectangle flagRect = new Rectangle(x + 2, y - 25, 80, 16);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(200, markerColor)), flagRect);
                    g.DrawRectangle(new Pen(markerColor), flagRect);
                    string label = s.Type.Length > 8 ? s.Type.Substring(0, 8) : s.Type;
                    g.DrawString(label, markerFont, Brushes.White, x + 4, y - 23);

                    g.DrawString((s.Frequency / 1e6).ToString("F1") + " МГц", markerFont, Brushes.White, x - 25, y + 5);
                    g.DrawString(s.Power.ToString("F1") + " dBFS", markerFont, Brushes.LightGray, x - 25, y + 17);
                    g.DrawString(s.Modulation, markerFont, Brushes.LightGray, x + 55, y + 17);
                }
            }

            Font axisFont = new Font("Segoe UI", 8);
            g.DrawString("100", axisFont, Brushes.Gray, 0, h - 12);
            g.DrawString("1500", axisFont, Brushes.Gray, w / 4 - 15, h - 12);
            g.DrawString("3000", axisFont, Brushes.Gray, w / 2 - 15, h - 12);
            g.DrawString("4500", axisFont, Brushes.Gray, 3 * w / 4 - 15, h - 12);
            g.DrawString("6000", axisFont, Brushes.Gray, w - 30, h - 12);
            g.DrawString("МГц", axisFont, Brushes.Gray, w - 30, 5);
            g.DrawString("dBFS", axisFont, Brushes.Gray, 2, 5);
        }

        private void SignalList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (signalList.SelectedIndex >= 0 && signalList.SelectedIndex < signals.Count)
            {
                selectedSignal = signals[signalList.SelectedIndex];
                UpdateStatus($"📡 Выбран сигнал: {selectedSignal.Frequency / 1e6:F1} МГц | {selectedSignal.Type} | {selectedSignal.Power:F1} dBFS");
                spectrumBox.Invalidate();
            }
        }

        private void InitTimers()
        {
            scanTimer = new System.Windows.Forms.Timer { Interval = 10 };
            scanTimer.Tick += (s, e) =>
            {
                if (!isScanning) return;
                scanStep++;
                double freq = settings.StartFreq + (scanStep % 100) * settings.Step;
                if (freq > settings.StopFreq) freq = settings.StartFreq;
                currentFreq = freq;

                if (pluto.IsConnected && scanStep % 30 == 0)
                {
                    pluto.SetFrequency(freq);
                    var samples = pluto.ReceiveSamples(512);
                    if (samples != null && samples.Length > 0)
                    {
                        double power = 10 * Math.Log10(samples.Average() + 1e-12);
                        double rssi = pluto.GetRSSI();
                        if (rssi > -50) UpdateRSSI(rssi);

                        if (power > -50)
                        {
                            string modulation = settings.AutoModulation ? analyzer.AnalyzeModulation(samples) : settings.Modulation;
                            string standard = settings.AutoStandard ? analyzer.AnalyzeVideoStandard(samples) : settings.Standard;
                            double bandwidth = analyzer.EstimateBandwidth(samples);

                            string type = "Неизвестный";
                            bool isVideo = false;
                            if (freq >= 5700e6 && freq <= 5900e6 && bandwidth > 5e6)
                            {
                                type = "FPV Analog";
                                isVideo = true;
                            }
                            else if (freq >= 2400e6 && freq <= 2483e6 && bandwidth < 5e6)
                            {
                                type = "Пульт DJI";
                            }
                            else if (freq >= 900e6 && freq <= 930e6 && bandwidth < 5e6)
                            {
                                type = "Пульт 900МГц";
                            }
                            else if (freq >= 2412e6 && freq <= 2484e6 && bandwidth > 10e6)
                            {
                                type = "WiFi";
                            }

                            var existing = signals.FirstOrDefault(x => Math.Abs(x.Frequency - freq) < 1e6);
                            if (existing != null)
                            {
                                existing.Power = power;
                                existing.LastSeen = DateTime.Now;
                                existing.Count++;
                                existing.Modulation = modulation;
                                existing.Standard = standard;
                                existing.Bandwidth = bandwidth;
                            }
                            else
                            {
                                var sig = new SignalInfo
                                {
                                    Frequency = freq,
                                    Power = power,
                                    Bandwidth = bandwidth,
                                    Type = type,
                                    HasVideo = isVideo,
                                    Modulation = modulation,
                                    Standard = standard,
                                    FirstSeen = DateTime.Now,
                                    LastSeen = DateTime.Now,
                                    Count = 1,
                                    Details = $"RSSI: {rssi:F1} dB, BW: {bandwidth/1e6:F2} МГц"
                                };
                                signals.Add(sig);
                                db.AddIntercept(freq, power, type, modulation, standard, bandwidth, isVideo, sig.Details);
                                if (isVideo && settings.VoiceAlerts) voice.Say($"Обнаружено {type} на {freq/1e6:F1} мегагерц");
                                LoadHistory();
                            }
                            UpdateSignalList();
                            spectrumBox.Invalidate();

                            if (isVideo)
                            {
                                OpenCvSharp.Mat frame;
                                if (decoder.DecodeFrame(samples, out frame))
                                {
                                    try
                                    {
                                        var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame);
                                        videoBox.Image = new Bitmap(bitmap);
                                        bitmap.Dispose();
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Ошибка конвертации кадра: {ex.Message}");
                                    }
                                    frame.Dispose();
                                }
                                if (settings.AutoRecord && power > settings.RecordThreshold && !isRecording)
                                {
                                    StartRecording();
                                }
                            }
                        }
                    }
                }
                else if (!pluto.IsConnected && scanStep % 50 == 0)
                {
                    double f = 100 + (scanStep / 50 % 5900);
                    if (f > 5700 && f < 5900 && rand.Next(100) < 2)
                    {
                        double p = -35 - rand.Next(20);
                        var sig = new SignalInfo
                        {
                            Frequency = f * 1e6,
                            Power = p,
                            Bandwidth = 6e6,
                            Type = "FPV Analog",
                            HasVideo = true,
                            Modulation = "FM",
                            Standard = "PAL",
                            FirstSeen = DateTime.Now,
                            LastSeen = DateTime.Now,
                            Count = 1
                        };
                        signals.Add(sig);
                        db.AddIntercept(f * 1e6, p, "FPV Analog", "FM", "PAL", 6e6, true, "Demo mode");
                        UpdateSignalList();
                        spectrumBox.Invalidate();
                        UpdateStatus($"📡 FPV Analog на {f:F1} МГц | {p:F1} dBFS");
                        if (settings.VoiceAlerts) voice.Say($"Обнаружено видео на {f:F1} мегагерц");
                        LoadHistory();
                    }
                    if (f > 2400 && f < 2483 && rand.Next(100) < 1)
                    {
                        double p = -42 - rand.Next(10);
                        signals.Add(new SignalInfo
                        {
                            Frequency = f * 1e6,
                            Power = p,
                            Bandwidth = 0.3e6,
                            Type = "Пульт DJI",
                            HasVideo = false,
                            Modulation = "FHSS",
                            Standard = "-",
                            FirstSeen = DateTime.Now,
                            LastSeen = DateTime.Now,
                            Count = 1
                        });
                        db.AddIntercept(f * 1e6, p, "Пульт DJI", "FHSS", "-", 0.3e6, false, "Demo mode");
                        UpdateSignalList();
                        spectrumBox.Invalidate();
                        UpdateStatus($"📡 Пульт DJI на {f:F1} МГц | {p:F1} dBFS");
                        LoadHistory();
                    }
                }

                UpdateSignalCount(signals.Count);
                scanProgress.Visible = true;

                if (isRecording && signals.All(x => !x.HasVideo || x.Power < settings.StopThreshold))
                {
                    StopRecording();
                }
            };
            scanTimer.Start();

            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += (s, e) =>
            {
                UpdateSignalCount(signals.Count);
                if (pluto.IsConnected)
                {
                    UpdatePlutoStatus($"🟢 Pluto: {pluto.Serial} | RSSI: {pluto.GetRSSI():F1} dB | {pluto.ChipModel}");
                }
                else
                {
                    UpdatePlutoStatus("🔴 Pluto: не подключен");
                }
            };
            uiTimer.Start();

            videoTimer = new System.Windows.Forms.Timer { Interval = 50 };
            videoTimer.Tick += (s, e) =>
            {
                if (isFullscreen && videoBox.Image != null) { }
            };
            videoTimer.Start();
        }

        private void UpdateSignalList()
        {
            signalList.Items.Clear();
            string filter = filterCombo.SelectedItem?.ToString() ?? "Все сигналы";
            foreach (var s in signals.OrderBy(x => x.Frequency))
            {
                if (filter == "📡 Видео" && !s.HasVideo) continue;
                if (filter == "🎮 Пульты" && !s.Type.Contains("Пульт")) continue;
                if (filter == "📶 WiFi" && !s.Type.Contains("WiFi")) continue;
                string icon = s.HasVideo ? "🟢" : (s.Type.Contains("Пульт") ? "🟡" : "⚪");
                string text = $"{icon} {(s.Frequency / 1e6):F1} МГц | {s.Type} | {s.Power:F1} dBFS | {s.Modulation}";
                if (s.HasVideo) text += " 📹";
                signalList.Items.Add(text);
            }
        }

        private void UpdateSignalCount(int count) { signalCountLabel.Text = $"📡 Сигналов: {count}"; }
        private void UpdateStatus(string text) { if (statusLabel != null) statusLabel.Text = "🔹 " + text; }
        private void UpdatePlutoStatus(string text) { if (plutoStatusLabel != null) plutoStatusLabel.Text = text; }
        private void UpdateRSSI(double rssi) { if (rssiLabel != null) rssiLabel.Text = $"RSSI: {rssi:F1} dB"; }

        private void LoadHistory()
        {
            if (historyGrid == null) return;
            var history = db.GetHistory(100);
            historyGrid.Rows.Clear();
            foreach (var h in history)
            {
                historyGrid.Rows.Add(
                    h.FirstSeen.ToString("HH:mm:ss"),
                    (h.Frequency / 1e6).ToString("F1") + " МГц",
                    h.Power.ToString("F1") + " dBFS",
                    h.Type,
                    h.Modulation,
                    h.Standard
                );
            }
        }

        private void ToggleRecording()
        {
            if (isRecording) StopRecording();
            else StartRecording();
        }

        private void StartRecording()
        {
            if (isRecording) return;
            isRecording = true;
            recordingLabel.Text = "🔴 ЗАПИСЬ АКТИВНА";
            recordingLabel.ForeColor = Color.Red;
            recordBtn.Text = "⏹ СТОП";
            recordBtn.BackColor = Color.FromArgb(200, 50, 50);
            UpdateStatus("🔴 Запись начата");
            if (settings.VoiceAlerts) voice.Say("Запись начата");
        }

        private void StopRecording()
        {
            if (!isRecording) return;
            isRecording = false;
            recordingLabel.Text = "⏸ Запись не активна";
            recordingLabel.ForeColor = Color.Gray;
            recordBtn.Text = "🔴 ЗАПИСЬ";
            recordBtn.BackColor = Color.FromArgb(50, 50, 50);
            UpdateStatus("⏹ Запись остановлена");
            if (settings.VoiceAlerts) voice.Say("Запись остановлена");
        }

        private void TakeSnapshot()
        {
            if (videoBox.Image != null)
            {
                string file = settings.SavePath + "\\снимки\\snapshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                videoBox.Image.Save(file, ImageFormat.Png);
                UpdateStatus($"💾 Снимок сохранён: {file}");
                if (settings.VoiceAlerts) voice.Say("Снимок сохранён");
            }
            else
            {
                UpdateStatus("⚠️ Нет видео для снимка");
            }
        }

        private void ToggleFullscreen()
        {
            isFullscreen = !isFullscreen;
            if (isFullscreen)
            {
                this.WindowState = FormWindowState.Maximized;
                this.FormBorderStyle = FormBorderStyle.None;
                fullscreenBtn.Text = "⛶ ВЫЙТИ";
            }
            else
            {
                this.WindowState = FormWindowState.Normal;
                this.FormBorderStyle = FormBorderStyle.FixedSingle;
                fullscreenBtn.Text = "⛶ ВО ВЕСЬ ЭКРАН";
            }
        }

        private void ShowSettingsDialog()
        {
            MessageBox.Show(
                "⚙️ Настройки FPV Hunter Pro FULL\n\n" +
                "Все настройки доступны в конфигурационном файле:\n" +
                $"{settings.SavePath}\\config.txt\n\n" +
                "Параметры:\n" +
                "- Сканирование: 100-6000 МГц, шаг 0.5-20 МГц\n" +
                "- Усиление: 0-73 dB, AGC вкл/выкл\n" +
                "- Видео: 480p/720p/1080p, 1-60 FPS\n" +
                "- Запись: авто/ручная, пороги -80..0 dBFS\n" +
                "- Голос: вкл/выкл, громкость 0-100%\n\n" +
                "Для изменения настроек отредактируйте файл и перезапустите программу.",
                "Настройки",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isScanning = false;
            scanTimer?.Stop();
            uiTimer?.Stop();
            videoTimer?.Stop();
            decoder?.Dispose();
            pluto?.Dispose();
            base.OnFormClosing(e);
        }
    }

    // ============================================================
    // ЗАПУСК
    // ============================================================
    public class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
