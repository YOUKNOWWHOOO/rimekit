namespace RimeKit.Windows.Gui;

static class Program
{
    /// <summary>
    /// 应用程序主入口。
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string startDirectory = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? AppContext.BaseDirectory;
        Application.Run(new MainForm(startDirectory));
    }
}
