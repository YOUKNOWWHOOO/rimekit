using System.Text.Encodings.Web;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimeKit.Windows.Core;

/// <summary>
/// 提供仓库路径、共享契约和本地状态文件访问能力。
/// Thread safety: Not thread-safe. Designed for single-threaded CLI/GUI usage.
/// GetAwaiter().GetResult() calls in ResumableDownloader and WindowsWorkflowService
/// are safe because they execute on non-UI threads
/// (CLI or Task.Run workers). Never call these from a UI synchronization context.
/// </summary>
internal sealed class RepositoryContext
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public RepositoryContext(string startDirectory)
    {
        RepositoryRoot = DiscoverRepositoryRoot(startDirectory);
        SharedRoot = Path.Combine(RepositoryRoot, "shared");
        WorkspaceRoot = Path.Combine(RepositoryRoot, "workspace", "windows");
        SnapshotsRoot = Path.Combine(RepositoryRoot, "snapshots");
        BackupsRoot = Path.Combine(RepositoryRoot, "backups");
        LogsRoot = Path.Combine(RepositoryRoot, "logs");
        ExportsRoot = Path.Combine(RepositoryRoot, "exports");
        StateRoot = Path.Combine(WorkspaceRoot, "state");
        DownloadsRoot = Path.Combine(WorkspaceRoot, "downloads");
        ResourcesRoot = Path.Combine(WorkspaceRoot, "resources");
        CurrentConfigModelPath = ResolveCurrentConfigModelPath();
        InstalledResourcesStatePath = ResolveInstalledResourcesStatePath();

        Directory.CreateDirectory(WorkspaceRoot);
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(ExportsRoot);
        Directory.CreateDirectory(StateRoot);
        Directory.CreateDirectory(DownloadsRoot);
        Directory.CreateDirectory(ResourcesRoot);

        ErrorCodes = LoadErrorCodes();
        WindowsTasks = LoadWindowsTasks();
        (SchemaIds, DictionaryIds, ModelIds, FuzzyPresetIds, SymbolProfileIds, PreeditProfileIds) = LoadResourceMetadata();
    }

    public string RepositoryRoot { get; }

    public string SharedRoot { get; }

    public string WorkspaceRoot { get; }

    public string SnapshotsRoot { get; }

    public string BackupsRoot { get; }

    public string LogsRoot { get; }

    public string ExportsRoot { get; }

    public string StateRoot { get; }

    public string DownloadsRoot { get; }

    public string ResourcesRoot { get; }

    public string CurrentConfigModelPath { get; }

    public string InstalledResourcesStatePath { get; }

    public IReadOnlyDictionary<string, ErrorCodeDefinition> ErrorCodes { get; }

    public IReadOnlyDictionary<string, WorkflowTaskDefinition> WindowsTasks { get; }

    public IReadOnlySet<string> SchemaIds { get; }

    public IReadOnlySet<string> DictionaryIds { get; }

    public IReadOnlySet<string> ModelIds { get; }

    public IReadOnlySet<string> FuzzyPresetIds { get; }

    public IReadOnlySet<string> SymbolProfileIds { get; }

    public IReadOnlySet<string> PreeditProfileIds { get; }

    public static string ExpandPath(string rawPath)
    {
        return Environment.ExpandEnvironmentVariables(rawPath);
    }

    public static string CreateOperationId(string suffix)
    {
        return $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{suffix}";
    }

    public static string ComputeSha256(string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSha256(byte[] content)
    {
        byte[] hash = SHA256.HashData(content);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ReadUtf8(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            throw new IOException($"无法读取 UTF-8 文件：{path}", exception);
        }
    }

    public static byte[] ReadBytes(string path)
    {
        return File.ReadAllBytes(path);
    }

    public static void WriteUtf8(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string normalized = content.ReplaceLineEndings("\r\n");
        UTF8Encoding encoding = new(encoderShouldEmitUTF8Identifier: false);
        string tempPath = path + ".tmp";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.WriteAllText(tempPath, normalized, encoding);
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4)
                {
                    throw;
                }

                Thread.Sleep(Math.Min(150 * (1 << attempt), 2000));
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }

    public static void WriteBytes(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tempPath = path + ".tmp";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.WriteAllBytes(tempPath, content);
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4)
                {
                    throw;
                }

                Thread.Sleep(Math.Min(150 * (1 << attempt), 2000));
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }

    public void PersistStateReference(string fileName, string value)
    {
        WriteUtf8(Path.Combine(StateRoot, fileName), value);
    }

    public string? ResolveStateReference(string fileName)
    {
        string path = Path.Combine(StateRoot, fileName);
        return File.Exists(path) ? ReadUtf8(path).Trim() : null;
    }

    public void ClearStateReference(string fileName)
    {
        string path = Path.Combine(StateRoot, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void PersistLastDiagnostic(DiagnosticReport report)
    {
        string payload = JsonSerializer.Serialize(report, JsonOptions);
        WriteUtf8(Path.Combine(StateRoot, "last_diagnostic.json"), payload);
        WriteDiagnosticTrail(report);
    }

    public void PersistCurrentConfigModel(ConfigModel model)
    {
        WriteUtf8(
            CurrentConfigModelPath,
            JsonSerializer.Serialize(model, JsonOptions));
    }

    private string ResolveCurrentConfigModelPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("RIMEKIT_CURRENT_CONFIG_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overridePath));
        }

        return Path.Combine(StateRoot, "current_config_model.json");
    }

    private string ResolveInstalledResourcesStatePath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("RIMEKIT_INSTALLED_RESOURCES_STATE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overridePath));
        }

        return Path.Combine(StateRoot, "installed_resources.json");
    }

    public void PersistRuntimePathCache(WindowsEnvironmentState environment)
    {
        string payload = JsonSerializer.Serialize(new
        {
            windows_target_root = environment.WindowsTargetRoot,
            deployer_path = environment.DeployerPath,
            uninstaller_path = environment.UninstallerPath,
            uninstaller_arguments = environment.UninstallerArguments,
            weasel_version = environment.WeaselVersion,
            weasel_update_source = environment.WeaselUpdateSource,
            default_input_method_tip = environment.DefaultInputMethodTip,
            foreground_process_name = environment.ForegroundProcessName,
            foreground_keyboard_layout = environment.ForegroundKeyboardLayout,
            foreground_input_context_open = environment.ForegroundInputContextOpen,
            foreground_conversion_status = environment.ForegroundConversionStatus,
            target_root_accessible = environment.TargetRootAccessible,
            detected_at = DateTimeOffset.UtcNow,
        }, JsonOptions);
        WriteUtf8(Path.Combine(StateRoot, "runtime_paths.json"), payload);
    }

    public void PersistRecheckSummary(
        string snapshotId,
        string status,
        IReadOnlyList<DiagnosticFinding> findings)
    {
        string payload = JsonSerializer.Serialize(new
        {
            snapshot_id = snapshotId,
            status,
            findings,
            recorded_at = DateTimeOffset.UtcNow,
        }, JsonOptions);
        WriteUtf8(Path.Combine(StateRoot, "last_recheck_summary.json"), payload);
    }

    public void PersistBackupStatus(object payload)
    {
        WriteUtf8(
            Path.Combine(StateRoot, "latest_backup_status.json"),
            JsonSerializer.Serialize(payload, JsonOptions));
    }

    public void PersistConflictRecoveryDecision(string decision)
    {
        WriteUtf8(Path.Combine(StateRoot, "last_conflict_recovery_decision.txt"), decision);
    }

    public void PersistPendingWeaselUninstallTargets(IReadOnlyList<string> targets)
    {
        string payload = JsonSerializer.Serialize(targets, JsonOptions);
        WriteUtf8(Path.Combine(StateRoot, "pending_weasel_uninstall_targets.json"), payload);
    }

    public IReadOnlyList<string> ResolvePendingWeaselUninstallTargets()
    {
        string path = Path.Combine(StateRoot, "pending_weasel_uninstall_targets.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            IReadOnlyList<string>? deserialized = JsonSerializer.Deserialize<IReadOnlyList<string>>(ReadUtf8(path), JsonOptions);
            if (deserialized is null)
            {
                System.Diagnostics.Debug.WriteLine($"[Repository] pending uninstall targets 反序列化为 null, 返回空列表");
                return [];
            }
            return deserialized;
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Repository] pending uninstall targets JSON 解析失败: {ex.Message}");
            try
            {
                string backupPath = path + ".corrupted";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(path, backupPath);
                System.Diagnostics.Debug.WriteLine($"[Repository] 已保留损坏文件副本: {backupPath}");
            }
            catch (Exception backupEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Repository] 保留损坏文件副本失败: {backupEx.Message}");
            }
            return [];
        }
    }

    public void ClearPendingWeaselUninstallTargets()
    {
        string path = Path.Combine(StateRoot, "pending_weasel_uninstall_targets.json");
        Utilities.FileHelper.DeleteFileWithBackoff(
            path,
            maxRetries: 5,
            baseDelayMs: 100,
            maxDelayMs: 2000);
    }

    public WindowsRuntimeControls LoadWindowsRuntimeControls()
    {
        string path = Path.Combine(StateRoot, "windows_runtime_controls.json");
        if (!File.Exists(path))
        {
            WindowsRuntimeControls defaults = new();
            SaveWindowsRuntimeControls(defaults);
            return defaults;
        }

        WindowsRuntimeControls? deserialized = JsonSerializer.Deserialize<WindowsRuntimeControls>(ReadUtf8(path), JsonOptions);
        if (deserialized is null)
        {
            System.Diagnostics.Debug.WriteLine("[Repository] windows_runtime_controls 反序列化为 null, 使用默认值");
            return new WindowsRuntimeControls();
        }
        return deserialized;
    }

    public void SaveWindowsRuntimeControls(WindowsRuntimeControls controls)
    {
        WriteUtf8(
            Path.Combine(StateRoot, "windows_runtime_controls.json"),
            JsonSerializer.Serialize(controls, JsonOptions));
    }

    public void WritePhaseLog(
        string phase,
        string status,
        string summary,
        string nextAction,
        string snapshotId,
        string? backupId,
        IReadOnlyList<DiagnosticFinding> findings)
    {
        string displayKind = findings.Count == 0
            ? FeedbackDisplayKinds.None
            : findings[0].DisplayKind;
        IReadOnlyList<ActionEntryPoint> entryPoints = BuildEntryPointsFromFindings(findings);
        WritePhaseLogPayload(
            phase,
            status,
            summary,
            nextAction,
            snapshotId,
            backupId,
            findings,
            displayKind,
            entryPoints,
            rollbackAvailable: false,
            rollbackRecommended: false,
            targetStateMutated: false);
    }

    private void WriteDiagnosticTrail(DiagnosticReport report)
    {
        WritePhaseLogPayload(
            report.Phase,
            report.Status,
            report.NextAction,
            report.NextAction,
            report.SnapshotId,
            report.BackupId,
            report.Findings,
            report.DisplayKind,
            report.EntryPoints,
            report.RollbackAvailable,
            report.RollbackRecommended,
            report.TargetStateMutated);
    }

    private void WritePhaseLogPayload(
        string phase,
        string status,
        string summary,
        string nextAction,
        string snapshotId,
        string? backupId,
        IReadOnlyList<DiagnosticFinding> findings,
        string displayKind,
        IReadOnlyList<ActionEntryPoint> entryPoints,
        bool rollbackAvailable,
        bool rollbackRecommended,
        bool targetStateMutated)
    {
        string phaseDirectory = Path.Combine(LogsRoot, phase);
        Directory.CreateDirectory(phaseDirectory);
        string logPath = Path.Combine(
            phaseDirectory,
            $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-windows-{phase}-{snapshotId}.log");

        string payload = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            platform = "windows",
            phase,
            status,
            summary,
            snapshot_id = snapshotId,
            backup_id = backupId,
            message_kind = displayKind,
            next_action = nextAction,
            target_state_mutated = targetStateMutated,
            rollback_available = rollbackAvailable,
            rollback_recommended = rollbackRecommended,
            entry_points = entryPoints,
            findings,
        }, JsonOptions);
        WriteUtf8(logPath, payload);
    }

    private static IReadOnlyList<ActionEntryPoint> BuildEntryPointsFromFindings(IReadOnlyList<DiagnosticFinding> findings)
    {
        return findings
            .Select(item => item.EntryPointKind)
            .Where(kind => !string.IsNullOrWhiteSpace(kind) && !string.Equals(kind, EntryPointKinds.None, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(kind => new ActionEntryPoint
            {
                Kind = kind,
                Label = kind,
            })
            .ToArray();
    }

    public string SharedSpecPath(string fileName)
    {
        return Path.Combine(SharedRoot, "spec", fileName);
    }

    public string SharedTemplatePath(string fileName)
    {
        return Path.Combine(SharedRoot, "templates", fileName);
    }

    internal static string DiscoverRepositoryRoot(string startDirectory)
    {
        string? explicitRoot = Environment.GetEnvironmentVariable("RIMEKIT_SOURCE_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            string fullExplicitRoot = Path.GetFullPath(explicitRoot);
            if (File.Exists(Path.Combine(fullExplicitRoot, "shared", "spec", "config_model.schema.json")))
            {
                return fullExplicitRoot;
            }
        }

        DirectoryInfo? current = new(startDirectory);
        while (current is not null)
        {
            string marker = Path.Combine(current.FullName, "shared", "spec", "config_model.schema.json");
            if (File.Exists(marker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到共享契约层，无法定位仓库根目录。");
    }

    private IReadOnlyDictionary<string, ErrorCodeDefinition> LoadErrorCodes()
    {
        string path = SharedSpecPath("error_codes.json");
        ErrorCodeManifest manifest = JsonSerializer.Deserialize<ErrorCodeManifest>(ReadUtf8(path), JsonOptions)
            ?? throw new InvalidOperationException("无法读取错误码清单。");
        return manifest.Codes.ToDictionary(item => item.Code, item => item, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyDictionary<string, WorkflowTaskDefinition> LoadWindowsTasks()
    {
        string path = SharedSpecPath("windows_tasks.json");
        WorkflowTaskManifest manifest = JsonSerializer.Deserialize<WorkflowTaskManifest>(ReadUtf8(path), JsonOptions)
            ?? throw new InvalidOperationException("无法读取 Windows 正式任务清单。");
        return manifest.Tasks.ToDictionary(item => item.TaskId, item => item, StringComparer.OrdinalIgnoreCase);
    }

    private (
        IReadOnlySet<string> schemaIds,
        IReadOnlySet<string> dictionaryIds,
        IReadOnlySet<string> modelIds,
        IReadOnlySet<string> fuzzyPresetIds,
        IReadOnlySet<string> symbolProfileIds,
        IReadOnlySet<string> preeditProfileIds) LoadResourceMetadata()
    {
        string resourcePath = SharedSpecPath("resource_manifest.json");
        using JsonDocument document = JsonDocument.Parse(ReadUtf8(resourcePath));
        HashSet<string> schemaIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> dictionaryIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> modelIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> fuzzyPresetIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> symbolProfileIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> preeditProfileIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement schema in document.RootElement.GetProperty("schemas").EnumerateArray())
        {
            schemaIds.Add(schema.GetProperty("schema_id").GetString() ?? string.Empty);
        }

        foreach (JsonElement dictionary in document.RootElement.GetProperty("dictionaries").EnumerateArray())
        {
            dictionaryIds.Add(dictionary.GetProperty("dictionary_id").GetString() ?? string.Empty);
        }

        foreach (JsonElement model in document.RootElement.GetProperty("models").EnumerateArray())
        {
            modelIds.Add(model.GetProperty("model_id").GetString() ?? string.Empty);
        }

        JsonElement presets = document.RootElement.GetProperty("feature_presets");
        foreach (JsonElement preset in presets.GetProperty("fuzzy_pinyin_presets").EnumerateArray())
        {
            fuzzyPresetIds.Add(preset.GetProperty("preset_id").GetString() ?? string.Empty);
        }
        foreach (JsonElement preset in presets.GetProperty("symbol_profiles").EnumerateArray())
        {
            symbolProfileIds.Add(preset.GetProperty("preset_id").GetString() ?? string.Empty);
        }
        foreach (JsonElement preset in presets.GetProperty("preedit_profiles").EnumerateArray())
        {
            preeditProfileIds.Add(preset.GetProperty("preset_id").GetString() ?? string.Empty);
        }

        return (schemaIds, dictionaryIds, modelIds, fuzzyPresetIds, symbolProfileIds, preeditProfileIds);
    }
}
