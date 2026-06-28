using System.Text.Json.Serialization;

namespace RimeKit.Windows.Core;

/// <summary>
/// 统一阶段名常量。
/// </summary>
public static class WorkflowPhases
{
    public const string Detect = "detect";
    public const string Configure = "configure";
    public const string Generate = "generate";
    public const string Backup = "backup";
    public const string Apply = "apply";
    public const string Deploy = "deploy";
    public const string Recheck = "recheck";
    public const string Rollback = "rollback";
    public const string Diagnose = "diagnose";
}

/// <summary>
/// 统一结果语义常量。
/// </summary>
public static class WorkflowStatuses
{
    public const string Ready = "ready";
    public const string ManualActionRequired = "manual_action_required";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
    public const string Completed = "completed";
}

/// <summary>
/// 统一错误分级常量。
/// </summary>
public static class WorkflowSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Blocking = "blocking";
    public const string Fatal = "fatal";
}

/// <summary>
/// 统一显式反馈类型常量。
/// </summary>
public static class FeedbackDisplayKinds
{
    public const string ExplicitWarning = "explicit_warning";
    public const string ExplicitError = "explicit_error";
    public const string ExplicitPrompt = "explicit_prompt";
    public const string None = "none";
}

/// <summary>
/// 统一自动动作类型常量。
/// </summary>
public static class AutoActionKinds
{
    public const string None = "none";
    public const string DetectOnly = "detect_only";
    public const string InstallRequest = "install_request";
    public const string ReinstallRequest = "reinstall_request";
    public const string RepairCheck = "repair_check";
    public const string OpenSettings = "open_settings";
    public const string OpenPicker = "open_picker";
    public const string OpenDirectory = "open_directory";
    public const string OpenLogs = "open_logs";
    public const string RetryExecution = "retry_execution";
}

/// <summary>
/// 统一入口类型常量。
/// </summary>
public static class EntryPointKinds
{
    public const string None = "none";
    public const string InstallUrl = "install_url";
    public const string InstallerLaunch = "installer_launch";
    public const string UninstallLaunch = "uninstall_launch";
    public const string SettingsDeepLink = "settings_deep_link";
    public const string InputMethodPicker = "input_method_picker";
    public const string DirectoryAuthorization = "directory_authorization";
    public const string DeployConfirmation = "deploy_confirmation";
    public const string DirectoryOpen = "directory_open";
    public const string LogsOpen = "logs_open";
    public const string Retry = "retry";
    public const string Rollback = "rollback";
}

/// <summary>
/// 统一冲突范围常量。
/// </summary>
public static class ConflictScopes
{
    public const string ConfigModel = "config_model";
    public const string FormalResource = "formal_resource";
    public const string TargetConfig = "target_config";
    public const string RuntimeState = "runtime_state";
}

/// <summary>
/// 正式诊断项。
/// </summary>
public sealed class DiagnosticFinding
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = WorkflowSeverities.Info;

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;

    [JsonPropertyName("display_kind")]
    public string DisplayKind { get; init; } = FeedbackDisplayKinds.ExplicitError;

    [JsonPropertyName("auto_action_kind")]
    public string AutoActionKind { get; init; } = AutoActionKinds.None;

    [JsonPropertyName("entry_point_kind")]
    public string EntryPointKind { get; init; } = EntryPointKinds.None;

    [JsonPropertyName("backup_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackupId { get; init; }

    [JsonPropertyName("conflict_scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConflictScope { get; init; }

    [JsonPropertyName("related_task_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RelatedTaskId { get; init; }

    [JsonPropertyName("log_refs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? LogRefs { get; init; }
}

/// <summary>
/// 结构化动作入口。
/// </summary>
public sealed class ActionEntryPoint
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = EntryPointKinds.None;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }
}

/// <summary>
/// 正式诊断报告。
/// </summary>
public sealed class DiagnosticReport
{
    [JsonPropertyName("platform")]
    public string Platform { get; init; } = "windows";

    [JsonPropertyName("phase")]
    public string Phase { get; init; } = WorkflowPhases.Diagnose;

    [JsonPropertyName("status")]
    public string Status { get; init; } = WorkflowStatuses.Completed;

    [JsonPropertyName("findings")]
    public IReadOnlyList<DiagnosticFinding> Findings { get; init; } = [];

    [JsonPropertyName("next_action")]
    public string NextAction { get; init; } = string.Empty;

    [JsonPropertyName("snapshot_id")]
    public string SnapshotId { get; init; } = "none";

    [JsonPropertyName("backup_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackupId { get; init; }

    [JsonPropertyName("target_state_mutated")]
    public bool TargetStateMutated { get; init; }

    [JsonPropertyName("rollback_available")]
    public bool RollbackAvailable { get; init; }

    [JsonPropertyName("rollback_recommended")]
    public bool RollbackRecommended { get; init; }

    [JsonPropertyName("display_kind")]
    public string DisplayKind { get; init; } = FeedbackDisplayKinds.None;

    [JsonPropertyName("entry_points")]
    public IReadOnlyList<ActionEntryPoint> EntryPoints { get; init; } = [];
}

/// <summary>
/// CLI 命令执行结果。
/// </summary>
public sealed class CommandExecutionResult
{
    public int ExitCode { get; init; }

    public string TextOutput { get; init; } = string.Empty;

    public object? JsonPayload { get; init; }
}

/// <summary>
/// 错误码元信息。
/// </summary>
public sealed class ErrorCodeDefinition
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("platform_scope")]
    public string PlatformScope { get; init; } = string.Empty;

    [JsonPropertyName("phase_scope")]
    public string PhaseScope { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = WorkflowSeverities.Info;

    [JsonPropertyName("default_summary")]
    public string DefaultSummary { get; init; } = string.Empty;

    [JsonPropertyName("recommended_next_action")]
    public string RecommendedNextAction { get; init; } = string.Empty;

    [JsonPropertyName("stable_contract_since")]
    public string StableContractSince { get; init; } = string.Empty;

    [JsonPropertyName("display_kind")]
    public string DisplayKind { get; init; } = FeedbackDisplayKinds.ExplicitError;

    [JsonPropertyName("auto_action_kind")]
    public string AutoActionKind { get; init; } = AutoActionKinds.None;

    [JsonPropertyName("entry_point_kind")]
    public string EntryPointKind { get; init; } = EntryPointKinds.None;
}

/// <summary>
/// 错误码清单根对象。
/// </summary>
public sealed class ErrorCodeManifest
{
    [JsonPropertyName("codes")]
    public IReadOnlyList<ErrorCodeDefinition> Codes { get; init; } = [];
}

/// <summary>
/// 正式任务定义。
/// </summary>
public sealed class WorkflowTaskDefinition
{
    [JsonPropertyName("task_id")]
    public string TaskId { get; init; } = string.Empty;

    [JsonPropertyName("phase")]
    public string Phase { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("message_kind")]
    public string MessageKind { get; init; } = FeedbackDisplayKinds.ExplicitError;

    [JsonPropertyName("auto_action_kind")]
    public string AutoActionKind { get; init; } = AutoActionKinds.None;

    [JsonPropertyName("entry_points")]
    public IReadOnlyList<string> EntryPoints { get; init; } = [];
}

/// <summary>
/// 正式任务清单根对象。
/// </summary>
public sealed class WorkflowTaskManifest
{
    [JsonPropertyName("tasks")]
    public IReadOnlyList<WorkflowTaskDefinition> Tasks { get; init; } = [];
}

/// <summary>
/// Windows 运行环境检测结果。
/// </summary>
public sealed class WindowsEnvironmentState
{
    public string WindowsTargetRoot { get; init; } = string.Empty;

    public string? DeployerPath { get; init; }

    public string? UninstallerPath { get; init; }

    public string? UninstallerArguments { get; init; }

    public string? WeaselVersion { get; init; }

    public string? WeaselUpdateSource { get; init; }

    public string? DefaultInputMethodTip { get; init; }

    public bool TargetRootAccessible { get; init; }

    public string? ForegroundProcessName { get; init; }

    public string? ForegroundKeyboardLayout { get; init; }

    public bool? ForegroundInputContextOpen { get; init; }

    public string? ForegroundConversionStatus { get; init; }

    public bool WeaselAvailable => !string.IsNullOrWhiteSpace(DeployerPath);

    public bool DefaultInputMethodIsWeasel =>
        string.Equals(
            DefaultInputMethodTip,
            "0804:{A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A}{3D02CAB6-2B8E-4781-BA20-1C9267529467}",
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Windows 正式运行控制项。
/// </summary>
public sealed class WindowsRuntimeControls
{
    public bool AutoRecheckOnReturn { get; init; } = true;

    public bool AutoCheckFormalResourcesOnReturn { get; init; } = true;

    public bool CleanupInstallerArtifactsOnSuccess { get; init; } = true;

    public bool AutoOpenLogsAfterRepairFailure { get; init; } = true;

    public bool PreferSilentWeaselInstall { get; init; } = true;

    public bool PreferSilentWeaselUninstall { get; init; } = true;

    public string WeaselVersionStrategy { get; init; } = "latest";

    public string WeaselPinnedInstallerUrl { get; init; } = string.Empty;

    public string FormalResourceVersionStrategy { get; init; } = "latest";

    public string FormalResourcePinnedRef { get; init; } = string.Empty;
}

public sealed class FormalResourceDescriptor
{
    public string ResourceId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ResourceKind { get; init; } = string.Empty;
}

public sealed class InputMethodPickerResult
{
    public string Status { get; init; } = WorkflowStatuses.ManualActionRequired;

    public string Detail { get; init; } = string.Empty;

    public bool WasLaunched { get; init; }

    public string LaunchMethod { get; init; } = string.Empty;

    public string EvidenceKind { get; init; } = string.Empty;

    public int DurationMs { get; init; }

    public bool RequiresManualConfirmation { get; init; }
}

public interface IInputMethodPickerLauncher
{
    InputMethodPickerResult Launch();
}
