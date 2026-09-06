using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using WledSRServer.Audio;
using WledSRServer.Audio.AudioProcessor.FFTBuckets;
using WledSRServer.Properties;

namespace WledSRServer
{
    public partial class MainForm : Form
    {
        private Stopwatch PPSwatch = new Stopwatch();

        public MainForm()
        {
            InitializeComponent();
            DarkTheme.Apply(this);

            if (!DesignMode)
                this.Icon = Properties.Resources.NotifIcon;

            // Console.WriteLine("===[ packet preview ]======================================================");
            // Console.WriteLine($"sampleRaw  : {packet.sampleRaw,-20:F45}");
            // Console.WriteLine($"sampleSmth : {packet.sampleSmth,-20:F45}");
            // Console.WriteLine($"samplePeak : {packet.samplePeak,-20}");

            // Console.WriteLine($"FFT_Magnitude : {packet.FFT_Magnitude,-20:F32}");
            // Console.WriteLine($"FFT_MajorPeak : {packet.FFT_MajorPeak,10:F4} (hz)                  ");

            // V=FreqToDisplay, V0=FreqLow, V1=FreqHigh, X0-X1 control width
            // X = X0 + (X1 - X0)(log(V) - log(V0))/(log(V1) - log(V0))

            Text = $"WLED SoundReactive Server - {Program.Version(false)}";

            var settings = Properties.Settings.Default;
            AudioCaptureManager.LoadFFTBandGains();
            txtFFTBandNotches.Text = settings.FFTBandNotches is >= 0 and <= 20 ? settings.FFTBandNotches.ToString() : "10";
            FFTBandNotches_TextChanged(null, EventArgs.Empty);
            txtFFTBandNotches.KeyPress += txtFFTBandNotches_KeyPress;
            txtFFTBandNotches.TextChanged += FFTBandNotches_TextChanged;
            LoadProfileNames();
            ddlProfiles.SelectedIndexChanged += ddlProfiles_SelectedIndexChanged;
            btnSaveProfile.Click += btnSaveProfile_Click;

            btnSetAutoRun.CheckboxChecked = AdminFunctions.GetAutoRun();
            btnSetStartupGUI.CheckboxChecked = settings.StartWithoutGUI;

            #region Audio devices

            ddlAudioDevices.DataSource = AudioCaptureManager.GetDevices();
            ddlAudioDevices.DisplayMember = nameof(AudioCaptureManager.SimpleDeviceDescriptor.Name);
            ddlAudioDevices.ValueMember = nameof(AudioCaptureManager.SimpleDeviceDescriptor.ID);
            ddlAudioDevices.SelectedValue = settings.AudioCaptureDeviceId;
            ddlAudioDevices.SelectedIndexChanged += ddlAudioDevices_Changed;

            #endregion

            #region FFT and Scaling

            ddlValueScale.ValueMember = "Key";
            ddlValueScale.DisplayMember = "Value";
            ddlValueScale.DataSource = (new Dictionary<Bucketizer.Scale, string> {
                { Bucketizer.Scale.Linear,      "Linear (Amplitude)" },
                { Bucketizer.Scale.SquareRoot,  "Square Root (Energy)" },
                { Bucketizer.Scale.Logarithmic, "Logarithmic (Loudness)" }
            }).ToList();
            ddlValueScale.SelectedValue = Bucketizer.ScaleFromString(settings.FFTValueScale, Bucketizer.Scale.SquareRoot);
            ddlValueScale.SelectedIndexChanged += ddlValueScale_Changed;

            chbFFTLogFreq.Checked = settings.FFTFreqLogScale;
            chbFFTLogFreq.CheckedChanged += ChbFFTLogFreq_Changed;

            #region Gain control

            chbAutoGainControl.Checked = !settings.ManualGain;
            chbAutoGainControl.CheckedChanged += chbAutoGainControl_Changed;

            tbAutoGainLower.Value = Math.Clamp(settings.AutoGainLowerLimit, 0, 255);
            tbAutoGainUpper.Value = Math.Clamp(settings.AutoGainUpperLimit, 0, 255);
            tbAutoGainLower.ValueChanged += AutoGainLimit_ValueChanged;
            tbAutoGainUpper.ValueChanged += AutoGainLimit_ValueChanged;
            UpdateAutoGainLimitControls();

            tbGainValue.Enabled = !chbAutoGainControl.Checked;
            tbGainValue.Value = settings.ManualGainReference;
            tbGainValue.ValueChanged += tbGainValue_ValueChanged;

            #endregion

            #endregion

            #region Advanced Network Settings

            txtUdpPort.Text = settings.WledUdpMulticastPort.ToString();
            txtUdpPort.AutoCompleteCustomSource.Add("11988"); // the default one
            txtUdpPort.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            txtUdpPort.AutoCompleteSource = AutoCompleteSource.CustomSource;
            txtUdpPort.TextChanged += txtUdpPort_TextChanged;

            txtLocalIpAddress.AutoCompleteCustomSource.AddRange(NetworkManager.GetLocalIPAddresses());
            txtLocalIpAddress.AutoCompleteMode = AutoCompleteMode.Suggest;
            txtLocalIpAddress.AutoCompleteSource = AutoCompleteSource.CustomSource;
            txtLocalIpAddress.Text = settings.LocalIPToBind;
            txtLocalIpAddress.TextChanged += txtLocalIpAddress_Changed;

            txtFFTLower.Text = settings.FFTLow.ToString();
            txtFFTLower.TextChanged += txtFFTLower_TextChanged;
            txtFFTUpper.Text = settings.FFTHigh.ToString();
            txtFFTUpper.TextChanged += txtFFTUpper_TextChanged;

            cbSendMode.Items.Clear();
            //cbSendMode.Items.Add("Broadcast LAN (default)");

            cbSendMode.DataSource = Enum.GetValues<NetworkManager.SendMode>().Select(e => new { val = (int)e, text = e.GetType().GetMember(e.ToString()).First().GetCustomAttributes<DisplayAttribute>().FirstOrDefault()?.Name ?? e.ToString() }).ToList();
            cbSendMode.DisplayMember = "text";
            cbSendMode.ValueMember = "val";
            cbSendMode.SelectedValue = settings.NetworkSendMode;
            cbSendMode.SelectedIndexChanged += cbSendMode_Changed;
            cbSendMode_Changed(null, null);

            txtRelevantIP.TextChanged += txtRelevantIP_TextChanged;

            #endregion

            toolTip1.InitialDelay = 100;

            pnlSettings.MinimumSize = new Size(pnlSettingsMain.Width, pnlSettingsMain.Height);
            pnlSettings.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pnlSettings.AutoSize = true;

            Program.ServerContext.PacketCounter = 0;
            PPSwatch.Start();
            tmrUpdateStats.Enabled = true;

            beatPixel1.DoubleClick += (s, e) => new InsidesForm().Show();
        }

        #region Periodic stats update

        private void tmrUpdateStats_Tick(object sender, EventArgs e)
        {
            if (PPSwatch.ElapsedMilliseconds > 500)
            {
                var pps = 0;
                if (Program.ServerContext.PacketCounter > 0)
                {
                    pps = (int)(Program.ServerContext.PacketCounter * 1000 / PPSwatch.ElapsedMilliseconds);
                    Program.ServerContext.PacketCounter = 0;
                    PPSwatch.Restart();
                }
                lblPPS.Text = $"Packet per second : {pps:D}";
            }

            if (Program.ServerContext.PacketSendingStatus == PacketSendingStatus.Error)
            {
                lblPPS.ForeColor = Color.Red;
                SetToolTip(lblPPS, $"There is some problem sending out the packages.\nError: {Program.ServerContext.PacketSendErrorMessage}");
            }
            else
            {
                lblPPS.ForeColor = Color.FromKnownColor(KnownColor.ControlText);
                SetToolTip(lblPPS, null);
            }

            switch (Program.ServerContext.AudioCaptureStatus)
            {
                case AudioCaptureStatus.unknown:
                    lblCapturing.BackColor = Color.FromKnownColor(KnownColor.Control);
                    SetToolTip(lblCapturing, "Audio capture is not initialized yet.");
                    break;
                case AudioCaptureStatus.Capturing_Sound:
                    lblCapturing.BackColor = Color.LightGreen;
                    SetToolTip(lblCapturing, "Capturing sound.");
                    break;
                case AudioCaptureStatus.Capturing_Silence:
                    lblCapturing.BackColor = Color.Gold;
                    SetToolTip(lblCapturing, "Capturing silence.");
                    break;
                case AudioCaptureStatus.Error:
                    lblCapturing.BackColor = Color.Tomato;
                    SetToolTip(lblCapturing, $"There are some error during capturing. Try to change the audio input.\nError: {Program.ServerContext.AudioCaptureErrorMessage}");
                    break;
            }
        }

        #endregion

        private void btnSettings_Click(object sender, EventArgs e)
        {
            pnlSettings.Visible = !pnlSettings.Visible;
        }

        internal void ShowSettings(bool showAdvancedNetworkSettings = false)
        {
            txtLocalIpAddress_Changed(null, null); //re-check
            pnlSettings.Visible = true;
            if (showAdvancedNetworkSettings) pnlSettings.Visible = true;
        }

        private void SetToolTip(Control control, string? toolTip)
        {
            toolTip1.SetToolTip(control, toolTip);
        }

        #region Close or Exit app

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Program.GuiContext.FormClosed(e.CloseReason);
        }

        private void btnExitApplication_Click(object sender, EventArgs e)
        {
            Program.GuiContext.ExitApp();
        }

        #endregion

        private void btnSetAutoRun_Click(object sender, EventArgs e)
        {
            var newState = !AdminFunctions.GetAutoRun();
            if (!AdminFunctions.SetAutoRun(newState))
                MessageBox.Show("Cannot set the AutoStart value", Program.MboxTitle);
            btnSetAutoRun.CheckboxChecked = AdminFunctions.GetAutoRun();
        }

        private void btnSetStartupGUI_Click(object sender, EventArgs e)
        {
            var newValue = !Properties.Settings.Default.StartWithoutGUI;
            Properties.Settings.Default.StartWithoutGUI = newValue;
            Properties.Settings.Default.Save();
            btnSetStartupGUI.CheckboxChecked = newValue;
        }

        private void FFTBandNotches_TextChanged(object? sender, EventArgs e)
        {
            if (!int.TryParse(txtFFTBandNotches.Text, out var notches) || notches < 0 || notches > 20)
            {
                AudioCaptureManager.FFTBandNotches = 10;
                txtFFTBandNotches.BackColor = Color.Salmon;
                SetToolTip(txtFFTBandNotches, "Enter an integer from 0 to 20");
                return;
            }

            AudioCaptureManager.FFTBandNotches = notches;
            AudioCaptureManager.NormalizeFFTBandGains();
            Properties.Settings.Default.FFTBandNotches = notches;
            AudioCaptureManager.SaveFFTBandGains();
            Properties.Settings.Default.Save();
            txtFFTBandNotches.BackColor = DarkTheme.InputColor;
            SetToolTip(txtFFTBandNotches, "Number of slider steps (0 to 20; 0 is free movement)");
            fftDisplay2.Invalidate();
        }

        private void txtFFTBandNotches_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                e.Handled = true;
        }

        private sealed class AudioProfile
        {
            public int FFTLow { get; set; }
            public int FFTHigh { get; set; }
            public string FFTValueScale { get; set; } = string.Empty;
            public bool FFTFreqLogScale { get; set; }
            public int FFTBandNotches { get; set; }
            public bool ManualGain { get; set; }
            public int ManualGainReference { get; set; }
            public int AutoGainLowerLimit { get; set; }
            public int AutoGainUpperLimit { get; set; }
            public float[] FFTBandGains { get; set; } = Array.Empty<float>();
        }

        private static string ProfilesDirectory => Path.Combine(AppContext.BaseDirectory, "Profiles");

        private void LoadProfileNames()
        {
            Directory.CreateDirectory(ProfilesDirectory);
            ddlProfiles.Items.Clear();
            ddlProfiles.Items.AddRange(Directory.GetFiles(ProfilesDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name)
                .ToArray());
        }

        private void btnSaveProfile_Click(object? sender, EventArgs e)
        {
            var profileName = ddlProfiles.Text.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
                return;

            if (profileName.Any(Path.GetInvalidFileNameChars().Contains))
            {
                SetToolTip(ddlProfiles, "Profile name contains invalid characters");
                return;
            }

            var profile = new AudioProfile
            {
                FFTLow = Properties.Settings.Default.FFTLow,
                FFTHigh = Properties.Settings.Default.FFTHigh,
                FFTValueScale = Properties.Settings.Default.FFTValueScale,
                FFTFreqLogScale = Properties.Settings.Default.FFTFreqLogScale,
                FFTBandNotches = AudioCaptureManager.FFTBandNotches,
                ManualGain = Properties.Settings.Default.ManualGain,
                ManualGainReference = Properties.Settings.Default.ManualGainReference,
                AutoGainLowerLimit = Properties.Settings.Default.AutoGainLowerLimit,
                AutoGainUpperLimit = Properties.Settings.Default.AutoGainUpperLimit,
                FFTBandGains = AudioCaptureManager.FFTBandGains.ToArray()
            };
            File.WriteAllText(Path.Combine(ProfilesDirectory, profileName + ".json"), JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            LoadProfileNames();
            ddlProfiles.Text = profileName;
            SetToolTip(ddlProfiles, "Profile saved");
        }

        private void ddlProfiles_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (ddlProfiles.SelectedItem is not string profileName)
                return;

            try
            {
                var profile = JsonSerializer.Deserialize<AudioProfile>(File.ReadAllText(Path.Combine(ProfilesDirectory, profileName + ".json")));
                if (profile == null || profile.FFTBandGains.Length != AudioCaptureManager.FFTBandGains.Length)
                    return;

                Properties.Settings.Default.FFTLow = profile.FFTLow;
                Properties.Settings.Default.FFTHigh = profile.FFTHigh;
                Properties.Settings.Default.FFTValueScale = profile.FFTValueScale;
                Properties.Settings.Default.FFTFreqLogScale = profile.FFTFreqLogScale;
                Properties.Settings.Default.ManualGain = profile.ManualGain;
                Properties.Settings.Default.ManualGainReference = profile.ManualGainReference;
                Properties.Settings.Default.AutoGainLowerLimit = Math.Clamp(profile.AutoGainLowerLimit, 0, 255);
                Properties.Settings.Default.AutoGainUpperLimit = Math.Clamp(profile.AutoGainUpperLimit, Properties.Settings.Default.AutoGainLowerLimit, 255);
                Properties.Settings.Default.FFTBandNotches = Math.Clamp(profile.FFTBandNotches, 0, 20);
                Properties.Settings.Default.Save();

                Array.Copy(profile.FFTBandGains, AudioCaptureManager.FFTBandGains, profile.FFTBandGains.Length);
                AudioCaptureManager.FFTBandNotches = Properties.Settings.Default.FFTBandNotches;
                AudioCaptureManager.NormalizeFFTBandGains();
                AudioCaptureManager.SaveFFTBandGains();

                txtFFTLower.Text = profile.FFTLow.ToString();
                txtFFTUpper.Text = profile.FFTHigh.ToString();
                ddlValueScale.SelectedValue = Bucketizer.ScaleFromString(profile.FFTValueScale, Bucketizer.Scale.SquareRoot);
                chbFFTLogFreq.Checked = profile.FFTFreqLogScale;
                txtFFTBandNotches.Text = AudioCaptureManager.FFTBandNotches.ToString();
                chbAutoGainControl.Checked = !profile.ManualGain;
                tbGainValue.Value = Math.Clamp(profile.ManualGainReference, tbGainValue.Minimum, tbGainValue.Maximum);
                tbAutoGainLower.Value = Properties.Settings.Default.AutoGainLowerLimit;
                tbAutoGainUpper.Value = Properties.Settings.Default.AutoGainUpperLimit;
                AudioCaptureManager.RestartCapture();
                fftDisplay2.Invalidate();
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                SetToolTip(ddlProfiles, "Could not load this profile");
            }
        }

        #region Network

        private void txtUdpPort_TextChanged(object sender, EventArgs e)
        {
            if (!int.TryParse(txtUdpPort.Text, out var udpport) || udpport < 0 || udpport > 65535)
            {
                txtUdpPort.BackColor = Color.Salmon;
                SetToolTip(txtUdpPort, "Not a valid port number");
                return;
            }
            SetToolTip(txtUdpPort, null);
            txtUdpPort.BackColor = DarkTheme.InputColor;

            Properties.Settings.Default.WledUdpMulticastPort = udpport;
            Properties.Settings.Default.Save();
            NetworkManager.ReStart();
        }

        private void txtLocalIpAddress_Changed(object? sender, EventArgs e)
        {
            var newIpAddress = txtLocalIpAddress.Text;
            if (!string.IsNullOrEmpty(newIpAddress))
            {
                if (!IPAddress.TryParse(newIpAddress, out var newAddress))
                {
                    txtLocalIpAddress.BackColor = Color.Salmon;
                    SetToolTip(txtLocalIpAddress, "Not valid IP address.");
                    return;
                }
                if (!NetworkManager.TestLocalIP(newAddress, out var errorMessage))
                {
                    txtLocalIpAddress.BackColor = Color.Salmon;
                    var err = "There is a problem with this IP address.";
                    if (!string.IsNullOrEmpty(errorMessage)) err += $"\nError: {errorMessage}";
                    SetToolTip(txtLocalIpAddress, err);
                    return;
                }
            }
            SetToolTip(txtLocalIpAddress, null);
            txtLocalIpAddress.BackColor = DarkTheme.InputColor;

            Properties.Settings.Default.LocalIPToBind = newIpAddress;
            Properties.Settings.Default.Save();
            NetworkManager.ReStart();
        }

        #endregion

        #region Input device

        private void ddlAudioDevices_Changed(object sender, EventArgs e)
        {
            var selected = ddlAudioDevices.SelectedItem as AudioCaptureManager.SimpleDeviceDescriptor;
            if (selected == null) return;
            Properties.Settings.Default.AudioCaptureDeviceId = selected.ID;
            Properties.Settings.Default.Save();
            AudioCaptureManager.RestartCapture();
        }

        #endregion

        #region FFT and Scaling

        private void txtFFTLower_TextChanged(object sender, EventArgs e)
        {
            if (!int.TryParse(txtFFTLower.Text, out var newValue) || newValue < 1 || newValue >= Properties.Settings.Default.FFTHigh)
            {
                txtFFTLower.BackColor = Color.Salmon;
                SetToolTip(txtFFTLower, "Needs to be a number between 1 and the higher end of the range");
                return;
            }
            SetToolTip(txtFFTLower, null);
            txtFFTLower.BackColor = DarkTheme.InputColor;

            Properties.Settings.Default.FFTLow = newValue;
            Properties.Settings.Default.Save();
            AudioCaptureManager.RestartCapture();
        }

        private void txtFFTUpper_TextChanged(object sender, EventArgs e)
        {
            if (!int.TryParse(txtFFTUpper.Text, out var newValue) || newValue > 99999 || newValue <= Properties.Settings.Default.FFTLow)
            {
                txtFFTUpper.BackColor = Color.Salmon;
                SetToolTip(txtFFTUpper, "Needs to be a number between the lower end of the range and 99999");
                return;
            }
            SetToolTip(txtFFTUpper, null);
            txtFFTUpper.BackColor = DarkTheme.InputColor;

            Properties.Settings.Default.FFTHigh = newValue;
            Properties.Settings.Default.Save();
            AudioCaptureManager.RestartCapture();
        }

        private void ChbFFTLogFreq_Changed(object? sender, EventArgs e)
        {
            Properties.Settings.Default.FFTFreqLogScale = chbFFTLogFreq.Checked;
            Properties.Settings.Default.Save();
            AudioCaptureManager.RestartCapture();
        }

        private void ddlValueScale_Changed(object? sender, EventArgs e)
        {
            if (ddlValueScale.SelectedValue == null) return;
            var selected = (Bucketizer.Scale)ddlValueScale.SelectedValue;
            Properties.Settings.Default.FFTValueScale = selected.ToString();
            Properties.Settings.Default.Save();
            AudioCaptureManager.RestartCapture();
        }

        #region Gain settings

        private void btnGainSettings_Click(object sender, EventArgs e)
        {
            gbGainControl.Visible = !gbGainControl.Visible;
        }

        private void chbAutoGainControl_Changed(object? sender, EventArgs e)
        {
            tbGainValue.Enabled = !chbAutoGainControl.Checked;
            UpdateAutoGainLimitControls();
            Properties.Settings.Default.ManualGain = !chbAutoGainControl.Checked;
            Properties.Settings.Default.Save();
            AudioCaptureManager.RestartCapture();
        }

        private void AutoGainLimit_ValueChanged(object? sender, EventArgs e)
        {
            if (tbAutoGainLower.Value > tbAutoGainUpper.Value)
            {
                if (sender == tbAutoGainLower)
                    tbAutoGainUpper.Value = tbAutoGainLower.Value;
                else
                    tbAutoGainLower.Value = tbAutoGainUpper.Value;
            }

            Properties.Settings.Default.AutoGainLowerLimit = tbAutoGainLower.Value;
            Properties.Settings.Default.AutoGainUpperLimit = tbAutoGainUpper.Value;
            Properties.Settings.Default.Save();
        }

        private void UpdateAutoGainLimitControls()
        {
            var enabled = chbAutoGainControl.Checked;
            tbAutoGainLower.Enabled = enabled;
            tbAutoGainUpper.Enabled = enabled;
        }

        private void tbGainValue_ValueChanged(object? sender, EventArgs e)
        {
            Properties.Settings.Default.ManualGainReference = tbGainValue.Value;
            Properties.Settings.Default.Save();
            AudioCaptureManager.RestartCapture();
        }

        #endregion

        #endregion

        #region Advanced Network Settings

        private void btnAdvancedNetwork_Click(object sender, EventArgs e)
        {
            gbAdvancedNetwork.Visible = !gbAdvancedNetwork.Visible;
        }

        private bool cbSendMode_change = false;
        private void cbSendMode_Changed(object sender, EventArgs e)
        {
            var newValue = (int)(cbSendMode.SelectedValue ?? 0);
            Properties.Settings.Default.NetworkSendMode = newValue;
            Properties.Settings.Default.Save();
            NetworkManager.ReStart();

            cbSendMode_change = true;

            switch ((NetworkManager.SendMode)newValue)
            {
                case NetworkManager.SendMode.BroadcastLAN:
                    lblRelevantIP.Text = "Broadcast IP";
                    txtRelevantIP.Text = "255.255.255.255";
                    txtRelevantIP.Enabled = false;
                    break;
                case NetworkManager.SendMode.BroadcastSubNet:
                    lblRelevantIP.Text = "Broadcast IP";
                    txtRelevantIP.Text = Settings.Default.NetworkBroadcastIPList;
                    txtRelevantIP.Enabled = true;
                    break;
                case NetworkManager.SendMode.Multicast:
                    lblRelevantIP.Text = "Multicast IP";
                    txtRelevantIP.Text = "239.0.0.1";
                    txtRelevantIP.Enabled = false;
                    break;
                case NetworkManager.SendMode.TargetIPList:
                    lblRelevantIP.Text = "Target IP list";
                    txtRelevantIP.Text = Settings.Default.NetworkTargetIPList;
                    txtRelevantIP.Enabled = true;
                    break;
            }

            cbSendMode_change = false;
        }

        private void txtRelevantIP_TextChanged(object sender, EventArgs e)
        {
            if (cbSendMode_change) return;

            bool save = false;

            switch ((NetworkManager.SendMode)Settings.Default.NetworkSendMode)
            {
                case NetworkManager.SendMode.BroadcastSubNet:
                    try
                    {
                        NetworkManager.IPAddressList(txtRelevantIP.Text);
                        Settings.Default.NetworkBroadcastIPList = txtRelevantIP.Text;
                        txtRelevantIP.BackColor = DarkTheme.InputColor;
                        save = true;
                    }
                    catch (Exception ex)
                    {
                        txtRelevantIP.BackColor = Color.Salmon;
                        SetToolTip(txtRelevantIP, "There is one or more invalid address in the list");
                    }
                    break;
                case NetworkManager.SendMode.TargetIPList:
                    try
                    {
                        NetworkManager.IPAddressList(txtRelevantIP.Text);
                        Settings.Default.NetworkTargetIPList = txtRelevantIP.Text;
                        txtRelevantIP.BackColor = DarkTheme.InputColor;
                        save = true;
                    }
                    catch (Exception ex)
                    {
                        txtRelevantIP.BackColor = Color.Salmon;
                        SetToolTip(txtRelevantIP, "There is one or more invalid address in the list");
                    }
                    break;
            }

            if (!save) return;

            Properties.Settings.Default.Save();
            NetworkManager.ReStart();
        }

        #endregion
    }
}
