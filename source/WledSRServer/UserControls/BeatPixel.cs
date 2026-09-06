using WledSRServer.Audio;

namespace WledSRServer.UserControls
{
    public partial class BeatPixel : UserControl
    {
        public BeatPixel()
        {
            InitializeComponent();
            SetupRedrawOnNewPacket();
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
            };
        }

        private const int innerBorder = 1;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(DarkTheme.InputColor);
            using var border = new Pen(DarkTheme.BorderColor);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

            var pixelRect = new Rectangle(1 + innerBorder, 1 + innerBorder, Width - 2 - innerBorder * 2, Height - 2 - innerBorder * 2);

            if (DesignMode)
            {
                e.Graphics.FillRectangle(Brushes.LightGray, pixelRect);
                return;
            }

            var Beat = Program.ServerContext.Packet.SamplePeak > 0;
            if (Beat)
                e.Graphics.FillRectangle(Brushes.LightGray, pixelRect);
        }
    }
}
