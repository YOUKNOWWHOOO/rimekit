using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using RimeKit.Windows.Core;

string? debugLogPath = Environment.GetEnvironmentVariable("RIMEKIT_DEBUG_LOG");
if (!string.IsNullOrEmpty(debugLogPath))
{
    Trace.Listeners.Add(new TextWriterTraceListener(debugLogPath));
    Trace.AutoFlush = true;
    Trace.WriteLine($"[CLI] debug log initialized at {DateTime.Now:O}");
}

try
{
CommandLineOptions options = CommandLineOptions.Parse(args);
WindowsWorkflowService workflowService = new(Environment.CurrentDirectory);
TemplateService.EnsureRepositoryRoot(Environment.CurrentDirectory);

CommandExecutionResult result = options.Command switch
{
    "doctor" => workflowService.RunDoctor(options.ConfigPath, options.OutputFormat),
    "activate-weasel-profile" => workflowService.RunActivateWeaselProfile(options.OutputFormat),
    "open-input-method-picker" => workflowService.RunOpenInputMethodPicker(options.OutputFormat),
    "apply" => workflowService.RunApply(options.ConfigPath, options.OutputFormat, options.ForceStopWeasel),
    "install-resource" => string.IsNullOrWhiteSpace(options.ResourceId)
        ? new CommandExecutionResult { ExitCode = 1, TextOutput = "错误：install-resource 需要 --resource-id 参数。" }
        : string.IsNullOrWhiteSpace(options.FromFile)
            ? workflowService.RunInstallFormalResource(options.ResourceId, options.ConfigPath, options.OutputFormat, options.ForceStopWeasel)
            : workflowService.RunInstallFormalResourceFromFile(options.ResourceId, options.FromFile, options.ConfigPath, options.OutputFormat, options.ForceStopWeasel),
    "rollback" => workflowService.RunRollback(options.BackupId, options.OutputFormat, options.ForceStopWeasel),
    "export" => options.ExportKind switch
    {
        null or "diagnostic" or "user-config-toml" or "installed-resources" or "config-model" => options.ExportKind == "user-config-toml"
            ? workflowService.RunExportUserConfigToml(options.OutputPath, options.ConfigPath, options.OutputFormat)
            : workflowService.RunExport(
                options.ExportKind ?? "diagnostic",
                options.OutputPath,
                options.ConfigPath,
                options.BackupId,
                options.OutputFormat),
        _ => new CommandExecutionResult { ExitCode = 1, TextOutput = $"不支持的导出类型：{options.ExportKind}" },
    },
    "print-config" => workflowService.RunPrintConfig(options.ConfigPath, options.OutputFormat),
    "set-config" => workflowService.RunSetConfig(options.ConfigPath, options.Field, options.Value, options.OutputFormat),
    "uninstall-resource" => workflowService.RunUninstallFormalResource(options.ResourceId ?? string.Empty, options.ConfigPath, options.OutputFormat, options.ForceStopWeasel),

    "list-custom-entries" => workflowService.RunListCustomEntries(options.ConfigPath, options.OutputFormat),
    "add-custom-entry" => workflowService.RunAddCustomEntry(options.ConfigPath, options.Text, options.Code, options.Weight, options.OutputFormat),
    "delete-custom-entry" => workflowService.RunDeleteCustomEntry(options.ConfigPath, options.Text, options.Code, options.OutputFormat),
    "install-weasel" => string.IsNullOrWhiteSpace(options.FromFile)
        ? (options.DownloadOnly && !string.IsNullOrWhiteSpace(options.OutputPath)
            ? workflowService.RunDownloadWeaselInstallerToFile(options.OutputPath, options.OutputFormat)
            : workflowService.RunDownloadAndLaunchWeaselInstaller(options.OutputFormat))
        : workflowService.RunLaunchWeaselInstallerFromFile(options.FromFile, options.OutputFormat),
    "uninstall-weasel" => workflowService.RunLaunchWeaselUninstaller(options.OutputFormat),
    "resource-status" => workflowService.RunResourceStatus(options.ConfigPath, options.OutputFormat),
    "apply-custom-entries" => workflowService.RunApplyCustomEntries(options.ConfigPath, options.OutputFormat, options.ForceStopWeasel),
    "reset-config" => workflowService.RunResetConfig(options.ConfigPath, options.OutputFormat),
    "uninstall-all" => workflowService.RunUninstallAll(options.ConfigPath, options.OutputFormat),
    "start-weasel-server" => workflowService.RunStartWeaselServer(options.OutputFormat),
    "stop-weasel-server" => workflowService.RunStopWeaselServer(options.OutputFormat),
    "restart-weasel-server" => workflowService.RunRestartWeaselServer(options.OutputFormat),
    _ => new CommandExecutionResult
    {
        ExitCode = 1,
        TextOutput = JsonSerializer.Serialize(new
        {
            error = "不支持的命令",
            command = options.Command,
            supported_commands = new[]
            {
                // NOTE: This list must be kept in sync with the switch expression above.
                // Add new command names here when adding new switch arms.
                "doctor", "activate-weasel-profile", "open-input-method-picker", "apply",
                "install-resource", "uninstall-resource", "rollback", "export",
                "print-config", "set-config", "resource-status",
                "list-custom-entries", "add-custom-entry", "delete-custom-entry",
                "install-weasel", "uninstall-weasel", "apply-custom-entries", "reset-config",
                "uninstall-all", "start-weasel-server", "stop-weasel-server", "restart-weasel-server",
            },
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
    },
};

Console.WriteLine(result.TextOutput);
Environment.ExitCode = result.ExitCode;
}
catch (Exception exception)
{
    string errorJson = JsonSerializer.Serialize(new
    {
        error = "unhandled_exception",
        message = exception.Message,
        type = exception.GetType().Name,
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    Console.Error.WriteLine(errorJson);
    Environment.ExitCode = 1;
}

internal sealed class CommandLineOptions
{
    public string Command { get; private init; } = "doctor";

    public string OutputFormat { get; private init; } = "text";

    public string? ConfigPath { get; private init; }

    public string? ResourceId { get; private init; }

    public string? BackupId { get; private init; }

    public string? OutputPath { get; private init; }

    public string? ExportKind { get; private init; }

    public string? Field { get; private init; }

    public string? Value { get; private init; }

    public string? SchemaId { get; private init; }

    public string? DictionaryId { get; private init; }

    public string? ModelId { get; private init; }

    public bool Enable { get; private init; } = true;

    public string? Text { get; private init; }

    public string? Code { get; private init; }

    public int Weight { get; private init; } = 1;

    public bool ForceStopWeasel { get; private init; }

    public string? FromFile { get; private init; }

    public bool DownloadOnly { get; private init; }

    public static CommandLineOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandLineOptions();
        }

        Dictionary<string, string> parsedOptions = new(StringComparer.OrdinalIgnoreCase);
        string command = args[0].Trim().ToLowerInvariant();

        for (int index = 1; index < args.Length; index++)
        {
            string current = args[index];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string key = current[2..];
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsedOptions[key] = args[index + 1];
                index++;
            }
        }

        string format = parsedOptions.GetValueOrDefault("format", "text");
        bool enable = !parsedOptions.TryGetValue("disable", out _);
        bool forceStopWeasel = parsedOptions.ContainsKey("force-stop-weasel");
        bool downloadOnly = parsedOptions.ContainsKey("download-only");
        int weight = int.TryParse(parsedOptions.GetValueOrDefault("weight", "1"), out int w) ? w : 1;

        return new CommandLineOptions
        {
            Command = command,
            OutputFormat = format.Equals("json", StringComparison.OrdinalIgnoreCase) ? "json" : "text",
            ConfigPath = parsedOptions.GetValueOrDefault("config"),
            ResourceId = parsedOptions.GetValueOrDefault("resource-id"),
            BackupId = parsedOptions.GetValueOrDefault("backup-id"),
            OutputPath = parsedOptions.GetValueOrDefault("output"),
            ExportKind = parsedOptions.GetValueOrDefault("kind"),
            Field = parsedOptions.GetValueOrDefault("field"),
            Value = parsedOptions.GetValueOrDefault("value"),
            SchemaId = parsedOptions.GetValueOrDefault("schema-id"),
            DictionaryId = parsedOptions.GetValueOrDefault("dictionary-id"),
            ModelId = parsedOptions.GetValueOrDefault("model-id"),
            Enable = enable,
            Text = parsedOptions.GetValueOrDefault("text"),
            Code = parsedOptions.GetValueOrDefault("code"),
            Weight = weight,
            ForceStopWeasel = forceStopWeasel,
            FromFile = parsedOptions.GetValueOrDefault("from-file"),
            DownloadOnly = downloadOnly,
        };
    }
}
