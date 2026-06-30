using System.Text.Encodings.Web;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using RimeKit.Windows.Core.Utilities;

namespace RimeKit.Windows.Core;

/// <summary>
/// 承载 Windows W2 最小闭环工作流。
/// </summary>
public sealed class WindowsWorkflowService
{
    private const string ElevatedCleanupScriptName = "pending_weasel_elevated_cleanup.cmd";
    private const string ElevatedCleanupResultName = "pending_weasel_elevated_cleanup_result.txt";
    private const string WeaselExpectedAbsentStateName = "weasel_expected_absent.txt";
    private const string UnknownVersionDisplayText = "未知";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly RepositoryContext _repositoryContext;
    private readonly ConfigModelService _configModelService;
    private readonly ArtifactService _artifactService;
    private readonly ResourceUpdateService _resourceUpdateService;

    public WindowsWorkflowService(string startDirectory)
    {
        _repositoryContext = new RepositoryContext(startDirectory);
        TemplateService.EnsureRepositoryRoot(startDirectory);
        _configModelService = new ConfigModelService(_repositoryContext);
        _artifactService = new ArtifactService(_repositoryContext);
        _resourceUpdateService = new ResourceUpdateService(_repositoryContext);
    }

    public ConfigModel GetConfigModelForEditing(string? configPath)
    {
        return LoadPreferredConfigModel(configPath, allowDefault: true);
    }

    public WorkflowTaskDefinition? GetWindowsTaskDefinition(string taskId)
    {
        return _repositoryContext.WindowsTasks.TryGetValue(taskId, out WorkflowTaskDefinition? definition)
            ? definition
            : null;
    }

    public CommandExecutionResult RunDownloadAndLaunchWeaselInstaller(string outputFormat, Action<string>? phase = null)
    {
        phase?.Invoke("正在下载…");
        return RunLaunchWeaselInstaller(
            outputFormat,
            relatedTaskId: "windows_download_weasel_installer",
            successNextAction: "安装器已启动；如系统弹出确认，请完成确认后返回应用重新检测。",
            silentSuccessNextAction: "静默安装已完成；程序已自动再次确认当前状态。",
            phase: phase);
    }

    public CommandExecutionResult RunDownloadWeaselInstallerToFile(string outputPath, string outputFormat)
    {
        try
        {
            ConfigModel configModel = LoadPreferredConfigModel(null, allowDefault: true);
            WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
            WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(configModel);
            string downloadUrl = ResolveGitHubReleaseAssetUrl(prerelease: false);
            string downloadedPath = DownloadInstaller(downloadUrl);
            string targetDir = Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException($"无法解析输出路径的目录：{outputPath}");
            Directory.CreateDirectory(targetDir);
            Utilities.FileHelper.CopyFileWithBackoff(downloadedPath, outputPath, overwrite: true);

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Detect,
                status = WorkflowStatuses.Completed,
                download_url = downloadUrl,
                output_path = outputPath,
                next_action = "Weasel 安装器已下载到指定路径，可使用 install-weasel --from-file 从本地文件安装。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"下载链接: {downloadUrl}",
                            $"输出路径: {outputPath}",
                            "下一步: 已下载到指定路径。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_WEASEL_DOWNLOAD_FAILED", $"下载 Weasel 安装器失败：{exception.Message}", relatedTaskId: "windows_download_weasel_installer_to_file")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public CommandExecutionResult RunRequestWeaselReinstall(string outputFormat)
    {
        return RunLaunchWeaselInstaller(
            outputFormat,
            relatedTaskId: "windows_request_weasel_reinstall",
            successNextAction: "重新安装入口已启动；请完成重新安装后返回应用重新检测。",
            silentSuccessNextAction: "静默重新安装已完成；程序已自动再次确认当前状态。");
    }

    public CommandExecutionResult RunCheckWeaselUpdate(string outputFormat)
    {
        try
        {
            ConfigModel configModel = LoadPreferredConfigModel(null, allowDefault: true);
            WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(configModel);
            string currentVersion = environment.WeaselVersion ?? UnknownVersionDisplayText;
            string rawPayload = _resourceUpdateService.BuildCheckForUpdatesPayload();
            using JsonDocument doc = JsonDocument.Parse(rawPayload);
            string summary;
            if (doc.RootElement.TryGetProperty("items", out JsonElement items))
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (string.Equals(item.GetProperty("resource_id").GetString(), "windows_weasel", StringComparison.OrdinalIgnoreCase))
                    {
                        string latestVersion = item.GetProperty("latest_version").GetString() ?? UnknownVersionDisplayText;
                        bool hasUpdate = item.GetProperty("has_update").GetBoolean();
                        summary = hasUpdate
                            ? $"发现新版本：{latestVersion}（当前本地版本：{currentVersion}）。请前往下载并安装。"
                            : $"已是最新版本：{currentVersion}。无需更新。";
                        object payload = new { platform = "windows", phase = WorkflowPhases.Detect, status = WorkflowStatuses.Completed, current_version = currentVersion, latest_version = latestVersion, has_update = hasUpdate, summary };
                        return new CommandExecutionResult { ExitCode = 0, TextOutput = summary, JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null };
                    }
                }
            }
            string noResultMsg = $"当前小狼毫版本：{currentVersion}。未能获取最新版本信息。";
            return new CommandExecutionResult { ExitCode = 1, TextOutput = noResultMsg };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or JsonException)
        {
            return new CommandExecutionResult { ExitCode = 1, TextOutput = $"检查小狼毫更新失败：{ex.Message}" };
        }
    }

    public CommandExecutionResult RunCheckFormalResourceUpdate(string resourceId, string outputFormat)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return new CommandExecutionResult { ExitCode = 1, TextOutput = "当前未选择有效资源，无法检查更新。" };
            }
            string rawPayload = _resourceUpdateService.BuildCheckForUpdatesPayload();
            using JsonDocument doc = JsonDocument.Parse(rawPayload);
            if (doc.RootElement.TryGetProperty("items", out JsonElement items))
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (string.Equals(item.GetProperty("resource_id").GetString(), resourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        string latestVersion = item.GetProperty("latest_version").GetString() ?? UnknownVersionDisplayText;
                        string currentVersion = item.GetProperty("current_version").GetString() ?? UnknownVersionDisplayText;
                        bool hasUpdate = item.GetProperty("has_update").GetBoolean();
                        string summary = hasUpdate
                            ? $"发现新版本：{latestVersion}（当前本地版本：{currentVersion}）。请使用「下载并安装」或「下载并部署」按钮进行更新。"
                            : $"已是最新版本：{currentVersion}。无需更新。";
                        object payload = new { platform = "windows", phase = WorkflowPhases.Detect, status = WorkflowStatuses.Completed, resource_id = resourceId, current_version = currentVersion, latest_version = latestVersion, has_update = hasUpdate, summary };
                        return new CommandExecutionResult { ExitCode = 0, TextOutput = summary, JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null };
                    }
                }
            }
            return new CommandExecutionResult { ExitCode = 1, TextOutput = $"未能获取 {resourceId} 的更新信息。" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or JsonException)
        {
            return new CommandExecutionResult { ExitCode = 1, TextOutput = $"检查更新失败：{ex.Message}" };
        }
    }

    public CommandExecutionResult RunOpenInputMethodPicker(string outputFormat)
    {
        try
        {
            InputMethodPickerResult pickerResult = WindowsEnvironmentService.InputMethodPickerLauncher.Launch();
            return RenderInputMethodPickerResult(pickerResult, outputFormat);
        }
        catch (Exception exception)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Blocked,
                [
                    CreateFinding(
                        "WINDOWS_FOREGROUND_IME_CLOSED",
                        $"打开输入法选择器失败：{exception.Message}",
                        relatedTaskId: null)
                ],
                snapshotId: "none",
                targetStateMutated: false,
                rollbackAvailable: false,
                rollbackRecommended: false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    private static CommandExecutionResult RenderInputMethodPickerResult(InputMethodPickerResult pickerResult, string outputFormat)
    {
        string status = pickerResult.Status;
        int exitCode = status == WorkflowStatuses.Completed ? 0 : 1;
        string nextAction = status switch
        {
            WorkflowStatuses.Completed => "系统输入法选择器已打开；请在系统界面完成切换后返回应用重新检测。",
            _ => pickerResult.Detail,
        };

        object payload = new
        {
            platform = "windows",
            phase = WorkflowPhases.Detect,
            status,
            detail = pickerResult.Detail,
            was_launched = pickerResult.WasLaunched,
            launch_method = pickerResult.LaunchMethod,
            evidence_kind = pickerResult.EvidenceKind,
            duration_ms = pickerResult.DurationMs,
            requires_manual_confirmation = pickerResult.RequiresManualConfirmation,
            next_action = nextAction,
        };

        string textOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Serialize(payload, JsonOptions)
            : string.Join(
                Environment.NewLine,
                [
                    $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                    $"结果: {StatusLabel(status)}",
                    $"说明: {pickerResult.Detail}",
                    $"方式: {pickerResult.LaunchMethod}",
                    $"证据类型: {pickerResult.EvidenceKind}",
                    $"耗时: {pickerResult.DurationMs}ms",
                    $"下一步: {nextAction}",
                ]);

        return new CommandExecutionResult
        {
            ExitCode = exitCode,
            TextOutput = textOutput,
            JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
        };
    }

    private CommandExecutionResult RunLaunchWeaselInstaller(
        string outputFormat,
        string relatedTaskId,
        string successNextAction,
        string silentSuccessNextAction,
        Action<string>? phase = null)
    {
        TemplateService.DeleteWeaselTemplate();
        ConfigModel configModel = LoadPreferredConfigModel(null, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(configModel);
        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
        _repositoryContext.PersistCurrentConfigModel(configModel);
        _repositoryContext.PersistRuntimePathCache(environment);

        try
        {
            string launchTarget;
            string nextAction;
            string resultLabel;
            string? installerPath = ResolveInstallerLaunchPath(environment);

            launchTarget = installerPath;
            string installerArguments = BuildInstallerLaunchArguments(installerPath, controls);
            bool useSilentMode = ShouldUseSilentInstaller(installerPath, controls);
            nextAction = useSilentMode ? silentSuccessNextAction : successNextAction;
            resultLabel = $"安装器路径: {installerPath}";
            _repositoryContext.ClearPendingWeaselUninstallTargets();
            _repositoryContext.ClearStateReference(WeaselExpectedAbsentStateName);
            phase?.Invoke("正在安装…");

            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = launchTarget,
                Arguments = installerArguments,
                UseShellExecute = true,
            });
            if (process is null)
            {
                throw new InvalidOperationException("安装器进程未成功启动。");
            }
            _repositoryContext.PersistStateReference("pending_weasel_installer.txt", installerPath);

            if (useSilentMode)
            {
                if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
                {
                    Utilities.ProcessHelper.TerminateProcess(process);
                    throw new InvalidOperationException("静默安装器执行超时（10分钟），已强制终止。");
                }
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"静默安装器返回了非零退出码：{process.ExitCode}");
                }

                CommandExecutionResult recheckResult = ApplyAndCleanupAfterPendingOperation(null, outputFormat);
                WindowsEnvironmentState refreshedEnvironment = WindowsEnvironmentService.Detect(configModel);
                if (!refreshedEnvironment.WeaselAvailable)
                {
                    DiagnosticReport report = BuildDiagnosticReport(
                        WorkflowPhases.Recheck,
                        WorkflowStatuses.Blocked,
                        [CreateFinding("WINDOWS_WEASEL_INSTALL_RECHECK_FAILED", "静默安装结束后，程序仍未检测到小狼毫部署器。", relatedTaskId: relatedTaskId)],
                        "none",
                        false,
                        false,
                        false);
                    _repositoryContext.PersistLastDiagnostic(report);
                    _repositoryContext.PersistRecheckSummary("none", report.Status, report.Findings);
                    return CreateCommandResult(report, outputFormat);
                }

                if (recheckResult.ExitCode != 0)
                {
                    return recheckResult;
                }

                object silentResult = new
                {
                    platform = "windows",
                    phase = WorkflowPhases.Detect,
                    status = WorkflowStatuses.Completed,
                    installer_path = installerPath,
                    installer_arguments = installerArguments,
                    execution_mode = "silent",
                    next_action = nextAction,
                };

                return new CommandExecutionResult
                {
                    ExitCode = 0,
                    TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                        ? JsonSerializer.Serialize(silentResult, JsonOptions)
                        : string.Join(
                            Environment.NewLine,
                            [
                                $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                                $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                                resultLabel,
                                $"安装方式: {DescribeExecutionMode(useSilentMode)}",
                                $"下一步: {nextAction}",
                            ]),
                    JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? silentResult : null,
                };
            }

            object result = new
            {
                platform = "windows",
                phase = WorkflowPhases.Detect,
                status = WorkflowStatuses.Completed,
                installer_path = installerPath,
                installer_arguments = installerArguments,
                execution_mode = "interactive",
                install_url = ResolveGitHubReleaseAssetUrl(prerelease: false),
                next_action = nextAction,
            };

            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(result, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            resultLabel,
                            $"安装方式: {DescribeExecutionMode(useSilentMode)}",
                            $"下一步: {nextAction}",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? result : null,
            };
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            string errorCode = exception is HttpRequestException or IOException
                ? "WINDOWS_WEASEL_DOWNLOAD_FAILED"
                : "WINDOWS_WEASEL_INSTALL_LAUNCH_FAILED";
            string detail = string.Equals(errorCode, "WINDOWS_WEASEL_DOWNLOAD_FAILED", StringComparison.Ordinal)
                ? $"自动下载 Weasel 安装器失败：{exception.Message}"
                : $"启动 Weasel 安装器失败：{exception.Message}";
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Failed,
                [CreateFinding(errorCode, detail, relatedTaskId: relatedTaskId)],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public CommandExecutionResult RunLaunchWeaselInstallerFromFile(string installerPath, string outputFormat, Action<string>? phase = null)
    {
        if (!File.Exists(installerPath))
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_WEASEL_INSTALL_FILE_NOT_FOUND", $"指定的安装器文件不存在：{installerPath}", relatedTaskId: "windows_install_weasel_from_file")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }

        TemplateService.DeleteWeaselTemplate();
        ConfigModel configModel = LoadPreferredConfigModel(null, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(configModel);
        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
        _repositoryContext.PersistCurrentConfigModel(configModel);
        _repositoryContext.PersistRuntimePathCache(environment);

        try
        {
            string launchTarget = installerPath;
            string nextAction;
            string resultLabel;
            string installerArguments = BuildInstallerLaunchArguments(installerPath, controls);
            bool useSilentMode = ShouldUseSilentInstaller(installerPath, controls);
            string relatedTaskId = "windows_install_weasel_from_file";
            nextAction = useSilentMode
                ? "静默安装已完成；程序已自动再次确认当前状态。"
                : "安装器已启动；如系统弹出确认，请完成确认后返回应用重新检测。";
            resultLabel = $"安装器路径: {installerPath}";
            _repositoryContext.ClearPendingWeaselUninstallTargets();
            _repositoryContext.ClearStateReference(WeaselExpectedAbsentStateName);

            phase?.Invoke("正在安装…");
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = launchTarget,
                Arguments = installerArguments,
                UseShellExecute = true,
            });
            if (process is null)
            {
                throw new InvalidOperationException("安装器进程未成功启动。");
            }
            _repositoryContext.PersistStateReference("pending_weasel_installer.txt", installerPath);

            if (useSilentMode)
            {
                if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
                {
                    Utilities.ProcessHelper.TerminateProcess(process);
                    throw new InvalidOperationException("静默安装器执行超时（10分钟），已强制终止。");
                }
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"静默安装器返回了非零退出码：{process.ExitCode}");
                }

                CommandExecutionResult recheckResult = ApplyAndCleanupAfterPendingOperation(null, outputFormat);
                WindowsEnvironmentState refreshedEnvironment = WindowsEnvironmentService.Detect(configModel);
                if (!refreshedEnvironment.WeaselAvailable)
                {
                    DiagnosticReport report = BuildDiagnosticReport(
                        WorkflowPhases.Recheck,
                        WorkflowStatuses.Blocked,
                        [CreateFinding("WINDOWS_WEASEL_INSTALL_RECHECK_FAILED", "静默安装结束后，程序仍未检测到小狼毫部署器。", relatedTaskId: relatedTaskId)],
                        "none",
                        false,
                        false,
                        false);
                    _repositoryContext.PersistLastDiagnostic(report);
                    _repositoryContext.PersistRecheckSummary("none", report.Status, report.Findings);
                    return CreateCommandResult(report, outputFormat);
                }

                if (recheckResult.ExitCode != 0)
                {
                    return recheckResult;
                }

                object silentResult = new
                {
                    platform = "windows",
                    phase = WorkflowPhases.Detect,
                    status = WorkflowStatuses.Completed,
                    installer_path = installerPath,
                    installer_arguments = installerArguments,
                    execution_mode = "silent",
                    next_action = nextAction,
                };

                return new CommandExecutionResult
                {
                    ExitCode = 0,
                    TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                        ? JsonSerializer.Serialize(silentResult, JsonOptions)
                        : string.Join(
                            Environment.NewLine,
                            [
                                $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                                $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                                resultLabel,
                                $"安装方式: {DescribeExecutionMode(useSilentMode)}",
                                $"下一步: {nextAction}",
                            ]),
                    JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? silentResult : null,
                };
            }

            object result = new
            {
                platform = "windows",
                phase = WorkflowPhases.Detect,
                status = WorkflowStatuses.Completed,
                installer_path = installerPath,
                installer_arguments = installerArguments,
                execution_mode = "interactive",
                next_action = nextAction,
            };

            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(result, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            resultLabel,
                            $"安装方式: {DescribeExecutionMode(useSilentMode)}",
                            $"下一步: {nextAction}",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? result : null,
            };
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_WEASEL_INSTALL_LAUNCH_FAILED", $"启动 Weasel 安装器失败：{exception.Message}", relatedTaskId: "windows_install_weasel_from_file")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public CommandExecutionResult RunCheckDeployerHealth(string outputFormat)
    {
        ConfigModel configModel = LoadPreferredConfigModel(null, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(configModel);

        if (string.IsNullOrWhiteSpace(environment.DeployerPath))
        {
            string message = "未检测到小狼毫部署器。请使用「下载并安装小狼毫」进行安装。";
            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Detect,
                status = WorkflowStatuses.Blocked,
                next_action = message,
            };
            return new CommandExecutionResult
            {
                ExitCode = 1,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : message,
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }

        try
        {
            FileInfo fileInfo = new(environment.DeployerPath);
            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                throw new IOException("部署器文件不存在或文件长度无效。");
            }

            object result = new
            {
                platform = "windows",
                phase = WorkflowPhases.Detect,
                status = WorkflowStatuses.Completed,
                deployer_path = environment.DeployerPath,
                next_action = "部署器检查通过；如果输入法还是没有正常生效，可以继续执行检测、写入输入法，或更新小狼毫。",
            };

            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(result, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"部署器路径: {environment.DeployerPath}",
                            "下一步: 部署器检查通过；如果输入法还是没有正常生效，可以继续执行检测、写入输入法，或更新小狼毫。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? result : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticReport failed = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_DEPLOYER_REPAIR_FAILED", $"部署器修复检查失败：{exception.Message}", relatedTaskId: "windows_check_deployer_health")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(failed);
            return CreateCommandResult(failed, outputFormat);
        }
    }

    private static void TryOpenExternalPath(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            System.Diagnostics.Debug.WriteLine($"[TryOpenExternalPath] 无法打开路径 {target}: {ex.Message}");
        }
    }

    private static void UninstallTrace(string message)
    {
        string tracePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "rimekit_uninstall_trace.log");
        string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        File.AppendAllText(tracePath, line + Environment.NewLine);
        System.Diagnostics.Debug.WriteLine(line);
    }

    private static void TryStopWeaselRuntime(WindowsEnvironmentState environment)
    {
        string? installDirectory = string.IsNullOrWhiteSpace(environment.DeployerPath)
            ? null
            : Path.GetDirectoryName(environment.DeployerPath);
        string? serverExecutable = string.IsNullOrWhiteSpace(installDirectory)
            ? null
            : Path.Combine(installDirectory, "WeaselServer.exe");

        if (!string.IsNullOrWhiteSpace(serverExecutable) && File.Exists(serverExecutable))
        {
            try
            {
                using Process? serverProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = serverExecutable,
                    Arguments = "/q",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                Utilities.ProcessHelper.WaitForExitWithBackoff(serverProcess, 10000);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                System.Diagnostics.Debug.WriteLine($"[TryStopWeaselRuntime] WeaselServer.exe /q 失败: {ex.Message}");
            }
        }

        foreach (string processName in new[] { "WeaselSetup", "WeaselDeployer", "WeaselServer" })
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!process.HasExited && process.CloseMainWindow())
                    {
                        Utilities.ProcessHelper.WaitForExitWithBackoff(process, 5000);
                    }

                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        Utilities.ProcessHelper.WaitForExitWithBackoff(process, 5000);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    System.Diagnostics.Debug.WriteLine($"[TryStopWeaselRuntime] 终止 {processName} 失败: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        Utilities.ProcessHelper.StopProcessesWithBackoff(
            new[] { "WeaselServer", "WeaselDeployer" },
            timeoutMs: 30000,
            baseDelayMs: 200,
            maxDelayMs: 2000);
    }

    private static void UnregisterWeaselTsf(WindowsEnvironmentState environment)
    {
        string? installDirectory = string.IsNullOrWhiteSpace(environment.DeployerPath)
            ? null
            : Path.GetDirectoryName(environment.DeployerPath);
        string? setupPath = string.IsNullOrWhiteSpace(installDirectory)
            ? null
            : Path.Combine(installDirectory, "WeaselSetup.exe");

        if (string.IsNullOrWhiteSpace(setupPath) || !File.Exists(setupPath))
            return;

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = "/u",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            Utilities.ProcessHelper.WaitForExitWithBackoff(process, 30000);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[UnregisterWeaselTsf] WeaselSetup.exe /u 失败: {ex.Message}");
        }
    }

    public CommandExecutionResult RunLaunchWeaselUninstaller(string outputFormat, Action<string>? phase = null)
    {
        ConfigModel configModel = LoadPreferredConfigModel(null, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(configModel);
        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
        _repositoryContext.PersistRuntimePathCache(environment);

        if (!environment.WeaselAvailable)
        {
            TemplateService.DeleteWeaselTemplate();
            _repositoryContext.PersistStateReference(WeaselExpectedAbsentStateName, "1");
            object alreadyAbsent = new
            {
                platform = "windows",
                phase = WorkflowPhases.Detect,
                status = WorkflowStatuses.Completed,
                next_action = "当前机器上未检测到小狼毫本体，也无可用的卸载工具，无需再次执行卸载。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(alreadyAbsent, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            "下一步: 当前机器上未检测到小狼毫本体，也无可用的卸载工具，无需再次执行卸载。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? alreadyAbsent : null,
            };
        }

        if (string.IsNullOrWhiteSpace(environment.UninstallerPath))
        {
            DiagnosticReport blocked = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Blocked,
                [CreateFinding("WINDOWS_WEASEL_UNINSTALL_ENTRY_MISSING", "未检测到 Weasel 专属卸载器或专属卸载命令，当前不能自动发起专属卸载。", relatedTaskId: "windows_launch_weasel_uninstaller")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(blocked);
            return CreateCommandResult(blocked, outputFormat);
        }

        string uninstallTarget = environment.UninstallerPath;
        string uninstallArguments = BuildUninstallerLaunchArguments(environment, controls);
        bool useSilentMode = ShouldUseSilentUninstaller(environment, controls);
        string nextAction = useSilentMode
            ? "静默卸载已执行；程序会立即再次确认当前状态。"
            : "卸载器已启动；如系统要求确认，请完成确认后返回应用重新检测。";

        try
        {
            TryStopWeaselRuntime(environment);
            UnregisterWeaselTsf(environment);
            TemplateService.DeleteWeaselTemplate();
            phase?.Invoke("正在卸载小狼毫…");
            _repositoryContext.PersistPendingWeaselUninstallTargets(BuildWeaselCleanupTargets(configModel, environment));
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = uninstallTarget,
                Arguments = uninstallArguments,
                UseShellExecute = true,
            });
            if (process is null)
            {
                throw new InvalidOperationException("卸载器进程未成功启动。");
            }

            if (useSilentMode)
            {
                if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
                {
                    Utilities.ProcessHelper.TerminateProcess(process);
                    throw new InvalidOperationException("静默卸载器执行超时（10分钟），已强制终止。");
                }
                TryStopWeaselRuntime(environment);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"静默安装器返回了非零退出码：{process.ExitCode}");
                }

                string rimeDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");
                bool handlesReleased = Utilities.FileHelper.WaitForDirectoryHandlesReleased(
                    rimeDataPath, timeoutMs: 45000);
                UninstallTrace(handlesReleased
                    ? "WaitForDirectoryHandles: SUCCESS"
                    : "WaitForDirectoryHandles: TIMEOUT (handles not released)");
                if (!handlesReleased)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[WeaselUninstall] 等待目录文件句柄释放超时，将继续执行清理。");
                }

                phase?.Invoke("卸载完成，正在确认…");
                UninstallTrace("===== POST-UNINSTALL START =====");
                UninstallTrace($"Step 1: ApplyAndCleanupAfterPendingOperation...");
                CommandExecutionResult recheckResult = ApplyAndCleanupAfterPendingOperation(null, outputFormat);
                UninstallTrace($"Step 1 done: ExitCode={recheckResult.ExitCode}");

                ConfigModel refreshedModel = LoadPreferredConfigModel(null, allowDefault: true);
                WindowsEnvironmentState refreshedEnvironment = WindowsEnvironmentService.Detect(refreshedModel);
                UninstallTrace($"Step 2: WeaselAvailable={refreshedEnvironment.WeaselAvailable}");
                UninstallTrace($"Step 3: Validate findings...");
                if (refreshedEnvironment.WeaselAvailable)
                {
                    DiagnosticReport uninstallFailed = BuildDiagnosticReport(
                        WorkflowPhases.Recheck,
                        WorkflowStatuses.Blocked,
                        [CreateFinding("WINDOWS_WEASEL_UNINSTALL_RECHECK_FAILED", "静默卸载结束后，程序仍检测到小狼毫部署器。卸载可能未成功完成。", relatedTaskId: "windows_launch_weasel_uninstaller")],
                        "none",
                        false,
                        false,
                        false);
                    _repositoryContext.PersistLastDiagnostic(uninstallFailed);
                    return CreateCommandResult(uninstallFailed, outputFormat);
                }

                IReadOnlyList<DiagnosticFinding> findings = WindowsEnvironmentService.Validate(refreshedEnvironment, refreshedModel, CreateFinding);
                bool hasPendingCleanupTargets = _repositoryContext.ResolvePendingWeaselUninstallTargets().Count > 0;
                bool onlyExpectedMissing = findings.Count == 1 &&
                                           string.Equals(findings[0].Code, "WINDOWS_WEASEL_MISSING", StringComparison.Ordinal) &&
                                           !hasPendingCleanupTargets;

                if (!onlyExpectedMissing && recheckResult.ExitCode != 0)
                {
                    return recheckResult;
                }

                if (onlyExpectedMissing)
                {
                    DiagnosticReport completedReport = BuildDiagnosticReport(
                        WorkflowPhases.Detect,
                        WorkflowStatuses.Completed,
                        [],
                        "none",
                        false,
                        false,
                        false);
                    _repositoryContext.PersistLastDiagnostic(completedReport);
                    _repositoryContext.PersistRecheckSummary("none", completedReport.Status, completedReport.Findings);
                }

                UninstallTrace($"===== SILENT MODE RETURN (ExitCode=0) =====");

                object silentResult = new
                {
                    platform = "windows",
                    phase = WorkflowPhases.Detect,
                    status = WorkflowStatuses.Completed,
                    uninstall_target = uninstallTarget,
                    uninstall_arguments = uninstallArguments,
                    execution_mode = "silent",
                    next_action = onlyExpectedMissing
                        ? "静默卸载已完成；程序已确认当前机器上不再检测到小狼毫。"
                        : nextAction,
                };

                return new CommandExecutionResult
                {
                    ExitCode = 0,
                    TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                        ? JsonSerializer.Serialize(silentResult, JsonOptions)
                        : string.Join(
                            Environment.NewLine,
                            [
                                $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                                $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                                $"卸载入口: {uninstallTarget}{(string.IsNullOrWhiteSpace(uninstallArguments) ? string.Empty : $" {uninstallArguments}")}",
                                $"卸载方式: {DescribeExecutionMode(useSilentMode)}",
                                $"下一步: {(onlyExpectedMissing ? "静默卸载已完成；程序已确认当前机器上不再检测到小狼毫。" : nextAction)}",
                            ]),
                    JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? silentResult : null,
                };
            }

            // 非静默模式不立即清理：卸载器尚在运行，文件可能被锁定。
            // 待卸载器完成后，由 RunUninstallAll 或 GUI OnActivated 的回检链路统一处理清理。

            object result = new
            {
                platform = "windows",
                phase = WorkflowPhases.Detect,
                status = WorkflowStatuses.Completed,
                uninstall_target = uninstallTarget,
                uninstall_arguments = uninstallArguments,
                execution_mode = "interactive",
                next_action = nextAction,
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(result, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"卸载入口: {uninstallTarget}{(string.IsNullOrWhiteSpace(uninstallArguments) ? string.Empty : $" {uninstallArguments}")}",
                            $"卸载方式: {DescribeExecutionMode(useSilentMode)}",
                            $"下一步: {nextAction}",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? result : null,
            };
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_WEASEL_UNINSTALL_LAUNCH_FAILED", $"启动 Weasel 卸载入口失败：{exception.Message}", relatedTaskId: "windows_launch_weasel_uninstaller")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public CommandExecutionResult RunSaveConfig(string configPath, ConfigModel model, string outputFormat)
    {
        List<DiagnosticFinding> findings = _configModelService.Validate(model, CreateFinding).ToList();
        if (findings.Count > 0)
        {
            DiagnosticReport invalidConfig = BuildDiagnosticReport(
                WorkflowPhases.Configure,
                WorkflowStatuses.Blocked,
                findings,
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(invalidConfig);
            return CreateCommandResult(invalidConfig, outputFormat);
        }

        string savedPath = _configModelService.Save(configPath, model);
        _repositoryContext.PersistCurrentConfigModel(model);

        object payload = new
        {
            platform = "windows",
            phase = WorkflowPhases.Configure,
            status = WorkflowStatuses.Completed,
            config_path = savedPath,
            next_action = "配置模型已保存，可继续执行生成或应用并部署。",
        };
        string textOutput = string.Join(
            Environment.NewLine,
            [
                $"阶段: {WorkflowPhases.Configure}",
                $"结果: {WorkflowStatuses.Completed}",
                $"配置文件: {savedPath}",
                "下一步: 配置模型已保存，可继续执行生成或应用并部署。",
            ]);

        return new CommandExecutionResult
        {
            ExitCode = 0,
            TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Serialize(payload, JsonOptions)
                : textOutput,
            JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
        };
    }

    public CommandExecutionResult RunDoctor(string? configPath, string outputFormat)
    {
        return RunReadonlyRecheck(configPath, outputFormat);
    }

    public CommandExecutionResult RunActivateWeaselProfile(string outputFormat)
    {
        TryActivateWeaselProfileInDetachedProcess();
        string? activationAttempt = _repositoryContext.ResolveStateReference("last_weasel_activation_attempt.txt");
        bool activationSucceeded = !string.IsNullOrWhiteSpace(activationAttempt)
            && activationAttempt.Contains("exited\t0", StringComparison.Ordinal);
        object payload = new
        {
            platform = "windows",
            phase = WorkflowPhases.Detect,
            status = activationSucceeded ? WorkflowStatuses.Completed : WorkflowStatuses.Blocked,
            next_action = activationSucceeded
                ? "已切换到小狼毫输入法配置。"
                : "未能确认是否已成功切换到小狼毫输入法配置，请手动切换后再试。",
            activation_detail = activationAttempt ?? "未产生回执",
        };

        return new CommandExecutionResult
        {
            ExitCode = activationSucceeded ? 0 : 1,
            TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Serialize(payload, JsonOptions)
                : string.Join(
                    Environment.NewLine,
                    [
                        $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                        $"结果: {StatusLabel(activationSucceeded ? WorkflowStatuses.Completed : WorkflowStatuses.Blocked)}",
                        activationSucceeded
                            ? "下一步: 已切换到小狼毫输入法配置。"
                            : "下一步: 未能确认是否已成功切换到小狼毫输入法配置，请手动切换。",
                    ]),
            JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
        };
    }

    public CommandExecutionResult RunPendingExternalFlowRecheck(string? configPath, string outputFormat)
    {
        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
        return controls.AutoRecheckOnReturn
            ? RunReadonlyRecheck(configPath, outputFormat)
            : new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(new
                    {
                        platform = "windows",
                        phase = WorkflowPhases.Detect,
                        status = WorkflowStatuses.Completed,
                        next_action = "已关闭返回后自动确认；请手动点击“检测”。",
                    }, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Detect)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            "下一步: 已关闭返回后自动确认；请手动点击“检测”。",
                        ]),
            };
    }

    public bool HasPendingWeaselOperation()
    {
        string? pendingInstaller = _repositoryContext.ResolveStateReference("pending_weasel_installer.txt");
        string? pendingUninstall = _repositoryContext.ResolveStateReference("pending_weasel_uninstall_targets.json");
        return !string.IsNullOrWhiteSpace(pendingInstaller) || !string.IsNullOrWhiteSpace(pendingUninstall);
    }

    public bool IsCarrierTargetDirectoryPresent(string? configPath)
    {
        ConfigModel configModel = LoadPreferredConfigModel(configPath, allowDefault: true);
        string targetRoot = RepositoryContext.ExpandPath(configModel.SyncSettings.WindowsTargetRoot);
        return Directory.Exists(targetRoot);
    }

    public bool IsWeaselAvailable()
    {
        return WindowsEnvironmentService.Detect(ConfigModel.CreateDefault()).WeaselAvailable;
    }

    public string ResolveTargetRootPath(string? configPath)
    {
        ConfigModel configModel = LoadPreferredConfigModel(configPath, allowDefault: true);
        return RepositoryContext.ExpandPath(configModel.SyncSettings.WindowsTargetRoot);
    }

    /// <summary>
    /// 纯只读的重新检测方法。仅执行检测和诊断，不做任何写入/删除/清理操作。
    /// 用于 doctor 命令和所有 GUI"检测"按钮。
    /// </summary>
    private CommandExecutionResult RunReadonlyRecheck(string? configPath, string outputFormat)
    {
        ConfigModel configModel = LoadPreferredConfigModel(configPath, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(configModel);

        List<DiagnosticFinding> findings = [];
        IReadOnlyList<DiagnosticFinding> environmentFindings = WindowsEnvironmentService.Validate(environment, configModel, CreateFinding);
        if (string.Equals(environment.ForegroundProcessName, "RimeKit.Windows.Gui.exe", StringComparison.OrdinalIgnoreCase))
        {
            environmentFindings = environmentFindings
                .Where(finding => !string.Equals(finding.Code, "WINDOWS_FOREGROUND_IME_CLOSED", StringComparison.Ordinal))
                .ToArray();
        }
        findings.AddRange(environmentFindings);

        if (environment.WeaselAvailable)
        {
            string[] missingRuntimeFiles = FindMissingWindowsRuntimeFiles(environment.WindowsTargetRoot);
            if (missingRuntimeFiles.Length > 0)
            {
                findings.Add(CreateFinding(
                    "WINDOWS_RUNTIME_FILES_MISSING",
                    $"当前输入法目录缺少关键运行文件：{string.Join("、", missingRuntimeFiles)}"));
            }
        }
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            findings.AddRange(_configModelService.Validate(configModel, CreateFinding));
        }

        List<DiagnosticFinding> blockingFindings = findings
            .Where(static finding => !string.Equals(finding.Severity, WorkflowSeverities.Warning, StringComparison.OrdinalIgnoreCase))
            .ToList();
        DiagnosticReport report = BuildDiagnosticReport(
            WorkflowPhases.Detect,
            blockingFindings.Count == 0
                ? findings.Count == 0 ? WorkflowStatuses.Completed : WorkflowStatuses.Completed
                : WorkflowStatuses.Blocked,
            findings,
            "none",
            false,
            false,
            blockingFindings.Count > 0);
        return CreateCommandResult(report, outputFormat);
    }

    /// <summary>
    /// 在安装/卸载等操作完成后执行待处理的清理任务并重新检测。
    /// 此方法会调用 FinalizePendingWeaselInstall 和 FinalizePendingWeaselUninstall，
    /// 可能执行破坏性操作（删除目录、清除 installed_resources.json 等）。
    /// 仅限安装/卸载成功回调调用，不得从检测/诊断路径调用。
    /// </summary>
    private CommandExecutionResult ApplyAndCleanupAfterPendingOperation(string? configPath, string outputFormat)
    {
        ConfigModel configModel = LoadPreferredConfigModel(configPath, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(configModel);
        bool hadPendingInstaller = !string.IsNullOrWhiteSpace(_repositoryContext.ResolveStateReference("pending_weasel_installer.txt"));
        FinalizePendingWeaselInstall(environment);
        DiagnosticFinding? uninstallCleanupFailure = FinalizePendingWeaselUninstall(configModel, environment);
        environment = WindowsEnvironmentService.Detect(configModel);
        if (environment.WeaselAvailable)
        {
            _repositoryContext.ClearStateReference(WeaselExpectedAbsentStateName);
        }
        _repositoryContext.PersistCurrentConfigModel(configModel);
        _repositoryContext.PersistRuntimePathCache(environment);

        List<DiagnosticFinding> findings = [];
        if (uninstallCleanupFailure is not null)
        {
            findings.Add(uninstallCleanupFailure);
        }
        IReadOnlyList<DiagnosticFinding> environmentFindings = WindowsEnvironmentService.Validate(environment, configModel, CreateFinding);
        if (string.Equals(environment.ForegroundProcessName, "RimeKit.Windows.Gui.exe", StringComparison.OrdinalIgnoreCase))
        {
            environmentFindings = environmentFindings
                .Where(finding => !string.Equals(finding.Code, "WINDOWS_FOREGROUND_IME_CLOSED", StringComparison.Ordinal))
                .ToArray();
        }
        bool expectedAbsent = string.Equals(_repositoryContext.ResolveStateReference(WeaselExpectedAbsentStateName), "1", StringComparison.Ordinal);
        if (expectedAbsent && !environment.WeaselAvailable)
        {
            environmentFindings = environmentFindings
                .Where(finding => !string.Equals(finding.Code, "WINDOWS_WEASEL_MISSING", StringComparison.Ordinal))
                .ToArray();
        }
        findings.AddRange(environmentFindings);
        if (!expectedAbsent && !hadPendingInstaller && environment.WeaselAvailable)
        {
            string[] missingRuntimeFiles = FindMissingWindowsRuntimeFiles(environment.WindowsTargetRoot);
            if (missingRuntimeFiles.Length > 0)
            {
                findings.Add(CreateFinding(
                    "WINDOWS_RUNTIME_FILES_MISSING",
                    $"当前输入法目录缺少关键运行文件：{string.Join("、", missingRuntimeFiles)}"));
            }
        }
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            findings.AddRange(_configModelService.Validate(configModel, CreateFinding));
        }

        List<DiagnosticFinding> blockingFindings = findings
            .Where(static finding => !string.Equals(finding.Severity, WorkflowSeverities.Warning, StringComparison.OrdinalIgnoreCase))
            .ToList();
        DiagnosticReport report = BuildDiagnosticReport(
            WorkflowPhases.Detect,
            blockingFindings.Count == 0
                ? findings.Count == 0 ? WorkflowStatuses.Completed : WorkflowStatuses.Completed
                : WorkflowStatuses.Blocked,
            findings,
            "none",
            false,
            false,
            blockingFindings.Count > 0);
        _repositoryContext.PersistLastDiagnostic(report);
        _repositoryContext.PersistRecheckSummary("none", report.Status, findings);
        return CreateCommandResult(report, outputFormat);
    }

    public CommandExecutionResult RunPrintConfig(string? configPath, string outputFormat)
    {
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        string targetRoot = RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot);
        WeaselUserSettings weasel = UserSettingsReader.ReadWeasel(targetRoot);
        MintUserSettings mint = UserSettingsReader.ReadMint(targetRoot, "rime_mint");
        object payload = new
        {
            platform = "windows",
            config_version = model.ConfigVersion,
            enabled_schema_ids = model.ProfileSettings.EnabledSchemaIds,
            windows_default_schema_id = model.ProfileSettings.WindowsDefaultSchemaId,
            android_default_schema_id = model.ProfileSettings.AndroidDefaultSchemaId,
            enabled_dictionary_ids = model.DictionarySettings.EnabledDictionaryIds,
            dictionary_order = model.DictionarySettings.DictionaryOrder,
            custom_entries_count = model.DictionarySettings.CustomEntries.Count,
            enabled_model_ids = model.ModelSettings.EnabledModelIds,
            active_model_id = model.ModelSettings.ActiveModelId,
            model_root = RepositoryContext.ExpandPath(model.ModelSettings.ModelRoot),
            windows_target_root = targetRoot,
            fuzzy_pinyin_preset_id = model.FuzzyPinyinSettings.PresetId,
            fuzzy_pinyin_target_schema_ids = model.FuzzyPinyinSettings.TargetSchemaIds,
            symbol_profile_id = model.PersonalizationSettings.SymbolProfileId,
            preedit_format_mode = model.PersonalizationSettings.PreeditFormatMode,
            dpi_scale_mode = model.WindowsSettings.DpiScaleMode,
            snapshot_retention_limit = model.SyncSettings.SnapshotRetentionLimit,
        };
        string textOutput = JsonSerializer.Serialize(payload, JsonOptions);
        return new CommandExecutionResult
        {
            ExitCode = 0,
            TextOutput = textOutput,
            JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
        };
    }

    public CommandExecutionResult RunSetConfig(string? configPath, string? field, string? value, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
        {
            DiagnosticFinding finding = CreateFinding("WINDOWS_SET_CONFIG_MISSING_ARGS",
                "set-config requires --field and --value");
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Configure, WorkflowStatuses.Blocked,
                [finding], "none", false, false, false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }

        string f = field.Trim();
        string v = value.Trim();

        if (f.StartsWith("windows_settings.", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("behavior_settings.", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("candidate_settings.", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("pinyin_settings.", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("personalization_settings.custom_phrase_mode", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("fuzzy_pinyin_settings.enabled", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("fuzzy_pinyin_settings.additional_rules", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
                string targetRoot = RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot);
                WeaselUserSettings weasel = UserSettingsReader.ReadWeasel(targetRoot);
                MintUserSettings mint = UserSettingsReader.ReadMint(targetRoot, "rime_mint");

                if (f.StartsWith("windows_settings.", StringComparison.OrdinalIgnoreCase))
                    weasel = ApplyWeaselField(weasel, f["windows_settings.".Length..], v);
                else
                    mint = ApplyMintField(mint, f, v);

                UserSettingsReader.WriteWeaselCrossLayer(targetRoot, weasel, mint);
                UserSettingsReader.WriteMint(targetRoot, "rime_mint", mint);

                return new CommandExecutionResult
                {
                    ExitCode = 0,
                    TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                        ? JsonSerializer.Serialize(new { status = "completed", field = f, updated = true }, JsonOptions)
                        : $"已设置: {f}",
                };
            }
            catch (Exception ex)
            {
                DiagnosticFinding finding = CreateFinding("WINDOWS_SET_CONFIG_FIELD_ERROR",
                    $"Failed to set field '{field}': {ex.Message}");
                DiagnosticReport report = BuildDiagnosticReport(
                    WorkflowPhases.Configure, WorkflowStatuses.Failed,
                    [finding], "none", false, false, false);
                _repositoryContext.PersistLastDiagnostic(report);
                return CreateCommandResult(report, outputFormat);
            }
        }

        ConfigModel jsonModel = LoadPreferredConfigModel(configPath, allowDefault: true);
        ConfigModel updated;
        try
        {
            updated = ApplyFieldOverride(jsonModel, f, v);
        }
        catch (Exception ex)
        {
            DiagnosticFinding finding = CreateFinding("WINDOWS_SET_CONFIG_FIELD_ERROR",
                $"Failed to set field '{field}': {ex.Message}");
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Configure, WorkflowStatuses.Failed,
                [finding], "none", false, false, false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }

        _configModelService.Save(ResolveMutableConfigPath(configPath), updated);
        _repositoryContext.PersistCurrentConfigModel(updated);
        return new CommandExecutionResult
        {
            ExitCode = 0,
            TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Serialize(new { status = "completed", field = f, updated = true }, JsonOptions)
                : $"已设置: {f}",
        };
    }

    private static WeaselUserSettings ApplyWeaselField(WeaselUserSettings s, string field, string rawValue)
    {
        bool bOk = bool.TryParse(rawValue, out bool bv);
        bool nOk = int.TryParse(rawValue, out int nv);
        return field.ToLowerInvariant() switch
        {
            "color_scheme" => s with { ColorScheme = rawValue.Length > 0 ? rawValue : null },
            "color_scheme_dark" => s with { ColorSchemeDark = rawValue.Length > 0 ? rawValue : null },
            "font_face" => s with { FontFace = rawValue.Length > 0 ? rawValue : null },
            "font_point" => nOk ? s with { FontPoint = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "label_font_face" => s with { LabelFontFace = rawValue.Length > 0 ? rawValue : null },
            "label_font_point" => nOk ? s with { LabelFontPoint = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "comment_font_face" => s with { CommentFontFace = rawValue.Length > 0 ? rawValue : null },
            "comment_font_point" => nOk ? s with { CommentFontPoint = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "show_notification" => bOk ? s with { ShowNotification = bv } : throw BoolError(field, rawValue),
            "notification_time_ms" => nOk ? s with { NotificationTimeMs = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "label_format" => s with { LabelFormat = rawValue.Length > 0 ? rawValue : null },
            "mark_text" => s with { MarkText = rawValue.Length > 0 ? rawValue : null },
            "paging_on_scroll" => bOk ? s with { PagingOnScroll = bv } : throw BoolError(field, rawValue),
            "candidate_abbreviate_length" => nOk ? s with { CandidateAbbreviateLength = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "fullscreen" => bOk ? s with { Fullscreen = bv } : throw BoolError(field, rawValue),
            "vertical_text" => bOk ? s with { VerticalText = bv } : throw BoolError(field, rawValue),
            "vertical_text_left_to_right" => bOk ? s with { VerticalTextLeftToRight = bv } : throw BoolError(field, rawValue),
            "vertical_text_with_wrap" => bOk ? s with { VerticalTextWithWrap = bv } : throw BoolError(field, rawValue),
            "vertical_auto_reverse" => bOk ? s with { VerticalAutoReverse = bv } : throw BoolError(field, rawValue),
            "inline_preedit" => bOk ? s with { InlinePreedit = bv } : throw BoolError(field, rawValue),
            "preedit_type" => s with { PreeditType = rawValue.Length > 0 ? rawValue : null },
            "global_ascii" => bOk ? s with { GlobalAscii = bv } : throw BoolError(field, rawValue),
            "hover_type" => s with { HoverType = rawValue.Length > 0 ? rawValue : null },
            "click_to_capture" => bOk ? s with { ClickToCapture = bv } : throw BoolError(field, rawValue),
            "antialias_mode" => s with { AntialiasMode = rawValue.Length > 0 ? rawValue : null },
            "display_tray_icon" => bOk ? s with { DisplayTrayIcon = bv } : throw BoolError(field, rawValue),
            "enhanced_position" => bOk ? s with { EnhancedPosition = bv } : throw BoolError(field, rawValue),
            "ascii_tip_follow_cursor" => bOk ? s with { AsciiTipFollowCursor = bv } : throw BoolError(field, rawValue),
            "layout_min_width" => nOk ? s with { LayoutMinWidth = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "layout_max_width" => nOk ? s with { LayoutMaxWidth = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "layout_min_height" => nOk ? s with { LayoutMinHeight = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "layout_max_height" => nOk ? s with { LayoutMaxHeight = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "layout_margin_x" => nOk ? s with { LayoutMarginX = nv } : throw IntError(field, rawValue),
            "layout_margin_y" => nOk ? s with { LayoutMarginY = nv } : throw IntError(field, rawValue),
            "layout_border_width" => nOk ? s with { LayoutBorderWidth = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "layout_linespacing" => nOk ? s with { LayoutLineSpacing = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "layout_baseline" => nOk ? s with { LayoutBaseline = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "layout_align_type" => s with { LayoutAlignType = rawValue.Length > 0 ? rawValue : null },
            "layout_spacing" => nOk ? s with { LayoutSpacing = nv } : throw IntError(field, rawValue),
            "layout_candidate_spacing" => nOk ? s with { LayoutCandidateSpacing = nv } : throw IntError(field, rawValue),
            "layout_hilite_spacing" => nOk ? s with { LayoutHiliteSpacing = nv } : throw IntError(field, rawValue),
            "layout_hilite_padding" => nOk ? s with { LayoutHilitePadding = nv } : throw IntError(field, rawValue),
            "layout_hilite_padding_x" => nOk ? s with { LayoutHilitePaddingX = nv } : throw IntError(field, rawValue),
            "layout_hilite_padding_y" => nOk ? s with { LayoutHilitePaddingY = nv } : throw IntError(field, rawValue),
            "layout_shadow_radius" => nOk ? s with { LayoutShadowRadius = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "layout_shadow_offset_x" => nOk ? s with { LayoutShadowOffsetX = nv } : throw IntError(field, rawValue),
            "layout_shadow_offset_y" => nOk ? s with { LayoutShadowOffsetY = nv } : throw IntError(field, rawValue),
            "layout_corner_radius" => nOk ? s with { LayoutCornerRadius = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "day_text_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { TextColor = rawValue } },
            "day_candidate_text_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { CandidateTextColor = rawValue } },
            "day_label_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { LabelColor = rawValue } },
            "day_comment_text_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { CommentTextColor = rawValue } },
            "day_back_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { BackColor = rawValue } },
            "day_candidate_back_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { CandidateBackColor = rawValue } },
            "day_border_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { BorderColor = rawValue } },
            "day_shadow_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { ShadowColor = rawValue } },
            "day_hilited_text_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedTextColor = rawValue } },
            "day_hilited_back_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedBackColor = rawValue } },
            "day_hilited_label_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedLabelColor = rawValue } },
            "day_hilited_candidate_text_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedCandidateTextColor = rawValue } },
            "day_hilited_candidate_back_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedCandidateBackColor = rawValue } },
            "day_hilited_candidate_label_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedCandidateLabelColor = rawValue } },
            "day_hilited_candidate_border_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedCandidateBorderColor = rawValue } },
            "day_hilited_comment_text_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedCommentTextColor = rawValue } },
            "day_hilited_mark_color" => s with { DayColors = EnsureDayColors(s.DayColors) with { HilitedMarkColor = rawValue } },
            "night_text_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { TextColor = rawValue } },
            "night_candidate_text_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { CandidateTextColor = rawValue } },
            "night_label_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { LabelColor = rawValue } },
            "night_comment_text_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { CommentTextColor = rawValue } },
            "night_back_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { BackColor = rawValue } },
            "night_candidate_back_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { CandidateBackColor = rawValue } },
            "night_border_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { BorderColor = rawValue } },
            "night_shadow_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { ShadowColor = rawValue } },
            "night_hilited_text_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedTextColor = rawValue } },
            "night_hilited_back_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedBackColor = rawValue } },
            "night_hilited_label_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedLabelColor = rawValue } },
            "night_hilited_candidate_text_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedCandidateTextColor = rawValue } },
            "night_hilited_candidate_back_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedCandidateBackColor = rawValue } },
            "night_hilited_candidate_label_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedCandidateLabelColor = rawValue } },
            "night_hilited_candidate_border_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedCandidateBorderColor = rawValue } },
            "night_hilited_comment_text_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedCommentTextColor = rawValue } },
            "night_hilited_mark_color" => s with { NightColors = EnsureNightColors(s.NightColors) with { HilitedMarkColor = rawValue } },
            _ => throw new InvalidOperationException($"不支持的 weasel 设置字段: '{field}'。"),
        };
    }

    private static MintUserSettings ApplyMintField(MintUserSettings s, string field, string rawValue)
    {
        bool bOk = bool.TryParse(rawValue, out bool bv);
        bool nOk = int.TryParse(rawValue, out int nv);

        if (string.Equals(field, "fuzzy_pinyin_settings.additional_rules", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<string> rules = ParseFuzzyRulesArg(rawValue);
            return rules.Count > 0
                ? s with { FuzzyAdditionalRules = rules, FuzzyEnabled = true }
                : s with { FuzzyAdditionalRules = rules };
        }

        return field.ToLowerInvariant() switch
        {
            "behavior_settings.simplification_mode" => s with { SimplificationMode = rawValue },
            "behavior_settings.full_shape_enabled" => bOk ? s with { FullShapeEnabled = bv } : throw BoolError(field, rawValue),
            "behavior_settings.ascii_punct_enabled" => bOk ? s with { AsciiPunctEnabled = bv } : throw BoolError(field, rawValue),
            "behavior_settings.emoji_suggestion_enabled" => bOk ? s with { EmojiSuggestionEnabled = bv } : throw BoolError(field, rawValue),
            "behavior_settings.tone_display_enabled" => bOk ? s with { ToneDisplayEnabled = bv } : throw BoolError(field, rawValue),
            "behavior_settings.enable_user_dict" => bOk ? s with { EnableUserDict = bv } : throw BoolError(field, rawValue),
            "candidate_settings.page_size" => nOk ? s with { PageSize = nv > 0 ? nv : null } : throw IntError(field, rawValue),
            "candidate_settings.layout" => s with { Layout = rawValue.Length > 0 ? rawValue : null },
            "candidate_settings.show_emoji_comments" => bOk ? s with { ShowEmojiComments = bv } : throw BoolError(field, rawValue),
            "pinyin_settings.ue_compat_enabled" => bOk ? s with { UeCompatEnabled = bv } : throw BoolError(field, rawValue),
            "fuzzy_pinyin_settings.enabled" => bOk ? s with { FuzzyEnabled = bv ? true : null } : throw BoolError(field, rawValue),
            "personalization_settings.custom_phrase_mode" => s with { CustomPhraseMode = rawValue },
            _ => throw new InvalidOperationException($"不支持的 mint 设置字段: '{field}'。"),
        };
    }

    private static IReadOnlyList<string> ParseFuzzyRulesArg(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return [];
        string[]? rules = JsonSerializer.Deserialize<string[]>(rawValue);
        if (rules is null)
            throw new InvalidOperationException($"无法解析模糊音规则参数：{rawValue}");
        return rules;
    }

    private static Exception BoolError(string field, string value) =>
        new InvalidOperationException($"字段 '{field}' 的值 '{value}' 不是有效布尔值。请使用 true/false 或 1/0。");

    private static Exception IntError(string field, string value) =>
        new InvalidOperationException($"字段 '{field}' 的值 '{value}' 不是有效整数。");

    private static SchemeColors EnsureDayColors(SchemeColors? c) => c ?? new SchemeColors();

    private static SchemeColors EnsureNightColors(SchemeColors? c) => c ?? new SchemeColors();

    private static ConfigModel ApplyFieldOverride(ConfigModel model, string fieldPath, string rawValue)
    {
        // Use System.Text.Json to modify the JSON tree, then deserialize back
        string modelJson = JsonSerializer.Serialize(model, JsonOptions);
        using JsonDocument doc = JsonDocument.Parse(modelJson);
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        WriteWithOverride(writer, doc.RootElement, fieldPath.Split('.'), 0, rawValue);
        writer.Flush();
        string updatedJson = Encoding.UTF8.GetString(stream.ToArray());
        return JsonSerializer.Deserialize<ConfigModel>(updatedJson, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize updated config model");
    }

    private static void WriteWithOverride(Utf8JsonWriter writer, JsonElement element, string[] path, int depth, string overrideValue)
    {
        if (depth >= path.Length)
        {
            element.WriteTo(writer);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            element.WriteTo(writer);
            return;
        }

        string targetKey = path[depth];
        bool isLast = (depth == path.Length - 1);

        writer.WriteStartObject();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, targetKey, StringComparison.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(property.Name);
                if (isLast)
                {
                    // Write the override value — determine type from existing element
                    WriteOverrideValue(writer, property.Value, overrideValue);
                }
                else
                {
                    WriteWithOverride(writer, property.Value, path, depth + 1, overrideValue);
                }
            }
            else
            {
                property.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteOverrideValue(Utf8JsonWriter writer, JsonElement existingElement, string rawValue)
    {
        switch (existingElement.ValueKind)
        {
            case JsonValueKind.String:
                writer.WriteStringValue(rawValue);
                break;
            case JsonValueKind.Number:
                if (int.TryParse(rawValue, out int intVal))
                    writer.WriteNumberValue(intVal);
                else if (double.TryParse(rawValue, out double dblVal))
                    writer.WriteNumberValue(dblVal);
                else
                    throw new InvalidOperationException($"无法将 '{rawValue}' 转换为数值以覆写字段。");
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (!bool.TryParse(rawValue, out bool b))
                    throw new InvalidOperationException($"无法将 '{rawValue}' 转换为布尔值以覆写字段。");
                writer.WriteBooleanValue(b);
                break;
            case JsonValueKind.Array:
                using (JsonDocument arrDoc = JsonDocument.Parse(rawValue))
                {
                    arrDoc.RootElement.WriteTo(writer);
                }
                break;
            case JsonValueKind.Null:
                if (int.TryParse(rawValue, out int nullInt))
                    writer.WriteNumberValue(nullInt);
                else if (double.TryParse(rawValue, out double nullDbl))
                    writer.WriteNumberValue(nullDbl);
                else if (bool.TryParse(rawValue, out bool nullBool))
                    writer.WriteBooleanValue(nullBool);
                else
                    writer.WriteStringValue(rawValue);
                break;
            default:
                writer.WriteStringValue(rawValue);
                break;
        }
    }

    public CommandExecutionResult RunListCustomEntries(string? configPath, string outputFormat)
    {
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        var entries = model.DictionarySettings.CustomEntries.Select(e => new { text = e.Text, code = e.Code, weight = e.Weight }).ToList();
        return new CommandExecutionResult
        {
            ExitCode = 0,
            TextOutput = JsonSerializer.Serialize(new { custom_entries = entries }, JsonOptions),
        };
    }

    public CommandExecutionResult RunAddCustomEntry(string? configPath, string? text, string? code, int weight, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(code))
        {
            DiagnosticFinding finding = CreateFinding("WINDOWS_ADD_CUSTOM_ENTRY_MISSING_ARGS", "add-custom-entry requires --text and --code");
            DiagnosticReport report = BuildDiagnosticReport(WorkflowPhases.Configure, WorkflowStatuses.Blocked, [finding], "none", false, false, false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        List<CustomEntry> entries = [.. model.DictionarySettings.CustomEntries];
        entries.Add(new CustomEntry { Text = text, Code = code, Weight = Math.Max(1, weight) });
        ConfigModel updated = ApplyCustomEntriesOverride(model, entries.AsReadOnly());
        updated = EnsureCustomSimpleEnabled(updated, entries.AsReadOnly());
        _configModelService.Save(ResolveMutableConfigPath(configPath), updated);
        _repositoryContext.PersistCurrentConfigModel(updated);
        return new CommandExecutionResult { ExitCode = 0, TextOutput = $"added custom entry: {text} ({code})" };
    }

    public CommandExecutionResult RunDeleteCustomEntry(string? configPath, string? text, string? code, string outputFormat)
    {
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        List<CustomEntry> entries = [.. model.DictionarySettings.CustomEntries];
        int beforeCount = entries.Count;
        entries.RemoveAll(e => string.Equals(e.Text, text, StringComparison.Ordinal) && string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase));
        int removedCount = beforeCount - entries.Count;
        ConfigModel updated = ApplyCustomEntriesOverride(model, entries.AsReadOnly());
        _configModelService.Save(ResolveMutableConfigPath(configPath), updated);
        _repositoryContext.PersistCurrentConfigModel(updated);
        if (removedCount == 0)
        {
            return new CommandExecutionResult { ExitCode = 1, TextOutput = $"custom entry not found: {text} ({code})" };
        }
        return new CommandExecutionResult { ExitCode = 0, TextOutput = $"deleted custom entry: {text} ({code})" };
    }

    public CommandExecutionResult RunApplyCustomEntries(string? configPath, string outputFormat)
    {
        return RunApplyCustomEntries(configPath, outputFormat, forceStopWeasel: false);
    }

    public CommandExecutionResult RunApplyCustomEntries(string? configPath, string outputFormat, bool forceStopWeasel)
    {
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        if (model.DictionarySettings.CustomEntries.Count == 0)
        {
            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Configure,
                status = WorkflowStatuses.Completed,
                custom_entries_count = 0,
                next_action = "当前没有自定义词条，无需执行 apply-custom-entries。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Configure)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            "自定义词条数: 0",
                            "下一步: 当前没有自定义词条，无需执行 apply-custom-entries。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }

        return RunApply(configPath, outputFormat, forceStopWeasel);
    }

    public CommandExecutionResult RunResetConfig(string? configPath, string outputFormat)
    {
        ConfigModel defaultModel = ConfigModel.CreateDefault();
        return RunSaveConfig(configPath ?? _repositoryContext.CurrentConfigModelPath, defaultModel, outputFormat);
    }

    public CommandExecutionResult RunUninstallAll(string? configPath, string outputFormat, Action<string>? phase = null)
    {
        try
        {
            var uninstalledResources = new List<string>();
            var errors = new List<string>();

            phase?.Invoke("正在清理资源…");
            var installedSchemas = _resourceUpdateService.GetInstalledResourceIds("schema").ToList();
            var installedDictionaries = _resourceUpdateService.GetInstalledResourceIds("dictionary").ToList();
            var installedModels = _resourceUpdateService.GetInstalledResourceIds("model").ToList();
            var allInstalled = new List<string>();
            allInstalled.AddRange(installedSchemas);
            allInstalled.AddRange(installedDictionaries);
            allInstalled.AddRange(installedModels);

            foreach (string resourceId in allInstalled)
            {
                try
                {
                    if (IsFormalManagedResource(resourceId))
                    {
                        _resourceUpdateService.UninstallResource(resourceId);
                        uninstalledResources.Add(resourceId);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"无法卸载 {resourceId}: {ex.Message}");
                }
            }

            bool weaselUninstalled = false;
            bool weaselCleanupFailed = false;
            try
            {
                _repositoryContext.PersistPendingWeaselUninstallTargets(
                [
                    RepositoryContext.ExpandPath(ConfigModel.CreateDefault().SyncSettings.WindowsTargetRoot),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rime"),
                ]);

                WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(ConfigModel.CreateDefault());
                if (environment.WeaselAvailable)
                {
                    phase?.Invoke("正在卸载小狼毫…");
                    CommandExecutionResult uninstallResult = RunLaunchWeaselUninstaller(outputFormat, phase);
                    weaselUninstalled = uninstallResult.ExitCode == 0;
                    if (!weaselUninstalled)
                    {
                        errors.Add("承载器卸载未成功完成");
                    }
                }
                else
                {
                    weaselUninstalled = true;
                }

                if (weaselUninstalled)
                {
                    UninstallTrace("RunUninstallAll: weaselUninstalled=true, calling ApplyAndCleanupAfterPendingOperation...");
                    CommandExecutionResult recheckResult = ApplyAndCleanupAfterPendingOperation(null, outputFormat);
                    UninstallTrace($"RunUninstallAll: recheck done, ExitCode={recheckResult.ExitCode}");
                }

                {
                    UninstallTrace("RunUninstallAll: unconditional FinalizePendingWeaselUninstall...");
                    ConfigModel postCleanupModel = LoadPreferredConfigModel(null, allowDefault: true);
                    WindowsEnvironmentState postCleanupEnv = WindowsEnvironmentService.Detect(postCleanupModel);
                    DiagnosticFinding? cleanupFailure = FinalizePendingWeaselUninstall(postCleanupModel, postCleanupEnv);
                    System.Diagnostics.Debug.WriteLine(cleanupFailure is null
                        ? "[UninstallTrace] RunUninstallAll: unconditional Finalize OK"
                        : $"[UninstallTrace] RunUninstallAll: unconditional Finalize FAILED: {cleanupFailure.Summary}");
                    if (cleanupFailure is not null)
                    {
                        weaselCleanupFailed = true;
                        errors.Add($"残留目录清理失败: {cleanupFailure.Summary}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"无法卸载承载器: {ex.Message}");
            }

            try
            {
                phase?.Invoke("正在清理工作区…");
                CleanWorkspaceStateInternal();
                TemplateService.DeleteAllTemplates();
            }
            catch (Exception ex)
            {
                errors.Add($"无法清理工作区状态: {ex.Message}");
            }

            bool configReset = false;
            try
            {
                ConfigModel defaultModel = new()
                {
                    ProfileSettings = new()
                    {
                        EnabledSchemaIds = Array.Empty<string>(),
                        WindowsDefaultSchemaId = "",
                        AndroidDefaultSchemaId = "t9",
                    },
                };
                string effectiveConfigPath = ResolveMutableConfigPath(configPath);
                _configModelService.Save(effectiveConfigPath, defaultModel);
                _repositoryContext.PersistCurrentConfigModel(defaultModel);
                configReset = true;
            }
            catch (Exception ex)
            {
                errors.Add($"无法重置配置: {ex.Message}");
            }

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Apply,
                status = WorkflowStatuses.Completed,
                uninstalled_resources = uninstalledResources,
                config_reset = configReset,
                weasel_uninstalled = weaselUninstalled,
                workspace_cleaned = true,
                weasel_cleanup_failed = weaselCleanupFailed,
                errors = errors,
                detail = errors.Count > 0
                    ? $"部分操作未成功：{string.Join("; ", errors)}"
                    : "所有组件已完全清理并卸载。",
            };
            return new CommandExecutionResult
            {
                ExitCode = errors.Count > 0 ? 1 : 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Apply)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"已卸载资源: {(uninstalledResources.Count > 0 ? string.Join(", ", uninstalledResources) : "无")}",
                            $"配置已重置: {(configReset ? "是" : "否")}",
                            $"承载器已卸载: {(weaselUninstalled ? "是 (或未安装)" : "否")}",
                            $"工作区已清理: 是",
                            ..errors.Select(e => $"错误: {e}"),
                            "下一步: 所有韵匣组件已完全清理并卸载。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Apply,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_UNINSTALL_ALL_FAILED", $"完全清理失败：{exception.Message}", relatedTaskId: "windows_diagnose_result")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    private void CleanWorkspaceStateInternal()
    {
        string stateDir = _repositoryContext.StateRoot;
        string[] stateFiles =
        [
            "current_config_model.json",
            "installed_resources.json",
            "last_diagnostic.json",
            "last_recheck_summary.json",
            "runtime_paths.json",
            "windows_runtime_controls.json",
            "latest_sync_status.json",
            "latest_backup_status.json",
            "last_conflict_recovery_decision.txt",
            "pending_weasel_installer.txt",
            WeaselExpectedAbsentStateName,
            "pending_weasel_uninstall_targets.json",
            "last_weasel_activation_attempt.txt",
            "latest_generated_snapshot.txt",
            "latest_successful_snapshot.txt",
            "latest_backup.txt",
            "last_android_sync_endpoint.txt",
            "last_resource_update_report.json",
            "last_resource_install_report.json",
            "last_resource_uninstall_report.json",
            ElevatedCleanupScriptName,
            ElevatedCleanupResultName,
        ];
        foreach (string name in stateFiles)
        {
            DeletePathBestEffort(Path.Combine(stateDir, name));
        }

        if (Directory.Exists(stateDir))
        {
            foreach (string dir in Directory.GetDirectories(stateDir))
            {
                DeletePathBestEffort(dir);
            }
        }

        string resourcesDir = _repositoryContext.ResourcesRoot;
        if (Directory.Exists(resourcesDir))
        {
            foreach (string dir in Directory.GetDirectories(resourcesDir))
            {
                DeletePathBestEffort(dir);
            }
        }

        string downloadsDir = _repositoryContext.DownloadsRoot;
        if (Directory.Exists(downloadsDir))
        {
            foreach (string file in Directory.GetFiles(downloadsDir))
            {
                DeletePathBestEffort(file);
            }
            foreach (string dir in Directory.GetDirectories(downloadsDir))
            {
                DeletePathBestEffort(dir);
            }
        }
    }

    private static void DeletePathBestEffort(string path)
    {
        try
        {
            DeletePathWithRetry(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CleanWorkspace] 清理失败: {path} — {ex.Message}");
        }
    }

    private static void DeletePathWithRetry(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string resolvedPath = Path.GetFullPath(path);
        if (Directory.Exists(resolvedPath))
        {
            Utilities.FileHelper.DeleteDirectoryWithBackoff(
                resolvedPath,
                maxRetries: 10,
                baseDelayMs: 200,
                maxDelayMs: 5000);
        }
        else if (File.Exists(resolvedPath))
        {
            Utilities.FileHelper.DeleteFileWithBackoff(
                resolvedPath,
                maxRetries: 10,
                baseDelayMs: 100,
                maxDelayMs: 3000);
        }
    }

    public CommandExecutionResult RunResourceStatus(string? configPath, string outputFormat)
    {
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        var installedSchemas = _resourceUpdateService.GetInstalledResourceIds("schema");
        var installedDictionaries = _resourceUpdateService.GetInstalledResourceIds("dictionary");
        var installedModels = _resourceUpdateService.GetInstalledResourceIds("model");
        object payload = new
        {
            platform = "windows",
            installed_schemas = installedSchemas,
            installed_dictionaries = installedDictionaries,
            installed_models = installedModels,
            enabled_schemas = model.ProfileSettings.EnabledSchemaIds,
            enabled_dictionaries = model.DictionarySettings.EnabledDictionaryIds,
            enabled_models = model.ModelSettings.EnabledModelIds,
        };
        return new CommandExecutionResult
        {
            ExitCode = 0,
            TextOutput = JsonSerializer.Serialize(payload, JsonOptions),
        };
    }

    private static ConfigModel ApplyListOverride(ConfigModel model, string fieldPath, List<string> newList)
    {
        string modelJson = JsonSerializer.Serialize(model, JsonOptions);
        using JsonDocument doc = JsonDocument.Parse(modelJson);
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        WriteWithListOverride(writer, doc.RootElement, fieldPath.Split('.'), 0, newList);
        writer.Flush();
        return JsonSerializer.Deserialize<ConfigModel>(stream.ToArray(), JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize config model after list override");
    }

    private static ConfigModel EnsureCustomSimpleEnabled(ConfigModel model, IReadOnlyList<CustomEntry> entries)
    {
        if (entries.Count == 0)
        {
            return model;
        }

        bool hasCustomSimple = model.DictionarySettings.EnabledDictionaryIds
            .Contains("custom_simple", StringComparer.OrdinalIgnoreCase);
        if (hasCustomSimple)
        {
            return model;
        }

        List<string> enabledDictIds = [.. model.DictionarySettings.EnabledDictionaryIds, "custom_simple"];
        List<string> dictOrder = [.. model.DictionarySettings.DictionaryOrder, "custom_simple"];
        return new ConfigModel
        {
            ConfigVersion = model.ConfigVersion,
            ProfileSettings = model.ProfileSettings,
            FuzzyPinyinSettings = model.FuzzyPinyinSettings,
            PersonalizationSettings = model.PersonalizationSettings,
            DictionarySettings = new DictionarySettings
            {
                EnabledDictionaryIds = enabledDictIds,
                DictionaryOrder = dictOrder,
                CustomEntries = model.DictionarySettings.CustomEntries,
            },
            ModelSettings = model.ModelSettings,
            AndroidSettings = model.AndroidSettings,
            WindowsSettings = model.WindowsSettings,
            SyncSettings = model.SyncSettings,

        };
    }

    private static ConfigModel ApplyCustomEntriesOverride(ConfigModel model, IReadOnlyList<CustomEntry> entries)
    {
        string modelJson = JsonSerializer.Serialize(model, JsonOptions);
        using JsonDocument doc = JsonDocument.Parse(modelJson);
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        WriteWithCustomEntriesOverride(writer, doc.RootElement, entries);
        writer.Flush();
        return JsonSerializer.Deserialize<ConfigModel>(stream.ToArray(), JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize config model after custom entries override");
    }

    private static void WriteWithListOverride(Utf8JsonWriter writer, JsonElement element, string[] path, int depth, List<string> newList)
    {
        if (element.ValueKind != JsonValueKind.Object) { element.WriteTo(writer); return; }
        string targetKey = path[depth];
        bool isLast = depth == path.Length - 1;
        writer.WriteStartObject();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);
            if (string.Equals(property.Name, targetKey, StringComparison.OrdinalIgnoreCase) && isLast)
            {
                writer.WriteStartArray();
                foreach (string item in newList) writer.WriteStringValue(item);
                writer.WriteEndArray();
            }
            else if (string.Equals(property.Name, targetKey, StringComparison.OrdinalIgnoreCase))
            {
                WriteWithListOverride(writer, property.Value, path, depth + 1, newList);
            }
            else
            {
                property.Value.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteWithCustomEntriesOverride(Utf8JsonWriter writer, JsonElement element, IReadOnlyList<CustomEntry> entries)
    {
        if (element.ValueKind != JsonValueKind.Object) { element.WriteTo(writer); return; }
        writer.WriteStartObject();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);
            if (string.Equals(property.Name, "dictionary_settings", StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteStartObject();
                foreach (JsonProperty dictProp in property.Value.EnumerateObject())
                {
                    writer.WritePropertyName(dictProp.Name);
                    if (string.Equals(dictProp.Name, "custom_entries", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteStartArray();
                        foreach (CustomEntry e in entries)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("text", e.Text);
                            writer.WriteString("code", e.Code);
                            writer.WriteNumber("weight", e.Weight);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }
                    else
                    {
                        dictProp.Value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            else
            {
                property.Value.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    private static string[] FindMissingWindowsRuntimeFiles(string windowsTargetRoot)
    {
        string[] requiredRuntimeFiles =
        [
            "default.custom.yaml",
            "rime_mint.custom.yaml",
            "weasel.custom.yaml",
            "rime_mint.dict.yaml",
            "rime_mint.schema.yaml",
        ];
        return requiredRuntimeFiles
            .Where(fileName => !File.Exists(Path.Combine(windowsTargetRoot, fileName)))
            .ToArray();
    }

    public CommandExecutionResult RunRecordGuiEntryFailure(
        string phase,
        string taskId,
        string code,
        string detail,
        string outputFormat)
    {
        DiagnosticReport report = BuildDiagnosticReport(
            phase,
            WorkflowStatuses.Failed,
            [CreateFinding(code, detail, relatedTaskId: taskId)],
            "none",
            false,
            false,
            false);
        _repositoryContext.PersistLastDiagnostic(report);
        return CreateCommandResult(report, outputFormat);
    }

    public CommandExecutionResult RunCheckResourceUpdates(string outputFormat)
    {
        try
        {
            string report = _resourceUpdateService.CheckForUpdates();
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = report,
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Deserialize<object>(report, JsonOptions)
                    : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or HttpRequestException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Diagnose,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_RESOURCE_UPDATE_FAILED", $"资源更新检查失败：{exception.Message}")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    private string? ResolveLatestLocalSnapshotId()
    {
        string? snapshotId = _repositoryContext.ResolveStateReference("latest_successful_snapshot.txt")
            ?? _repositoryContext.ResolveStateReference("latest_generated_snapshot.txt");
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            return null;
        }

        return Directory.Exists(Path.Combine(_repositoryContext.SnapshotsRoot, snapshotId))
            ? snapshotId
            : null;
    }

    private string EnsureLatestSnapshotForSync(string? configPath)
    {
        string? existingSnapshotId = ResolveLatestLocalSnapshotId();
        if (!string.IsNullOrWhiteSpace(existingSnapshotId))
        {
            return existingSnapshotId;
        }

        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        List<DiagnosticFinding> findings = _configModelService.Validate(model, CreateFinding).ToList();
        if (findings.Count > 0)
        {
            throw new InvalidOperationException($"当前配置模型还不能生成同步快照：{findings[0].Detail}");
        }

        string snapshotId = RepositoryContext.CreateOperationId("windows");
        _artifactService.Generate(model, snapshotId);
        _repositoryContext.PersistCurrentConfigModel(model);
        _repositoryContext.PersistStateReference("latest_generated_snapshot.txt", snapshotId);
        return snapshotId;
    }

    public CommandExecutionResult RunImportRuntime(string outputFormat)
    {
        ConfigModel currentModel = LoadPreferredConfigModel(null, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(currentModel);
        _repositoryContext.PersistRuntimePathCache(environment);
        if (!environment.TargetRootAccessible)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Blocked,
                [CreateFinding("WINDOWS_TARGET_ROOT_INVALID", $"Windows 目标目录不可访问：{environment.WindowsTargetRoot}")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }

        ConfigModel importedModel = _artifactService.ImportRuntimeToConfig(environment.WindowsTargetRoot, currentModel);
        _repositoryContext.PersistCurrentConfigModel(importedModel);
        _repositoryContext.PersistConflictRecoveryDecision("import_runtime");

        DiagnosticReport success = BuildDiagnosticReport(
            WorkflowPhases.Diagnose,
            WorkflowStatuses.Completed,
            [],
            "none",
            false,
            false,
            false);
        _repositoryContext.PersistLastDiagnostic(success);
        return CreateCommandResult(success, outputFormat);
    }

    public CommandExecutionResult RunOverrideWithGui(string? configPath, string outputFormat)
    {
        return RunOverrideWithGui(configPath, outputFormat, forceStopWeasel: false);
    }

    public CommandExecutionResult RunOverrideWithGui(string? configPath, string outputFormat, bool forceStopWeasel)
    {
        if (forceStopWeasel)
        {
            ConfigModel defaultModel = ConfigModel.CreateDefault();
            WindowsEnvironmentState env = WindowsEnvironmentService.Detect(defaultModel);
            TryStopWeaselRuntime(env);
        }

        ConfigModel configModel;
        try
        {
            configModel = LoadPreferredConfigModel(configPath, allowDefault: false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Configure,
                WorkflowStatuses.Blocked,
                [CreateFinding("CONFIG_MODEL_SCHEMA_INVALID", exception.Message)],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }

        _repositoryContext.PersistConflictRecoveryDecision("override_with_gui");
        return ExecuteApplyWorkflow(
            configModel: configModel,
            environment: default!,
            outputFormat: outputFormat,
            snapshotId: null,
            windowsOutputFiles: null,
            windowsBinaryOutputFiles: null,
            userDictionaryFiles: null,
            createArtifacts: true);
    }

    public CommandExecutionResult RunApply(string? configPath, string outputFormat)
    {
        return RunApply(configPath, outputFormat, forceStopWeasel: false);
    }

    public CommandExecutionResult RunApply(string? configPath, string outputFormat, bool forceStopWeasel, Action<string>? phase = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            DiagnosticReport blocked = BuildDiagnosticReport(
                WorkflowPhases.Configure,
                WorkflowStatuses.Blocked,
                [
                    CreateFinding(
                        "CONFIG_MODEL_SCHEMA_INVALID",
                        "未提供正式配置模型文件路径。Windows W2 的 apply 命令必须通过 --config 指定配置模型文件。")
                ],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(blocked);
            return CreateCommandResult(blocked, outputFormat);
        }

        if (forceStopWeasel)
        {
            phase?.Invoke("正在停止输入法服务…");
            ConfigModel defaultModel = ConfigModel.CreateDefault();
            WindowsEnvironmentState env = WindowsEnvironmentService.Detect(defaultModel);
            TryStopWeaselRuntime(env);
        }

        phase?.Invoke("正在部署设置…");
        ConfigModel loadedModel = _configModelService.Load(configPath, allowDefault: false);
        ConfigModel normalizedModel = EnsureCustomSimpleEnabled(loadedModel, loadedModel.DictionarySettings.CustomEntries);
        if (!ReferenceEquals(loadedModel, normalizedModel))
        {
            _configModelService.Save(configPath, normalizedModel);
            _repositoryContext.PersistCurrentConfigModel(normalizedModel);
        }

            string targetRoot = RepositoryContext.ExpandPath(normalizedModel.SyncSettings.WindowsTargetRoot);
            Directory.CreateDirectory(targetRoot);
            string schemaPath = Path.Combine(targetRoot, "rime_mint.schema.yaml");
            if (!File.Exists(schemaPath) && normalizedModel.ProfileSettings.EnabledSchemaIds.Contains("rime_mint", StringComparer.OrdinalIgnoreCase))
            {
                FileHelper.WriteTextWithVerification(schemaPath, "schema_id: rime_mint\nswitches:\n  - name: ascii_mode\n    reset: 0\n  - name: emoji_suggestion\n    reset: 1\n  - name: full_shape\n    reset: 0\n  - name: tone_display\n    reset: 0\n  - name: transcription\n    reset: 0\n  - name: ascii_punct\n    reset: 0\nmenu:\n  page_size: 6\ntranslator:\n  dictionary: rime_mint\n");
            }

            CommandExecutionResult result = ExecuteApplyWorkflow(
                configModel: normalizedModel,
                environment: default!,
                outputFormat: outputFormat,
                snapshotId: null,
                windowsOutputFiles: null,
                windowsBinaryOutputFiles: null,
                userDictionaryFiles: null,
                createArtifacts: true);

            if (forceStopWeasel)

        if (forceStopWeasel)
        {
            phase?.Invoke("正在确认设置生效…");
            RunStartWeaselServer(outputFormat);
        }

        return result;
    }

    private CommandExecutionResult ExecuteApplyWorkflow(
        ConfigModel configModel,
        WindowsEnvironmentState environment,
        string outputFormat,
        string? snapshotId,
        IReadOnlyDictionary<string, string>? windowsOutputFiles,
        IReadOnlyDictionary<string, byte[]>? windowsBinaryOutputFiles,
        IReadOnlyDictionary<string, string>? userDictionaryFiles,
        bool createArtifacts)
    {
        string effectiveSnapshotId = snapshotId ?? "none";

        List<DiagnosticFinding> configFindings = _configModelService.Validate(configModel, CreateFinding).ToList();
        if (configFindings.Count > 0)
        {
            DiagnosticReport invalidConfig = BuildDiagnosticReport(
                WorkflowPhases.Configure,
                WorkflowStatuses.Blocked,
                configFindings,
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(invalidConfig);
            return CreateCommandResult(invalidConfig, outputFormat);
        }

        List<DiagnosticFinding> installedResourceFindings = _resourceUpdateService
            .ValidateRequiredInstalledResourcesForWindows(configModel)
            .Select(detail => CreateFinding(
                "WINDOWS_RESOURCE_UPDATE_FAILED",
                detail,
                relatedTaskId: "windows_diagnose_result"))
            .ToList();
        if (installedResourceFindings.Count > 0)
        {
            DiagnosticReport invalidResources = BuildDiagnosticReport(
                WorkflowPhases.Generate,
                WorkflowStatuses.Failed,
                installedResourceFindings,
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(invalidResources);
            return CreateCommandResult(invalidResources, outputFormat);
        }

        WindowsEnvironmentState detectedEnvironment = createArtifacts
            ? WindowsEnvironmentService.Detect(configModel)
            : environment;
        _repositoryContext.PersistRuntimePathCache(detectedEnvironment);
        List<DiagnosticFinding> detectFindings = WindowsEnvironmentService.Validate(detectedEnvironment, configModel, CreateFinding)
            .Where(static finding => !string.Equals(finding.Code, "WINDOWS_FOREGROUND_IME_CLOSED", StringComparison.Ordinal))
            .ToList();
        bool expectedAbsent = string.Equals(_repositoryContext.ResolveStateReference(WeaselExpectedAbsentStateName), "1", StringComparison.Ordinal);
        if (expectedAbsent && !detectedEnvironment.WeaselAvailable)
        {
            detectFindings = detectFindings
                .Where(finding => !string.Equals(finding.Code, "WINDOWS_WEASEL_MISSING", StringComparison.Ordinal))
                .ToList();
        }
        List<DiagnosticFinding> blockingFindings = detectFindings
            .Where(static finding => !string.Equals(finding.Severity, WorkflowSeverities.Warning, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (blockingFindings.Count > 0)
        {
            DiagnosticReport detectBlocked = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Blocked,
                blockingFindings,
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(detectBlocked);
            return CreateCommandResult(detectBlocked, outputFormat);
        }

        try
        {
            IReadOnlyDictionary<string, string> effectiveWindowsOutputFiles;
            IReadOnlyDictionary<string, byte[]> effectiveWindowsBinaryOutputFiles;
            IReadOnlyDictionary<string, string> effectiveUserDictionaryFiles;
            if (createArtifacts)
            {
                effectiveSnapshotId = RepositoryContext.CreateOperationId("windows");
                GeneratedArtifacts artifacts = _artifactService.Generate(configModel, effectiveSnapshotId);
                effectiveWindowsOutputFiles = artifacts.WindowsOutputFiles;
                effectiveWindowsBinaryOutputFiles = artifacts.WindowsBinaryOutputFiles;
                effectiveUserDictionaryFiles = artifacts.UserDictionaryFiles;
                // Snapshot state reference writes commented out — LAN sync not yet implemented.
                // _repositoryContext.PersistStateReference("latest_generated_snapshot.txt", effectiveSnapshotId);
            }
            else
            {
                effectiveWindowsOutputFiles = windowsOutputFiles
                    ?? throw new InvalidOperationException("缺少已暂存的 Windows 目标包。");
                effectiveWindowsBinaryOutputFiles = windowsBinaryOutputFiles
                    ?? throw new InvalidOperationException("缺少已暂存的 Windows 二进制目标包。");
                effectiveUserDictionaryFiles = userDictionaryFiles
                    ??                 throw new InvalidOperationException("缺少已暂存的用户词典同步载荷。");
                // Snapshot state reference writes commented out — LAN sync not yet implemented.
                // _repositoryContext.PersistStateReference("latest_generated_snapshot.txt", effectiveSnapshotId);
            }

            IReadOnlyList<string> cleanupFailures = _artifactService.ApplyWindowsTargets(
                effectiveWindowsOutputFiles,
                effectiveWindowsBinaryOutputFiles,
                effectiveUserDictionaryFiles,
                detectedEnvironment.WindowsTargetRoot);

            DiagnosticFinding? deployFailure = _artifactService.Deploy(detectedEnvironment, effectiveSnapshotId, null, CreateFinding);
            if (deployFailure is not null)
            {
                DiagnosticReport deployReport = BuildDiagnosticReport(
                    WorkflowPhases.Deploy,
                    WorkflowStatuses.Failed,
                    [deployFailure],
                    effectiveSnapshotId,
                    true,
                    true,
                    true);
                _repositoryContext.PersistLastDiagnostic(deployReport);
                _repositoryContext.PersistRecheckSummary(effectiveSnapshotId, deployReport.Status, deployReport.Findings);
                TryActivateWeaselProfileInDetachedProcess();
                return CreateCommandResult(deployReport, outputFormat);
            }

            TryActivateWeaselProfileInDetachedProcess();

            IReadOnlyList<DiagnosticFinding> recheckFindings = _artifactService.Recheck(
                effectiveWindowsOutputFiles,
                effectiveWindowsBinaryOutputFiles,
                effectiveUserDictionaryFiles,
                detectedEnvironment.WindowsTargetRoot,
                null,
                CreateFinding);

            List<DiagnosticFinding> allFindings = new(cleanupFailures.Count + recheckFindings.Count);
            foreach (string failedFile in cleanupFailures)
            {
                allFindings.Add(new DiagnosticFinding
                {
                    Code = "WINDOWS_STALE_CLEANUP_WARNING",
                    Severity = WorkflowSeverities.Warning,
                    Summary = "过期文件清理失败",
                    Detail = $"无法删除过期文件（文件可能被占用）：{failedFile}",
                    DisplayKind = FeedbackDisplayKinds.ExplicitWarning,
                    AutoActionKind = AutoActionKinds.None,
                    EntryPointKind = EntryPointKinds.None,
                });
            }

            allFindings.AddRange(recheckFindings);
            DiagnosticReport finalReport = BuildDiagnosticReport(
                recheckFindings.Count == 0 ? WorkflowPhases.Diagnose : WorkflowPhases.Recheck,
                recheckFindings.Count == 0 ? WorkflowStatuses.Completed : WorkflowStatuses.Failed,
                allFindings,
                effectiveSnapshotId,
                true,
                true,
                recheckFindings.Count > 0);

            _repositoryContext.PersistLastDiagnostic(finalReport);
            _repositoryContext.PersistRecheckSummary(effectiveSnapshotId, finalReport.Status, allFindings);
            if (finalReport.Status == WorkflowStatuses.Completed)
            {
                _repositoryContext.PersistCurrentConfigModel(configModel);
            }
            return CreateCommandResult(finalReport, outputFormat);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticReport failedReport = BuildDiagnosticReport(
                WorkflowPhases.Apply,
                WorkflowStatuses.Failed,
                [
                    CreateFinding(
                        "WINDOWS_APPLY_FAILED",
                        $"Windows 目标目录写入失败：{exception.Message}")
                ],
                effectiveSnapshotId,
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(failedReport);
            _repositoryContext.PersistRecheckSummary(effectiveSnapshotId, failedReport.Status, failedReport.Findings);
            TryActivateWeaselProfileInDetachedProcess();
            return CreateCommandResult(failedReport, outputFormat);
        }
    }

    public CommandExecutionResult RunRollback(string? backupId, string outputFormat)
    {
        return RunRollback(backupId, outputFormat, forceStopWeasel: false);
    }

    public CommandExecutionResult RunRollback(string? backupId, string outputFormat, bool forceStopWeasel)
    {
        if (forceStopWeasel)
        {
            ConfigModel defaultModel = ConfigModel.CreateDefault();
            WindowsEnvironmentState env = WindowsEnvironmentService.Detect(defaultModel);
            TryStopWeaselRuntime(env);
        }

        string? resolvedBackupId = string.IsNullOrWhiteSpace(backupId)
            ? _repositoryContext.ResolveStateReference("latest_backup.txt")
            : backupId;
        if (string.IsNullOrWhiteSpace(resolvedBackupId))
        {
            ConfigModel currentConfig = LoadPreferredConfigModel(null, allowDefault: true);
            return ExecuteApplyWorkflow(
                configModel: currentConfig,
                environment: default!,
                outputFormat: outputFormat,
                snapshotId: null,
                windowsOutputFiles: null,
                windowsBinaryOutputFiles: null,
                userDictionaryFiles: null,
                createArtifacts: true);
        }

        string backupDirectory = Path.Combine(_repositoryContext.BackupsRoot, resolvedBackupId);
        string manifestPath = Path.Combine(backupDirectory, "backup_manifest.json");
        string platformTargetsDirectory = Path.Combine(backupDirectory, "platform_targets");
        if (!File.Exists(manifestPath) || !Directory.Exists(platformTargetsDirectory))
        {
            DiagnosticReport invalidBackup = BuildDiagnosticReport(
                WorkflowPhases.Rollback,
                WorkflowStatuses.Failed,
                [CreateFinding("ROLLBACK_FAILED", $"备份目录不完整，无法从 {resolvedBackupId} 执行回滚。", resolvedBackupId)],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(invalidBackup);
            PersistBackupStatusFromReport(
                "执行回滚",
                invalidBackup,
                summary: "当前备份目录不完整，无法执行回滚。",
                backupId: resolvedBackupId);
            return CreateCommandResult(invalidBackup, outputFormat);
        }

        ConfigModel defaultConfig = ConfigModel.CreateDefault();
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(defaultConfig);
        _repositoryContext.PersistRuntimePathCache(environment);
        if (!environment.TargetRootAccessible)
        {
            DiagnosticReport blocked = BuildDiagnosticReport(
                WorkflowPhases.Detect,
                WorkflowStatuses.Blocked,
                [CreateFinding("WINDOWS_TARGET_ROOT_INVALID", $"Windows 目标目录不可访问：{environment.WindowsTargetRoot}", resolvedBackupId)],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(blocked);
            PersistBackupStatusFromReport(
                "执行回滚",
                blocked,
                summary: "当前输入法目录不可访问，回滚未执行。",
                backupId: resolvedBackupId);
            return CreateCommandResult(blocked, outputFormat);
        }

        try
        {
            TryStopWeaselRuntime(environment);
            _artifactService.RestoreBackup(resolvedBackupId, environment.WindowsTargetRoot);
            DiagnosticFinding? deployFailure = _artifactService.Deploy(environment, "rollback", resolvedBackupId, CreateFinding);
            if (deployFailure is not null)
            {
                DiagnosticReport failed = BuildDiagnosticReport(
                    WorkflowPhases.Rollback,
                    WorkflowStatuses.Failed,
                    [deployFailure],
                    "none",
                    true,
                    false,
                    false,
                    resolvedBackupId);
                _repositoryContext.PersistLastDiagnostic(failed);
                PersistBackupStatusFromReport(
                    "执行回滚",
                    failed,
                    summary: "回滚后重新部署失败。",
                    backupId: resolvedBackupId);
                TryActivateWeaselProfileInDetachedProcess();
                return CreateCommandResult(failed, outputFormat);
            }

            TryActivateWeaselProfileInDetachedProcess();

            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Diagnose,
                WorkflowStatuses.Completed,
                [],
                "none",
                true,
                false,
                false,
                resolvedBackupId);
            _repositoryContext.PersistLastDiagnostic(report);
            _repositoryContext.PersistRecheckSummary("none", report.Status, report.Findings);
            PersistBackupStatus(
                action: "执行回滚",
                status: report.Status,
                summary: "已从最近备份恢复并重新部署。",
                nextAction: report.NextAction,
                backupId: resolvedBackupId);
            return CreateCommandResult(report, outputFormat);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticReport failed = BuildDiagnosticReport(
                WorkflowPhases.Rollback,
                WorkflowStatuses.Failed,
                [CreateFinding("ROLLBACK_FAILED", $"回滚执行失败：{exception.Message}", resolvedBackupId)],
                "none",
                true,
                false,
                false,
                resolvedBackupId);
            _repositoryContext.PersistLastDiagnostic(failed);
            PersistBackupStatusFromReport(
                "执行回滚",
                failed,
                summary: "回滚执行失败。",
                backupId: resolvedBackupId);
            TryActivateWeaselProfileInDetachedProcess();
            return CreateCommandResult(failed, outputFormat);
        }
    }

    public CommandExecutionResult RunExport(string kind, string? outputPath, string? configPath, string? backupId, string outputFormat)
    {
        string targetPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(_repositoryContext.ExportsRoot, $"export-{kind}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}")
            : Path.GetFullPath(outputPath);
        Directory.CreateDirectory(targetPath);

        try
        {
            string exportedArtifact;
            switch (kind.ToLowerInvariant())
            {
                case "config":
                {
                    ConfigModel model = LoadExportConfigModel(configPath);
                    exportedArtifact = Path.Combine(targetPath, "config_model.json");
                    RepositoryContext.WriteUtf8(exportedArtifact, JsonSerializer.Serialize(model, JsonOptions));
                    break;
                }
                case "diagnostic":
                {
                    exportedArtifact = _artifactService.ExportLatestDiagnostic(targetPath);
                    break;
                }
                case "snapshot":
                {
                    exportedArtifact = _artifactService.ExportLatestSnapshot(targetPath);
                    break;
                }
                case "backup":
                {
                    string? resolvedBackupId = string.IsNullOrWhiteSpace(backupId)
                        ? _repositoryContext.ResolveStateReference("latest_backup.txt")
                        : backupId;
                    if (string.IsNullOrWhiteSpace(resolvedBackupId))
                    {
                        DiagnosticReport report = BuildDiagnosticReport(
                            WorkflowPhases.Diagnose,
                            WorkflowStatuses.Blocked,
                            [CreateFinding("EXPORT_FAILED", "当前没有可导出的备份。")],
                            "none",
                            false,
                            false,
                            false);
                        return CreateCommandResult(report, outputFormat);
                    }

                    exportedArtifact = _artifactService.ExportBackup(resolvedBackupId, targetPath);
                    break;
                }
                case "resource-manifest":
                {
                    exportedArtifact = Path.Combine(targetPath, "resource_manifest.json");
                    Utilities.FileHelper.CopyFileWithBackoff(_repositoryContext.SharedSpecPath("resource_manifest.json"), exportedArtifact, overwrite: true);
                    break;
                }
                case "resource-update-report":
                {
                    string sourcePath = Path.Combine(_repositoryContext.StateRoot, "last_resource_update_report.json");
                    if (!File.Exists(sourcePath))
                    {
                        DiagnosticReport report = BuildDiagnosticReport(
                            WorkflowPhases.Diagnose,
                            WorkflowStatuses.Blocked,
                            [CreateFinding("EXPORT_FAILED", "当前没有可导出的资源更新检查结果。")],
                            "none",
                            false,
                            false,
                            false);
                        return CreateCommandResult(report, outputFormat);
                    }

                    exportedArtifact = Path.Combine(targetPath, "resource_update_report.json");
                    Utilities.FileHelper.CopyFileWithBackoff(sourcePath, exportedArtifact, overwrite: true);
                    break;
                }
                case "user-data":
                {
                    ConfigModel model = LoadExportConfigModel(configPath);
                    exportedArtifact = _artifactService.ExportCurrentUserData(targetPath, model);
                    break;
                }
                default:
                {
                    DiagnosticReport report = BuildDiagnosticReport(
                        WorkflowPhases.Diagnose,
                        WorkflowStatuses.Blocked,
                        [CreateFinding("EXPORT_FAILED", $"不支持的导出类型：{kind}")],
                        "none",
                        false,
                        false,
                        false);
                    return CreateCommandResult(report, outputFormat);
                }
            }

            object payload = new { kind, output = exportedArtifact };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = JsonSerializer.Serialize(payload, JsonOptions),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Diagnose,
                WorkflowStatuses.Failed,
                [CreateFinding("EXPORT_FAILED", $"导出失败：{exception.Message}")],
                "none",
                false,
                false,
                false);
            return CreateCommandResult(report, outputFormat);
        }
    }

    private void PersistBackupStatusFromReport(
        string action,
        DiagnosticReport report,
        string summary,
        string? backupId = null)
    {
        PersistBackupStatus(
            action,
            report.Status,
            report.Findings.Count == 0 ? summary : report.Findings[0].Summary,
            report.NextAction,
            backupId ?? report.BackupId,
            report.SnapshotId);
    }

    private void PersistBackupStatus(
        string action,
        string status,
        string summary,
        string nextAction,
        string? backupId,
        string? snapshotId = null)
    {
        string recordedAt = DateTimeOffset.UtcNow.ToString("O");
        string resolvedBackupId = string.IsNullOrWhiteSpace(backupId) ? "none" : backupId;
        string resolvedSnapshotId = string.IsNullOrWhiteSpace(snapshotId) ? "none" : snapshotId;
        string[] targetFiles = [];
        string[] resourceState = [];
        string[] relatedFiles = [];
        bool includesUserData = false;
        if (TryLoadBackupDetails(
                resolvedBackupId,
                out string manifestCreatedAt,
                out string manifestSnapshotId,
                out targetFiles,
                out resourceState,
                out includesUserData,
                out relatedFiles))
        {
            recordedAt = manifestCreatedAt;
            resolvedSnapshotId = manifestSnapshotId;
        }

        _repositoryContext.PersistBackupStatus(new
        {
            action,
            status,
            summary,
            next_action = nextAction,
            backup_id = resolvedBackupId,
            snapshot_id = resolvedSnapshotId,
            recorded_at = recordedAt,
            target_files = targetFiles,
            resource_state = resourceState,
            includes_user_data = includesUserData,
            related_files = relatedFiles,
        });
    }

    private DiagnosticReport? TryLoadLastDiagnosticReport()
    {
        string path = Path.Combine(_repositoryContext.StateRoot, "last_diagnostic.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<DiagnosticReport>(RepositoryContext.ReadUtf8(path), JsonOptions);
    }

    private bool TryLoadBackupDetails(
        string backupId,
        out string createdAt,
        out string snapshotId,
        out string[] targetFiles,
        out string[] resourceState,
        out bool includesUserData,
        out string[] relatedFiles)
    {
        createdAt = DateTimeOffset.UtcNow.ToString("O");
        snapshotId = "none";
        targetFiles = [];
        resourceState = [];
        includesUserData = false;
        relatedFiles = [];
        if (string.IsNullOrWhiteSpace(backupId) || string.Equals(backupId, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string backupRoot = Path.Combine(_repositoryContext.BackupsRoot, backupId);
        string manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        relatedFiles = Directory.Exists(backupRoot)
            ? Directory.GetFiles(backupRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetFileName(path))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        using JsonDocument manifest = JsonDocument.Parse(RepositoryContext.ReadUtf8(manifestPath));
        createdAt = manifest.RootElement.TryGetProperty("created_at", out JsonElement createdAtElement)
            ? createdAtElement.GetString() ?? createdAt
            : createdAt;
        snapshotId = manifest.RootElement.TryGetProperty("snapshot_id", out JsonElement snapshotIdElement)
            ? snapshotIdElement.GetString() ?? snapshotId
            : snapshotId;
        targetFiles = manifest.RootElement.TryGetProperty("platform_targets", out JsonElement platformTargetsElement) && platformTargetsElement.ValueKind == JsonValueKind.Array
            ? platformTargetsElement.EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .Where(static value => value.Length > 0)
                .ToArray()
            : [];
        resourceState = manifest.RootElement.TryGetProperty("resource_state", out JsonElement resourceStateElement) && resourceStateElement.ValueKind == JsonValueKind.Array
            ? resourceStateElement.EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .Where(static value => value.Length > 0)
                .ToArray()
            : [];
        includesUserData = manifest.RootElement.TryGetProperty("includes_user_data", out JsonElement includesUserDataElement) &&
                           includesUserDataElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                           includesUserDataElement.GetBoolean();
        return true;
    }

    private static bool IsModelResourceId(string resourceId)
    {
        return resourceId.Contains("wanxiang", StringComparison.OrdinalIgnoreCase) ||
               resourceId.Contains("model", StringComparison.OrdinalIgnoreCase);
    }

    private ConfigModel LoadExportConfigModel(string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return NormalizeConfigModelPaths(_configModelService.Load(configPath, allowDefault: true));
        }

        string currentConfigPath = _repositoryContext.CurrentConfigModelPath;
        if (File.Exists(currentConfigPath))
        {
            return NormalizeConfigModelPaths(_configModelService.Load(currentConfigPath, allowDefault: false));
        }

        return NormalizeConfigModelPaths(ConfigModel.CreateDefault());
    }

    private ConfigModel LoadPreferredConfigModel(string? configPath, bool allowDefault)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return NormalizeConfigModelPaths(_configModelService.Load(configPath, allowDefault: false));
        }

        string currentConfigPath = _repositoryContext.CurrentConfigModelPath;
        if (File.Exists(currentConfigPath))
        {
            return NormalizeConfigModelPaths(_configModelService.Load(currentConfigPath, allowDefault: false));
        }

        if (!allowDefault)
        {
            throw new InvalidOperationException("当前没有可用于正式执行的配置模型。");
        }

        return NormalizeConfigModelPaths(ConfigModel.CreateDefault());
    }

    private string ResolveMutableConfigPath(string? configPath)
    {
        return !string.IsNullOrWhiteSpace(configPath)
            ? Path.GetFullPath(configPath)
            : _repositoryContext.CurrentConfigModelPath;
    }

    private ConfigModel MergeImportedUserConfigWithLocalPaths(ConfigModel importedModel, ConfigModel currentModel)
    {
        return new ConfigModel
        {
            ConfigVersion = importedModel.ConfigVersion,
            ProfileSettings = importedModel.ProfileSettings,

            FuzzyPinyinSettings = importedModel.FuzzyPinyinSettings,
            PersonalizationSettings = importedModel.PersonalizationSettings,
            DictionarySettings = importedModel.DictionarySettings,
            ModelSettings = new ModelSettings
            {
                EnabledModelIds = importedModel.ModelSettings.EnabledModelIds,
                ActiveModelId = importedModel.ModelSettings.ActiveModelId,
                ModelRoot = currentModel.ModelSettings.ModelRoot,
                ModelVersions = importedModel.ModelSettings.ModelVersions,
            },
            AndroidSettings = importedModel.AndroidSettings,
            WindowsSettings = importedModel.WindowsSettings,

            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = currentModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = currentModel.SyncSettings.WindowsTargetRoot,
                ExportRoot = currentModel.SyncSettings.ExportRoot,
                BackupRoot = currentModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = importedModel.SyncSettings.SnapshotRetentionLimit,
            },
        };
    }



    private ConfigModel NormalizeConfigModelPaths(ConfigModel model)
    {
        return new ConfigModel
        {
            ConfigVersion = model.ConfigVersion,
            ProfileSettings = model.ProfileSettings,
            FuzzyPinyinSettings = model.FuzzyPinyinSettings,
            PersonalizationSettings = model.PersonalizationSettings,
            DictionarySettings = model.DictionarySettings,
            ModelSettings = model.ModelSettings,
            AndroidSettings = model.AndroidSettings,
            WindowsSettings = model.WindowsSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = NormalizeRepoBoundPath(model.SyncSettings.AndroidImportRoot),
                WindowsTargetRoot = NormalizeRepoBoundPath(model.SyncSettings.WindowsTargetRoot),
                ExportRoot = NormalizeRepoBoundPath(model.SyncSettings.ExportRoot),
                BackupRoot = NormalizeRepoBoundPath(model.SyncSettings.BackupRoot),
                SnapshotRetentionLimit = model.SyncSettings.SnapshotRetentionLimit,
            },

        };
    }

    private string NormalizeRepoBoundPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('%', StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(_repositoryContext.RepositoryRoot, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string marker = $"{Path.DirectorySeparatorChar}Documents{Path.DirectorySeparatorChar}rimekit{Path.DirectorySeparatorChar}";
        int markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return path;
        }

        string suffix = path[(markerIndex + marker.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(_repositoryContext.RepositoryRoot, suffix);
    }

    private string ResolveInstallerLaunchPath(WindowsEnvironmentState environment)
    {
        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
        string? explicitPath = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
            {
                throw new IOException($"指定的安装器不存在：{explicitPath}");
            }

            return explicitPath;
        }

        if (string.Equals(controls.WeaselVersionStrategy, "pinned", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(controls.WeaselPinnedInstallerUrl))
        {
            return DownloadInstaller(controls.WeaselPinnedInstallerUrl);
        }

        return DownloadInstaller(ResolveGitHubReleaseAssetUrl(prerelease: false));
    }

    private static string ResolveGitHubReleaseAssetUrl(bool prerelease)
    {
        string json = ResumableDownloader.DownloadToString("https://api.github.com/repos/rime/weasel/releases");
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("prerelease").GetBoolean() != prerelease)
            {
                continue;
            }

            foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                if (!string.IsNullOrWhiteSpace(name) &&
                    Regex.IsMatch(name, @"^weasel-.*-installer\.(exe|cmd)$", RegexOptions.IgnoreCase))
                {
                    return asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private string DownloadInstaller(string downloadUrl)
    {
        Uri uri = new(downloadUrl, UriKind.Absolute);
        string fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new IOException($"安装器下载地址缺少文件名：{downloadUrl}");
        }

        string targetPath = Path.Combine(_repositoryContext.DownloadsRoot, fileName);
        try
        {
            ResumableDownloader.DownloadToFile(downloadUrl, targetPath);
            return targetPath;
        }
        catch (IOException)
        {
            string uniqueTargetPath = Path.Combine(
                _repositoryContext.DownloadsRoot,
                $"{Path.GetFileNameWithoutExtension(fileName)}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}{Path.GetExtension(fileName)}");
            ResumableDownloader.DownloadToFile(downloadUrl, uniqueTargetPath);
            return uniqueTargetPath;
        }
    }

    public CommandExecutionResult RunImportCustomEntries(string sourcePath, string? configPath, string outputFormat)
    {
        try
        {
            ConfigModel currentModel = LoadPreferredConfigModel(configPath, allowDefault: true);
            ConfigModel updatedModel = _artifactService.ImportCustomEntries(sourcePath, currentModel);
            _repositoryContext.PersistCurrentConfigModel(updatedModel);

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Configure,
                status = WorkflowStatuses.Completed,
                source_path = sourcePath,
                imported_custom_entry_count = updatedModel.DictionarySettings.CustomEntries.Count,
                next_action = "自定义词条已导入到当前配置模型，请保存配置后继续应用并部署。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Configure)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"来源文件: {sourcePath}",
                            $"导入词条数: {updatedModel.DictionarySettings.CustomEntries.Count}",
                            "下一步: 自定义词条已导入到当前配置模型，请保存配置后继续应用并部署。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Configure,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_USER_DATA_IMPORT_FAILED", $"导入自定义词条失败：{exception.Message}")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public CommandExecutionResult RunImportUserDictionaryDirectory(string sourceDirectory, string? configPath, string outputFormat)
    {
        try
        {
            ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
            WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(model);
            TryStopWeaselRuntime(environment);
            string targetRoot = RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot);
            string importedTarget = _artifactService.ImportUserDictionaryDirectory(sourceDirectory, targetRoot);

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Apply,
                status = WorkflowStatuses.Completed,
                source_directory = sourceDirectory,
                target_directory = importedTarget,
                next_action = "用户词典目录已导入到输入法目录；现在可以继续检测，或发布同步快照。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Apply)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"来源目录: {sourceDirectory}",
                            $"目标目录: {importedTarget}",
                            "下一步: 用户词典目录已导入到输入法目录；现在可以继续检测，或发布同步快照。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or DirectoryNotFoundException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Apply,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_USER_DATA_IMPORT_FAILED", $"导入用户词典目录失败：{exception.Message}")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public CommandExecutionResult RunExportUserConfigToml(string? outputPath, string? configPath, string outputFormat, Action<string>? phase = null)
    {
        try
        {
            phase?.Invoke("正在收集配置数据…");
            ConfigModel model = LoadExportConfigModel(configPath);
            string targetPath = string.IsNullOrWhiteSpace(outputPath)
                ? Path.Combine(_repositoryContext.ExportsRoot, $"user-config-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.toml")
                : Path.GetFullPath(outputPath);
            phase?.Invoke("正在写入文件…");
            string exportedPath = _artifactService.ExportUserConfigToml(targetPath, model);

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Diagnose,
                status = WorkflowStatuses.Completed,
                output_path = exportedPath,
                next_action = "用户配置 TOML 已导出；可在另一台设备中通过“导入用户配置”继续恢复。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Diagnose)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"导出文件: {exportedPath}",
                            "下一步: 用户配置 TOML 已导出；可在另一台设备中通过“导入用户配置”继续恢复。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Diagnose,
                WorkflowStatuses.Failed,
                [CreateFinding("EXPORT_FAILED", $"导出用户配置 TOML 失败：{exception.Message}")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public CommandExecutionResult RunImportUserConfigToml(string sourcePath, string? configPath, string outputFormat)
    {
        return RunImportUserConfigToml(sourcePath, configPath, outputFormat, forceStopWeasel: false);
    }

    public CommandExecutionResult RunImportUserConfigToml(string sourcePath, string? configPath, string outputFormat, bool forceStopWeasel, Action<string>? phase = null)
    {
        if (forceStopWeasel)
        {
            phase?.Invoke("正在停止输入法服务…");
            ConfigModel defaultModel = ConfigModel.CreateDefault();
            WindowsEnvironmentState env = WindowsEnvironmentService.Detect(defaultModel);
            TryStopWeaselRuntime(env);
        }

        try
        {
            phase?.Invoke("正在读取配置文件…");
            ConfigModel currentModel = LoadPreferredConfigModel(configPath, allowDefault: true);
            (ConfigModel importedModel, IReadOnlyDictionary<string, string> userDictionaryFiles) = _artifactService.ImportUserConfigToml(sourcePath);
            ConfigModel mergedModel = MergeImportedUserConfigWithLocalPaths(importedModel, currentModel);

            List<DiagnosticFinding> findings = _configModelService.Validate(mergedModel, CreateFinding).ToList();
            if (findings.Count > 0)
            {
                DiagnosticReport invalidConfig = BuildDiagnosticReport(
                    WorkflowPhases.Configure,
                    WorkflowStatuses.Blocked,
                    findings,
                    "none",
                    false,
                    false,
                    false);
                _repositoryContext.PersistLastDiagnostic(invalidConfig);
                return CreateCommandResult(invalidConfig, outputFormat);
            }

            string effectiveConfigPath = ResolveMutableConfigPath(configPath);
            _configModelService.Save(effectiveConfigPath, mergedModel);
            _repositoryContext.PersistCurrentConfigModel(mergedModel);
            if (userDictionaryFiles.Count > 0)
            {
                _artifactService.ImportUserDictionaryFiles(
                    userDictionaryFiles,
                    RepositoryContext.ExpandPath(mergedModel.SyncSettings.WindowsTargetRoot));
            }

            phase?.Invoke("正在应用配置…");
            CommandExecutionResult applyResult = ExecuteApplyWorkflow(
                configModel: mergedModel,
                environment: default!,
                outputFormat: outputFormat,
                snapshotId: null,
                windowsOutputFiles: null,
                windowsBinaryOutputFiles: null,
                userDictionaryFiles: null,
                createArtifacts: true);
            if (applyResult.ExitCode != 0)
            {
                return applyResult;
            }

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Apply,
                status = WorkflowStatuses.Completed,
                source_path = Path.GetFullPath(sourcePath),
                config_path = effectiveConfigPath,
                imported_user_dictionary_count = userDictionaryFiles.Count,
                next_action = "用户配置 TOML 已导入并重新应用到当前输入法目录。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Apply)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"来源文件: {Path.GetFullPath(sourcePath)}",
                            $"配置文件: {effectiveConfigPath}",
                            $"导入用户词典文件数: {userDictionaryFiles.Count}",
                            "下一步: 用户配置 TOML 已导入并重新应用到当前输入法目录。",
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Apply,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_USER_DATA_IMPORT_FAILED", $"导入用户配置 TOML 失败：{exception.Message}")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    private ConfigModel EnableFormalResourceInConfigModel(ConfigModel currentModel, string resourceId)
    {
        if (_repositoryContext.SchemaIds.Contains(resourceId))
        {
            List<string> enabledSchemaIds = currentModel.ProfileSettings.EnabledSchemaIds
                .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            enabledSchemaIds.Add(resourceId);
            string windowsDefaultSchemaId = string.IsNullOrWhiteSpace(currentModel.ProfileSettings.WindowsDefaultSchemaId)
                ? resourceId
                : currentModel.ProfileSettings.WindowsDefaultSchemaId;

            return new ConfigModel
            {
                ConfigVersion = currentModel.ConfigVersion,
                ProfileSettings = new ProfileSettings
                {
                    EnabledSchemaIds = enabledSchemaIds,
                    WindowsDefaultSchemaId = windowsDefaultSchemaId,
                    AndroidDefaultSchemaId = currentModel.ProfileSettings.AndroidDefaultSchemaId,
                },
                FuzzyPinyinSettings = currentModel.FuzzyPinyinSettings,
                PersonalizationSettings = currentModel.PersonalizationSettings,
                DictionarySettings = currentModel.DictionarySettings,
                ModelSettings = currentModel.ModelSettings,
                AndroidSettings = currentModel.AndroidSettings,
                WindowsSettings = currentModel.WindowsSettings,
                SyncSettings = currentModel.SyncSettings,
            };
        }

        if (_repositoryContext.DictionaryIds.Contains(resourceId))
        {
            List<string> enabledDictIds = currentModel.DictionarySettings.EnabledDictionaryIds
                .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            enabledDictIds.Add(resourceId);
            List<string> dictOrder = currentModel.DictionarySettings.DictionaryOrder
                .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            dictOrder.Add(resourceId);

            return new ConfigModel
            {
                ConfigVersion = currentModel.ConfigVersion,
                ProfileSettings = currentModel.ProfileSettings,
                FuzzyPinyinSettings = currentModel.FuzzyPinyinSettings,
                PersonalizationSettings = currentModel.PersonalizationSettings,
                DictionarySettings = new DictionarySettings
                {
                    EnabledDictionaryIds = enabledDictIds,
                    DictionaryOrder = dictOrder,
                    CustomEntries = currentModel.DictionarySettings.CustomEntries,
                },
                ModelSettings = currentModel.ModelSettings,
                AndroidSettings = currentModel.AndroidSettings,
                WindowsSettings = currentModel.WindowsSettings,
                SyncSettings = currentModel.SyncSettings,
            };
        }

        if (_repositoryContext.ModelIds.Contains(resourceId))
        {
            List<string> enabledModelIds = currentModel.ModelSettings.EnabledModelIds
                .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            enabledModelIds.Add(resourceId);

            return new ConfigModel
            {
                ConfigVersion = currentModel.ConfigVersion,
                ProfileSettings = currentModel.ProfileSettings,
                FuzzyPinyinSettings = currentModel.FuzzyPinyinSettings,
                PersonalizationSettings = currentModel.PersonalizationSettings,
                DictionarySettings = currentModel.DictionarySettings,
                ModelSettings = new ModelSettings
                {
                    EnabledModelIds = enabledModelIds,
                    ActiveModelId = resourceId,
                    ModelRoot = currentModel.ModelSettings.ModelRoot,
                    ModelVersions = currentModel.ModelSettings.ModelVersions,
                },
                AndroidSettings = currentModel.AndroidSettings,
                WindowsSettings = currentModel.WindowsSettings,
                SyncSettings = currentModel.SyncSettings,
            };
        }

        return currentModel;
    }

    public CommandExecutionResult RunInstallFormalResource(string resourceId, string? configPath, string outputFormat)
    {
        return RunInstallFormalResource(resourceId, configPath, outputFormat, forceStopWeasel: false);
    }

    public CommandExecutionResult RunInstallFormalResource(string resourceId, string? configPath, string outputFormat, bool forceStopWeasel, Action<string>? phase = null)
    {
        try
        {
            if (!IsFormalManagedResource(resourceId))
            {
                return new CommandExecutionResult
                {
                    ExitCode = 1,
                    TextOutput = $"此资源（{resourceId}）不在正式资源清单中，无法安装或更新。\n" +
                                 "当前正式纳管方案仅包含 resource_manifest.json 中登记的资源。\n" +
                                 "运行时发现的承载器自带方案仅支持启用和停用，不支持由本产品安装或更新。",
                };
            }

            if (forceStopWeasel)
            {
                phase?.Invoke("正在停止输入法服务…");
                ConfigModel defaultModel = ConfigModel.CreateDefault();
                WindowsEnvironmentState env = WindowsEnvironmentService.Detect(defaultModel);
                TryStopWeaselRuntime(env);
            }

            if (string.Equals(resourceId, "rime_mint", StringComparison.OrdinalIgnoreCase))
            {
                TemplateService.DeleteSchemaTemplate("rime_mint");
            }

            phase?.Invoke("正在下载资源…");
            string report = _resourceUpdateService.InstallOrUpdateResource(resourceId);
            ConfigModel currentModel = LoadPreferredConfigModel(configPath, allowDefault: true);
            ConfigModel updatedModel = EnableFormalResourceInConfigModel(currentModel, resourceId);
            string effectiveConfigPath = ResolveMutableConfigPath(configPath);

            phase?.Invoke("正在部署并验证…");
            string targetRoot = RepositoryContext.ExpandPath(updatedModel.SyncSettings.WindowsTargetRoot);
            Directory.CreateDirectory(targetRoot);
            string schemaPath = Path.Combine(targetRoot, "rime_mint.schema.yaml");
            if (!File.Exists(schemaPath) && updatedModel.ProfileSettings.EnabledSchemaIds.Contains("rime_mint", StringComparer.OrdinalIgnoreCase))
            {
                FileHelper.WriteTextWithVerification(schemaPath, "schema_id: rime_mint\nswitches:\n  - name: ascii_mode\n    reset: 0\n  - name: emoji_suggestion\n    reset: 1\n  - name: full_shape\n    reset: 0\n  - name: tone_display\n    reset: 0\n  - name: transcription\n    reset: 0\n  - name: ascii_punct\n    reset: 0\nmenu:\n  page_size: 6\ntranslator:\n  dictionary: rime_mint\n");
            }
            CommandExecutionResult applyResult = ExecuteApplyWorkflow(
                configModel: updatedModel,
                environment: default!,
                outputFormat: outputFormat,
                snapshotId: null,
                windowsOutputFiles: null,
                windowsBinaryOutputFiles: null,
                userDictionaryFiles: null,
                createArtifacts: true);

            if (applyResult.ExitCode == 0)
            {
                _configModelService.Save(effectiveConfigPath, updatedModel);
                _repositoryContext.PersistCurrentConfigModel(updatedModel);
            }
            if (applyResult.ExitCode != 0)
            {
                return applyResult;
            }

            List<string> captureErrors = [];
            string runtimeTargetRoot = Environment.ExpandEnvironmentVariables(updatedModel.SyncSettings.WindowsTargetRoot);
            foreach (string schemaId in updatedModel.ProfileSettings.EnabledSchemaIds)
            {
                try
                {
                    TemplateService.CaptureTemplates(schemaId, runtimeTargetRoot);
                }
                catch (IOException ex)
                {
                    captureErrors.Add($"schema={schemaId}: {ex.Message}");
                }
            }

            if (_repositoryContext.ModelIds.Contains(resourceId))
            {
                foreach (string schemaId in updatedModel.ProfileSettings.EnabledSchemaIds)
                {
                    try
                    {
                        UserSettingsReader.WriteGrammarDefaults(runtimeTargetRoot, schemaId);
                    }
                    catch (IOException ex)
                    {
                        captureErrors.Add($"grammar_defaults schema={schemaId}: {ex.Message}");
                    }
                }
            }

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Apply,
                status = WorkflowStatuses.Completed,
                resource_id = resourceId,
                config_path = effectiveConfigPath,
                report = JsonSerializer.Deserialize<object>(report),
                capture_errors = captureErrors,
                next_action = "正式资源已安装并部署到当前输入法目录。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Apply)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"资源对象: {resourceId}",
                            $"配置文件: {effectiveConfigPath}",
                            "下一步: 正式资源已安装并部署到当前输入法目录。",
                            report,
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Apply,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_RESOURCE_UPDATE_FAILED", $"正式资源安装或更新失败（{resourceId}）：{exception.Message}", relatedTaskId: "windows_diagnose_result")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public CommandExecutionResult RunInstallFormalResourceFromFile(string resourceId, string filePath, string? configPath, string outputFormat, bool forceStopWeasel = true, Action<string>? phase = null)
    {
        try
        {
            if (!IsFormalManagedResource(resourceId))
            {
                return new CommandExecutionResult
                {
                    ExitCode = 1,
                    TextOutput = $"此资源（{resourceId}）不在正式资源清单中，无法安装或更新。\n" +
                                 "当前正式纳管方案仅包含 resource_manifest.json 中登记的资源。\n" +
                                 "运行时发现的承载器自带方案仅支持启用和停用，不支持由本产品安装或更新。",
                };
            }

            if (forceStopWeasel)
            {
                phase?.Invoke("正在停止输入法服务…");
                ConfigModel defaultModel = ConfigModel.CreateDefault();
                WindowsEnvironmentState env = WindowsEnvironmentService.Detect(defaultModel);
                TryStopWeaselRuntime(env);
            }

            if (string.Equals(resourceId, "rime_mint", StringComparison.OrdinalIgnoreCase))
            {
                TemplateService.DeleteSchemaTemplate("rime_mint");
            }

            phase?.Invoke("正在安装资源…");
            string report = _resourceUpdateService.InstallResourceFromLocalFile(resourceId, filePath);
            ConfigModel currentModel = LoadPreferredConfigModel(configPath, allowDefault: true);
            ConfigModel updatedModel = EnableFormalResourceInConfigModel(currentModel, resourceId);
            string effectiveConfigPath = ResolveMutableConfigPath(configPath);

            phase?.Invoke("正在部署并验证…");
            CommandExecutionResult applyResult = ExecuteApplyWorkflow(
                configModel: updatedModel,
                environment: default!,
                outputFormat: outputFormat,
                snapshotId: null,
                windowsOutputFiles: null,
                windowsBinaryOutputFiles: null,
                userDictionaryFiles: null,
                createArtifacts: true);

            if (applyResult.ExitCode == 0)
            {
                _configModelService.Save(effectiveConfigPath, updatedModel);
                _repositoryContext.PersistCurrentConfigModel(updatedModel);
            }
            if (applyResult.ExitCode != 0)
            {
                return applyResult;
            }

            List<string> captureErrors = [];
            string runtimeTargetRoot = Environment.ExpandEnvironmentVariables(updatedModel.SyncSettings.WindowsTargetRoot);
            foreach (string schemaId in updatedModel.ProfileSettings.EnabledSchemaIds)
            {
                try
                {
                    TemplateService.CaptureTemplates(schemaId, runtimeTargetRoot);
                }
                catch (IOException ex)
                {
                    captureErrors.Add($"schema={schemaId}: {ex.Message}");
                }
            }

            if (_repositoryContext.ModelIds.Contains(resourceId))
            {
                foreach (string schemaId in updatedModel.ProfileSettings.EnabledSchemaIds)
                {
                    try
                    {
                        UserSettingsReader.WriteGrammarDefaults(runtimeTargetRoot, schemaId);
                    }
                    catch (IOException ex)
                    {
                        captureErrors.Add($"grammar_defaults schema={schemaId}: {ex.Message}");
                    }
                }
            }

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Apply,
                status = WorkflowStatuses.Completed,
                resource_id = resourceId,
                config_path = effectiveConfigPath,
                report = JsonSerializer.Deserialize<object>(report),
                capture_errors = captureErrors,
                next_action = "正式资源已安装并部署到当前输入法目录。",
            };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Apply)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"资源对象: {resourceId}",
                            $"配置文件: {effectiveConfigPath}",
                            "下一步: 正式资源已安装并部署到当前输入法目录。",
                            report,
                        ]),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Apply,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_RESOURCE_UPDATE_FAILED", $"正式资源从文件安装失败（{resourceId}）：{exception.Message}", relatedTaskId: "windows_diagnose_result")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    private static bool IsResourceEnabledInConfig(ConfigModel model, string resourceId)
    {
        return model.ProfileSettings.EnabledSchemaIds.Contains(resourceId, StringComparer.OrdinalIgnoreCase)
            || model.DictionarySettings.EnabledDictionaryIds.Contains(resourceId, StringComparer.OrdinalIgnoreCase)
            || model.ModelSettings.EnabledModelIds.Contains(resourceId, StringComparer.OrdinalIgnoreCase);
    }

    private static ConfigModel RemoveFormalResourceFromConfigModel(ConfigModel currentModel, string resourceId)
    {
        List<string> enabledSchemaIds = currentModel.ProfileSettings.EnabledSchemaIds
            .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        string windowsDefaultSchemaId = string.Equals(currentModel.ProfileSettings.WindowsDefaultSchemaId, resourceId, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : currentModel.ProfileSettings.WindowsDefaultSchemaId;
        List<string> enabledDictIds = currentModel.DictionarySettings.EnabledDictionaryIds
            .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<string> dictOrder = currentModel.DictionarySettings.DictionaryOrder
            .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<string> enabledModelIds = currentModel.ModelSettings.EnabledModelIds
            .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        string activeModelId = string.Equals(currentModel.ModelSettings.ActiveModelId, resourceId, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : currentModel.ModelSettings.ActiveModelId;
        List<string> targetSchemaIds = currentModel.FuzzyPinyinSettings.TargetSchemaIds
            .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new ConfigModel
        {
            ConfigVersion = currentModel.ConfigVersion,
            ProfileSettings = new ProfileSettings
            {
                EnabledSchemaIds = enabledSchemaIds,
                WindowsDefaultSchemaId = windowsDefaultSchemaId,
                AndroidDefaultSchemaId = currentModel.ProfileSettings.AndroidDefaultSchemaId,
            },
            FuzzyPinyinSettings = new FuzzyPinyinSettings
            {
                PresetId = currentModel.FuzzyPinyinSettings.PresetId,
                TargetSchemaIds = targetSchemaIds,
            },
            PersonalizationSettings = currentModel.PersonalizationSettings,
            DictionarySettings = new DictionarySettings
            {
                EnabledDictionaryIds = enabledDictIds,
                DictionaryOrder = dictOrder,
                CustomEntries = currentModel.DictionarySettings.CustomEntries,
            },
            ModelSettings = new ModelSettings
            {
                EnabledModelIds = enabledModelIds,
                ActiveModelId = activeModelId,
                ModelRoot = currentModel.ModelSettings.ModelRoot,
                ModelVersions = currentModel.ModelSettings.ModelVersions,
            },
            AndroidSettings = currentModel.AndroidSettings,
            WindowsSettings = currentModel.WindowsSettings,
            SyncSettings = currentModel.SyncSettings,
        };
    }

    public CommandExecutionResult RunUninstallFormalResource(string resourceId, string? configPath, string outputFormat)
    {
        return RunUninstallFormalResource(resourceId, configPath, outputFormat, forceStopWeasel: false);
    }

    public CommandExecutionResult RunUninstallFormalResource(string resourceId, string? configPath, string outputFormat, bool forceStopWeasel, Action<string>? phase = null)
    {
        try
        {
            if (forceStopWeasel)
            {
                phase?.Invoke("正在停止输入法服务…");
                ConfigModel defaultModel = ConfigModel.CreateDefault();
                WindowsEnvironmentState env = WindowsEnvironmentService.Detect(defaultModel);
                TryStopWeaselRuntime(env);
            }

            if (!IsFormalManagedResource(resourceId))
            {
                return new CommandExecutionResult
                {
                    ExitCode = 1,
                    TextOutput = $"此资源（{resourceId}）不在正式资源清单中，无法卸载。\n" +
                                 "当前正式纳管方案仅包含 resource_manifest.json 中登记的资源。\n" +
                                 "运行时发现的承载器自带方案仅支持启用和停用，不支持由本产品卸载。",
                };
            }

            ConfigModel currentModel = LoadPreferredConfigModel(configPath, allowDefault: true);
            bool wasEnabledInConfig = IsResourceEnabledInConfig(currentModel, resourceId);
            ConfigModel updatedModel = RemoveFormalResourceFromConfigModel(currentModel, resourceId);

            List<DiagnosticFinding> findings = _configModelService.Validate(updatedModel, CreateFinding).ToList();
            if (findings.Count > 0)
            {
                DiagnosticReport invalidConfig = BuildDiagnosticReport(
                    WorkflowPhases.Configure,
                    WorkflowStatuses.Blocked,
                    findings,
                    "none",
                    false,
                    false,
                    false);
                _repositoryContext.PersistLastDiagnostic(invalidConfig);
                return CreateCommandResult(invalidConfig, outputFormat);
            }

            string effectiveConfigPath = ResolveMutableConfigPath(configPath);
            phase?.Invoke("正在卸载资源…");
            string report = _resourceUpdateService.UninstallResource(resourceId);

            phase?.Invoke("正在部署并验证…");
            CommandExecutionResult applyResult = ExecuteApplyWorkflow(
                configModel: updatedModel,
                environment: default!,
                outputFormat: outputFormat,
                snapshotId: null,
                windowsOutputFiles: null,
                windowsBinaryOutputFiles: null,
                userDictionaryFiles: null,
                createArtifacts: true);
            if (applyResult.ExitCode == 0)
            {
                _configModelService.Save(effectiveConfigPath, updatedModel);
                _repositoryContext.PersistCurrentConfigModel(updatedModel);
                if (string.Equals(resourceId, "rime_mint", StringComparison.OrdinalIgnoreCase))
                {
                    TemplateService.DeleteSchemaTemplate("rime_mint");
                }

                if (_repositoryContext.ModelIds.Contains(resourceId))
                {
                    string runtimeTargetRoot = Environment.ExpandEnvironmentVariables(updatedModel.SyncSettings.WindowsTargetRoot);
                    foreach (string schemaId in updatedModel.ProfileSettings.EnabledSchemaIds)
                    {
                        try
                        {
                            UserSettingsReader.RemoveGrammarDefaults(runtimeTargetRoot, schemaId);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[RunUninstallResource] RemoveGrammarDefaults 失败 ({schemaId}): {ex.Message}");
                        }
                    }
                }
            }
            if (applyResult.ExitCode != 0)
            {
                return applyResult;
            }

            object payload = new
            {
                platform = "windows",
                phase = WorkflowPhases.Apply,
                status = WorkflowStatuses.Completed,
                resource_id = resourceId,
                config_path = effectiveConfigPath,
                report = JsonSerializer.Deserialize<object>(report),
                config_auto_cleaned = wasEnabledInConfig,
                detail = wasEnabledInConfig
                    ? $"已从配置模型中自动移除对 {resourceId} 的启用引用。"
                    : null,
                next_action = "正式资源已卸载并重新应用到当前输入法目录。",
            };
            string textExtra = wasEnabledInConfig
                ? $"\n注意：资源 {resourceId} 在配置模型中处于启用状态，已自动从配置中移除该引用。"
                : string.Empty;
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : string.Join(
                        Environment.NewLine,
                        [
                            $"阶段: {PhaseLabel(WorkflowPhases.Apply)}",
                            $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                            $"资源对象: {resourceId}",
                            $"配置文件: {effectiveConfigPath}",
                            "下一步: 正式资源已卸载并重新应用到当前输入法目录。",
                            report,
                        ]) + textExtra,
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DiagnosticReport report = BuildDiagnosticReport(
                WorkflowPhases.Apply,
                WorkflowStatuses.Failed,
                [CreateFinding("WINDOWS_RESOURCE_UPDATE_FAILED", $"正式资源卸载失败（{resourceId}）：{exception.Message}", relatedTaskId: "windows_diagnose_result")],
                "none",
                false,
                false,
                false);
            _repositoryContext.PersistLastDiagnostic(report);
            return CreateCommandResult(report, outputFormat);
        }
    }

    public string BuildInstalledResourceStateView()
    {
        return _resourceUpdateService.BuildInstalledResourceStateView();
    }

    public string BuildModelInstallStateView()
    {
        return _resourceUpdateService.BuildModelInstallStateView();
    }

    public IReadOnlyList<FormalResourceDescriptor> GetFormalModelDescriptors()
    {
        return _resourceUpdateService.GetFormalResourceDescriptors("model");
    }

    public IReadOnlyList<FormalResourceDescriptor> GetFormalSchemaDescriptors()
    {
        return _resourceUpdateService.GetFormalResourceDescriptors("schema");
    }

    public bool IsFormalManagedSchema(string schemaId)
    {
        return _resourceUpdateService.GetFormalResourceDescriptors("schema")
            .Any(d => string.Equals(d.ResourceId, schemaId, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsFormalManagedResource(string resourceId)
    {
        return _resourceUpdateService.GetFormalResourceDescriptors("schema")
            .Any(d => string.Equals(d.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            || _resourceUpdateService.GetFormalResourceDescriptors("dictionary")
            .Any(d => string.Equals(d.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            || _resourceUpdateService.GetFormalResourceDescriptors("model")
            .Any(d => string.Equals(d.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<FormalResourceDescriptor> GetFormalDictionaryDescriptors()
    {
        return _resourceUpdateService.GetGuiDictionaryDescriptors();
    }

    public IReadOnlySet<string> GetInstalledDictionaryIds()
    {
        return _resourceUpdateService.GetInstalledResourceIds("dictionary");
    }

    public IReadOnlySet<string> GetInstalledSchemaIds()
    {
        return _resourceUpdateService.GetInstalledResourceIds("schema");
    }

    public IReadOnlySet<string> GetInstalledModelIds()
    {
        return _resourceUpdateService.GetInstalledResourceIds("model");
    }

    public WindowsRuntimeControls GetWindowsRuntimeControls()
    {
        return _repositoryContext.LoadWindowsRuntimeControls();
    }

    public CommandExecutionResult SaveWindowsRuntimeControls(WindowsRuntimeControls controls, string outputFormat)
    {
        _repositoryContext.SaveWindowsRuntimeControls(controls);
        object payload = new
        {
            platform = "windows",
            phase = WorkflowPhases.Configure,
            status = WorkflowStatuses.Completed,
            next_action = "Windows 正式运行控制项已保存。",
            controls,
        };
        return new CommandExecutionResult
        {
            ExitCode = 0,
            TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Serialize(payload, JsonOptions)
                : string.Join(
                Environment.NewLine,
                [
                    $"阶段: {PhaseLabel(WorkflowPhases.Configure)}",
                        $"结果: {StatusLabel(WorkflowStatuses.Completed)}",
                        "下一步: Windows 正式运行控制项已保存。",
                    ]),
            JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
        };
    }

    public string BuildWindowsRuntimeControlsView()
    {
        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
        static string EnabledDisabled(bool value) => value ? "已开启" : "已关闭";
        static string PresentOrNone(string? value) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? "未设置" : value;
        return string.Join(
            Environment.NewLine,
            [
                "自动检查与更新方式",
                "====================",
                $"返回前台自动确认: {EnabledDisabled(controls.AutoRecheckOnReturn)}",
                $"返回后自动检查词库: {EnabledDisabled(controls.AutoCheckFormalResourcesOnReturn)}",
                $"安装成功后自动清理安装文件: {EnabledDisabled(controls.CleanupInstallerArtifactsOnSuccess)}",
                $"部署组件检查失败后自动打开日志: {EnabledDisabled(controls.AutoOpenLogsAfterRepairFailure)}",
                $"小狼毫安装方式: {(controls.PreferSilentWeaselInstall ? "尽量静默完成" : "保留交互式安装器")}",
                $"小狼毫卸载方式: {(controls.PreferSilentWeaselUninstall ? "尽量静默完成" : "保留交互式卸载器")}",
                $"小狼毫版本策略: {controls.WeaselVersionStrategy}",
                $"小狼毫固定安装器地址: {PresentOrNone(controls.WeaselPinnedInstallerUrl)}",
                $"词库版本策略: {controls.FormalResourceVersionStrategy}",
                $"词库固定版本: {PresentOrNone(controls.FormalResourcePinnedRef)}",
            ]);
    }

    public string BuildWindowsCarrierStateView(string? configPath)
    {
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(model);
        string recheckSummaryPath = Path.Combine(_repositoryContext.StateRoot, "last_recheck_summary.json");
        string? pendingInstaller = _repositoryContext.ResolveStateReference("pending_weasel_installer.txt");
        string? lastActivationAttempt = _repositoryContext.ResolveStateReference("last_weasel_activation_attempt.txt");
        static string ReadyMissing(bool value) => value ? "已检测到" : "未检测到";
        static string PresentOrNone(string? value, string fallback) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? fallback : value;
        string latestCheckSummary = BuildFriendlyRecheckSummary(recheckSummaryPath);

        return string.Join(
            Environment.NewLine,
            [
                "小狼毫状态",
                "====================",
                "说明：这里显示小狼毫本体、安装程序和卸载程序是否已经准备好。",
                $"小狼毫本体: {ReadyMissing(environment.WeaselAvailable)}",
                $"当前小狼毫版本: {PresentOrNone(environment.WeaselVersion, "暂未检测到")}",
                $"当前下载来源: {PresentOrNone(environment.WeaselUpdateSource, "暂未检测到")}",
                $"系统默认输入法覆盖: {(environment.DefaultInputMethodIsWeasel ? "已指向小狼毫" : PresentOrNone(environment.DefaultInputMethodTip, "当前未读取到"))}",
                $"部署工具位置: {PresentOrNone(environment.DeployerPath, "未找到")}",
                $"卸载工具位置: {PresentOrNone(environment.UninstallerPath, "未找到")}",
                $"卸载工具参数: {PresentOrNone(environment.UninstallerArguments, "无")}",
                $"上次启动的安装程序: {PresentOrNone(pendingInstaller, "无")}",
                $"上次自动激活尝试: {PresentOrNone(lastActivationAttempt, "无")}",
                string.Empty,
                "最近一次自动确认",
                "--------------------",
                latestCheckSummary,
            ]);
    }

    public string BuildUserDataStateView(string? configPath)
    {
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        string targetRoot = RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot);
        string[] userDictFiles = Directory.Exists(targetRoot)
            ? Directory.GetFiles(targetRoot, "*.userdb.txt", SearchOption.TopDirectoryOnly)
            : [];
        string currentConfigPath = _repositoryContext.CurrentConfigModelPath;
        string? latestSnapshot = _repositoryContext.ResolveStateReference("latest_successful_snapshot.txt")
            ?? _repositoryContext.ResolveStateReference("latest_generated_snapshot.txt");
        string? latestBackup = _repositoryContext.ResolveStateReference("latest_backup.txt");
        string? conflictDecision = _repositoryContext.ResolveStateReference("last_conflict_recovery_decision.txt");

        return string.Join(
            Environment.NewLine,
            [
                "正式用户数据与恢复状态",
                "====================",
                $"输入法目录: {targetRoot}",
                $"自定义词条数量: {model.DictionarySettings.CustomEntries.Count}",
                $"用户词典文件数: {userDictFiles.Length}",
                $"最近快照: {latestSnapshot ?? "无"}",
                $"最近备份: {latestBackup ?? "无"}",
                $"最近恢复方式: {conflictDecision ?? "无"}",
                string.Empty,
                "用户词典文件",
                "--------------------",
                userDictFiles.Length == 0 ? "当前还没有用户词典文件。" : string.Join(Environment.NewLine, userDictFiles),
                string.Empty,
                "当前已保存的用户数据设置",
                "--------------------",
                File.Exists(currentConfigPath)
                    ? $"已保存配置文件。自定义词条 {model.DictionarySettings.CustomEntries.Count} 条，用户词典文件 {userDictFiles.Length} 个。"
                    : "当前还没有保存过设置。",
            ]);
    }

    public string BuildInputSchemeStateView(string? configPath)
    {
        ConfigModel expected = LoadPreferredConfigModel(configPath, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(expected);
        string? lastActivationAttempt = _repositoryContext.ResolveStateReference("last_weasel_activation_attempt.txt");
        static string PresentOrNone(string? value, string fallback) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? fallback : value;
        if (!environment.TargetRootAccessible || !Directory.Exists(environment.WindowsTargetRoot))
        {
            return string.Join(
                Environment.NewLine,
                [
                    "输入方案与设置生效情况",
                    "====================",
                    "说明：这里会检查薄荷方案、候选区、简繁、字体字号等是否已经写入输入法目录。",
                    $"当前还不能读取输入法目录：{environment.WindowsTargetRoot}",
                    "所以这一步暂时不能确认这些设置有没有真正生效。",
                ]);
        }

        return string.Join(
            Environment.NewLine,
            [
                "输入方案与设置生效情况",
                "====================",
                $"系统默认输入法覆盖: {(environment.DefaultInputMethodIsWeasel ? "已指向小狼毫" : PresentOrNone(environment.DefaultInputMethodTip, "当前未读取到"))}",
                $"当前前台窗口: {PresentOrNone(environment.ForegroundProcessName, "当前未读取到")}",
                $"当前前台窗口布局: {PresentOrNone(environment.ForegroundKeyboardLayout, "当前未读取到")}",
                $"当前前台窗口 IME 打开状态: {DescribeForegroundInputOpenState(environment.ForegroundInputContextOpen)}",
                $"当前前台窗口转换状态: {PresentOrNone(environment.ForegroundConversionStatus, "当前未读取到")}",
                $"上次自动激活尝试: {PresentOrNone(lastActivationAttempt, "无")}",
                "说明：文件已写入、已执行部署、已尝试激活，并不等于每个新窗口都已经自动进入中文态；真正的中文/英文输入状态仍需在目标窗口里实际输入确认。",
                string.Empty,
                BuildEffectiveSettingsAuditView(configPath),
            ]);
    }

    public string BuildSettingsDetectionView(string? configPath)
    {
        if (!WindowsEnvironmentService.Detect(ConfigModel.CreateDefault()).WeaselAvailable)
        {
            return "承载器未安装，无法检测设置是否已在输入法目录中生效。请先安装承载器。";
        }

        return BuildEffectiveSettingsAuditView(configPath);
    }

    public string BuildLexiconAndModelStateView(string? configPath)
    {
        ConfigModel model = LoadPreferredConfigModel(configPath, allowDefault: true);
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            [
                "词库与语法模型状态",
                "====================",
                "说明：这里显示已安装词库、已安装模型，以及它们是否已经进入当前输入法目录。",
                BuildInstalledResourceStateView(),
                BuildModelInstallStateView(),
                DescribeModelLanguageCompatibility(model),
                $"当前模型: {(string.IsNullOrWhiteSpace(model.ModelSettings.ActiveModelId) ? "未选择" : model.ModelSettings.ActiveModelId)}",
                $"词库加载顺序: {(model.DictionarySettings.DictionaryOrder.Count == 0 ? "无" : string.Join("、", model.DictionarySettings.DictionaryOrder))}",
            ]);
    }

    private static string BuildFriendlyRecheckSummary(string recheckSummaryPath)
    {
        if (!File.Exists(recheckSummaryPath))
        {
            return "还没有检查记录。";
        }

        using JsonDocument document = JsonDocument.Parse(RepositoryContext.ReadUtf8(recheckSummaryPath));
        string status = document.RootElement.TryGetProperty("status", out JsonElement statusElement)
            ? StatusLabel(statusElement.GetString() ?? string.Empty)
            : "未知";
        JsonElement findings = document.RootElement.TryGetProperty("findings", out JsonElement findingsElement) &&
                               findingsElement.ValueKind == JsonValueKind.Array
            ? findingsElement
            : default;
        int findingCount = findings.ValueKind == JsonValueKind.Array ? findings.GetArrayLength() : 0;
        string latestFinding = findingCount > 0
            ? findings[0].GetProperty("summary").GetString() ?? "存在问题"
            : "没有发现阻塞问题。";

        return string.Join(
            Environment.NewLine,
            [
                $"检查结果: {status}",
                $"发现的问题数量: {findingCount}",
                $"当前最需要先处理的问题: {LocalizeUserFacingText(latestFinding)}",
            ]);
    }

    private static string LocalizeUserFacingText(string text)
    {
        const string deployerPlaceholder = "__RIMEKIT_WEASEL_DEPLOYER__";
        return text
            .Replace("WeaselDeployer.exe", deployerPlaceholder, StringComparison.Ordinal)
            .Replace("Windows 承载器 Weasel", "桌面输入法承载器（小狼毫）", StringComparison.Ordinal)
            .Replace("Weasel", "小狼毫", StringComparison.Ordinal)
            .Replace("Windows 目标目录", "输入法目录", StringComparison.Ordinal)
            .Replace("回检", "再次确认", StringComparison.Ordinal)
            .Replace("运行态", "当前输入法状态", StringComparison.Ordinal)
            .Replace(deployerPlaceholder, "小狼毫部署器", StringComparison.Ordinal);
    }

    public string BuildEffectiveSettingsAuditView(string? configPath)
    {
        ConfigModel expected = LoadPreferredConfigModel(configPath, allowDefault: true);
        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(expected);
        static string PresentOrNone(string? value, string fallback) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? fallback : value;
        if (!environment.TargetRootAccessible || !Directory.Exists(environment.WindowsTargetRoot))
        {
            return string.Join(
                Environment.NewLine,
                [
                    "已生效检查",
                    "====================",
                    $"当前还不能读取输入法目录：{environment.WindowsTargetRoot}",
                    "所以这一步暂时不能确认设置有没有真正生效。",
                    "请先完成安装，或把输入法目录改成当前机器上可访问的位置，再点击“检查当前状态”。",
                ]);
        }

        ConfigModel actual = _artifactService.ImportRuntimeToConfig(environment.WindowsTargetRoot, expected);
        string defaultCustomPath = Path.Combine(environment.WindowsTargetRoot, "default.custom.yaml");
        string sharedCustomPath = Path.Combine(environment.WindowsTargetRoot, "rime_mint.custom.yaml");
        string weaselCustomPath = Path.Combine(environment.WindowsTargetRoot, "weasel.custom.yaml");
        string runtimeDictionaryPath = Path.Combine(environment.WindowsTargetRoot, "rime_mint.dict.yaml");
        string runtimeCustomDictionaryPath = Path.Combine(environment.WindowsTargetRoot, "rime_mint.custom.dict.yaml");
        string runtimeSimpleTablePath = Path.Combine(environment.WindowsTargetRoot, "dicts", "rime_mint.simple.txt");

        bool hasDefaultCustom = File.Exists(defaultCustomPath);
        bool hasSharedCustom = File.Exists(sharedCustomPath);
        bool hasWeaselCustom = File.Exists(weaselCustomPath);
        bool hasRuntimeDictionary = File.Exists(runtimeDictionaryPath) || File.Exists(runtimeCustomDictionaryPath);
        bool hasRuntimeCustomEntries = File.Exists(runtimeSimpleTablePath);

        static string DescribeRuntimeMissing(string fileName) => $"当前输入法目录里缺少 {fileName}，所以这一项现在不能判成已生效。";
        List<string> lines =
        [
            "已生效检查",
            "====================",
            $"当前前台窗口: {PresentOrNone(environment.ForegroundProcessName, "当前未读取到")}",
            $"当前前台窗口布局: {PresentOrNone(environment.ForegroundKeyboardLayout, "当前未读取到")}",
            $"当前前台窗口 IME 打开状态: {DescribeForegroundInputOpenState(environment.ForegroundInputContextOpen)}",
            $"当前前台窗口转换状态: {PresentOrNone(environment.ForegroundConversionStatus, "当前未读取到")}",
            DescribeForegroundRuntimeBlock(environment),
            string.Empty,
            hasDefaultCustom
                ? DescribeAuditLine("启用方案", string.Join("、", expected.ProfileSettings.EnabledSchemaIds), string.Join("、", actual.ProfileSettings.EnabledSchemaIds))
                : DescribeAuditUnavailable("启用方案", DescribeRuntimeMissing("default.custom.yaml")),
            hasSharedCustom
                ? DescribeAuditLine("模糊拼音适用方案", string.Join("、", expected.FuzzyPinyinSettings.TargetSchemaIds), string.Join("、", actual.FuzzyPinyinSettings.TargetSchemaIds))
                : DescribeAuditUnavailable("模糊拼音适用方案", DescribeRuntimeMissing("rime_mint.custom.yaml")),
            hasWeaselCustom
                ? DescribeAuditLine("字体", "设置已迁移到输入法配置文件，请直接查看 weasel.custom.yaml", "设置已迁移到输入法配置文件，请直接查看 weasel.custom.yaml")
                : DescribeAuditUnavailable("字体", DescribeRuntimeMissing("weasel.custom.yaml")),
            hasWeaselCustom
                ? DescribeAuditLine("字号", "设置已迁移到输入法配置文件，请直接查看 weasel.custom.yaml", "设置已迁移到输入法配置文件，请直接查看 weasel.custom.yaml")
                : DescribeAuditUnavailable("字号", DescribeRuntimeMissing("weasel.custom.yaml")),
            hasWeaselCustom
                ? DescribeAuditLine("通知", "设置已迁移到输入法配置文件，请直接查看 weasel.custom.yaml", "设置已迁移到输入法配置文件，请直接查看 weasel.custom.yaml")
                : DescribeAuditUnavailable("通知", DescribeRuntimeMissing("weasel.custom.yaml")),
            hasRuntimeDictionary
                ? DescribeAuditLine("启用词库", string.Join("、", expected.DictionarySettings.EnabledDictionaryIds), string.Join("、", actual.DictionarySettings.EnabledDictionaryIds))
                : DescribeAuditUnavailable("启用词库", DescribeRuntimeMissing("rime_mint.dict.yaml")),
            hasRuntimeDictionary
                ? DescribeAuditLine("词库加载顺序", string.Join("、", expected.DictionarySettings.DictionaryOrder), string.Join("、", actual.DictionarySettings.DictionaryOrder))
                : DescribeAuditUnavailable("词库加载顺序", DescribeRuntimeMissing("rime_mint.dict.yaml")),
            hasRuntimeCustomEntries
                ? DescribeAuditLine("自定义词条数量", expected.DictionarySettings.CustomEntries.Count.ToString(), actual.DictionarySettings.CustomEntries.Count.ToString())
                : DescribeAuditUnavailable("自定义词条数量", DescribeRuntimeMissing(@"dicts\\rime_mint.simple.txt")),
            hasSharedCustom
                ? DescribeAuditLine("当前模型", string.IsNullOrWhiteSpace(expected.ModelSettings.ActiveModelId) ? "未选择" : expected.ModelSettings.ActiveModelId, string.IsNullOrWhiteSpace(actual.ModelSettings.ActiveModelId) ? "未选择" : actual.ModelSettings.ActiveModelId)
                : DescribeAuditUnavailable("当前模型", DescribeRuntimeMissing("rime_mint.custom.yaml")),
            DescribeModelLanguageCompatibility(expected),
            DescribeModelVersionAudit(expected, environment.WindowsTargetRoot),
            string.Empty,
            "当前无法直接自动确认的设置",
            "--------------------",
            DescribeAuditUnavailable("符号配置 ID", "当前目标文件里没有稳定、可直接反查的字段。"),
            DescribeAuditUnavailable("预编辑格式", "当前目标文件里没有稳定、可直接反查的字段。"),
            DescribeAuditUnavailable("自定义词条模式", "当前目标文件里没有稳定、可直接反查的字段。"),
            DescribeAuditUnavailable("注释样式变体", "当前目标文件里没有稳定、可直接反查的字段。"),
        ];

        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeAuditLine(string label, string expected, string actual)
    {
        bool matched = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        return matched
            ? $"{label}: 已生效"
            : $"{label}: 未生效（当前读取到：{actual}；你保存的是：{expected}）";
    }

    private static string DescribeAuditUnavailable(string label, string reason)
    {
        return $"{label}: 当前环境还不能自动确认（原因: {reason}）。这不能当成已经生效。";
    }

    private static string DescribeModelLanguageCompatibility(ConfigModel model)
    {
        if (string.Equals(model.ModelSettings.ActiveModelId, "wanxiang_lts_zh_hans", StringComparison.OrdinalIgnoreCase) &&
            model.ModelSettings.EnabledModelIds.Contains("wanxiang_lts_zh_hans", StringComparer.OrdinalIgnoreCase))
        {
            return "模型与简繁模式: 当前启用的是简体万象模型，但简繁模式仍是繁体。这个组合会让句子级效果与官方简体示例不一致；在这种状态下，不能把“模型已就绪”等同于“模型已按官方示例正确生效”。";
        }

        return "模型与简繁模式: 当前没有发现“简体模型配繁体模式”的明显组合冲突。";
    }

    private static string DescribeForegroundInputOpenState(bool? isOpen)
    {
        return isOpen switch
        {
            true => "已打开",
            false => "未打开",
            null => "当前未读取到",
        };
    }

    private static string DescribeForegroundRuntimeBlock(WindowsEnvironmentState environment)
    {
        if (environment.ForegroundInputContextOpen == false)
        {
            return "运行态阻塞: 当前前台窗口的输入法上下文还没有打开。在这种窗口里直接输入，可能只会得到英文或数字；这时不能把候选、翻页、候选数等真实效果判成失败，也不能当成已经生效。";
        }

        if (environment.ForegroundInputContextOpen == true)
        {
            return "运行态提示: 当前前台窗口的输入法上下文已打开，可以继续用真实输入来确认候选、翻页和配置项是否真的生效。";
        }

        return "运行态提示: 当前还没有读到前台窗口的输入法上下文状态；在目标窗口实际输入之前，不能把“已写入”和“已生效”视为同一件事。";
    }

    private static string DescribeModelVersionAudit(ConfigModel expected, string targetRoot)
    {
        if (expected.ModelSettings.ModelVersions.Count == 0)
        {
            return "模型版本记录: 当前没有登记版本。";
        }

        List<string> states = [];
        foreach ((string modelId, string version) in expected.ModelSettings.ModelVersions.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            bool exists = modelId switch
            {
                "wanxiang_lts_zh_hans" => File.Exists(Path.Combine(targetRoot, "wanxiang-lts-zh-hans.gram")),
                _ => false,
            };
            states.Add($"{modelId}: {(exists ? $"已就绪（登记版本: {version}）" : $"未就绪（登记版本: {version}）")}");
        }

        return "模型版本记录: " + string.Join("；", states);
    }

    private void FinalizePendingWeaselInstall(WindowsEnvironmentState environment)
    {
        string? installerPath = _repositoryContext.ResolveStateReference("pending_weasel_installer.txt");
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            return;
        }

        _repositoryContext.ClearStateReference("pending_weasel_installer.txt");

        try
        {
            RepositoryContext.WriteUtf8(_repositoryContext.InstalledResourcesStatePath, "[]");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[r47] 重置 installed_resources 失败: {ex.Message}");
        }

        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();

        try
        {
            if (Path.GetFullPath(installerPath).StartsWith(_repositoryContext.DownloadsRoot, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(installerPath))
            {
                Utilities.FileHelper.DeleteFileWithBackoff(installerPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[r47] 安装器清理失败: {ex.Message}");
        }

        if (controls.AutoCheckFormalResourcesOnReturn)
        {
            _resourceUpdateService.CheckForUpdates();
        }
    }

    private DiagnosticFinding? FinalizePendingWeaselUninstall(ConfigModel configModel, WindowsEnvironmentState environment)
    {
        UninstallTrace("Finalize: checking targets...");
        IReadOnlyList<string> pendingTargets = _repositoryContext.ResolvePendingWeaselUninstallTargets();
        if (pendingTargets.Count == 0)
        {
            UninstallTrace("Finalize: 0 targets, nothing to clean");
            _repositoryContext.ClearPendingWeaselUninstallTargets();
            return null;
        }

        UninstallTrace($"Finalize: {pendingTargets.Count} targets to clean");

        List<string> regularTargets = [];
        List<string> protectedTargets = [];
        foreach (string target in pendingTargets)
        {
            if (IsProtectedInstallRoot(target))
                protectedTargets.Add(target);
            else
                regularTargets.Add(target);
        }

        List<string> cleanupFailures = [];
        foreach (string target in regularTargets)
        {
            UninstallTrace($"Finalize: deleting regular target [{target}]...");
            string? error = DeleteDirectoryIfExists(target);
            if (!string.IsNullOrWhiteSpace(error))
            {
                UninstallTrace($"Finalize: FAILED [{target}]: {error}");
                cleanupFailures.Add(error);
            }
            else
            {
                UninstallTrace($"Finalize: OK [{target}]");
            }
        }

        foreach (string target in protectedTargets)
        {
            UninstallTrace($"Finalize: protected target [{target}] — needs elevation");
            cleanupFailures.Add($"{target}: 需要管理员权限才能删除。");
        }

        if (cleanupFailures.Count == 0)
        {
            UninstallTrace("Finalize: all targets cleaned");
            _repositoryContext.ClearPendingWeaselUninstallTargets();
            _repositoryContext.PersistStateReference(WeaselExpectedAbsentStateName, "1");
            return null;
        }

        List<string> failedDirectories = [];
        foreach (string target in pendingTargets)
        {
            string resolved = Path.GetFullPath(target);
            if (Directory.Exists(resolved) && !IsProtectedInstallRoot(target))
                failedDirectories.Add(target);
        }
        foreach (string target in protectedTargets)
        {
            string resolved = Path.GetFullPath(target);
            if (Directory.Exists(resolved))
                failedDirectories.Add(target);
        }

        UninstallTrace($"Finalize: launching elevated cleanup for {failedDirectories.Count} directories...");
        bool isAdmin = IsCurrentProcessAdmin();
        UninstallTrace($"Finalize: IsAdmin={isAdmin}");

        if (isAdmin)
        {
            UninstallTrace("Finalize: running as admin, deleting protected dirs directly...");
            RunElevatedCleanupScript(failedDirectories);
            bool allGone = true;
            foreach (string dir in failedDirectories)
            {
                if (Directory.Exists(Path.GetFullPath(dir)))
                {
                    allGone = false;
                    UninstallTrace($"Finalize: STILL EXISTS [{dir}]");
                }
            }

            if (allGone)
            {
                UninstallTrace("Finalize: admin direct cleanup succeeded");
                _repositoryContext.ClearPendingWeaselUninstallTargets();
                _repositoryContext.PersistStateReference(WeaselExpectedAbsentStateName, "1");
                return null;
            }

            UninstallTrace("Finalize: admin direct cleanup FAILED");
            return CreateFinding(
                "WINDOWS_RESIDUAL_CLEANUP_FAILED",
                $"卸载后自动清理 Rime/Weasel 目录失败：以下目录无法删除 — {string.Join(", ", failedDirectories)}。",
                relatedTaskId: "windows_launch_weasel_uninstaller");
        }

        bool elevatedLaunched = TryLaunchElevatedResidualCleanup(failedDirectories);
        if (!elevatedLaunched)
        {
            UninstallTrace("Finalize: elevated cleanup failed to launch");
            return CreateFinding(
                "WINDOWS_RESIDUAL_CLEANUP_FAILED",
                $"卸载后自动清理 Rime/Weasel 目录失败。以下目录需要管理员权限：{string.Join(", ", protectedTargets)}",
                relatedTaskId: "windows_launch_weasel_uninstaller");
        }

        UninstallTrace("Finalize: polling elevated cleanup result...");
        bool elevatedSucceeded = PollElevatedCleanupResult(failedDirectories, timeoutMs: 60000);
        if (elevatedSucceeded)
        {
            UninstallTrace("Finalize: elevated cleanup succeeded");
            _repositoryContext.ClearPendingWeaselUninstallTargets();
            _repositoryContext.PersistStateReference(WeaselExpectedAbsentStateName, "1");
            return null;
        }

        UninstallTrace("Finalize: elevated cleanup timed out or failed");
        return CreateFinding(
            "WINDOWS_RESIDUAL_CLEANUP_FAILED",
            $"卸载后自动清理 Rime/Weasel 目录失败：以下目录无法删除 — {string.Join(", ", failedDirectories)}。",
            relatedTaskId: "windows_launch_weasel_uninstaller");
    }

    private static string? DeleteDirectoryIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string resolvedPath = Path.GetFullPath(path);
        if (!Directory.Exists(resolvedPath))
        {
            return null;
        }

        if (IsProtectedInstallRoot(resolvedPath) && !Directory.EnumerateFileSystemEntries(resolvedPath).Any())
        {
            return null;
        }

        try
        {
            UninstallTrace($"DeleteDir: [{resolvedPath}] calling DeleteDirectoryWithBackoff (30 retries, maxDelay=10000)...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Utilities.FileHelper.DeleteDirectoryWithBackoff(
                resolvedPath,
                maxRetries: 30,
                baseDelayMs: 200,
                maxDelayMs: 10000);
            sw.Stop();
            UninstallTrace($"DeleteDir: [{resolvedPath}] SUCCESS in {sw.ElapsedMilliseconds}ms");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UninstallTrace($"DeleteDir: [{resolvedPath}] FAILED after retries: {ex.Message}");
            try
            {
                if (IsProtectedInstallRoot(resolvedPath) &&
                    Directory.Exists(resolvedPath) &&
                    !Directory.EnumerateFileSystemEntries(resolvedPath).Any())
                {
                    return null;
                }
            }
            catch (Exception checkEx) when (checkEx is InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupPath] 检查目录失败 ({resolvedPath}): {checkEx.Message}");
            }

            return $"{resolvedPath}: {ex.Message}";
        }
    }

    private static bool IsCurrentProcessAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void RunElevatedCleanupScript(IReadOnlyList<string> targets)
    {
        List<string> lines = ["@echo off"];
        foreach (string target in targets.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            string escaped = target.Replace("\"", "\"\"");
            lines.Add($"if exist \"{escaped}\" rd /s /q \"{escaped}\" >nul 2>nul");
        }

        string scriptPath = Path.Combine(Path.GetTempPath(), $"rimekit_cleanup_{Guid.NewGuid():N}.cmd");
        try
        {
            File.WriteAllText(scriptPath, string.Join("\r\n", lines) + "\r\n", new System.Text.UTF8Encoding(false));
            using Process? proc = Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            proc?.WaitForExit(30000);
            Utilities.FileHelper.DeleteFileWithBackoff(scriptPath, maxRetries: 5, baseDelayMs: 100, maxDelayMs: 2000);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RunElevatedCleanupScript] 失败: {ex.Message}");
            TryDeleteStaleFile(scriptPath);
        }
    }

    private bool PollElevatedCleanupResult(IReadOnlyList<string> targets, int timeoutMs)
    {
        string resultPath = Path.Combine(_repositoryContext.StateRoot, ElevatedCleanupResultName);
        int delay = 200;
        int maxDelay = 2000;
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (File.Exists(resultPath))
            {
                string status = RepositoryContext.ReadUtf8(resultPath).Trim();
                if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    bool anyStillExists = targets.Any(t => Directory.Exists(Path.GetFullPath(t)));
                    if (!anyStillExists)
                        return true;
                }

                if (!string.Equals(status, "started", StringComparison.OrdinalIgnoreCase))
                {
                    UninstallTrace($"PollElevated: final result={status}, anyStillExists={targets.Any(t => Directory.Exists(Path.GetFullPath(t)))}");
                    return false;
                }

                UninstallTrace($"PollElevated: script running (status={status}), continuing to wait...");
            }

            Thread.Sleep(delay);
            waited += delay;
            delay = Math.Min(delay * 2, maxDelay);
        }

        UninstallTrace("PollElevated: timeout");
        return false;
    }

    private static bool IsProtectedInstallRoot(string path)
    {
        string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        return (!string.IsNullOrWhiteSpace(programFiles) &&
                normalizedPath.Equals(Path.Combine(programFiles, "Rime"), StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(programFilesX86) &&
                normalizedPath.Equals(Path.Combine(programFilesX86, "Rime"), StringComparison.OrdinalIgnoreCase));
    }

    private static void TryDeleteStaleFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Utilities.FileHelper.DeleteFileWithBackoff(path, maxRetries: 5, baseDelayMs: 100, maxDelayMs: 2000);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Workflow] Failed to delete stale file: {path} — {ex.Message}");
        }
    }

    private bool TryLaunchElevatedResidualCleanup(IReadOnlyList<string> targets)
    {
        try
        {
            string scriptPath = Path.Combine(_repositoryContext.StateRoot, ElevatedCleanupScriptName);
            string resultPath = Path.Combine(_repositoryContext.StateRoot, ElevatedCleanupResultName);
            TryDeleteStaleFile(scriptPath);
            TryDeleteStaleFile(resultPath);
            List<string> lines =
            [
                "@echo off",
                "setlocal enableextensions",
                $"set \"RESULT_PATH={resultPath}\"",
                "> \"%RESULT_PATH%\" echo started",
                "taskkill /IM WeaselSetup.exe /F >nul 2>nul",
                "taskkill /IM WeaselServer.exe /F >nul 2>nul",
                "taskkill /IM WeaselDeployer.exe /F >nul 2>nul",
            ];

            foreach (string target in targets.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string escapedTarget = target.Replace("\"", "\"\"");
                lines.Add($"if exist \"{escapedTarget}\" rd /s /q \"{escapedTarget}\" >nul 2>nul");
            }

            lines.Add("set STATUS=completed");
            foreach (string target in targets.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string escapedTarget = target.Replace("\"", "\"\"");
                lines.Add($"if exist \"{escapedTarget}\" set STATUS=failed");
            }

            lines.Add("> \"%RESULT_PATH%\" echo %STATUS%");
            Utilities.FileHelper.WriteTextWithVerification(scriptPath, string.Join("\r\n", lines) + "\r\n", new System.Text.UTF8Encoding(false));

            string cmdExe = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(cmdExe))
                cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");

            UninstallTrace($"ElevatedCleanup: cmdExe={cmdExe}");
            UninstallTrace($"ElevatedCleanup: scriptPath={scriptPath}");
            UninstallTrace($"ElevatedCleanup: script content:");
            foreach (string line in lines)
                UninstallTrace($"  {line}");

            Process.Start(new ProcessStartInfo
            {
                FileName = cmdExe,
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            UninstallTrace("ElevatedCleanup: Process.Start returned");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[r47] 提权清理失败: {ex.Message}");
            return false;
        }
    }

    private bool TryConsumeElevatedCleanupResult(IReadOnlyList<string> targets)
    {
        string resultPath = Path.Combine(_repositoryContext.StateRoot, ElevatedCleanupResultName);
        if (!File.Exists(resultPath))
        {
            return false;
        }

        string status = RepositoryContext.ReadUtf8(resultPath).Trim();
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool anyTargetStillExists = targets.Any(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
        if (anyTargetStillExists)
        {
            return false;
        }

        Utilities.FileHelper.DeleteFileWithBackoff(resultPath, maxRetries: 5, baseDelayMs: 100, maxDelayMs: 2000);
        string scriptPath = Path.Combine(_repositoryContext.StateRoot, ElevatedCleanupScriptName);
        if (File.Exists(scriptPath))
        {
            Utilities.FileHelper.DeleteFileWithBackoff(scriptPath, maxRetries: 5, baseDelayMs: 100, maxDelayMs: 2000);
        }

        _repositoryContext.PersistStateReference(WeaselExpectedAbsentStateName, "1");
        return true;
    }

    private static IReadOnlyList<string> BuildWeaselCleanupTargets(ConfigModel configModel, WindowsEnvironmentState environment)
    {
        HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase)
        {
            RepositoryContext.ExpandPath(configModel.SyncSettings.WindowsTargetRoot),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rime"),
        };

        foreach (string? path in new[]
                 {
                     environment.DeployerPath,
                     environment.UninstallerPath,
                 })
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string directoryPath = File.Exists(path)
                ? Path.GetDirectoryName(path) ?? string.Empty
                : path;
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                targets.Add(Path.GetFullPath(directoryPath));
                string leafDirectoryName = Path.GetFileName(directoryPath);
                string? parentDirectory = Path.GetDirectoryName(directoryPath);
                if (!string.IsNullOrWhiteSpace(parentDirectory) &&
                    leafDirectoryName.Contains("weasel", StringComparison.OrdinalIgnoreCase))
                {
                    targets.Add(Path.GetFullPath(parentDirectory));
                }
            }
        }

        return targets.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
    }

    private static bool ShouldUseSilentInstaller(string installerPath, WindowsRuntimeControls controls)
    {
        return controls.PreferSilentWeaselInstall &&
               string.Equals(Path.GetExtension(installerPath), ".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseSilentUninstaller(WindowsEnvironmentState environment, WindowsRuntimeControls controls)
    {
        return controls.PreferSilentWeaselUninstall &&
               !string.IsNullOrWhiteSpace(environment.UninstallerPath) &&
               string.Equals(Path.GetExtension(environment.UninstallerPath), ".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildInstallerLaunchArguments(string installerPath, WindowsRuntimeControls controls)
    {
        return ShouldUseSilentInstaller(installerPath, controls) ? "/S" : string.Empty;
    }

    private static string BuildUninstallerLaunchArguments(WindowsEnvironmentState environment, WindowsRuntimeControls controls)
    {
        string existingArguments = environment.UninstallerArguments ?? string.Empty;
        if (!ShouldUseSilentUninstaller(environment, controls))
        {
            return existingArguments;
        }

        if (!string.IsNullOrWhiteSpace(existingArguments))
        {
            return existingArguments.Contains("/S", StringComparison.OrdinalIgnoreCase)
                ? existingArguments
                : $"/S {existingArguments}".Trim();
        }

        string fileName = Path.GetFileName(environment.UninstallerPath ?? string.Empty);
        if (string.Equals(fileName, "uninstall.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "unins000.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "/S";
        }

        return "/S";
    }

    private static string DescribeExecutionMode(bool silentMode)
    {
        return silentMode ? "程序静默执行" : "外部向导交互执行";
    }

    private void TryActivateWeaselProfileInDetachedProcess()
    {
        foreach (ProcessStartInfo startInfo in ResolveWeaselActivatorStartInfos())
        {
            string attemptPath = startInfo.FileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? startInfo.Arguments
                : startInfo.FileName;

            try
            {
                _repositoryContext.PersistStateReference("last_weasel_activation_attempt.txt", $"launching\t{attemptPath}");
                using Process? process = Process.Start(startInfo);
                if (process is null)
                {
                    _repositoryContext.PersistStateReference("last_weasel_activation_attempt.txt", $"failed_to_launch\t{attemptPath}");
                    continue;
                }

                if (!Utilities.ProcessHelper.WaitForExitWithBackoff(process, 5000))
                {
                    TryTerminateProcess(process);
                    _repositoryContext.PersistStateReference("last_weasel_activation_attempt.txt", $"timed_out\t{attemptPath}");
                    continue;
                }

                _repositoryContext.PersistStateReference(
                    "last_weasel_activation_attempt.txt",
                    $"exited\t{process.ExitCode}\t{attemptPath}");
                if (process.ExitCode == 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _repositoryContext.PersistStateReference("last_weasel_activation_attempt.txt", $"exception\\{ex.GetType().Name}\\{attemptPath}");
                System.Diagnostics.Debug.WriteLine($"[Activator] TryActivateWeaselProfile failed: {ex.Message}");
            }
        }

        _repositoryContext.PersistStateReference("last_weasel_activation_attempt.txt", "activator_missing");
    }

    private IEnumerable<ProcessStartInfo> ResolveWeaselActivatorStartInfos()
    {
        string? explicitActivatorPath = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_ACTIVATOR_PATH");
        if (!string.IsNullOrWhiteSpace(explicitActivatorPath))
        {
            string fullExplicitActivatorPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitActivatorPath));
            if (File.Exists(fullExplicitActivatorPath))
            {
                string explicitWorkingDirectory = Path.GetDirectoryName(fullExplicitActivatorPath) ?? AppContext.BaseDirectory;
                yield return new ProcessStartInfo
                {
                    FileName = fullExplicitActivatorPath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = explicitWorkingDirectory,
                };
            }
        }

        HashSet<string> candidateRoots = [ _repositoryContext.RepositoryRoot ];
        string? sourceRepositoryRoot = ResolveSourceRepositoryRootFromAppBase();
        if (!string.IsNullOrWhiteSpace(sourceRepositoryRoot))
        {
            candidateRoots.Add(sourceRepositoryRoot);
        }

        foreach (string root in candidateRoots)
        {
            foreach (string config in new[] { "Debug", "Release" })
            {
                string buildDirectory = Path.Combine(
                    root,
                    "apps",
                    "windows",
                    "RimeKit.Windows.Activator",
                    "bin",
                    config,
                    "net10.0-windows");
                string activatorExePath = Path.Combine(buildDirectory, "RimeKit.Windows.Activator.exe");
                if (File.Exists(activatorExePath))
                {
                    yield return new ProcessStartInfo
                {
                    FileName = activatorExePath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = buildDirectory,
                };
                }

                string activatorDllPath = Path.Combine(buildDirectory, "RimeKit.Windows.Activator.dll");
                if (File.Exists(activatorDllPath))
                {
                    yield return new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{activatorDllPath}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = buildDirectory,
                };
                }
            }
        }
    }

    private static string? ResolveSourceRepositoryRootFromAppBase()
    {
        string?[] candidateStarts =
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(WindowsWorkflowService).Assembly.Location),
            Environment.CurrentDirectory,
        };

        foreach (string? start in candidateStarts.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return RepositoryContext.DiscoverRepositoryRoot(start!);
            }
            catch (DirectoryNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscoverRepositoryRoot] Directory not found: {ex.Message}");
            }
        }

        return null;
    }

    private static void TryTerminateProcess(Process process)
    {
        Utilities.ProcessHelper.TerminateProcess(process);
    }

    private DiagnosticFinding CreateFinding(
        string code,
        string detail,
        string? backupId = null,
        string? conflictScope = null,
        string? relatedTaskId = null,
        IReadOnlyList<string>? logRefs = null)
    {
        if (!_repositoryContext.ErrorCodes.TryGetValue(code, out ErrorCodeDefinition? definition))
        {
            return new DiagnosticFinding
            {
                Code = code,
                Severity = WorkflowSeverities.Fatal,
                Summary = code,
                Detail = detail,
                DisplayKind = ResolveTaskDisplayKind(relatedTaskId) ?? FeedbackDisplayKinds.ExplicitError,
                AutoActionKind = ResolveTaskAutoActionKind(relatedTaskId) ?? AutoActionKinds.None,
                EntryPointKind = EntryPointKinds.None,
                BackupId = backupId,
                ConflictScope = conflictScope,
                RelatedTaskId = relatedTaskId,
                LogRefs = logRefs,
            };
        }

        return new DiagnosticFinding
        {
            Code = definition.Code,
            Severity = definition.Severity,
            Summary = definition.DefaultSummary,
            Detail = detail,
            DisplayKind = ResolveTaskDisplayKind(relatedTaskId) ?? definition.DisplayKind,
            AutoActionKind = ResolveTaskAutoActionKind(relatedTaskId) ?? definition.AutoActionKind,
            EntryPointKind = definition.EntryPointKind,
            BackupId = backupId,
            ConflictScope = conflictScope,
            RelatedTaskId = relatedTaskId,
            LogRefs = logRefs,
        };
    }

    private DiagnosticReport BuildDiagnosticReport(
        string phase,
        string status,
        IReadOnlyList<DiagnosticFinding> findings,
        string snapshotId,
        bool targetStateMutated,
        bool rollbackAvailable,
        bool rollbackRecommended,
        string? backupId = null)
    {
        string nextAction = findings.Count == 0
            ? ResolveSuccessNextAction(targetStateMutated)
            : _repositoryContext.ErrorCodes.TryGetValue(findings[0].Code, out ErrorCodeDefinition? code)
                ? code.RecommendedNextAction
                : "请根据当前诊断结果继续处理。";
        string displayKind = findings.Count == 0
            ? FeedbackDisplayKinds.None
            : findings[0].DisplayKind;
        List<ActionEntryPoint> entryPoints = BuildEntryPoints(findings, rollbackAvailable, rollbackRecommended);

        return new DiagnosticReport
        {
            Platform = "windows",
            Phase = phase,
            Status = status,
            Findings = findings,
            NextAction = nextAction,
            SnapshotId = snapshotId,
            BackupId = backupId ?? findings.Select(item => item.BackupId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            TargetStateMutated = targetStateMutated,
            RollbackAvailable = rollbackAvailable,
            RollbackRecommended = rollbackRecommended,
            DisplayKind = displayKind,
            EntryPoints = entryPoints,
        };
    }

    private CommandExecutionResult CreateCommandResult(DiagnosticReport report, string outputFormat)
    {
        string json = JsonSerializer.Serialize(report, JsonOptions);
        string text = BuildTextSummary(report);
        return new CommandExecutionResult
        {
            ExitCode = report.Status == WorkflowStatuses.Completed ? 0 : 1,
            TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? json : text,
            JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? report : null,
        };
    }

    private static string BuildTextSummary(DiagnosticReport report)
    {
        static string YesNo(bool value) => value ? "是" : "否";
        static string PresentOrNone(string? value) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? "无" : value;

        List<string> lines =
        [
            $"当前进度: {PhaseLabel(report.Phase)}",
            $"当前结果: {StatusLabel(report.Status)}",
            $"提示类型: {DisplayKindToLabel(report.DisplayKind)}",
            $"本次操作编号: {PresentOrNone(report.SnapshotId)}",
            $"可恢复备份: {PresentOrNone(report.BackupId)}",
            $"是否已经写入输入法目录: {YesNo(report.TargetStateMutated)}",
            $"是否可以恢复到之前状态: {YesNo(report.RollbackAvailable)}",
            $"当前是否建议恢复: {YesNo(report.RollbackRecommended)}",
            $"日志位置: logs\\{report.Phase}",
            "详细记录位置: workspace\\windows\\state\\last_diagnostic.json",
        ];

        if (report.TargetStateMutated)
        {
            lines.Add("运行态提醒: 刚完成写入或重新部署后，目标窗口可能还需要几秒刷新；是否真正生效必须以实际输入结果为准。");
        }

        foreach (DiagnosticFinding finding in report.Findings)
        {
                lines.Add($"- {finding.Summary}");
                lines.Add($"  说明: {finding.Detail}");
                lines.Add($"  程序准备执行的动作: {ActionKindLabel(finding.AutoActionKind)}");
                lines.Add($"  可直接打开的入口: {EntryPointLabel(finding.EntryPointKind)}");
                if (!string.IsNullOrWhiteSpace(finding.ConflictScope))
                {
                    lines.Add($"  出现分歧的位置: {finding.ConflictScope}");
            }
        }

        if (report.EntryPoints.Count > 0)
        {
            lines.Add("你现在可以直接使用的入口:");
            foreach (ActionEntryPoint entryPoint in report.EntryPoints)
            {
                lines.Add($"- {EntryPointLabel(entryPoint.Kind)}: {entryPoint.Label}{(string.IsNullOrWhiteSpace(entryPoint.Target) ? string.Empty : $" ({entryPoint.Target})")}");
            }
        }

        lines.Add($"建议下一步: {report.NextAction}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveSuccessNextAction(bool targetStateMutated)
    {
        return targetStateMutated
            ? "已写入并触发重新部署。请等待几秒后在目标窗口实际输入，确认当前设置是否已经生效。"
            : "当前检查已完成。";
    }

    private List<ActionEntryPoint> BuildEntryPoints(
        IReadOnlyList<DiagnosticFinding> findings,
        bool rollbackAvailable,
        bool rollbackRecommended)
    {
        HashSet<string> seenKinds = new(StringComparer.Ordinal);
        List<ActionEntryPoint> entryPoints = [];

        foreach (DiagnosticFinding finding in findings)
        {
            if (!string.IsNullOrWhiteSpace(finding.RelatedTaskId) &&
                _repositoryContext.WindowsTasks.TryGetValue(finding.RelatedTaskId, out WorkflowTaskDefinition? taskDefinition))
            {
                foreach (string taskEntryPoint in taskDefinition.EntryPoints)
                {
                    AddEntryPoint(entryPoints, seenKinds, taskEntryPoint);
                }
            }
            else
            {
                AddEntryPoint(entryPoints, seenKinds, finding.EntryPointKind);
            }
        }

        if (rollbackAvailable || rollbackRecommended)
        {
            AddEntryPoint(entryPoints, seenKinds, EntryPointKinds.Rollback);
        }

        return entryPoints;
    }

    private void AddEntryPoint(
        List<ActionEntryPoint> entryPoints,
        HashSet<string> seenKinds,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.Equals(kind, EntryPointKinds.None, StringComparison.Ordinal) || !seenKinds.Add(kind))
        {
            return;
        }

        entryPoints.Add(new ActionEntryPoint
        {
            Kind = kind,
            Label = EntryPointLabel(kind),
            Target = ResolveEntryPointTarget(kind),
        });
    }

    private string? ResolveEntryPointTarget(string kind)
    {
        return kind switch
        {
            EntryPointKinds.InstallUrl => ResolveGitHubReleaseAssetUrl(prerelease: false),
            EntryPointKinds.InstallerLaunch => Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH"),
            EntryPointKinds.UninstallLaunch => WindowsEnvironmentService.Detect(LoadPreferredConfigModel(null, allowDefault: true)).UninstallerPath,
            EntryPointKinds.DirectoryOpen => RepositoryContext.ExpandPath(
                LoadPreferredConfigModel(null, allowDefault: true).SyncSettings.WindowsTargetRoot),
            EntryPointKinds.LogsOpen => _repositoryContext.LogsRoot,
            _ => null,
        };
    }

    private static string EntryPointLabel(string kind)
    {
        return kind switch
        {
            EntryPointKinds.InstallUrl => "打开安装入口",
            EntryPointKinds.InstallerLaunch => "启动安装器",
            EntryPointKinds.UninstallLaunch => "启动卸载入口",
            EntryPointKinds.SettingsDeepLink => "打开系统设置",
            EntryPointKinds.InputMethodPicker => "打开输入法选择器",
            EntryPointKinds.DirectoryAuthorization => "补齐目录授权",
            EntryPointKinds.DeployConfirmation => "完成部署确认",
            EntryPointKinds.DirectoryOpen => "打开目录",
            EntryPointKinds.LogsOpen => "打开日志",
            EntryPointKinds.Retry => "重新检测或重试",
            EntryPointKinds.Rollback => "执行回滚",
            _ => kind,
        };
    }

    private static string DisplayKindToLabel(string displayKind)
    {
        return displayKind switch
        {
            FeedbackDisplayKinds.ExplicitWarning => "显式告警",
            FeedbackDisplayKinds.ExplicitPrompt => "显式提示",
            FeedbackDisplayKinds.ExplicitError => "显式报错",
            _ => "无",
        };
    }

    private static string PhaseLabel(string phase)
    {
        return phase switch
        {
            WorkflowPhases.Detect => "检测",
            WorkflowPhases.Configure => "配置",
            WorkflowPhases.Generate => "生成",
            WorkflowPhases.Backup => "备份",
            WorkflowPhases.Apply => "应用",
            WorkflowPhases.Deploy => "部署",
            WorkflowPhases.Recheck => "回检",
            WorkflowPhases.Rollback => "回滚",
            WorkflowPhases.Diagnose => "诊断",
            _ => phase,
        };
    }

    private static string StatusLabel(string status)
    {
        return status switch
        {
            WorkflowStatuses.Ready => "就绪",
            WorkflowStatuses.ManualActionRequired => "等待手动步骤",
            WorkflowStatuses.Blocked => "阻塞",
            WorkflowStatuses.Failed => "失败",
            WorkflowStatuses.Completed => "完成",
            _ => status,
        };
    }

    private static string ActionKindLabel(string kind)
    {
        return kind switch
        {
            AutoActionKinds.None => "无自动动作",
            AutoActionKinds.DetectOnly => "仅检测",
            AutoActionKinds.InstallRequest => "安装请求",
            AutoActionKinds.ReinstallRequest => "重新安装请求",
            AutoActionKinds.RepairCheck => "修复检查",
            AutoActionKinds.OpenSettings => "打开设置",
            AutoActionKinds.OpenPicker => "打开选择器",
            AutoActionKinds.OpenDirectory => "打开目录",
            AutoActionKinds.OpenLogs => "打开日志",
            AutoActionKinds.RetryExecution => "重试执行",
            _ => kind,
        };
    }

    private string? ResolveTaskDisplayKind(string? relatedTaskId)
    {
        return !string.IsNullOrWhiteSpace(relatedTaskId) &&
            _repositoryContext.WindowsTasks.TryGetValue(relatedTaskId, out WorkflowTaskDefinition? taskDefinition)
            ? taskDefinition.MessageKind
            : null;
    }

    private string? ResolveTaskAutoActionKind(string? relatedTaskId)
    {
        return !string.IsNullOrWhiteSpace(relatedTaskId) &&
            _repositoryContext.WindowsTasks.TryGetValue(relatedTaskId, out WorkflowTaskDefinition? taskDefinition)
            ? taskDefinition.AutoActionKind
            : null;
    }

    public CommandExecutionResult RunStartWeaselServer(string outputFormat)
    {
        try
        {
            string? serverExe = FindWeaselServerExe();
            if (serverExe is null)
            {
                return new CommandExecutionResult
                {
                    ExitCode = 1,
                    TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                        ? JsonSerializer.Serialize(new { status = "blocked", reason = "WeaselServer.exe not found" }, JsonOptions)
                        : "未找到 WeaselServer.exe。请先执行 install-weasel 安装承载器。",
                };
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = serverExe,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            string installYaml = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime", "installation.yaml");
            int delay = 200;
            int waited = 0;
            while (waited < 20000)
            {
                Thread.Sleep(delay);
                waited += delay;
                delay = Math.Min(delay * 2, 2000);
                if (File.Exists(installYaml))
                {
                    object payload = new { status = "completed", server_started = true, server_path = serverExe, installation_yaml_exists = true };
            return new CommandExecutionResult
            {
                ExitCode = 0,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                            ? JsonSerializer.Serialize(payload, JsonOptions)
                            : $"WeaselServer 已启动，路径: {serverExe}",
                        JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
                    };
                }
            }

            object timeout = new { status = "completed", server_started = true, server_path = serverExe, installation_yaml_exists = false };
            return new CommandExecutionResult
            {
                ExitCode = 1,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(timeout, JsonOptions)
                    : $"WeaselServer 已启动但 installation.yaml 未生成，路径: {serverExe}",
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? timeout : null,
            };
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult
            {
                ExitCode = 1,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(new { status = "failed", reason = ex.Message }, JsonOptions)
                    : $"启动 WeaselServer 失败：{ex.Message}",
            };
        }
    }

    public CommandExecutionResult RunStopWeaselServer(string outputFormat)
    {
        try
        {
            ConfigModel model = ConfigModel.CreateDefault();
            WindowsEnvironmentState env = WindowsEnvironmentService.Detect(model);
            TryStopWeaselRuntime(env);
            WindowsEnvironmentState afterEnv = WindowsEnvironmentService.Detect(model);
            bool anyProcessAlive = false;
        foreach (string processName in new[] { "WeaselDeployer", "WeaselServer" })
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        anyProcessAlive = true;
                        break;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception)
                {
                }
            }
            bool serverStopped = !anyProcessAlive;
            object payload = new { status = "completed", server_stopped = serverStopped };
            return new CommandExecutionResult
            {
                ExitCode = serverStopped ? 0 : 1,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(payload, JsonOptions)
                    : (serverStopped ? "WeaselServer 已停止。" : "WeaselServer 未完全停止，部分进程可能仍在运行。"),
                JsonPayload = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase) ? payload : null,
            };
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult
            {
                ExitCode = 1,
                TextOutput = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.Serialize(new { status = "failed", reason = ex.Message }, JsonOptions)
                    : $"停止 WeaselServer 失败：{ex.Message}",
            };
        }
    }

    public CommandExecutionResult RunRestartWeaselServer(string outputFormat)
    {
        CommandExecutionResult stopResult = RunStopWeaselServer(outputFormat);
        if (stopResult.ExitCode != 0)
        {
            return stopResult;
        }
        Utilities.ProcessHelper.StopProcessesWithBackoff(
            new[] { "WeaselServer", "WeaselDeployer" },
            timeoutMs: 30000,
            baseDelayMs: 200,
            maxDelayMs: 2000);
        return RunStartWeaselServer(outputFormat);
    }

    private static string? FindWeaselServerExe()
    {
        string weaselRoot = @"C:\Program Files\Rime";
        if (!Directory.Exists(weaselRoot))
            return null;
        var dirs = Directory.GetDirectories(weaselRoot);
        foreach (string dir in dirs.OrderByDescending(d => d))
        {
            string candidate = Path.Combine(dir, "WeaselServer.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
