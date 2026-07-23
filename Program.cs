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
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using OpenCvSharp;

namespace FPV_Hunter_FULL
{
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
        public static extern int iio_device_attr_write_string(IntPtr dev, string attr, string value);
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_device_attr_read_string(IntPtr dev, string attr, out IntPtr value);
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
        [DllImport("libiio.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int iio_context_set_timeout(IntPtr ctx, int timeout);
    }

    public class PlutoSDR : IDisposable
    {
        private IntPtr ctx, phy, rx, tx, rx_channel, tx_channel, buffer;
        private bool connected;
        private double sampleRate = 4e6;
        private double frequency = 100e6;
        private double gain = 40;
        private double gain2 = 40;
        private bool agcEnabled = false;
        private bool agcEnabled2 = false;
        private bool dcTracking = true;
        private bool quadTracking = true;
        private double bandwidth = 4e6;

        public string Serial { get; private set; } = "Неизвестно";
        public string Firmware { get; private set; } = "Неизвестно";
        public string ChipModel { get; private set; } = "Неизвестно";
        public string HardwareModel { get; private set; } = "Неизвестно";
        public event Action<string> OnStatusUpdate;

        public bool Connect(string ip = "192.168.2.1")
        {
            Log("Подключение к Pluto SDR...");
            Disconnect();
            ctx = libiio.iio_create_context_from_uri($"ip:{ip}");
            if (ctx == IntPtr.Zero) ctx = libiio.iio_create_context_from_uri("usb:");
            if (ctx == IntPtr.Zero) { Log("Pluto SDR не найден!"); return false; }
            libiio.iio_context_set_timeout(ctx, 5000);
            phy = libiio.iio_context_find_device(ctx, "ad9361-phy");
            rx = libiio.iio_context_find_device(ctx, "cf-ad9361-lpc");
            tx = libiio.iio_context_find_device(ctx, "cf-ad9361-dds-core-lpc");
            if (phy == IntPtr.Zero || rx == IntPtr.Zero) { libiio.iio_context_destroy(ctx); ctx = IntPtr.Zero; Log("AD9361 не найден!"); return false; }
            rx_channel = libiio.iio_device_find_channel(rx, "voltage0", false);
            if (rx_channel == IntPtr.Zero) { Log("RX1 канал не найден!"); return false; }
            libiio.iio_channel_enable(rx_channel);
            var rx_channel2 = libiio.iio_device_find_channel(rx, "voltage1", false);
            if (rx_channel2 != IntPtr.Zero) { libiio.iio_channel_enable(rx_channel2); Log("RX2 канал найден (Rev.C)"); }
            tx_channel = libiio.iio_device_find_channel(tx, "voltage0", true);
            if (tx_channel != IntPtr.Zero) { libiio.iio_channel_enable(tx_channel); }
            connected = true;
            Serial = GetSerial();
            Firmware = GetFirmware();
            ChipModel = GetChipModel();
            HardwareModel = GetHardwareModel();
            ConfigurePluto();
            Log($"Pluto SDR подключен! Серийный: {Serial}, Модель: {ChipModel}");
            return true;
        }

        public void Disconnect()
        {
            if (buffer != IntPtr.Zero) { libiio.iio_buffer_destroy(buffer); buffer = IntPtr.Zero; }
            if (ctx != IntPtr.Zero) { libiio.iio_context_destroy(ctx); ctx = IntPtr.Zero; }
            connected = false;
        }

        private string GetSerial() { if (ctx == IntPtr.Zero) return "Неизвестно"; IntPtr p = libiio.iio_context_get_attr_value(ctx, "serial"); return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : "Неизвестно"; }
        private string GetFirmware() { if (phy == IntPtr.Zero) return "Неизвестно"; IntPtr val; int ret = libiio.iio_device_attr_read_string(phy, "fw_version", out val); return ret >= 0 ? Marshal.PtrToStringAnsi(val) : "Неизвестно"; }
        private string GetChipModel() { if (phy == IntPtr.Zero) return "Неизвестно"; IntPtr val; int ret = libiio.iio_device_attr_read_string(phy, "model", out val); return ret >= 0 ? Marshal.PtrToStringAnsi(val) : "Неизвестно"; }
        private string GetHardwareModel() { if (ctx == IntPtr.Zero) return "Неизвестно"; IntPtr p = libiio.iio_context_get_attr_value(ctx, "hw_model"); return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : "Неизвестно"; }

        private void ConfigurePluto()
        {
            SetFrequency(frequency);
            SetSampleRate(sampleRate);
            SetBandwidth(bandwidth);
            SetGain(gain);
            SetGain2(gain2);
            SetAGC(agcEnabled);
            SetAGC2(agcEnabled2);
            SetDCTracking(dcTracking);
            SetQuadTracking(quadTracking);
        }

        public bool SetFrequency(double freq) { if (!connected || phy == IntPtr.Zero) return false; frequency = freq; int ret = libiio.iio_device_attr_write_double(phy, "RX_LO_FREQ", freq); if (ret >= 0) Log($"Частота: {freq/1e6:F1} МГц"); return ret >= 0; }
        public bool SetSampleRate(double rate) { if (!connected || phy == IntPtr.Zero) return false; sampleRate = rate; int ret = libiio.iio_device_attr_write_double(phy, "RX_SAMPLING_FREQ", rate); if (ret >= 0) libiio.iio_device_attr_write_double(rx, "RX_RF_BANDWIDTH", rate); return ret >= 0; }
        public bool SetBandwidth(double bw) { if (!connected || phy == IntPtr.Zero) return false; bandwidth = bw; int ret = libiio.iio_device_attr_write_double(phy, "in_voltage_rf_bandwidth", bw); if (ret >= 0) Log($"Полоса: {bw/1e6:F1} МГц"); return ret >= 0; }
        public bool SetGain(double gainValue) { if (!connected || phy == IntPtr.Zero) return false; gain = Math.Max(-3, Math.Min(71, gainValue)); int ret = libiio.iio_device_attr_write_double(phy, "RX_GAIN", gain); if (ret >= 0) Log($"Усиление RX1: {gain:F0} дБ"); return ret >= 0; }
        public bool SetGain2(double gainValue) { if (!connected || phy == IntPtr.Zero) return false; gain2 = Math.Max(-3, Math.Min(71, gainValue)); int ret = libiio.iio_device_attr_write_double(phy, "RX_GAIN2", gain2); if (ret >= 0) Log($"Усиление RX2: {gain2:F0} дБ"); return ret >= 0; }
        public bool SetAGC(bool enable) { if (!connected || phy == IntPtr.Zero) return false; agcEnabled = enable; int ret = libiio.iio_device_attr_write_string(phy, "RX_GAIN_MODE", enable ? "fast_attack" : "manual"); if (ret >= 0) Log($"AGC RX1: {(enable ? "Вкл" : "Выкл")}"); return ret >= 0; }
        public bool SetAGC2(bool enable) { if (!connected || phy == IntPtr.Zero) return false; agcEnabled2 = enable; int ret = libiio.iio_device_attr_write_string(phy, "RX_GAIN_MODE2", enable ? "fast_attack" : "manual"); if (ret >= 0) Log($"AGC RX2: {(enable ? "Вкл" : "Выкл")}"); return ret >= 0; }
        public bool SetDCTracking(bool enable) { if (!connected || phy == IntPtr.Zero) return false; dcTracking = enable; int ret = libiio.iio_device_attr_write_double(phy, "in_voltage_rf_dc_offset_tracking_en", enable ? 1 : 0); if (ret >= 0) Log($"DC Offset: {(enable ? "Вкл" : "Выкл")}"); return ret >= 0; }
        public bool SetQuadTracking(bool enable) { if (!connected || phy == IntPtr.Zero) return false; quadTracking = enable; int ret = libiio.iio_device_attr_write_double(phy, "in_voltage_quadrature_tracking_en", enable ? 1 : 0); if (ret >= 0) Log($"Quadrature: {(enable ? "Вкл" : "Выкл")}"); return ret >= 0; }
        public double GetRSSI() { if (!connected || phy == IntPtr.Zero) return -100; libiio.iio_device_attr_read_double(phy, "RX_RSSI", out double rssi); return rssi; }
        public double GetRSSI2() { if (!connected || phy == IntPtr.Zero) return -100; libiio.iio_device_attr_read_double(phy, "RX_RSSI2", out double rssi); return rssi; }
        public double GetTemperature() { if (!connected || phy == IntPtr.Zero) return 0; double temp = 0; libiio.iio_device_attr_read_double(phy, "in_voltage_temperature", out temp); return temp; }

        public float[] ReceiveSamples(int count = 4096)
        {
            if (!connected || rx == IntPtr.Zero || rx_channel == IntPtr.Zero) return null;
            buffer = libiio.iio_device_create_buffer(rx, count, false);
            if (buffer == IntPtr.Zero) return null;
            int bytes = libiio.iio_buffer_refill(buffer);
            if (bytes < 0) { libiio.iio_buffer_destroy(buffer); buffer = IntPtr.Zero; return null; }
            IntPtr data = libiio.iio_buffer_first(buffer, rx_channel);
            if (data == IntPtr.Zero) { libiio.iio_buffer_destroy(buffer); buffer = IntPtr.Zero; return null; }
            int sampleCount = bytes / 4;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short i_val = Marshal.ReadInt16(data, i * 4);
                short q_val = Marshal.ReadInt16(data, i * 4 + 2);
                samples[i] = (float)Math.Sqrt(i_val * i_val + q_val * q_val) / 2048.0f;
            }
            libiio.iio_buffer_destroy(buffer);
            buffer = IntPtr.Zero;
            return samples;
        }

        public bool IsConnected => connected;
        public double Frequency => frequency;
        public double SampleRate => sampleRate;
        public double Gain => gain;
        public double Gain2 => gain2;
        public bool AGC => agcEnabled;
        public bool AGC2 => agcEnabled2;
        public double Bandwidth => bandwidth;

        private void Log(string msg) { OnStatusUpdate?.Invoke(msg); }
        public void Dispose() { Disconnect(); }
    }

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
        public string Details { get; set; }
    }

    public class Settings
    {
        public string PlutoIP { get; set; } = "192.168.2.1";
        public double StartFreq { get; set; } = 70e6;
        public double StopFreq { get; set; } = 6000e6;
        public double Step { get; set; } = 5e6;
        public double SampleRate { get; set; } = 4e6;
        public double Bandwidth { get; set; } = 4e6;
        public double Gain { get; set; } = 40;
        public double Gain2 { get; set; } = 40;
        public bool AGC { get; set; } = false;
        public bool AGC2 { get; set; } = false;
        public bool DCTracking { get; set; } = true;
        public bool QuadTracking { get; set; } = true;
        public int VideoWidth { get; set; } = 640;
        public int VideoHeight { get; set; } = 480;
        public int FPS { get; set; } = 30;
        public bool AutoRecord { get; set; } = true;
        public double RecordThreshold { get; set; } = -35;
        public double StopThreshold { get; set; } = -60;
        public bool VoiceAlerts { get; set; } = true;
        public int VoiceVolume { get; set; } = 100;
        public bool SoundAlerts { get; set; } = true;
        public string SavePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FPV_Captures");
        public string VideoPath => Path.Combine(SavePath, "видео");
        public string SnapshotPath => Path.Combine(SavePath, "снимки");
        public string IQPath => Path.Combine(SavePath, "iq_samples");
        public string ReportPath => Path.Combine(SavePath, "отчеты");
        public string DatabasePath => Path.Combine(SavePath, "история");
    }

    public class Database
    {
        private string logFile;
        private List<SignalInfo> history = new List<SignalInfo>();
        private object lockObj = new object();

        public Database(string path = null)
        {
            if (string.IsNullOrEmpty(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            logFile = Path.Combine(path, "signals.log");
            LoadHistory();
        }

        private void LoadHistory()
        {
            lock (lockObj)
            {
                try
                {
                    if (File.Exists(logFile))
                    {
                        foreach (var line in File.ReadAllLines(logFile))
                        {
                            try
                            {
                                var parts = line.Split('|');
                                if (parts.Length >= 9)
                                {
                                    history.Add(new SignalInfo
                                    {
                                        Frequency = double.Parse(parts[0]),
                                        Power = double.Parse(parts[1]),
                                        Type = parts[2],
                                        Modulation = parts[3],
                                        Standard = parts[4],
                                        Bandwidth = double.Parse(parts[5]),
                                        HasVideo = parts[6] == "1",
                                        Details = parts[7],
                                        FirstSeen = DateTime.Parse(parts[8])
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }

        public void AddIntercept(double freq, double power, string type, string modulation, string standard, double bandwidth, bool hasVideo, string details = "")
        {
            lock (lockObj)
            {
                try
                {
                    var existing = history.FirstOrDefault(x => Math.Abs(x.Frequency - freq) < 100000);
                    if (existing != null)
                    {
                        existing.Power = power;
                        existing.LastSeen = DateTime.Now;
                        existing.Count++;
                        return;
                    }
                    var signal = new SignalInfo
                    {
                        Frequency = freq,
                        Power = power,
                        Type = type,
                        Modulation = modulation,
                        Standard = standard,
                        Bandwidth = bandwidth,
                        HasVideo = hasVideo,
                        Details = details,
                        FirstSeen = DateTime.Now,
                        LastSeen = DateTime.Now,
                        Count = 1
                    };
                    history.Add(signal);
                    File.AppendAllText(logFile, $"{freq}|{power}|{type}|{modulation}|{standard}|{bandwidth}|{(hasVideo ? 1 : 0)}|{details}|{DateTime.Now}\n");
                }
                catch { }
            }
        }

        public List<SignalInfo> GetHistory(int limit = 100)
        {
            lock (lockObj) { return history.OrderByDescending(x => x.FirstSeen).Take(limit).ToList(); }
        }

        public void ClearHistory()
        {
            lock (lockObj) { history.Clear(); try { File.Delete(logFile); } catch { } }
        }
    }

    public class VideoDecoder : IDisposable
    {
        private VideoWriter writer;
        private bool isRecording;
        private int width = 640;
        private int height = 480;
        private int fps = 30;
        private Mat currentFrame;
        private object lockObj = new object();

        public VideoDecoder() { isRecording = false; }
        public void SetResolution(int w, int h) { width = w; height = h; }
        public void SetFPS(int fpsValue) { fps = fpsValue; }

        public void StartRecording(string path)
        {
            try
            {
                currentFrame = null;
                writer = new VideoWriter(path, FourCC.Default, fps, new OpenCvSharp.Size(width, height));
                isRecording = true;
            }
            catch { }
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

        public bool DecodeFrame(float[] iqData, out Mat frame)
        {
            frame = null;
            lock (lockObj)
            {
                try
                {
                    if (iqData == null || iqData.Length < 100) return false;
                    frame = new Mat(height, width, MatType.CV_8UC3);
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int idx = (y * width + x) % (iqData.Length / 2);
                            float i = iqData[idx * 2];
                            float q = iqData[idx * 2 + 1];
                            float amplitude = (float)Math.Sqrt(i * i + q * q);
                            float phase = (float)Math.Atan2(q, i);
                            byte r = (byte)((amplitude * 0.5f + 0.5f) * 255);
                            byte g = (byte)(((Math.Sin(phase * 2) * 0.5f + 0.5f)) * 255);
                            byte b = (byte)(((Math.Cos(phase * 2) * 0.5f + 0.5f)) * 255);
                            frame.Set(y, x, new Vec3b(r, g, b));
                        }
                    }
                    if (isRecording && writer != null && writer.IsOpened()) writer.Write(frame);
                    currentFrame = frame.Clone();
                    return true;
                }
                catch { return false; }
            }
        }

        public Bitmap MatToBitmap(Mat mat)
        {
            if (mat == null || mat.Empty()) return null;
            try
            {
                int w = mat.Width, h = mat.Height;
                Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                IntPtr ptr = bmpData.Scan0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Vec3b pixel = mat.Get<Vec3b>(y, x);
                        int offset = (y * w + x) * 3;
                        Marshal.WriteByte(ptr, offset, pixel.Item2);
                        Marshal.WriteByte(ptr, offset + 1, pixel.Item1);
                        Marshal.WriteByte(ptr, offset + 2, pixel.Item0);
                    }
                }
                bmp.UnlockBits(bmpData);
                return bmp;
            }
            catch { return null; }
        }

        public void Dispose() { StopRecording(); }
    }

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
            double peakFreq = 0, maxPower = 0;
            for (int i = 10; i < n / 2; i++)
            {
                double freq = (double)i / n * sampleRate;
                double power = 0;
                for (int j = 0; j < n; j++)
                    power += iqData[j] * Math.Cos(2 * Math.PI * freq * j / sampleRate);
                power = Math.Abs(power);
                if (power > maxPower) { maxPower = power; peakFreq = freq; }
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

    public class VoiceAnnouncer
    {
        private SpeechSynthesizer synth;
        private bool enabled = true;

        public VoiceAnnouncer()
        {
            try { synth = new SpeechSynthesizer(); synth.Rate = 0; synth.Volume = 100; }
            catch { enabled = false; }
        }

        public void Say(string text) { if (!enabled || synth == null) return; try { synth.SpeakAsync(text); } catch { } }
        public void SetEnabled(bool enable) { enabled = enable; }
        public bool IsEnabled => enabled;
    }

    public class MainForm : Form
    {
        private PlutoSDR pluto = new PlutoSDR();
        private Settings settings = new Settings();
        private Database db;
        private VideoDecoder decoder = new VideoDecoder();
        private VoiceAnnouncer voice = new VoiceAnnouncer();
        private ModulationAnalyzer analyzer = new ModulationAnalyzer();
        private List<SignalInfo> signals = new List<SignalInfo>();
        private System.Windows.Forms.Timer scanTimer, uiTimer;
        private ListBox signalList;
        private PictureBox videoBox, spectrumBox;
        private Label statusLabel, plutoStatusLabel, signalCountLabel, recordingLabel, rssiLabel, tempLabel;
        private Button recordBtn, snapshotBtn, settingsBtn, fullscreenBtn, connectBtn;
        private ProgressBar scanProgress;
        private ComboBox filterCombo;
        private DataGridView historyGrid;
        private bool isScanning = true, isRecording = false, isFullscreen = false;
        private Random rand = new Random();
        private int scanStep = 0;
        private double currentFreq = 100e6;

        public MainForm()
        {
            Text = "FPV HUNTER PRO v8.0";
            Size = new Size(1400, 900);
            BackColor = Color.FromArgb(10, 10, 30);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = true;

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir = Path.Combine(appDir, "data");
            string docsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FPV_Captures");

            foreach (var dir in new[] { dataDir, docsDir, settings.VideoPath, settings.SnapshotPath,
                settings.IQPath, settings.ReportPath, settings.DatabasePath })
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }

            LoadConfig();
            db = new Database(dataDir);
            pluto.OnStatusUpdate += (msg) => UpdateStatus(msg);

            InitUI();
            InitTimers();
            UpdateStatus("Готов к работе");
            LoadHistory();

            ConnectPluto();
        }

        private void LoadConfig()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (!File.Exists(configPath)) return;

                var lines = File.ReadAllLines(configPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith(";") || string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "PlutoIP": settings.PlutoIP = value; break;
                        case "SampleRate": settings.SampleRate = double.Parse(value) * 1e6; break;
                        case "Bandwidth": settings.Bandwidth = double.Parse(value) * 1e6; break;
                        case "Gain": settings.Gain = double.Parse(value); break;
                        case "Gain2": settings.Gain2 = double.Parse(value); break;
                        case "AGC": settings.AGC = bool.Parse(value); break;
                        case "AGC2": settings.AGC2 = bool.Parse(value); break;
                        case "DCTracking": settings.DCTracking = bool.Parse(value); break;
                        case "QuadTracking": settings.QuadTracking = bool.Parse(value); break;
                        case "StartFreq": settings.StartFreq = double.Parse(value) * 1e6; break;
                        case "StopFreq": settings.StopFreq = double.Parse(value) * 1e6; break;
                        case "Step": settings.Step = double.Parse(value) * 1e6; break;
                        case "VideoWidth": settings.VideoWidth = int.Parse(value); break;
                        case "VideoHeight": settings.VideoHeight = int.Parse(value); break;
                        case "FPS": settings.FPS = int.Parse(value); break;
                        case "AutoRecord": settings.AutoRecord = bool.Parse(value); break;
                        case "RecordThreshold": settings.RecordThreshold = double.Parse(value); break;
                        case "StopThreshold": settings.StopThreshold = double.Parse(value); break;
                        case "VoiceAlerts": settings.VoiceAlerts = bool.Parse(value); break;
                        case "VoiceVolume": settings.VoiceVolume = int.Parse(value); break;
                        case "SoundAlerts": settings.SoundAlerts = bool.Parse(value); break;
                        case "SavePath": settings.SavePath = value.Replace("%USERNAME%", Environment.UserName); break;
                    }
                }
                UpdateStatus("Настройки загружены из config.ini");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка загрузки config.ini: {ex.Message}");
            }
        }

        private void InitUI()
        {
            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 65, BackColor = Color.FromArgb(20, 20, 40) };

            Label title = new Label
            {
                Text = "FPV HUNTER PRO v8.0",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Location = new Point(10, 15),
                Size = new Size(400, 30)
            };
            topPanel.Controls.Add(title);

            plutoStatusLabel = new Label
            {
                Text = "Pluto: не подключен",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Red,
                Location = new Point(500, 10),
                Size = new Size(250, 20)
            };
            topPanel.Controls.Add(plutoStatusLabel);

            connectBtn = new Button
            {
                Text = "Подключить",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(500, 30),
                Size = new Size(100, 25)
            };
            connectBtn.Click += (s, e) => ConnectPluto();
            topPanel.Controls.Add(connectBtn);

            tempLabel = new Label
            {
                Text = "🌡️ --°C",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.LightBlue,
                Location = new Point(620, 10),
                Size = new Size(100, 20)
            };
            topPanel.Controls.Add(tempLabel);

            rssiLabel = new Label
            {
                Text = "📶 RSSI: ---",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.LightGreen,
                Location = new Point(620, 30),
                Size = new Size(150, 20)
            };
            topPanel.Controls.Add(rssiLabel);

            recordingLabel = new Label
            {
                Text = "⏸ Запись не активна",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.Gray,
                Location = new Point(780, 10),
                Size = new Size(150, 20)
            };
            topPanel.Controls.Add(recordingLabel);

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
                Text = "🔴 Запись",
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
                Text = "📷 Снимок",
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
                Text = "⛶ Во весь экран",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 30, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(940, 15),
                Size = new Size(110, 30)
            };
            fullscreenBtn.Click += (s, e) => ToggleFullscreen();
            topPanel.Controls.Add(fullscreenBtn);

            var reloadBtn = new Button
            {
                Text = "🔄 Конфиг",
                Font = new Font("Segoe UI", 9),
                BackColor = Color.FromArgb(40, 40, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(1180, 15),
                Size = new Size(80, 30)
            };
            reloadBtn.Click += (s, e) => { LoadConfig(); UpdateStatus("Конфиг перезагружен"); };
            topPanel.Controls.Add(reloadBtn);

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
                Items = { "Все сигналы", "Видео", "Пульты", "WiFi" },
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
                Text = "Сигналов: 0",
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
            videoBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            videoTab.Controls.Add(videoBox);
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
                Text = "Ожидание...",
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
            int w = spectrumBox.Width, h = spectrumBox.Height;
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
                var s = signals[signalList.SelectedIndex];
                UpdateStatus($"Выбран сигнал: {s.Frequency / 1e6:F1} МГц | {s.Type} | {s.Power:F1} dBFS");
                spectrumBox.Invalidate();
            }
        }

        private void InitTimers()
        {
            scanTimer = new System.Windows.Forms.Timer { Interval = 10 };
            scanTimer.Tick += (s, e) =>
            {
                if (!isScanning || !pluto.IsConnected) return;
                scanStep++;
                double freq = settings.StartFreq + (scanStep % 100) * settings.Step;
                if (freq > settings.StopFreq) freq = settings.StartFreq;
                currentFreq = freq;

                if (scanStep % 5 == 0)
                {
                    pluto.SetFrequency(freq);
                    var samples = pluto.ReceiveSamples(512);
                    if (samples != null && samples.Length > 0)
                    {
                        double power = 10 * Math.Log10(samples.Average() + 1e-12);
                        double rssi = pluto.GetRSSI();
                        UpdateRSSI(rssi);

                        if (power > -50)
                        {
                            string modulation = analyzer.AnalyzeModulation(samples);
                            string standard = analyzer.AnalyzeVideoStandard(samples);
                            double bandwidth = analyzer.EstimateBandwidth(samples);

                            string type = "Неизвестный";
                            bool isVideo = false;
                            if (freq >= 5700e6 && freq <= 5900e6 && bandwidth > 5e6) { type = "FPV Analog"; isVideo = true; }
                            else if (freq >= 2400e6 && freq <= 2483e6 && bandwidth < 5e6) { type = "Пульт DJI"; }
                            else if (freq >= 900e6 && freq <= 930e6 && bandwidth < 5e6) { type = "Пульт 900МГц"; }
                            else if (freq >= 2412e6 && freq <= 2484e6 && bandwidth > 10e6) { type = "WiFi"; }

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
                                var complex = pluto.ReceiveSamples(1024);
                                if (complex != null)
                                {
                                    Mat frame;
                                    if (decoder.DecodeFrame(complex, out frame))
                                    {
                                        var bmp = decoder.MatToBitmap(frame);
                                        if (bmp != null) videoBox.Image = bmp;
                                        frame.Dispose();
                                    }
                                }
                                if (settings.AutoRecord && power > settings.RecordThreshold && !isRecording)
                                    StartRecording();
                            }
                        }
                    }
                }
                UpdateSignalCount(signals.Count);
                scanProgress.Visible = true;

                if (isRecording && signals.All(x => !x.HasVideo || x.Power < settings.StopThreshold))
                    StopRecording();
            };
            scanTimer.Start();

            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += (s, e) =>
            {
                UpdateSignalCount(signals.Count);
                if (pluto.IsConnected)
                {
                    double temp = pluto.GetTemperature();
                    tempLabel.Text = $"🌡️ {temp:F1}°C";
                    UpdatePlutoStatus($"Pluto: {pluto.Serial} | {pluto.Frequency/1e6:F1} МГц");
                    UpdateRSSI(pluto.GetRSSI());
                }
                else
                    UpdatePlutoStatus("Pluto: не подключен");
            };
            uiTimer.Start();
        }

        private void ConnectPluto()
        {
            UpdateStatus("Подключение к Pluto...");
            if (pluto.Connect(settings.PlutoIP))
            {
                UpdatePlutoStatus($"Pluto: {pluto.Serial} подключен");
                connectBtn.Text = "✅ Pluto OK";
                connectBtn.BackColor = Color.FromArgb(30, 60, 30);
                UpdateStatus("Pluto SDR готов к работе");
            }
            else
            {
                UpdatePlutoStatus("Pluto: не найден");
                connectBtn.Text = "🔄 Повторить";
                connectBtn.BackColor = Color.FromArgb(60, 30, 30);
                UpdateStatus("Pluto SDR не найден. Проверьте подключение.");
            }
        }

        private void UpdateSignalList()
        {
            signalList.Items.Clear();
            string filter = filterCombo.SelectedItem?.ToString() ?? "Все сигналы";
            foreach (var s in signals.OrderBy(x => x.Frequency))
            {
                if (filter == "Видео" && !s.HasVideo) continue;
                if (filter == "Пульты" && !s.Type.Contains("Пульт")) continue;
                if (filter == "WiFi" && !s.Type.Contains("WiFi")) continue;
                string icon = s.HasVideo ? "🟢" : (s.Type.Contains("Пульт") ? "🟡" : "⚪");
                string text = $"{icon} {(s.Frequency / 1e6):F1} МГц | {s.Type} | {s.Power:F1} dBFS | {s.Modulation}";
                if (s.HasVideo) text += " 📹";
                signalList.Items.Add(text);
            }
        }

        private void UpdateSignalCount(int count) { signalCountLabel.Text = $"Сигналов: {count}"; }
        private void UpdateStatus(string text) { if (statusLabel != null) statusLabel.Text = " " + text; }
        private void UpdatePlutoStatus(string text) { if (plutoStatusLabel != null) { plutoStatusLabel.Text = text; plutoStatusLabel.ForeColor = pluto.IsConnected ? Color.LightGreen : Color.Red; } }
        private void UpdateRSSI(double rssi) { if (rssiLabel != null) rssiLabel.Text = $"📶 RSSI: {rssi:F1} dB"; }

        private void LoadHistory()
        {
            if (historyGrid == null) return;
            try
            {
                var history = db.GetHistory(100);
                historyGrid.Rows.Clear();
                foreach (var h in history)
                    historyGrid.Rows.Add(h.FirstSeen.ToString("HH:mm:ss"), (h.Frequency / 1e6).ToString("F1") + " МГц", h.Power.ToString("F1") + " dBFS", h.Type, h.Modulation, h.Standard);
            }
            catch { }
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
            recordBtn.Text = "⏹ Стоп";
            recordBtn.BackColor = Color.FromArgb(200, 50, 50);
            UpdateStatus("Запись начата");
            if (settings.VoiceAlerts) voice.Say("Запись начата");
        }

        private void StopRecording()
        {
            if (!isRecording) return;
            isRecording = false;
            recordingLabel.Text = "⏸ Запись не активна";
            recordingLabel.ForeColor = Color.Gray;
            recordBtn.Text = "🔴 Запись";
            recordBtn.BackColor = Color.FromArgb(50, 50, 50);
            UpdateStatus("Запись остановлена");
            if (settings.VoiceAlerts) voice.Say("Запись остановлена");
        }

        private void TakeSnapshot()
        {
            if (videoBox.Image != null)
            {
                string file = settings.SnapshotPath + "\\snapshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                videoBox.Image.Save(file, ImageFormat.Png);
                UpdateStatus($"Снимок сохранён: {file}");
                if (settings.VoiceAlerts) voice.Say("Снимок сохранён");
            }
            else UpdateStatus("Нет видео для снимка");
        }

        private void ToggleFullscreen()
        {
            isFullscreen = !isFullscreen;
            if (isFullscreen) { this.WindowState = FormWindowState.Maximized; this.FormBorderStyle = FormBorderStyle.None; fullscreenBtn.Text = "⛶ Выйти"; }
            else { this.WindowState = FormWindowState.Normal; this.FormBorderStyle = FormBorderStyle.FixedSingle; fullscreenBtn.Text = "⛶ Во весь экран"; }
        }

        private void ShowSettingsDialog()
        {
            var dialog = new Form
            {
                Text = "⚙️ НАСТРОЙКИ",
                Size = new Size(800, 600),
                BackColor = Color.FromArgb(10, 10, 30),
                ForeColor = Color.White,
                StartPosition = FormStartPosition.CenterParent
            };

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            int y = 10;

            panel.Controls.Add(CreateGroup("📡 Сканирование", ref y));
            panel.Controls.Add(CreateParam("Начальная частота", settings.StartFreq / 1e6, "МГц", 70, 6000, ref y));
            panel.Controls.Add(CreateParam("Конечная частота", settings.StopFreq / 1e6, "МГц", 70, 6000, ref y));
            panel.Controls.Add(CreateParam("Шаг сканирования", settings.Step / 1e6, "МГц", 0.5, 20, ref y));
            y += 10;

            panel.Controls.Add(CreateGroup("📻 Pluto SDR", ref y));
            panel.Controls.Add(CreateParam("Частота дискретизации", settings.SampleRate / 1e6, "МГц", 0.5, 61.44, ref y));
            panel.Controls.Add(CreateParam("Полоса пропускания", settings.Bandwidth / 1e6, "МГц", 0.2, 56, ref y));
            panel.Controls.Add(CreateParam("Усиление RX1", settings.Gain, "дБ", -3, 71, ref y));
            panel.Controls.Add(CreateParam("Усиление RX2", settings.Gain2, "дБ", -3, 71, ref y));
            panel.Controls.Add(CreateCheckbox("AGC RX1", settings.AGC, ref y));
            panel.Controls.Add(CreateCheckbox("AGC RX2", settings.AGC2, ref y));
            panel.Controls.Add(CreateCheckbox("DC Offset", settings.DCTracking, ref y));
            panel.Controls.Add(CreateCheckbox("Quadrature", settings.QuadTracking, ref y));
            y += 10;

            panel.Controls.Add(CreateGroup("🎬 Видео", ref y));
            panel.Controls.Add(CreateParam("Разрешение", settings.VideoWidth, "px", 320, 1920, ref y));
            panel.Controls.Add(CreateParam("FPS", settings.FPS, "fps", 1, 60, ref y));
            y += 10;

            panel.Controls.Add(CreateGroup("💾 Запись", ref y));
            panel.Controls.Add(CreateCheckbox("Автозапись", settings.AutoRecord, ref y));
            panel.Controls.Add(CreateParam("Порог записи", settings.RecordThreshold, "dBFS", -80, 0, ref y));
            panel.Controls.Add(CreateParam("Порог остановки", settings.StopThreshold, "dBFS", -80, 0, ref y));
            y += 10;

            panel.Controls.Add(CreateGroup("🔊 Оповещения", ref y));
            panel.Controls.Add(CreateCheckbox("Голосовые", settings.VoiceAlerts, ref y));
            panel.Controls.Add(CreateParam("Громкость", settings.VoiceVolume, "%", 0, 100, ref y));

            var saveBtn = new Button
            {
                Text = "✅ Сохранить",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 60, 30),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(10, y + 20),
                Size = new Size(150, 35)
            };
            saveBtn.Click += (s, e) => { SaveSettings(); dialog.Close(); };
            panel.Controls.Add(saveBtn);

            var cancelBtn = new Button
            {
                Text = "❌ Отмена",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(60, 30, 30),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(170, y + 20),
                Size = new Size(150, 35)
            };
            cancelBtn.Click += (s, e) => dialog.Close();
            panel.Controls.Add(cancelBtn);

            dialog.Controls.Add(panel);
            dialog.ShowDialog();
        }

        private Label CreateGroup(string text, ref int y)
        {
            var label = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Location = new Point(10, y),
                Size = new Size(300, 25)
            };
            y += 30;
            return label;
        }

        private Panel CreateParam(string name, double value, string unit, double min, double max, ref int y)
        {
            var panel = new Panel { Location = new Point(20, y), Size = new Size(400, 30) };
            var label = new Label { Text = $"{name}:", ForeColor = Color.LightGray, Location = new Point(0, 5), Size = new Size(150, 20) };
            var track = new TrackBar { Minimum = (int)(min * 100), Maximum = (int)(max * 100), Value = (int)(value * 100), Location = new Point(160, 0), Size = new Size(180, 25) };
            var valLabel = new Label { Text = $"{value:F1} {unit}", ForeColor = Color.White, Location = new Point(345, 5), Size = new Size(80, 20) };
            track.ValueChanged += (s, e) =>
            {
                double val = track.Value / 100.0;
                valLabel.Text = $"{val:F1} {unit}";
                if (name.Contains("Начальная")) settings.StartFreq = val * 1e6;
                else if (name.Contains("Конечная")) settings.StopFreq = val * 1e6;
                else if (name.Contains("Шаг")) settings.Step = val * 1e6;
                else if (name.Contains("дискретизации")) settings.SampleRate = val * 1e6;
                else if (name.Contains("полосы")) settings.Bandwidth = val * 1e6;
                else if (name.Contains("Усиление RX1")) settings.Gain = val;
                else if (name.Contains("Усиление RX2")) settings.Gain2 = val;
                else if (name.Contains("Порог записи")) settings.RecordThreshold = val;
                else if (name.Contains("Порог остановки")) settings.StopThreshold = val;
                else if (name.Contains("Разрешение")) settings.VideoWidth = (int)val;
                else if (name.Contains("FPS")) settings.FPS = (int)val;
                else if (name.Contains("Громкость")) settings.VoiceVolume = (int)val;
            };
            panel.Controls.Add(label);
            panel.Controls.Add(track);
            panel.Controls.Add(valLabel);
            y += 35;
            return panel;
        }

        private Panel CreateCheckbox(string name, bool value, ref int y)
        {
            var panel = new Panel { Location = new Point(20, y), Size = new Size(400, 30) };
            var check = new CheckBox
            {
                Text = name,
                ForeColor = Color.White,
                Checked = value,
                Location = new Point(0, 5),
                Size = new Size(200, 20)
            };
            check.CheckedChanged += (s, e) =>
            {
                if (name.Contains("AGC RX1")) settings.AGC = check.Checked;
                else if (name.Contains("AGC RX2")) settings.AGC2 = check.Checked;
                else if (name.Contains("DC Offset")) settings.DCTracking = check.Checked;
                else if (name.Contains("Quadrature")) settings.QuadTracking = check.Checked;
                else if (name.Contains("Автозапись")) settings.AutoRecord = check.Checked;
                else if (name.Contains("Голосовые")) settings.VoiceAlerts = check.Checked;
            };
            panel.Controls.Add(check);
            y += 35;
            return panel;
        }

        private void SaveSettings()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                using (var writer = new StreamWriter(configPath))
                {
                    writer.WriteLine("; ============================================================");
                    writer.WriteLine("; FPV Hunter Pro v8.0 - Конфигурационный файл");
                    writer.WriteLine("; ============================================================");
                    writer.WriteLine();
                    writer.WriteLine("[PlutoSDR]");
                    writer.WriteLine($"PlutoIP={settings.PlutoIP}");
                    writer.WriteLine($"SampleRate={settings.SampleRate/1e6:F1}");
                    writer.WriteLine($"Bandwidth={settings.Bandwidth/1e6:F1}");
                    writer.WriteLine($"Gain={settings.Gain:F0}");
                    writer.WriteLine($"Gain2={settings.Gain2:F0}");
                    writer.WriteLine($"AGC={settings.AGC.ToString().ToLower()}");
                    writer.WriteLine($"AGC2={settings.AGC2.ToString().ToLower()}");
                    writer.WriteLine($"DCTracking={settings.DCTracking.ToString().ToLower()}");
                    writer.WriteLine($"QuadTracking={settings.QuadTracking.ToString().ToLower()}");
                    writer.WriteLine();
                    writer.WriteLine("[Scanning]");
                    writer.WriteLine($"StartFreq={settings.StartFreq/1e6:F0}");
                    writer.WriteLine($"StopFreq={settings.StopFreq/1e6:F0}");
                    writer.WriteLine($"Step={settings.Step/1e6:F1}");
                    writer.WriteLine();
                    writer.WriteLine("[Video]");
                    writer.WriteLine($"VideoWidth={settings.VideoWidth}");
                    writer.WriteLine($"VideoHeight={settings.VideoHeight}");
                    writer.WriteLine($"FPS={settings.FPS}");
                    writer.WriteLine();
                    writer.WriteLine("[Recording]");
                    writer.WriteLine($"AutoRecord={settings.AutoRecord.ToString().ToLower()}");
                    writer.WriteLine($"RecordThreshold={settings.RecordThreshold:F0}");
                    writer.WriteLine($"StopThreshold={settings.StopThreshold:F0}");
                    writer.WriteLine();
                    writer.WriteLine("[Voice]");
                    writer.WriteLine($"VoiceAlerts={settings.VoiceAlerts.ToString().ToLower()}");
                    writer.WriteLine($"VoiceVolume={settings.VoiceVolume}");
                    writer.WriteLine($"SoundAlerts={settings.SoundAlerts.ToString().ToLower()}");
                    writer.WriteLine();
                    writer.WriteLine("[Paths]");
                    writer.WriteLine($"SavePath={settings.SavePath}");
                }
                UpdateStatus("✅ Настройки сохранены!");
                if (pluto.IsConnected)
                {
                    pluto.SetSampleRate(settings.SampleRate);
                    pluto.SetBandwidth(settings.Bandwidth);
                    pluto.SetGain(settings.Gain);
                    pluto.SetGain2(settings.Gain2);
                    pluto.SetAGC(settings.AGC);
                    pluto.SetAGC2(settings.AGC2);
                    pluto.SetDCTracking(settings.DCTracking);
                    pluto.SetQuadTracking(settings.QuadTracking);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Ошибка сохранения: {ex.Message}");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isScanning = false;
            scanTimer?.Stop();
            uiTimer?.Stop();
            decoder?.Dispose();
            pluto?.Dispose();
            base.OnFormClosing(e);
        }
    }

    public class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                Directory.SetCurrentDirectory(exeDir);
                string dataDir = Path.Combine(exeDir, "data");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка:\n{ex.Message}\n\n{ex.StackTrace}",
                    "FPV Hunter Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                File.WriteAllText("fatal_error.log", $"{DateTime.Now}: {ex}");
            }
        }
    }
}
