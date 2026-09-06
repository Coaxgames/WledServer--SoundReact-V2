using WledSRServer.Audio;

namespace WledSRServer
{
    public partial class FFTBucketDisplay : UserControl
    {
        public FFTBucketDisplay()
        {
            InitializeComponent();

            SetupRedrawOnNewPacket();

            RecalculateRectangles();
            this.Resize += (s, e) => RecalculateRectangles();

            this.MouseMove += FFTDisplay_MouseMove;
            this.MouseDown += FFTDisplay_MouseDown;
            this.MouseUp += FFTDisplay_MouseUp;
            this.MouseWheel += FFTDisplay_MouseWheel;
            this.MouseEnter += (s, e) => Focus();

            _saveTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                AudioCaptureManager.SaveFFTBandGains();
            };
        }

        private void SetupRedrawOnNewPacket()
        {
            var cancelUpdate = new CancellationTokenSource();

            var packetUpdated = new AudioCaptureManager.PacketUpdatedHandler(Invalidate);
            AudioCaptureManager.PacketUpdated += packetUpdated;

            Disposed += (s, e) =>
            {
                AudioCaptureManager.PacketUpdated -= packetUpdated;
                cancelUpdate.Cancel();
                _saveTimer.Stop();
                AudioCaptureManager.SaveFFTBandGains();
                _saveTimer.Dispose();
                foreach (var brush in _barColor)
                    brush.Dispose();
                foreach (var brush in _originalBarColor)
                    brush.Dispose();
                _barBG.Dispose();
                _barBorder.Dispose();
            };
        }

        private void FFTDisplay_MouseMove(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SetBandGain(e.Location);
                return;
            }

            var mouseX = e.Location.X;
            var undexRextIdx = _rectanglesFull.Select((r, idx) => new { x0 = r.X, x1 = r.X + r.Width, idx }).FirstOrDefault(r => r.x0 <= mouseX && r.x1 >= mouseX)?.idx;
            toolTip1.SetToolTip(this, (undexRextIdx == null || AudioCaptureManager.FFTfreqBands == null) ? null : AudioCaptureManager.FFTfreqBands[undexRextIdx.Value]);
        }

        private void FFTDisplay_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Capture = true;
                SetBandGain(e.Location);
            }
        }

        private void FFTDisplay_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Capture = false;
                SaveBandGainsSoon();
            }
        }

        private void FFTDisplay_MouseWheel(object? sender, MouseEventArgs e)
        {
            var band = GetBandIndex(e.Location.X);
            if (band < 0 || band >= AudioCaptureManager.FFTBandGains.Length)
                return;

            var direction = e.Delta > 0 ? 1 : -1;
            var increment = (ModifierKeys & Keys.Shift) != 0 ? 2 : 1;
            var notchCount = AudioCaptureManager.FFTBandNotches;

            if (notchCount == 0)
            {
                var gainPercent = AudioCaptureManager.FFTBandGains[band] * 50f;
                gainPercent = Math.Clamp(gainPercent + direction * (increment == 2 ? 5 : 1), 0, 100);
                AudioCaptureManager.FFTBandGains[band] = gainPercent / 50f;
            }
            else
            {
                var notch = (int)Math.Round(AudioCaptureManager.FFTBandGains[band] * notchCount / 2f);
                notch = Math.Clamp(notch + direction * increment, 0, notchCount);
                AudioCaptureManager.FFTBandGains[band] = notch * 2f / notchCount;
            }

            SaveBandGainsSoon();
            Invalidate();
        }

        private void SaveBandGainsSoon()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SetBandGain(Point location)
        {
            var band = GetBandIndex(location.X);
            if (band < 0 || band >= AudioCaptureManager.FFTBandGains.Length)
                return;

            var sliderHeight = Math.Max(1, _rectanglesFull[band].Height * 0.2f);
            var gain = 2f * (sliderHeight - Math.Clamp(location.Y - (_rectanglesFull[band].Height - sliderHeight), 0, sliderHeight)) / sliderHeight;
            var notchCount = AudioCaptureManager.FFTBandNotches;
            if (notchCount > 0)
            {
                var notch = (int)Math.Round(gain * notchCount / 2f);
                gain = notch * 2f / notchCount;
            }
            AudioCaptureManager.FFTBandGains[band] = gain;
            Invalidate();
        }

        private int GetBandIndex(float mouseX)
        {
            return _rectanglesFull.Select((r, index) => new { r, index })
                .FirstOrDefault(item => item.r.Left <= mouseX && item.r.Right >= mouseX)?.index ?? -1;
        }

        private const int PADDING = 4;
        private RectangleF[] _rectanglesFull;
        private RectangleF[] _rectanglesBar;
        private Brush[] _barColor;
        private Brush[] _originalBarColor;
        private Brush _barBG;
        private Pen _barBorder;
        private System.Windows.Forms.Timer _saveTimer;

        private void RecalculateRectangles()
        {
            if (_barColor != null)
                foreach (var brush in _barColor)
                    brush.Dispose();
            if (_originalBarColor != null)
                foreach (var brush in _originalBarColor)
                    brush.Dispose();
            _barBG?.Dispose();
            _barBorder?.Dispose();

            var barCount = Program.ServerContext.Packet.FFT_Bins.Length;
            _rectanglesFull = new RectangleF[barCount];
            _rectanglesBar = new RectangleF[barCount];
            var width = (float)(this.Width - 1 + PADDING) / barCount - PADDING;
            var fullHeight = (float)this.Height - 1;

            for (int i = 0; i < barCount; i++)
            {
                _rectanglesFull[i] = new RectangleF(i * (width + PADDING), 0, width, fullHeight);
                _rectanglesBar[i] = new RectangleF(i * (width + PADDING), 0, width, 0);
            }

            _barColor = new Brush[barCount];
            _originalBarColor = new Brush[barCount];
            for (int i = 0; i < barCount; i++)
            {
                _barColor[i] = new SolidBrush(hsv2rgb(i / 15f * 0.85f, 1f, 1f));
                _originalBarColor[i] = new SolidBrush(ControlPaint.Dark(((SolidBrush)_barColor[i]).Color, 0.45f));
            }

            _barBG = new SolidBrush(DarkTheme.InputColor);
            _barBorder = new Pen(DarkTheme.BorderColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var fftBytes = Program.ServerContext.Packet.FFT_Bins;

            if (DesignMode)
                new Random().NextBytes(fftBytes);

            var fullHeight = _rectanglesFull[0].Height;
            for (int i = 0; i < fftBytes.Length; i++)
            {
                var barHeight = fullHeight * fftBytes[i] / 255;
                _rectanglesBar[i].Y = fullHeight - barHeight;
                _rectanglesBar[i].Height = barHeight;
            }

            e.Graphics.FillRectangles(_barBG, _rectanglesFull);     // bg

            for (int i = 0; i < fftBytes.Length; i++)
                e.Graphics.FillRectangle(_barColor[i], _rectanglesBar[i]); // bar

            var originalBarWidth = Math.Min(8f, _rectanglesFull[0].Width);
            for (var i = 0; i < fftBytes.Length; i++)
            {
                var originalBarHeight = fullHeight * AudioCaptureManager.FFTBandOriginalValues[i] / 255;
                var originalBar = new RectangleF(
                    _rectanglesFull[i].Right - originalBarWidth,
                    fullHeight - originalBarHeight,
                    originalBarWidth,
                    originalBarHeight);
                e.Graphics.FillRectangle(_originalBarColor[i], originalBar);
            }

            var sliderHeight = fullHeight * 0.2f;
            for (var i = 0; i < fftBytes.Length; i++)
            {
                var gain = AudioCaptureManager.FFTBandGains[i];
                var slider = new RectangleF(_rectanglesFull[i].X, fullHeight - sliderHeight, _rectanglesFull[i].Width, sliderHeight);
                var thumbY = slider.Bottom - slider.Height * gain / 2;
                e.Graphics.FillRectangle(Brushes.DimGray, slider);
                for (var notch = 0; notch <= AudioCaptureManager.FFTBandNotches; notch++)
                {
                    var notchY = slider.Bottom - slider.Height * notch / Math.Max(1, AudioCaptureManager.FFTBandNotches);
                    e.Graphics.DrawLine(Pens.LightGray, slider.Left, notchY, slider.Right, notchY);
                }
                e.Graphics.FillRectangle(Brushes.White, new RectangleF(slider.X, thumbY - 1, slider.Width, 2));
            }

            e.Graphics.DrawRectangles(_barBorder, _rectanglesFull);   // border
        }

        private Color hsv2rgb(float h, float s, float v)
        {
            Func<float, int> f = delegate (float n)
            {
                float k = (n + h * 6) % 6;
                return (int)((v - (v * s * (Math.Max(0, Math.Min(Math.Min(k, 4 - k), 1))))) * 255);
            };
            return Color.FromArgb(f(5), f(3), f(1));
        }
    }
}
