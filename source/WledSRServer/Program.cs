using System.Configuration;
using System.Reflection;
using WledSRServer;
using WledSRServer.Audio;
using WledSRServer.Properties;

internal class Program
{
    public static GuiContext? GuiContext { get; private set; } = new GuiContext();
    public static ServerContext ServerContext { get; } = new ServerContext();
    public static LuaRuntimeManager LuaRuntime { get; } = new LuaRuntimeManager();

    protected static bool IsInDesigner => Assembly.GetEntryAssembly() == null;

    public const string MboxTitle = "WLED SR Server";

    public static string Version(bool withCommitHash)
    {
        var productVersion = Application.ProductVersion;
        var commitSeparator = productVersion.IndexOf('+');
        if (commitSeparator < 0)
            return productVersion;

        var length = withCommitHash ? Math.Min(productVersion.Length, commitSeparator + 8) : commitSeparator;
        return productVersion.Substring(0, length);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        var culture = new System.Globalization.CultureInfo("en-US"); // For english error messages
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        Application.CurrentCulture = culture;

        if (Arg_SetAutoRun(args))
            return;

        using (Mutex mutex = new Mutex(false, "Global\\WLedSRServerMultipleInstancePrevention"))
        {
            if (!mutex.WaitOne(0, false))
            {
                MessageBox.Show("The server is already running.", MboxTitle);
                return;
            }

            if (!IsInDesigner)
            {
                if (Settings.Default.UpdateSettings)
                {
                    Settings.Default.Upgrade();
                    Settings.Default.UpdateSettings = false;
                    Settings.Default.Save();
                }

                AudioCaptureManager.Run();
                NetworkManager.Run();
                LuaRuntime.StartDefaultTheme();
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, teea) => LogException(teea.Exception);

            GuiContext.Init();
            Application.Run(GuiContext);

            if (!IsInDesigner)
            {
                LuaRuntime.Stop();
                AudioCaptureManager.Stop();
                NetworkManager.Stop();
            }
        }
    }

    public static void LogException(Exception ex)
    {
        var logFile = Path.Combine(Environment.CurrentDirectory, "exceptionlog.txt");
        var writer = new StreamWriter(logFile, true);
        writer.WriteLine($"===[ {DateTime.Now} ]==============================================================================");
        writer.WriteLine($"App version: {Version(true)}");
        writer.WriteLine($"Message: {ex.Message}");
        writer.WriteLine($"Stack Trace");
        writer.WriteLine($"{ex.StackTrace}");
        writer.WriteLine($"Config values");
        foreach (var prop in Settings.Default.Properties.OfType<SettingsProperty>().OrderBy(p => p.Name))
            writer.WriteLine(@$"  {prop.Name} = ""{Settings.Default[prop.Name]}""");
        writer.WriteLine();
        writer.Close();
        MessageBox.Show("HOT DIGGITY DAMN!\n\nSomething unexpected happened.\n\nCan you help me out and send the exceptionlog.txt from the app directory?\n\nThanks.", MboxTitle);
    }


    private static bool Arg_SetAutoRun(string[] args)
    {
        var setAutoRun = args.Where(a => a.StartsWith("-setAutoRun=", StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
        if (setAutoRun == null)
            return false;

        if (!AdminFunctions.IsElevated())
        {
            MessageBox.Show("This function needs elevated permissions. Run as administrator!", MboxTitle);
            Environment.ExitCode = 3;
            return true;
        }

        var value = setAutoRun.EndsWith("true", StringComparison.InvariantCultureIgnoreCase);
        Environment.ExitCode = AdminFunctions.SetAutoRun(true) ? 0 : 1;
        return true;
    }
}