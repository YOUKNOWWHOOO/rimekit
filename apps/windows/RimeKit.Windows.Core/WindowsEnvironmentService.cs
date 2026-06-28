using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RimeKit.Windows.Core;

/// <summary>
/// 负责 Windows 环境探测。
/// </summary>
internal static class WindowsEnvironmentService
{
    // NOTE: COM interfaces ITfInputProcessorProfiles, ITfInputProcessorProfileMgr,
    // ITfCompartment, ITfCompartmentMgr are duplicated in RimeKit.Windows.Activator/Program.cs.
    // If adding or modifying these interfaces, update both files.
    public static IInputMethodPickerLauncher InputMethodPickerLauncher { get; set; } = new WinSpaceInputMethodPickerLauncher();

    // NOTE: This property is intended to be set once during initialization.
    // It is not synchronized for concurrent access — do not mutate after startup.

    public static WindowsEnvironmentState Detect(ConfigModel model)
    {
        string targetRoot = RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot);
        string deployerPath = ResolveDeployerPath();
        (string uninstallerPath, string? uninstallerArguments) = ResolveUninstallerCommand(deployerPath);
        string? weaselVersion = ResolveWeaselVersion(deployerPath, targetRoot);
        (string? foregroundProcessName, string? foregroundKeyboardLayout, bool? foregroundInputContextOpen, string? foregroundConversionStatus) =
            CaptureForegroundInputSnapshot();

        bool targetRootAccessible;
        try
        {
            string accessProbeRoot = Directory.Exists(targetRoot)
                ? targetRoot
                : Path.GetDirectoryName(targetRoot) ?? targetRoot;
            Directory.CreateDirectory(accessProbeRoot);
            string probeFile = Path.Combine(accessProbeRoot, $".rimekit-access-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, "probe");
            if (File.Exists(probeFile))
            {
                File.Delete(probeFile);
            }
            targetRootAccessible = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnvironmentService] Validate target-root check failed: {ex.Message}");
            targetRootAccessible = false;
        }

        return new WindowsEnvironmentState
        {
            WindowsTargetRoot = targetRoot,
            DeployerPath = string.IsNullOrWhiteSpace(deployerPath) ? null : deployerPath,
            UninstallerPath = string.IsNullOrWhiteSpace(uninstallerPath) ? null : uninstallerPath,
            UninstallerArguments = uninstallerArguments,
            WeaselVersion = weaselVersion,
            WeaselUpdateSource = null,
            DefaultInputMethodTip = ResolveDefaultInputMethodTip(),
            ForegroundProcessName = foregroundProcessName,
            ForegroundKeyboardLayout = foregroundKeyboardLayout,
            ForegroundInputContextOpen = foregroundInputContextOpen,
            ForegroundConversionStatus = foregroundConversionStatus,
            TargetRootAccessible = targetRootAccessible,
        };
    }

    public static IReadOnlyList<DiagnosticFinding> Validate(
        WindowsEnvironmentState environment,
        ConfigModel model,
        Func<string, string, string?, string?, string?, IReadOnlyList<string>?, DiagnosticFinding> createFinding)
    {
        List<DiagnosticFinding> findings = [];

        if (!environment.TargetRootAccessible)
        {
            findings.Add(createFinding(
                "WINDOWS_TARGET_ROOT_INVALID",
                $"Windows 目标目录不可访问：{environment.WindowsTargetRoot}",
                null,
                null,
                null,
                null));
        }

        if (!environment.WeaselAvailable)
        {
            findings.Add(createFinding(
                "WINDOWS_WEASEL_MISSING",
                "未在常见安装路径中检测到 WeaselDeployer.exe，当前机器还不具备桌面端完整运行条件。",
                null,
                null,
                null,
                null));
        }

        if (environment.WeaselAvailable &&
            !string.IsNullOrWhiteSpace(environment.DefaultInputMethodTip) &&
            !environment.DefaultInputMethodIsWeasel)
        {
            findings.Add(createFinding(
                "WINDOWS_INPUT_METHOD_NOT_SELECTED",
                $"当前系统默认输入法不是小狼毫，当前值为：{environment.DefaultInputMethodTip}",
                null,
                null,
                null,
                null));
        }

        if (environment.WeaselAvailable &&
            environment.DefaultInputMethodIsWeasel &&
            !string.IsNullOrWhiteSpace(environment.ForegroundProcessName) &&
            environment.ForegroundInputContextOpen == false)
        {
            string keyboardLayout = string.IsNullOrWhiteSpace(environment.ForegroundKeyboardLayout)
                ? "当前未读取到"
                : environment.ForegroundKeyboardLayout;
            findings.Add(createFinding(
                "WINDOWS_FOREGROUND_IME_CLOSED",
                $"当前前台窗口 {environment.ForegroundProcessName} 的输入法上下文没有打开；当前布局 {keyboardLayout}。在这种窗口里直接输入，可能只会得到英文或数字。",
                null,
                null,
                null,
                null));
        }

        if (HasHansModelMismatch(model))
        {
            findings.Add(createFinding(
                "WINDOWS_MODEL_LANGUAGE_MISMATCH",
                "当前启用的是简体万象模型，但当前简繁模式仍设为繁体。这个组合会让模型效果和官方简体示例不一致，甚至把“模型已就绪”误判成“模型已正确生效”。",
                null,
                null,
                null,
                null));
        }

        if (!string.Equals(model.WindowsSettings.DpiScaleMode, "per_monitor_v2", StringComparison.Ordinal))
        {
            findings.Add(createFinding(
                "WINDOWS_DPI_STATE_INVALID",
                $"Windows DPI 模式必须固定为 per_monitor_v2，当前值为：{model.WindowsSettings.DpiScaleMode}",
                null,
                null,
                null,
                null));
        }

        return findings;
    }

    private static string ResolveDeployerPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return File.Exists(overridePath) ? overridePath : string.Empty;
        }

        string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");

        List<string> candidates =
        [
            Path.Combine(localAppData ?? string.Empty, "Programs", "Rime", "WeaselDeployer.exe"),
            Path.Combine(programFiles ?? string.Empty, "Rime", "WeaselDeployer.exe"),
            Path.Combine(programFilesX86 ?? string.Empty, "Rime", "WeaselDeployer.exe"),
        ];

        string resolvedFlatPath = candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedFlatPath))
        {
            return resolvedFlatPath;
        }

        foreach (string root in new[] { programFiles, programFilesX86, localAppData }.Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>())
        {
            string rimeRoot = Path.Combine(root, "Rime");
            if (!Directory.Exists(rimeRoot))
            {
                continue;
            }

            string? nestedMatch = Directory
                .EnumerateFiles(rimeRoot, "WeaselDeployer.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(nestedMatch))
            {
                return nestedMatch;
            }
        }

        return string.Empty;
    }

    private static bool HasHansModelMismatch(ConfigModel model)
    {
        return string.Equals(model.ModelSettings.ActiveModelId, "wanxiang_lts_zh_hans", StringComparison.OrdinalIgnoreCase) &&
               model.ModelSettings.EnabledModelIds.Contains("wanxiang_lts_zh_hans", StringComparer.OrdinalIgnoreCase);
    }

    private static (string Path, string? Arguments) ResolveUninstallerCommand(string deployerPath)
    {
        string? overridePath = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_UNINSTALLER_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return (overridePath, Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_UNINSTALLER_ARGS"));
        }

        if (!string.IsNullOrWhiteSpace(deployerPath))
        {
            string deployerDirectory = Path.GetDirectoryName(deployerPath) ?? string.Empty;
            foreach (string candidate in new[]
                     {
                         Path.Combine(deployerDirectory, "uninstall.exe"),
                         Path.Combine(deployerDirectory, "WeaselSetup.exe"),
                         Path.Combine(deployerDirectory, "weasel-setup.exe"),
                         Path.Combine(deployerDirectory, "unins000.exe"),
                     })
            {
                if (File.Exists(candidate))
                {
                    return (candidate, null);
                }
            }
        }

        foreach (string uninstallKeyPath in new[]
                 {
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                 })
        {
            using RegistryKey? uninstallRoot = Registry.LocalMachine.OpenSubKey(uninstallKeyPath);
            if (uninstallRoot is null)
            {
                continue;
            }

            foreach (string subKeyName in uninstallRoot.GetSubKeyNames())
            {
                using RegistryKey? subKey = uninstallRoot.OpenSubKey(subKeyName);
                string? displayName = subKey?.GetValue("DisplayName") as string;
                string? publisher = subKey?.GetValue("Publisher") as string;
                if (string.IsNullOrWhiteSpace(displayName) ||
                    (!displayName.Contains("weasel", StringComparison.OrdinalIgnoreCase) &&
                     !displayName.Contains("小狼毫", StringComparison.OrdinalIgnoreCase) &&
                     !(publisher?.Contains("rime", StringComparison.OrdinalIgnoreCase) ?? false)))
                {
                    continue;
                }

                string? uninstallString = (subKey?.GetValue("QuietUninstallString") as string)
                    ?? (subKey?.GetValue("UninstallString") as string);
                if (string.IsNullOrWhiteSpace(uninstallString))
                {
                    continue;
                }

                (string executablePath, string? arguments) = SplitCommand(uninstallString.Trim());
                if (File.Exists(executablePath))
                {
                    return (executablePath, arguments);
                }
            }
        }

        return (string.Empty, null);
    }

    private static (string ExecutablePath, string? Arguments) SplitCommand(string command)
    {
        if (command.StartsWith("\"", StringComparison.Ordinal))
        {
            int endQuote = command.IndexOf('"', 1);
            if (endQuote > 1)
            {
                string executable = command[1..endQuote];
                string? arguments = command[(endQuote + 1)..].Trim();
                return (executable, string.IsNullOrWhiteSpace(arguments) ? null : arguments);
            }
        }

        int exeSuffix = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeSuffix >= 0)
        {
            string executable = command[..(exeSuffix + 4)];
            string? arguments = command[(exeSuffix + 4)..].Trim();
            return (executable, string.IsNullOrWhiteSpace(arguments) ? null : arguments);
        }

        return (command, null);
    }

    private static bool FontExists(string fontFace)
    {
        using RegistryKey? fontsKey =
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
        if (fontsKey is null)
        {
            return false;
        }

        foreach (string? name in fontsKey.GetValueNames())
        {
            if (name is not null && name.Contains(fontFace, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveFileVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? null : info.FileVersion;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnvironmentService] ResolveWeaselVersion failed: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveWeaselVersion(string deployerPath, string targetRoot)
    {
        string? fileVersion = ResolveFileVersion(deployerPath);
        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            return fileVersion;
        }

        string installationYamlPath = Path.Combine(targetRoot, "installation.yaml");
        if (File.Exists(installationYamlPath))
        {
            try
            {
                foreach (string rawLine in RepositoryContext.ReadUtf8(installationYamlPath).Split(["\r\n", "\n"], StringSplitOptions.None))
                {
                    string line = rawLine.Trim();
                    if (!line.StartsWith("distribution_version:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string version = line["distribution_version:".Length..].Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return version;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EnvironmentService] ResolveWeaselDeployerVersion failed: {ex.Message}");
            }
        }

        string? deployerDirectoryName = Path.GetFileName(Path.GetDirectoryName(deployerPath));
        if (!string.IsNullOrWhiteSpace(deployerDirectoryName) &&
            deployerDirectoryName.StartsWith("weasel-", StringComparison.OrdinalIgnoreCase))
        {
            string version = deployerDirectoryName["weasel-".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }

        return null;
    }

    public static void TryActivateWeaselProfile()
    {
        const ushort ChineseLang = 0x0804;
        Guid managerClsid = new("33C53A50-F456-4884-B049-85FD643ECFED");
        Guid clsid = new("A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A");
        Guid profile = new("3D02CAB6-2B8E-4781-BA20-1C9267529467");

        try
        {
            object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(managerClsid)!)!;
            ITfInputProcessorProfileMgr manager = (ITfInputProcessorProfileMgr)instance;
            manager.ActivateProfile(
                0x0001,
                ChineseLang,
                ref clsid,
                ref profile,
                IntPtr.Zero,
                0x20000000 | 0x00000001 | 0x00000004);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnvironmentService] TryActivateWeaselProfile ActivateProfile failed: {ex.Message}");
        }

    IntPtr profilesPointer = IntPtr.Zero;
        int hr = TfsNative.TF_CreateInputProcessorProfiles(out profilesPointer);
        if (hr != 0 || profilesPointer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            ITfInputProcessorProfiles profiles =
                (ITfInputProcessorProfiles)Marshal.GetTypedObjectForIUnknown(profilesPointer, typeof(ITfInputProcessorProfiles));
            try
            {
                profiles.ActivateLanguageProfile(ref clsid, ChineseLang, ref profile);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EnvironmentService] TryActivateWeaselProfile ActivateLanguageProfile failed: {ex.Message}");
            }
        }
        finally
        {
            Marshal.Release(profilesPointer);
        }
    }

    public static bool OpenInputMethodPicker()
    {
        InputMethodPickerResult result = InputMethodPickerLauncher.Launch();
        return result.WasLaunched;
    }

    private static string? ResolveDefaultInputMethodTip()
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"(Get-WinDefaultInputMethodOverride).InputMethodTip\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            if (!Utilities.ProcessHelper.WaitForExitWithBackoff(process, 5000))
            {
                Utilities.ProcessHelper.TerminateProcess(process);
                return null;
            }
            string output = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnvironmentService] ResolveDefaultInputMethodTip failed: {ex.Message}");
            return null;
        }
    }

    private static (string? ProcessName, string? KeyboardLayout, bool? InputContextOpen, string? ConversionStatus) CaptureForegroundInputSnapshot()
    {
        try
        {
            IntPtr foregroundWindow = User32Native.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return default;
            }

            uint threadId = User32Native.GetWindowThreadProcessId(foregroundWindow, out uint processId);
            IntPtr keyboardLayout = User32Native.GetKeyboardLayout(threadId);
            string? processName = null;
            if (processId != 0)
            {
                try
                {
                    processName = $"{Process.GetProcessById((int)processId).ProcessName}.exe";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EnvironmentService] CaptureForegroundInputSnapshot GetProcessById failed: {ex.Message}");
                    processName = null;
                }
            }

            IntPtr inputContext = Imm32Native.ImmGetContext(foregroundWindow);
            bool? open = null;
            string? conversionStatus = null;
            if (inputContext != IntPtr.Zero)
            {
                try
                {
                    open = Imm32Native.ImmGetOpenStatus(inputContext);
                    if (Imm32Native.ImmGetConversionStatus(inputContext, out uint conversion, out _))
                    {
                        conversionStatus = $"0x{conversion:X}";
                    }
                }
                finally
                {
                    Imm32Native.ImmReleaseContext(foregroundWindow, inputContext);
                }
            }

            return (processName, $"0x{keyboardLayout.ToInt64():X}", open, conversionStatus);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnvironmentService] CaptureForegroundInputSnapshot failed: {ex.Message}");
            return default;
        }
    }

    [ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        void ActivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);
    }

    [ComImport, Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfiles
    {
        void Register(ref Guid rclsid);
        void Unregister(ref Guid rclsid);
        void AddLanguageProfile(ref Guid rclsid, ushort langid, ref Guid profile, string desc, uint cchDesc, string icon, uint cchFile, uint iconIndex);
        void RemoveLanguageProfile(ref Guid rclsid, ushort langid, ref Guid profile);
        void EnumInputProcessorInfo(out IntPtr enumGuid);
        void GetDefaultLanguageProfile(ushort langid, ref Guid catid, out Guid clsid, out Guid profile);
        void SetDefaultLanguageProfile(ushort langid, ref Guid clsid, ref Guid profile);
        void ActivateLanguageProfile(ref Guid clsid, ushort langid, ref Guid profile);
        void GetActiveLanguageProfile(ref Guid clsid, out ushort langid, out Guid profile);
    }

    private static class TfsNative
    {
        [DllImport("msctf.dll")]
        internal static extern int TF_CreateInputProcessorProfiles(out IntPtr profiles);
    }

    private static class User32Native
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetKeyboardLayout(uint idThread);
    }

    private static class Imm32Native
    {
        [DllImport("imm32.dll")]
        internal static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [DllImport("imm32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint conversion, out uint sentence);
    }
}

internal sealed class WinSpaceInputMethodPickerLauncher : IInputMethodPickerLauncher
{
    public InputMethodPickerResult Launch()
    {
        int elapsedMs = 0;
        try
        {
            Stopwatch sw = Stopwatch.StartNew();

            const ushort VK_LWIN = 0x5B;
            const ushort VK_SPACE = 0x20;
            const uint KeyUp = 0x0002;

            SendKeyNative.INPUT[] inputs =
            [
                SendKeyNative.CreateKeyboardInput(VK_LWIN, 0),
                SendKeyNative.CreateKeyboardInput(VK_SPACE, 0),
                SendKeyNative.CreateKeyboardInput(VK_SPACE, KeyUp),
                SendKeyNative.CreateKeyboardInput(VK_LWIN, KeyUp),
            ];

            uint sent = SendKeyNative.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<SendKeyNative.INPUT>());
            sw.Stop();
            elapsedMs = (int)sw.ElapsedMilliseconds;

            if (sent != inputs.Length)
                return new InputMethodPickerResult { Status = WorkflowStatuses.Failed, Detail = $"SendInput 失败：仅发送 {sent}/{inputs.Length} 个事件。", WasLaunched = false };

            return new InputMethodPickerResult
            {
                Status = WorkflowStatuses.ManualActionRequired,
                Detail = "已发送 Win+Space 组合键，但无法程序化验证系统输入法选择器是否已打开。请确认选择器已出现后继续操作。",
                WasLaunched = true,
                LaunchMethod = "Win+Space via SendInput",
                EvidenceKind = "key_dispatch_only",
                DurationMs = elapsedMs,
                RequiresManualConfirmation = true,
            };
        }
        catch (Exception ex)
        {
            return new InputMethodPickerResult { Status = WorkflowStatuses.Failed, Detail = ex.Message, WasLaunched = false, DurationMs = elapsedMs };
        }
    }

    private static class SendKeyNative
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT { internal uint type; internal InputUnion u; }
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion { [FieldOffset(0)] internal KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT { internal ushort wVk; internal ushort wScan; internal uint dwFlags; internal uint time; internal UIntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        internal static INPUT CreateKeyboardInput(ushort vk, uint flags) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = flags, time = 0, dwExtraInfo = UIntPtr.Zero } } };
    }
}

internal sealed class HermeticInputMethodPickerLauncher : IInputMethodPickerLauncher
{
    public InputMethodPickerResult Launch() => new()
    {
        Status = WorkflowStatuses.ManualActionRequired,
        Detail = "当前为隔离（hermetic）模式，不执行真实宿主 UI 操作。请手动按 Win+Space 打开系统输入法选择器。",
        WasLaunched = false,
        LaunchMethod = "none (hermetic)",
        EvidenceKind = "no_launch_attempted",
        DurationMs = 0,
        RequiresManualConfirmation = true,
    };
}
