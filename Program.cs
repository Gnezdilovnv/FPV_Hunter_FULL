
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
