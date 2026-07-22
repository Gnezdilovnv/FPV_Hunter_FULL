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

namespace FPV_Hunter_FULL
{
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

    public class VoiceAnnouncer
    {
        private SpeechSynthesizer synth;
        private bool enabled = true;
        public VoiceAnnouncer() { try { synth = new SpeechSynthesizer(); synth.Rate = 0; synth.Volume = 100; } catch { enabled = false; } }
        public void Say(string text) { if (!enabled || synth == null) return; try { synth.SpeakAsync(text); } catch { } }
        public void SetEnabled(bool enable) { enabled = enable; }
        public bool IsEnabled => enabled;
    }

    public class Database
    {
        private string logFile;
        private List<SignalInfo> history = new List<SignalInfo>();
        private object lockObj = new object();

        public Database(string path = null)
        {
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

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
                        var lines = File.ReadAllLines(logFile);
                        foreach (var line in lines)
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
                                        FirstSeen = DateTime.Parse(parts[8]),
                                        LastSeen = DateTime.Parse(parts[8])
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

                    // Сохраняем в файл
                    var line = $"{freq}|{power}|{type}|{modulation}|{standard}|{bandwidth}|{(hasVideo ? 1 : 0)}|{details}|{DateTime.Now}";
                    File.AppendAllText(logFile, line + Environment.NewLine);
                }
                catch { }
            }
        }

        public List<SignalInfo> GetHistory(int limit = 100)
        {
            lock (lockObj)
            {
                return history.OrderByDescending(x => x.FirstSeen).Take(limit).ToList();
            }
        }

        public void ClearHistory()
        {
            lock (lockObj)
            {
                history.Clear();
                try { File.Delete(logFile); } catch { }
            }
        }
    }

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
        public double Frequency => frequency;
        private void Log(string msg) { OnStatusUpdate?.Invoke(msg); }
        public void Dispose() { Disconnect(); }
    }

    public class MainForm : Form
    {
        private PlutoSDR pluto = new PlutoSDR();
        private Database db;
        private List<SignalInfo> signals = new List<SignalInfo>();
        private System.Windows.Forms.Timer scanTimer;
        private Label statusLabel;
        private Label signalCountLabel;
        private ListBox signalList;
        private Random rand = new Random();

        public MainForm()
        {
            Text = "FPV HUNTER PRO v8.0";
            Size = new Size(1024, 768);
            BackColor = Color.FromArgb(10, 10, 30);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;

            db = new Database();
            pluto.OnStatusUpdate += (msg) => UpdateStatus(msg);
            
            // Пытаемся подключиться к Pluto
            if (!pluto.Connect("192.168.2.1"))
            {
                UpdateStatus("Pluto+ не найден! Демо-режим.");
            }

            InitUI();
            InitTimers();
        }

        private void InitUI()
        {
            // Заголовок
            var title = new Label
            {
                Text = "FPV HUNTER PRO v8.0",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = Color.FromArgb(230, 126, 34),
                Location = new Point(20, 20),
                Size = new Size(500, 50)
            };
            Controls.Add(title);

            // Статус
            statusLabel = new Label
            {
                Text = "Status: Ready",
                Font = new Font("Segoe UI", 12),
                ForeColor = Color.LightGray,
                Location = new Point(20, 80),
                Size = new Size(600, 30)
            };
            Controls.Add(statusLabel);

            // Список сигналов
            signalList = new ListBox
            {
                Location = new Point(20, 120),
                Size = new Size(400, 400),
                BackColor = Color.FromArgb(20, 20, 40),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10)
            };
            Controls.Add(signalList);

            // Счетчик сигналов
            signalCountLabel = new Label
            {
                Text = "Signals: 0",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                Location = new Point(20, 530),
                Size = new Size(200, 25)
            };
            Controls.Add(signalCountLabel);

            // Кнопка очистки
            var clearBtn = new Button
            {
                Text = "Clear",
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(300, 530),
                Size = new Size(100, 30)
            };
            clearBtn.Click += (s, e) => { signals.Clear(); UpdateSignalList(); };
            Controls.Add(clearBtn);

            // Кнопка выхода
            var exitBtn = new Button
            {
                Text = "Exit",
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(50, 30, 30),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(410, 530),
                Size = new Size(100, 30)
            };
            exitBtn.Click += (s, e) => Application.Exit();
            Controls.Add(exitBtn);
        }

        private void InitTimers()
        {
            scanTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            scanTimer.Tick += (s, e) =>
            {
                if (pluto.IsConnected)
                {
                    var samples = pluto.ReceiveSamples(256);
                    if (samples != null && samples.Length > 0)
                    {
                        double power = 10 * Math.Log10(samples.Average() + 1e-12);
                        double rssi = pluto.GetRSSI();
                        double freq = pluto.Frequency;

                        // Детекция сигнала
                        if (power > -40)
                        {
                            string type = "Unknown";
                            bool hasVideo = false;
                            
                            if (freq >= 5700e6 && freq <= 5900e6)
                            {
                                type = "FPV Video";
                                hasVideo = true;
                            }
                            else if (freq >= 2400e6 && freq <= 2483e6)
                            {
                                type = "RC Control";
                            }
                            else if (freq >= 900e6 && freq <= 930e6)
                            {
                                type = "RC 900MHz";
                            }

                            var existing = signals.FirstOrDefault(x => Math.Abs(x.Frequency - freq) < 1e6);
                            if (existing != null)
                            {
                                existing.Power = power;
                                existing.LastSeen = DateTime.Now;
                                existing.Count++;
                            }
                            else
                            {
                                var signal = new SignalInfo
                                {
                                    Frequency = freq,
                                    Power = power,
                                    Type = type,
                                    HasVideo = hasVideo,
                                    Modulation = "FM",
                                    Standard = "PAL",
                                    FirstSeen = DateTime.Now,
                                    LastSeen = DateTime.Now,
                                    Count = 1,
                                    Details = $"RSSI: {rssi:F1} dB"
                                };
                                signals.Add(signal);
                                db.AddIntercept(freq, power, type, "FM", "PAL", 4e6, hasVideo, signal.Details);
                            }
                            UpdateSignalList();
                        }
                    }
                }
                else
                {
                    // Демо-режим
                    if (rand.Next(10) < 2)
                    {
                        double freq = 100 + rand.Next(5900);
                        double power = -30 - rand.Next(20);
                        string type = freq > 5700 && freq < 5900 ? "FPV Video" : "Unknown";
                        bool hasVideo = freq > 5700 && freq < 5900;

                        var signal = new SignalInfo
                        {
                            Frequency = freq * 1e6,
                            Power = power,
                            Type = type,
                            HasVideo = hasVideo,
                            Modulation = "FM",
                            Standard = "PAL",
                            FirstSeen = DateTime.Now,
                            LastSeen = DateTime.Now,
                            Count = 1
                        };
                        signals.Add(signal);
                        db.AddIntercept(freq * 1e6, power, type, "FM", "PAL", 4e6, hasVideo, "Demo mode");
                        UpdateSignalList();
                        UpdateStatus($"Signal found at {freq:F0} MHz, {power:F1} dB");
                    }
                }

                UpdateSignalCount();
            };
            scanTimer.Start();
        }

        private void UpdateSignalList()
        {
            signalList.Items.Clear();
            foreach (var s in signals.OrderBy(x => x.Frequency))
            {
                string icon = s.HasVideo ? "📹" : "📡";
                string text = $"{icon} {(s.Frequency/1e6):F1} MHz | {s.Type} | {s.Power:F1} dB";
                signalList.Items.Add(text);
            }
        }

        private void UpdateSignalCount()
        {
            signalCountLabel.Text = $"Signals: {signals.Count}";
        }

        private void UpdateStatus(string text)
        {
            if (statusLabel != null && !statusLabel.IsDisposed)
            {
                statusLabel.Text = "Status: " + text;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            scanTimer?.Stop();
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
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}\n\n{ex.StackTrace}", "FPV Hunter Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                File.WriteAllText("error.log", $"{DateTime.Now}: {ex}");
            }
        }
    }
}
