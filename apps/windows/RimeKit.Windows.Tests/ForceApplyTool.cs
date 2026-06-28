using System.Diagnostics;
using System.Text;
using RimeKit.Windows.Core;
using RimeKit.Windows.Core.Utilities;

namespace RimeKit.Windows.Tests;

internal static class ForceApplyTool
{
    public static int Run(string[] args)
    {
        string? resourceId = null;
        string? configPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--resource-id" && i + 1 < args.Length)
            {
                resourceId = args[++i];
            }
            else if (args[i] == "--config" && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            Console.Error.WriteLine("Usage: force-apply --resource-id <id> [--config <path>]");
            return 1;
        }

        string sourceRoot = ResolveSourceRepositoryRoot();
        Environment.SetEnvironmentVariable("RIMEKIT_SOURCE_REPOSITORY_ROOT", sourceRoot);
        Environment.SetEnvironmentVariable(
            "RIMEKIT_WEASEL_ACTIVATOR_PATH",
            Path.Combine(
                sourceRoot,
                "apps",
                "windows",
                "RimeKit.Windows.Activator",
                "bin",
                "Debug",
                "net10.0-windows",
                "RimeKit.Windows.Activator.exe"));

        RepositoryContext repo = new(Environment.CurrentDirectory);
        ConfigModelService configService = new(repo);
        ResourceUpdateService updateService = new(repo);
        ArtifactService artifactService = new(repo);

        bool isSchema = repo.SchemaIds.Contains(resourceId);
        bool isDictionary = repo.DictionaryIds.Contains(resourceId);
        bool isModel = repo.ModelIds.Contains(resourceId);

        if (!isSchema && !isDictionary && !isModel)
        {
            Console.Error.WriteLine($"ERROR: resource not in formal list: {resourceId}");
            return 1;
        }

        Console.WriteLine($"STEP: InstallOrUpdateResource({resourceId})...");
        string report = updateService.InstallOrUpdateResource(resourceId);
        Console.WriteLine(report);

        FixSogouPinyinSeparator(resourceId);

        string effectiveConfigPath = !string.IsNullOrWhiteSpace(configPath)
            ? Path.GetFullPath(configPath)
            : repo.CurrentConfigModelPath;
        ConfigModel currentModel = configService.Load(effectiveConfigPath, allowDefault: true);

        Console.WriteLine($"STEP: Enabling {resourceId} in config...");
        ConfigModel updatedModel = EnableResource(currentModel, resourceId, isSchema, isDictionary, isModel);

        Console.WriteLine($"STEP: Saving config to {effectiveConfigPath}...");
        configService.Save(effectiveConfigPath, updatedModel);
        repo.PersistCurrentConfigModel(updatedModel);

        Console.WriteLine("STEP: Detecting environment...");
        WindowsEnvironmentState env = WindowsEnvironmentService.Detect(updatedModel);
        repo.PersistRuntimePathCache(env);
        Console.WriteLine($"  WeaselAvailable={env.WeaselAvailable}, TargetRoot={env.WindowsTargetRoot}");

        Console.WriteLine("STEP: Generating artifacts...");
        string snapshotId = RepositoryContext.CreateOperationId("windows");
        GeneratedArtifacts artifacts = artifactService.Generate(updatedModel, snapshotId);
        Console.WriteLine($"  {artifacts.WindowsOutputFiles.Count} text + {artifacts.WindowsBinaryOutputFiles.Count} binary + {artifacts.UserDictionaryFiles.Count} user dict files");

        Console.WriteLine("STEP: Stopping WeaselServer...");
        bool stopped = ProcessHelper.StopProcessesWithBackoff(
            new[] { "WeaselServer" },
            timeoutMs: 15000,
            baseDelayMs: 200,
            maxDelayMs: 2000);

        Console.WriteLine($"  stopped={stopped}");

        Console.WriteLine("STEP: Applying to target root...");
        _ = artifactService.ApplyWindowsTargets(
            artifacts.WindowsOutputFiles,
            artifacts.WindowsBinaryOutputFiles,
            artifacts.UserDictionaryFiles,
            env.WindowsTargetRoot);

        Console.WriteLine($"  Applied {artifacts.WindowsOutputFiles.Count + artifacts.WindowsBinaryOutputFiles.Count} files to {env.WindowsTargetRoot}");
        Console.WriteLine("FORCE-APPLY COMPLETED (deploy left to caller)");
        return 0;
    }

    private static ConfigModel EnableResource(
        ConfigModel model,
        string resourceId,
        bool isSchema,
        bool isDictionary,
        bool isModel)
    {
        if (isSchema)
        {
            List<string> enabledSchemaIds = model.ProfileSettings.EnabledSchemaIds
                .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            enabledSchemaIds.Add(resourceId);
            string windowsDefaultSchemaId = string.IsNullOrWhiteSpace(model.ProfileSettings.WindowsDefaultSchemaId)
                ? resourceId
                : model.ProfileSettings.WindowsDefaultSchemaId;

            return new ConfigModel
            {
                ConfigVersion = model.ConfigVersion,
                ProfileSettings = new ProfileSettings
                {
                    EnabledSchemaIds = enabledSchemaIds,
                    WindowsDefaultSchemaId = windowsDefaultSchemaId,
                    AndroidDefaultSchemaId = model.ProfileSettings.AndroidDefaultSchemaId,
                },
                PersonalizationSettings = model.PersonalizationSettings,
                DictionarySettings = model.DictionarySettings,
                ModelSettings = model.ModelSettings,
                AndroidSettings = model.AndroidSettings,
                WindowsSettings = model.WindowsSettings,
                SyncSettings = model.SyncSettings,
            };
        }

        if (isDictionary)
        {
            List<string> enabledDictIds = model.DictionarySettings.EnabledDictionaryIds
                .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            enabledDictIds.Add(resourceId);
            List<string> dictOrder = model.DictionarySettings.DictionaryOrder
                .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            dictOrder.Add(resourceId);

            return new ConfigModel
            {
                ConfigVersion = model.ConfigVersion,
                ProfileSettings = model.ProfileSettings,
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

        if (isModel)
        {
            List<string> enabledModelIds = model.ModelSettings.EnabledModelIds
                .Where(item => !string.Equals(item, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            enabledModelIds.Add(resourceId);

            return new ConfigModel
            {
                ConfigVersion = model.ConfigVersion,
                ProfileSettings = model.ProfileSettings,
                PersonalizationSettings = model.PersonalizationSettings,
                DictionarySettings = model.DictionarySettings,
                ModelSettings = new ModelSettings
                {
                    EnabledModelIds = enabledModelIds,
                    ActiveModelId = resourceId,
                    ModelRoot = model.ModelSettings.ModelRoot,
                    ModelVersions = model.ModelSettings.ModelVersions,
                },
                AndroidSettings = model.AndroidSettings,
                WindowsSettings = model.WindowsSettings,
                SyncSettings = model.SyncSettings,
            };
        }

        return model;
    }

    private static DiagnosticFinding CreateSimpleFinding(
        string code,
        string detail,
        string? summary = null,
        string? relatedTaskId = null,
        string? conflictScope = null,
        IReadOnlyList<string>? relatedEntryPoints = null)
    {
        _ = summary;
        _ = relatedTaskId;
        _ = relatedEntryPoints;
        return new DiagnosticFinding
        {
            Code = code,
            Detail = detail,
            Summary = detail,
            Severity = WorkflowSeverities.Blocking,
            DisplayKind = FeedbackDisplayKinds.ExplicitError,
            AutoActionKind = AutoActionKinds.None,
            EntryPointKind = EntryPointKinds.None,
            ConflictScope = conflictScope,
        };
    }

    private static string ResolveSourceRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "shared", "spec", "config_model.schema.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到源仓库根目录。");
    }

    private static void FixSogouPinyinSeparator(string resourceId)
    {
        if (!string.Equals(resourceId, "sogou_network_popular_words", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string resourceRoot = Path.Combine(
            Environment.CurrentDirectory,
            "workspace", "windows", "resources", resourceId, "current");
        string dictPath = Path.Combine(resourceRoot, $"{resourceId}.dict.yaml");
        if (!File.Exists(dictPath))
        {
            Console.WriteLine("  FixSogouPinyin: dict file not found, skipping");
            return;
        }

        string content = File.ReadAllText(dictPath, Encoding.UTF8);
        List<string> lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        bool inHeader = true;
        int fixedCount = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (inHeader)
            {
                if (line.Trim() == "...")
                {
                    inHeader = false;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            int firstTab = line.IndexOf('\t');
            if (firstTab < 0)
            {
                continue;
            }

            int secondTab = line.IndexOf('\t', firstTab + 1);
            if (secondTab < 0)
            {
                continue;
            }

            string pinyin = line[(firstTab + 1)..secondTab];
            if (!pinyin.Contains('\''))
            {
                continue;
            }

            string fixedPinyin = pinyin.Replace('\'', ' ');
            lines[i] = line[..(firstTab + 1)] + fixedPinyin + line[secondTab..];
            fixedCount++;
        }

        if (fixedCount > 0)
        {
            string output = string.Join("\n", lines);
            FileHelper.WriteTextWithVerification(dictPath, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), maxRetries: 3, baseDelayMs: 100, maxDelayMs: 1000);
            Console.WriteLine($"  FixSogouPinyin: replaced ' → space in {fixedCount} entries");
        }
        else
        {
            Console.WriteLine("  FixSogouPinyin: no apostrophe entries found, already fixed");
        }
    }

    public static int RunUninstall(string[] args)
    {
        string? resourceId = null;
        string? configPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--resource-id" && i + 1 < args.Length)
            {
                resourceId = args[++i];
            }
            else if (args[i] == "--config" && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            Console.Error.WriteLine("Usage: force-unapply --resource-id <id> [--config <path>]");
            return 1;
        }

        string sourceRoot = ResolveSourceRepositoryRoot();
        Environment.SetEnvironmentVariable("RIMEKIT_SOURCE_REPOSITORY_ROOT", sourceRoot);
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_ACTIVATOR_PATH",
            Path.Combine(sourceRoot, "apps", "windows", "RimeKit.Windows.Activator", "bin", "Debug", "net10.0-windows", "RimeKit.Windows.Activator.exe"));

        RepositoryContext repo = new(Environment.CurrentDirectory);
        ConfigModelService configService = new(repo);
        ArtifactService artifactService = new(repo);

        string effectiveConfigPath = !string.IsNullOrWhiteSpace(configPath)
            ? Path.GetFullPath(configPath)
            : repo.CurrentConfigModelPath;
        ConfigModel currentModel = configService.Load(effectiveConfigPath, allowDefault: true);

        Console.WriteLine($"STEP: Removing {resourceId} from config...");
        ConfigModel updatedModel = RemoveResource(currentModel, resourceId);

        Console.WriteLine($"STEP: Saving config to {effectiveConfigPath}...");
        configService.Save(effectiveConfigPath, updatedModel);
        repo.PersistCurrentConfigModel(updatedModel);

        Console.WriteLine("STEP: Detecting environment...");
        WindowsEnvironmentState env = WindowsEnvironmentService.Detect(updatedModel);
        repo.PersistRuntimePathCache(env);
        Console.WriteLine($"  WeaselAvailable={env.WeaselAvailable}, TargetRoot={env.WindowsTargetRoot}");

        Console.WriteLine("STEP: Generating artifacts...");
        string snapshotId = RepositoryContext.CreateOperationId("windows");
        GeneratedArtifacts artifacts = artifactService.Generate(updatedModel, snapshotId);
        Console.WriteLine($"  {artifacts.WindowsOutputFiles.Count} text + {artifacts.WindowsBinaryOutputFiles.Count} binary");

        Console.WriteLine("STEP: Stopping WeaselServer...");
        bool stopped = ProcessHelper.StopProcessesWithBackoff(
            new[] { "WeaselServer" },
            timeoutMs: 15000,
            baseDelayMs: 200,
            maxDelayMs: 2000);

        Console.WriteLine($"  stopped={stopped}");

        Console.WriteLine("STEP: Applying to target root...");
        _ = artifactService.ApplyWindowsTargets(
            artifacts.WindowsOutputFiles,
            artifacts.WindowsBinaryOutputFiles,
            artifacts.UserDictionaryFiles,
            env.WindowsTargetRoot);

        Console.WriteLine($"  Applied {artifacts.WindowsOutputFiles.Count + artifacts.WindowsBinaryOutputFiles.Count} files to {env.WindowsTargetRoot}");
        Console.WriteLine("FORCE-UNAPPLY COMPLETED (deploy left to caller)");
        return 0;
    }

    private static ConfigModel RemoveResource(ConfigModel model, string resourceId)
    {
        return new ConfigModel
        {
            ConfigVersion = model.ConfigVersion,
            ProfileSettings = new ProfileSettings
            {
                EnabledSchemaIds = model.ProfileSettings.EnabledSchemaIds
                    .Where(id => !string.Equals(id, resourceId, StringComparison.OrdinalIgnoreCase)).ToList(),
                WindowsDefaultSchemaId = string.Equals(model.ProfileSettings.WindowsDefaultSchemaId, resourceId, StringComparison.OrdinalIgnoreCase)
                    ? "" : model.ProfileSettings.WindowsDefaultSchemaId,
                AndroidDefaultSchemaId = model.ProfileSettings.AndroidDefaultSchemaId,
            },

            PersonalizationSettings = model.PersonalizationSettings,
            DictionarySettings = new DictionarySettings
            {
                EnabledDictionaryIds = model.DictionarySettings.EnabledDictionaryIds
                    .Where(id => !string.Equals(id, resourceId, StringComparison.OrdinalIgnoreCase)).ToList(),
                DictionaryOrder = model.DictionarySettings.DictionaryOrder
                    .Where(id => !string.Equals(id, resourceId, StringComparison.OrdinalIgnoreCase)).ToList(),
                CustomEntries = model.DictionarySettings.CustomEntries,
            },
            ModelSettings = new ModelSettings
            {
                EnabledModelIds = model.ModelSettings.EnabledModelIds
                    .Where(id => !string.Equals(id, resourceId, StringComparison.OrdinalIgnoreCase)).ToList(),
                ActiveModelId = string.Equals(model.ModelSettings.ActiveModelId, resourceId, StringComparison.OrdinalIgnoreCase)
                    ? "" : model.ModelSettings.ActiveModelId,
                ModelRoot = model.ModelSettings.ModelRoot,
                ModelVersions = model.ModelSettings.ModelVersions,
            },
            AndroidSettings = model.AndroidSettings,
            WindowsSettings = model.WindowsSettings,
            SyncSettings = model.SyncSettings,
        };
    }
}
