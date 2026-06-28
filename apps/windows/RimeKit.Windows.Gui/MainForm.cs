namespace RimeKit.Windows.Gui;

/// <summary>
/// Windows 正式主窗口当前直接采用 prototype 结构。
/// </summary>
public sealed class MainForm : WindowsPrototypeForm
{
    public MainForm()
        : base()
    {
    }

    public MainForm(string startDirectory)
        : base(startDirectory)
    {
    }
}
