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
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using OpenCvSharp;

namespace FPV_Hunter_FULL
{
    // 1. SignalInfo
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

    // 2. VoiceAnnouncer
    public class VoiceAnnouncer
    {
        private SpeechSynthesizer synth;
        private bool enabled = true;
        public VoiceAnnouncer() { try { synth = new SpeechSynthesizer(); synth.Rate = 0; synth.Volume = 100; } catch { enabled = false; } }
        public void Say(string text) { if (!enabled || synth == null) return; try { synth.SpeakAsync(text); } catch { } }
        public void SetEnabled(bool enable) { enabled = enable; }
        public bool IsEnabled => enabled;
    }

    // 3. Database
    public class Database
    {
        private string connectionString;
        public Database(string path = null)
        {
            if (string.IsNullOrEmpty(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            string dbPath = Path.Combine(path, "history.db");
            connectionString = $"Data Source={dbPath};Version=3;";
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
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@limit", limit);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new SignalInfo
                            {
                                Frequency = reader.GetDouble(2),
                                Power = reader.GetDouble(3),
                                Type = reader.GetString(4),
                                Modulation = reader.GetString(5),
                                Standard = reader.GetString(6),
                                Bandwidth = reader.GetDouble(7),
                                HasVideo = reader.GetInt32(8) == 1,
                                Details = reader.GetString(9),
                                FirstSeen = DateTime.Parse(reader.GetString(1))
                            });
                        }
                    }
                }
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

    // 4. libiio
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

    // 5. PlutoSDR
    public class PlutoSDR : IDisposable
    {
        private IntPtr ctx, phy, rx, rx_channel, buffer;
        private bool connected;
        private double sampleRate = 4e6;
        public string Serial { get; private set; } = "Unknown";
        public string ChipModel { get; private set; } = "Unknown";
        public event Action<string> OnStatusUpdate;

        public bool Connect(string ip = "192.168.2.1")
        {
            Log("Connecting to Pluto+...");
            Disconnect();
            ctx = libiio.iio_create_context_from_uri($"ip:{ip}");
            if (ctx == IntPtr.Zero) ctx = libiio.iio_create_context_from_uri("usb:");
            if (ctx == IntPtr.Zero) { Log("Pluto+ not found!"); return false; }
            phy = libiio.iio_context_find_device(ctx, "ad9361-phy");
            rx = libiio.iio_context_find_device(ctx, "cf-ad9361-lpc");
            if (phy == IntPtr.Zero || rx == IntPtr.Zero) { libiio.iio_context_destroy(ctx); ctx = IntPtr.Zero; Log("AD9361 not found!"); return false; }
            rx_channel = libiio.iio_device_find_channel(rx, "voltage0", false);
            if (rx_channel == IntPtr.Zero) { Log("RX channel not found!"); return false; }
            libiio.iio_channel_enable(rx_channel);
            connected = true;
            Serial = GetSerial();
            ChipModel = GetChipModel();
            SetSampleRate(sampleRate);
            SetGain(40);
            SetFrequency(100e6);
            Log($"Pluto+ connected! Serial: {Serial}");
            return true;
        }

        public void Disconnect()
        {
            if (buffer != IntPtr.Zero) { libiio.iio_buffer_destroy(buffer); buffer = IntPtr.Zero; }
            if (ctx != IntPtr.Zero) { libiio.iio_context_destroy(ctx); ctx = IntPtr.Zero; }
            connected = false;
        }

        private string GetSerial() { if (ctx == IntPtr.Zero) return "Unknown"; IntPtr p = libiio.iio_context_get_attr_value(ctx, "serial"); return p != IntPtr.Zero ? Marshal.PtrToStringAnsi(p) : "Unknown"; }
        private string GetChipModel() { if (phy == IntPtr.Zero) return "Unknown"; double val = 0; int ret = libiio.iio_device_attr_read_double(phy, "model", out val); return ret >= 0 ? val.ToString() : "Unknown"; }

        public bool SetFrequency(double freq) { if (!connected || phy == IntPtr.Zero) return false; int ret = libiio.iio_device_attr_write_double(phy, "RX_LO_FREQ", freq); if (ret >= 0) Log($"Frequency: {freq/1e6:F1} MHz"); return ret >= 0; }
        public bool SetSampleRate(double rate) { if (!connected || phy == IntPtr.Zero) return false; sampleRate = rate; int ret = libiio.iio_device_attr_write_double(phy, "RX_SAMPLING_FREQ", rate); if (ret >= 0) libiio.iio_device_attr_write_double(rx, "RX_RF_BANDWIDTH", rate); return ret >= 0; }
        public bool SetGain(double gain) { if (!connected || phy == IntPtr.Zero) return false; return libiio.iio_device_attr_write_double(phy, "RX_GAIN", gain) >= 0; }
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

        public bool IsConnected => connected;
        public double SampleRate => sampleRate;
        private void Log(string msg) { OnStatusUpdate?.Invoke(msg); }
        public void Dispose() { Disconnect(); }
    }

    // 6. VideoDecoder
    public class VideoDecoder : IDisposable
    {
        private VideoWriter writer;
        private bool isRecording;
        public VideoDecoder() { isRecording = false; }
        public void StartRecording(string path, int fps, int width, int height) { try { writer = new VideoWriter(path, FourCC.Default, fps, new OpenCvSharp.Size(width, height)); isRecording = true; } catch { } }
        public void StopRecording() { if (writer != null && writer.IsOpened()) { writer.Release(); writer.Dispose(); writer = null; } isRecording = false; }
        public bool IsRecording => isRecording;
        public bool DecodeFrame(float[] iqData, out Mat frame)
        {
            frame = null;
            try
            {
                if (iqData == null || iqData.Length < 100) return false;
                int width = 320, height = 240;
                frame = new Mat(height, width, MatType.CV_8UC3);
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
                if (isRecording && writer != null && writer.IsOpened()) writer.Write(frame);
                return true;
            }
            catch { return false; }
        }
        public Bitmap MatToBitmap(Mat mat)
        {
            if (mat == null || mat.Empty()) return null;
            try
            {
                int width = mat.Width, height = mat.Height;
                Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                IntPtr ptr = bmpData.Scan0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Vec3b pixel = mat.Get<Vec3b>(y, x);
                        Marshal.WriteByte(ptr, (y * width + x) * 3, pixel.Item2);
                        Marshal.WriteByte(ptr, (y * width + x) * 3 + 1, pixel.Item1);
                        Marshal.WriteByte(ptr, (y * width + x) * 3 + 2, pixel.Item0);
                    }
                }
                bmp.UnlockBits(bmpData);
                return bmp;
            }
            catch { return null; }
        }
        public void Dispose() { StopRecording(); }
    }

    // 7. MainForm
    public class MainForm : Form
    {
        private PlutoSDR pluto = new PlutoSDR();
        private Database db;
        private List<SignalInfo> signals = new List<SignalInfo>();
        private System.Windows.Forms.Timer scanTimer;
        private Label statusLabel;

        public MainForm()
        {
            Text = "FPV HUNTER PRO v8.0";
            Size = new Size(1024, 768);
            BackColor = Color.FromArgb(10, 10, 30);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;

            db = new Database();
            pluto.OnStatusUpdate += (msg) => UpdateStatus(msg);
            pluto.Connect();

            var label = new Label
            {
                Text = "FPV Hunter Pro v8.0\nReady",
                Font = new Font("Segoe UI", 24),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(label);

            statusLabel = new Label
            {
                Text = "Status: Ready",
                Dock = DockStyle.Bottom,
                ForeColor = Color.LightGray,
                Height = 30
            };
            Controls.Add(statusLabel);

            scanTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            scanTimer.Tick += (s, e) =>
            {
                if (pluto.IsConnected)
                {
                    var samples = pluto.ReceiveSamples(256);
                    if (samples != null)
                    {
                        double power = 10 * Math.Log10(samples.Average() + 1e-12);
                        signals.Add(new SignalInfo
                        {
                            Frequency = 100e6,
                            Power = power,
                            Type = "Signal",
                            FirstSeen = DateTime.Now,
                            LastSeen = DateTime.Now
                        });
                        UpdateStatus($"Signal: {power:F1} dB");
                    }
                }
            };
            scanTimer.Start();
        }

        private void UpdateStatus(string msg)
        {
            if (statusLabel != null && !statusLabel.IsDisposed)
            {
                statusLabel.Text = "Status: " + msg;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            scanTimer?.Stop();
            pluto?.Dispose();
            base.OnFormClosing(e);
        }
    }

    // 8. Program
    public class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\n{ex.StackTrace}", "FPV Hunter Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                File.WriteAllText("fatal_error.log", $"{DateTime.Now}: {ex}");
            }
        }
    }
}
