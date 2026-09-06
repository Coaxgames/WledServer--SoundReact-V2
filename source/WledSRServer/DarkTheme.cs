namespace WledSRServer
{
    internal static class DarkTheme
    {
        public static readonly Color BackColor = Color.FromArgb(32, 32, 32);
        public static readonly Color PanelColor = Color.FromArgb(45, 45, 48);
        public static readonly Color InputColor = Color.FromArgb(55, 55, 58);
        public static readonly Color ForeColor = Color.FromArgb(235, 235, 235);
        public static readonly Color BorderColor = Color.FromArgb(90, 90, 94);

        public static void Apply(Control control)
        {
            control.BackColor = control is GroupBox or Panel ? PanelColor : BackColor;
            control.ForeColor = ForeColor;

            if (control is TextBox or ComboBox)
                control.BackColor = InputColor;

            foreach (Control child in control.Controls)
                Apply(child);
        }
    }
}
