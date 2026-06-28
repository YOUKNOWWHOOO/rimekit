using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using RimeKit.Windows.Core.Utilities;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RimeKit.Windows.Core;

/// <summary>
/// 负责生成、备份、写入、部署和回检。
/// </summary>
internal sealed class ArtifactService
{
    private static readonly IReadOnlyList<string> DefaultFuzzyRules =
    [
        "derive/zh/z",
        "derive/ch/c",
        "derive/sh/s",
    ];

    private static readonly Dictionary<string, string> FuzzySimpleToRegex = new(StringComparer.OrdinalIgnoreCase)
    {
        ["derive/zh/z"] = "derive/^zh([a-z]+)$/z$1/",
        ["derive/ch/c"] = "derive/^ch([a-z]+)$/c$1/",
        ["derive/sh/s"] = "derive/^sh([a-z]+)$/s$1/",
    };

    private static readonly Dictionary<string, string> FuzzyRegexToSimple = new(StringComparer.OrdinalIgnoreCase)
    {
        ["derive/^zh([a-z]+)$/z$1/"] = "derive/zh/z",
        ["derive/^ch([a-z]+)$/c$1/"] = "derive/ch/c",
        ["derive/^sh([a-z]+)$/s$1/"] = "derive/sh/s",
    };

    private const int CustomEntryWeightBoost = 1000000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly RepositoryContext _repositoryContext;

    private sealed class InstalledResourceState
    {
        public string ResourceId { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string ResourceKind { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string SourceClass { get; init; } = string.Empty;

        public string InstallPath { get; init; } = string.Empty;

        public string InstalledVersion { get; init; } = string.Empty;

        public string InstalledAt { get; init; } = string.Empty;

        public string Note { get; init; } = string.Empty;
    }

    public ArtifactService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    public GeneratedArtifacts Generate(ConfigModel model, string snapshotId)
    {
        // Snapshot directory creation and file writes are commented out.
        // LAN sync between Windows and Android is not yet implemented.
        // When re-enabled, uncomment the snapshot file writes and callers.

        string snapshotDirectory = Path.Combine(_repositoryContext.SnapshotsRoot, snapshotId);
        string targetRoot = RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot);

        string weaselCustomYaml = ReadOrGenerateWeaselCustom(targetRoot);
        string rimeMintCustomYaml = ReadOrGenerateRimeMintCustom(targetRoot, model);

        Dictionary<string, string> windowsFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["default.custom.yaml"] = RenderDefaultCustomYaml(model),
            ["rime_mint.custom.yaml"] = rimeMintCustomYaml,
            ["rime_mint.dict.yaml"] = RenderRimeMintDictionaryYaml(model, "rime_mint"),
            ["rime_mint.custom.dict.yaml"] = RenderRimeMintDictionaryYaml(model, "rime_mint.custom"),
            [Path.Combine("dicts", "custom_simple.dict.yaml")] = RenderCustomSimpleDictionaryYaml(model),
            [Path.Combine("dicts", "rime_mint.simple.txt")] = RenderRimeMintSimpleTable(model),
            ["weasel.custom.yaml"] = weaselCustomYaml,
        };

        Dictionary<string, string> androidFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["default.custom.yaml"] = windowsFiles["default.custom.yaml"],
            ["rime_mint.custom.yaml"] = windowsFiles["rime_mint.custom.yaml"],
            ["rime_mint.dict.yaml"] = windowsFiles["rime_mint.dict.yaml"],
            ["rime_mint.custom.dict.yaml"] = windowsFiles["rime_mint.custom.dict.yaml"],
            [Path.Combine("dicts", "custom_simple.dict.yaml")] = windowsFiles[Path.Combine("dicts", "custom_simple.dict.yaml")],
            [Path.Combine("dicts", "rime_mint.simple.txt")] = windowsFiles[Path.Combine("dicts", "rime_mint.simple.txt")],
            ["t9.custom.yaml"] = RenderT9CustomYaml(model),
            ["android_apply_manifest.json"] = RenderAndroidApplyManifest(model),
        };
        Dictionary<string, string> userDictionaryFiles = ExportUserDictionaryFiles(
            RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot));
        Dictionary<string, byte[]> windowsBinaryFiles = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, byte[]> androidBinaryFiles = new(StringComparer.OrdinalIgnoreCase);
        ApplyInstalledResourceOverlays(model, windowsFiles, androidFiles, windowsBinaryFiles, androidBinaryFiles);

        // Snapshot file writes are commented out — LAN sync not yet implemented.

        return new GeneratedArtifacts
        {
            SnapshotId = snapshotId,
            WindowsOutputFiles = windowsFiles,
            WindowsBinaryOutputFiles = windowsBinaryFiles,
            UserDictionaryFiles = userDictionaryFiles,
        };

        /* >>> Snapshot file writes commented out — LAN sync not yet implemented. Re-enable when ready.
        string customEntriesPath = Path.Combine(snapshotDirectory, "user_data", "custom_entries.json");
        RepositoryContext.WriteUtf8(
        {
            snapshot_id = snapshotId,
            created_at = DateTimeOffset.UtcNow,
            config_version = 1,
            config_model = model,
            resolved_resources = new object[]
            {
                new { resource_id = "rime_mint", resource_kind = "schema", source_class = "official_current", version_or_updated_at = "tracked_by_snapshot" },
                new { resource_id = "t9", resource_kind = "schema", source_class = "official_current", version_or_updated_at = "tracked_by_snapshot" },
                new { resource_id = "moetype", resource_kind = "dictionary", source_class = "product_fixed_decision", version_or_updated_at = "tracked_by_snapshot" },
                new { resource_id = "sogou_network_popular_words", resource_kind = "dictionary", source_class = "product_fixed_decision", version_or_updated_at = "tracked_by_snapshot" },
                new { resource_id = "custom_simple", resource_kind = "dictionary", source_class = "official_current", version_or_updated_at = "tracked_by_snapshot" },
            },
            resolved_feature_presets = new
            {
                fuzzy_pinyin = model.FuzzyPinyinSettings.PresetId,
                symbol_profile = model.PersonalizationSettings.SymbolProfileId,
                preedit_profile = model.PersonalizationSettings.PreeditFormatMode,
            },
        }, JsonOptions);

        string generationSummaryJson = JsonSerializer.Serialize(new
        {
            snapshot_id = snapshotId,
            config_version = 1,
            resource_versions = new Dictionary<string, string>
            {
                ["rime_mint"] = "tracked_by_snapshot",
                ["t9"] = "tracked_by_snapshot",
                ["moetype"] = "tracked_by_snapshot",
                ["sogou_network_popular_words"] = "tracked_by_snapshot",
                ["custom_simple"] = "tracked_by_snapshot",
            },
            resolved_defaults = new
            {
                windows_default_schema_id = model.ProfileSettings.WindowsDefaultSchemaId,
                android_default_schema_id = model.ProfileSettings.AndroidDefaultSchemaId,
            },
            resolved_feature_presets = new
            {
                fuzzy_pinyin = model.FuzzyPinyinSettings.PresetId,
                symbol_profile = model.PersonalizationSettings.SymbolProfileId,
                preedit_profile = model.PersonalizationSettings.PreeditFormatMode,
            },
            generated_files_by_platform = new
            {
                android = androidFiles.Keys.OrderBy(item => item).ToArray(),
                windows = windowsFiles.Keys.OrderBy(item => item).ToArray(),
            },
            shared_output_summary = new
            {
                candidate = new
                {
                },
                behavior = new
                {
                },
                fuzzy_pinyin = new
                {
                    preset_id = model.FuzzyPinyinSettings.PresetId,
                    target_schema_ids = model.FuzzyPinyinSettings.TargetSchemaIds,
                },
                personalization = new
                {
                    preedit_format_mode = model.PersonalizationSettings.PreeditFormatMode,
                },
                dictionary = new
                {
                    enabled_dictionary_ids = model.DictionarySettings.EnabledDictionaryIds,
                    dictionary_order = model.DictionarySettings.DictionaryOrder,
                    custom_entry_count = model.DictionarySettings.CustomEntries.Count,
                },
                model_tuning = new
                {
                },
            },
        }, JsonOptions);

        var resolvedFeaturePresets = new
        {
            fuzzy_pinyin = model.FuzzyPinyinSettings.PresetId,
            symbol_profile = model.PersonalizationSettings.SymbolProfileId,
            preedit_profile = model.PersonalizationSettings.PreeditFormatMode,
        };

        string syncManifestJson = JsonSerializer.Serialize(new
        {
            snapshot_id = snapshotId,
            config_payload = new
            {
                config_version = 1,
                payload_path = "config_snapshot.json",
                sha256 = RepositoryContext.ComputeSha256(configSnapshotJson),
            },
            resource_payloads = BuildResourcePayloads(windowsFiles, windowsBinaryFiles, androidFiles, androidBinaryFiles, resolvedFeaturePresets),
            user_data_payloads = new object[]
            {
                new { payload_id = "custom_entries", payload_kind = "custom_entries", path = "user_data/custom_entries.json", sha256 = RepositoryContext.ComputeSha256(RepositoryContext.ReadUtf8(customEntriesPath)) },
                new { payload_id = "user_dict_export_directory", payload_kind = "user_dict_export", path = "user_data/user_dict_exports", sha256 = ComputeCollectionHash(userDictionaryFiles) },
            },
            platform_targets = new
            {
                android = new
                {
                    files = androidFiles.Keys.OrderBy(item => item).ToArray(),
                    success_checks = BuildAndroidSuccessChecks(),
                },
                windows = new
                {
                    files = windowsFiles.Keys.OrderBy(item => item).ToArray(),
                    success_checks = BuildWindowsSuccessChecks(),
                },
            },
            delivery_plan = BuildDeliveryPlan(),
            success_criteria = new
            {
                config_model = new { required = true, comparison_basis = "config_snapshot.json" },
                formal_resource = new { required = true, comparison_basis = "resource_manifest + resolved_feature_presets + generated_template_files" },
                user_data = new { required = true, comparison_basis = "user_data/custom_entries.json + user_dict_exports/" },
                target_config = new { required = true, comparison_basis = "windows/*.yaml + android/*.yaml + android_apply_manifest.json" },
                runtime_state = new
                {
                    required = true,
                    comparison_basis =
                        "android carrier_available + rime_plugin_available + sync_root_authorized + android_import_root_authorized + required_schema_selected + keyboard_layout_applied + t9_delivery_completed; windows weasel_available + target_files_written + deployer_completed + font_resolved + candidate_layout_applied",
                },
            },
            consistency_hashes = new Dictionary<string, string>
            {
                ["default.custom.yaml"] = RepositoryContext.ComputeSha256(windowsFiles["default.custom.yaml"]),
                ["rime_mint.custom.yaml"] = RepositoryContext.ComputeSha256(windowsFiles["rime_mint.custom.yaml"]),
                ["rime_mint.custom.dict.yaml"] = RepositoryContext.ComputeSha256(windowsFiles["rime_mint.custom.dict.yaml"]),
                ["dicts/custom_simple.dict.yaml"] = RepositoryContext.ComputeSha256(windowsFiles[Path.Combine("dicts", "custom_simple.dict.yaml")]),
            },
        }, JsonOptions);

        RepositoryContext.WriteUtf8(Path.Combine(snapshotDirectory, "config_snapshot.json"), configSnapshotJson);
        RepositoryContext.WriteUtf8(Path.Combine(snapshotDirectory, "generation_summary.json"), generationSummaryJson);
        RepositoryContext.WriteUtf8(Path.Combine(snapshotDirectory, "sync_manifest.json"), syncManifestJson);

        return new GeneratedArtifacts
        {
            SnapshotId = snapshotId,
            WindowsOutputFiles = windowsFiles,
            WindowsBinaryOutputFiles = windowsBinaryFiles,
            UserDictionaryFiles = userDictionaryFiles,
        };
        */
    }

    public IReadOnlyList<string> ApplyWindowsTargets(
        IReadOnlyDictionary<string, string> windowsOutputFiles,
        IReadOnlyDictionary<string, byte[]> windowsBinaryOutputFiles,
        IReadOnlyDictionary<string, string> userDictionaryFiles,
        string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        List<string> cleanupFailures = CleanStaleArtifactFiles(targetRoot, windowsOutputFiles, windowsBinaryOutputFiles);
        foreach ((string fileName, string content) in windowsOutputFiles)
        {
            try
            {
                RepositoryContext.WriteUtf8(Path.Combine(targetRoot, fileName), PrepareWindowsRuntimeContent(fileName, content));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cleanupFailures.Add($"写入目标文件失败: {fileName} — {ex.Message}");
            }
        }

        foreach ((string fileName, byte[] content) in windowsBinaryOutputFiles)
        {
            try
            {
                RepositoryContext.WriteBytes(Path.Combine(targetRoot, fileName), content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cleanupFailures.Add($"写入目标二进制文件失败: {fileName} — {ex.Message}");
            }
        }

        foreach ((string fileName, string content) in userDictionaryFiles)
        {
            try
            {
                RepositoryContext.WriteUtf8(Path.Combine(targetRoot, fileName), content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cleanupFailures.Add($"写入用户词典文件失败: {fileName} — {ex.Message}");
            }
        }

        return cleanupFailures.AsReadOnly();
    }

    private static bool DeleteWithRetry(string path)
    {
        try
        {
            FileHelper.DeleteFileWithBackoff(
                path,
                maxRetries: 10,
                baseDelayMs: 200,
                maxDelayMs: 4000);
            System.Diagnostics.Debug.WriteLine($"[CleanStale] DELETED {path}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[CleanStale] GAVE_UP {path}: {ex.Message}");
            return false;
        }
    }

    private static List<string> CleanStaleArtifactFiles(
        string targetRoot,
        IReadOnlyDictionary<string, string> windowsOutputFiles,
        IReadOnlyDictionary<string, byte[]> windowsBinaryOutputFiles)
    {
        List<string> cleanupFailures = [];

        HashSet<string> keepFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in windowsOutputFiles.Keys)
        {
            keepFiles.Add(Path.GetFileName(name));
            keepFiles.Add(name.Replace('\\', '/'));
        }

        foreach (string name in windowsBinaryOutputFiles.Keys)
        {
            keepFiles.Add(Path.GetFileName(name));
            keepFiles.Add(name.Replace('\\', '/'));
        }

        System.Diagnostics.Debug.WriteLine($"[CleanStale] targetRoot={targetRoot}, keepFiles count={keepFiles.Count}");

        string[] staleExtensions = [".custom.yaml", ".schema.yaml", ".dict.yaml", ".lua", ".txt"];
        string[] staleTopFiles = ["default.yaml", "symbols.yaml", "terra_symbols.yaml", "rime.lua", "weasel.yaml", "grammar.yaml"];
        string[] staleDirectoriesDefault = ["dicts", "opencc", "lua"];
        HashSet<string> staleDirs = new(staleDirectoriesDefault, StringComparer.OrdinalIgnoreCase);
        foreach (string keptPath in keepFiles.Where(p => p.Contains('/') || p.Contains('\\')))
        {
            string topDir = keptPath.Replace('\\', '/').Split('/')[0];
            if (topDir.Length > 0 && topDir != ".")
            {
                staleDirs.Add(topDir);
            }
        }

        foreach (string dirName in staleDirs)
        {
            string dirPath = Path.Combine(targetRoot, dirName);
            if (!Directory.Exists(dirPath))
            {
                continue;
            }

            int scannedCount = 0;
            int deletedCount = 0;
            int keptCount = 0;
            foreach (string file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                scannedCount++;
                string relativePath = Path.GetRelativePath(targetRoot, file).Replace('\\', '/');
                if (keepFiles.Contains(relativePath) || keepFiles.Contains(Path.GetFileName(file)))
                {
                    keptCount++;
                    continue;
                }

                deletedCount++;
                if (!DeleteWithRetry(file))
                {
                    cleanupFailures.Add(file);
                }
            }
            System.Diagnostics.Debug.WriteLine($"[CleanStale] dir '{dirName}': scanned={scannedCount} kept={keptCount} deleted={deletedCount}");
        }

        foreach (string staleFile in staleTopFiles)
        {
            string fullPath = Path.Combine(targetRoot, staleFile);
            if (File.Exists(fullPath) && !keepFiles.Contains(staleFile))
            {
                if (!DeleteWithRetry(fullPath))
                {
                    cleanupFailures.Add(fullPath);
                }
            }
        }

        foreach (string file in Directory.GetFiles(targetRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(file);
            if (keepFiles.Contains(fileName) || keepFiles.Contains(fileName.Replace('\\', '/')))
            {
                continue;
            }

            bool isStale = false;
            foreach (string ext in staleExtensions)
            {
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    isStale = true;
                    break;
                }
            }

            if (!isStale)
            {
                continue;
            }

            if (!DeleteWithRetry(file))
            {
                cleanupFailures.Add(file);
            }
        }

        return cleanupFailures;
    }

    public DiagnosticFinding? Deploy(
        WindowsEnvironmentState environment,
        string snapshotId,
        string? backupId,
        Func<string, string, string?, string?, string?, IReadOnlyList<string>?, DiagnosticFinding> createFinding)
    {
        if (string.IsNullOrWhiteSpace(environment.DeployerPath))
        {
            return createFinding(
                "WINDOWS_DEPLOYER_MISSING",
                "未检测到 WeaselDeployer.exe，无法执行 deploy。",
                backupId,
                null,
                null,
                null);
        }

        string deployerFileName = environment.DeployerPath;
        string deployerArguments = "/deploy";
        if (deployerFileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            deployerFileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            deployerArguments = $"/c \"{deployerFileName}\" {deployerArguments}";
            deployerFileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = deployerFileName,
            Arguments = deployerArguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 WeaselDeployer 进程。");
        Task<string> readOut = process.StandardOutput.ReadToEndAsync();
        Task<string> readErr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            Utilities.ProcessHelper.TerminateProcess(process);
            return createFinding(
                "WINDOWS_DEPLOY_FAILED",
                "WeaselDeployer 部署超时（3分钟），已强制终止。",
                backupId,
                null,
                "windows_deploy_carrier",
                null);
        }

        string standardOutput = readOut.Result;
        string standardError = readErr.Result;

        string logDirectory = Path.Combine(_repositoryContext.LogsRoot, WorkflowPhases.Deploy);
        Directory.CreateDirectory(logDirectory);
        string logPath = Path.Combine(logDirectory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-windows-{WorkflowPhases.Deploy}-{snapshotId}.log");
        RepositoryContext.WriteUtf8(logPath, $"stdout:{Environment.NewLine}{standardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{standardError}");

        if (process.ExitCode != 0)
        {
            return createFinding(
                "WINDOWS_DEPLOY_FAILED",
                $"WeaselDeployer 执行失败，退出码为 {process.ExitCode}。",
                backupId,
                null,
                "windows_deploy_carrier",
                [logPath]);
        }

        string? semanticFailure = TryExtractSemanticDeployFailure(standardError);
        if (!string.IsNullOrWhiteSpace(semanticFailure))
        {
            return createFinding(
                "WINDOWS_DEPLOY_FAILED",
                semanticFailure,
                backupId,
                null,
                "windows_deploy_carrier",
                [logPath]);
        }

        return null;
    }

    private static string? TryExtractSemanticDeployFailure(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return null;
        }

        Match missingSchema = Regex.Match(
            standardError,
            @"missing input schema:\s*(?<schema>[^\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (missingSchema.Success)
        {
            string schemaId = missingSchema.Groups["schema"].Value.Trim();
            return string.IsNullOrWhiteSpace(schemaId)
                ? "WeaselDeployer 报告缺少正式输入方案，当前 deploy 未真正完成。"
                : $"WeaselDeployer 报告缺少正式输入方案：{schemaId}，当前 deploy 未真正完成。";
        }

        return null;
    }

    public IReadOnlyList<DiagnosticFinding> Recheck(
        IReadOnlyDictionary<string, string> windowsOutputFiles,
        IReadOnlyDictionary<string, byte[]> windowsBinaryOutputFiles,
        IReadOnlyDictionary<string, string> userDictionaryFiles,
        string targetRoot,
        string? backupId,
        Func<string, string, string?, string?, string?, IReadOnlyList<string>?, DiagnosticFinding> createFinding)
    {
        List<DiagnosticFinding> findings = [];

        foreach ((string fileName, string expectedContent) in windowsOutputFiles.Concat(userDictionaryFiles))
        {
            string targetPath = Path.Combine(targetRoot, fileName);
            if (!File.Exists(targetPath))
            {
                findings.Add(createFinding(
                    "WINDOWS_RECHECK_FAILED",
                    $"回检失败，目标文件不存在：{fileName}",
                    backupId,
                    ConflictScopes.TargetConfig,
                    null,
                    null));
                continue;
            }

            string actualContent = RepositoryContext.ReadUtf8(targetPath);
            string normalizedExpectedContent = PrepareWindowsRuntimeContent(fileName, expectedContent);
            if (!string.Equals(normalizedExpectedContent, actualContent, StringComparison.Ordinal))
            {
                findings.Add(createFinding(
                    "STATE_MISMATCH",
                    $"目标文件内容与生成结果不一致：{fileName}",
                    backupId,
                    ConflictScopes.TargetConfig,
                    null,
                    null));
            }
        }

        foreach ((string fileName, byte[] expectedContent) in windowsBinaryOutputFiles)
        {
            string targetPath = Path.Combine(targetRoot, fileName);
            if (!File.Exists(targetPath))
            {
                findings.Add(createFinding(
                    "WINDOWS_RECHECK_FAILED",
                    $"回检缺少目标文件：{targetPath}",
                    backupId,
                    ConflictScopes.TargetConfig,
                    null,
                    null));
                continue;
            }

            byte[] actualContent = RepositoryContext.ReadBytes(targetPath);
            if (!actualContent.AsSpan().SequenceEqual(expectedContent))
            {
                findings.Add(createFinding(
                    "WINDOWS_RECHECK_FAILED",
                    $"回检发现目标文件内容与快照不一致：{targetPath}",
                    backupId,
                    ConflictScopes.TargetConfig,
                    null,
                    null));
            }
        }

        return findings;
    }

    public void RestoreBackup(string backupId, string targetRoot)
    {
        string sourceDirectory = Path.Combine(_repositoryContext.BackupsRoot, backupId, "platform_targets");
        CopyDirectory(sourceDirectory, targetRoot);
    }

    public ConfigModel ImportRuntimeToConfig(string targetRoot, ConfigModel currentModel)
    {
        string defaultCustomPath = Path.Combine(targetRoot, "default.custom.yaml");
        string rimeMintCustomPath = Path.Combine(targetRoot, "rime_mint.custom.yaml");
        string rimeMintCustomDictionaryPath = Path.Combine(targetRoot, "rime_mint.custom.dict.yaml");
        string runtimeMergedDictionaryPath = Path.Combine(targetRoot, "rime_mint.dict.yaml");
        string rimeMintDictionaryPath = File.Exists(runtimeMergedDictionaryPath)
            ? runtimeMergedDictionaryPath
            : (File.Exists(rimeMintCustomDictionaryPath)
                ? rimeMintCustomDictionaryPath
                : runtimeMergedDictionaryPath);
        string customSimpleTablePath = Path.Combine(targetRoot, "dicts", "rime_mint.simple.txt");
        string customSimplePath = File.Exists(Path.Combine(targetRoot, "dicts", "custom_simple.dict.yaml"))
            ? Path.Combine(targetRoot, "dicts", "custom_simple.dict.yaml")
            : Path.Combine(targetRoot, "custom_simple.dict.yaml");
        string weaselCustomPath = Path.Combine(targetRoot, "weasel.custom.yaml");

        IReadOnlyList<string> enabledSchemaIds = File.Exists(defaultCustomPath)
            ? MergeImportedSchemaIds(
                ParseEnabledSchemaIds(RepositoryContext.ReadUtf8(defaultCustomPath)),
                currentModel.ProfileSettings.WindowsDefaultSchemaId,
                currentModel.ProfileSettings.AndroidDefaultSchemaId)
            : currentModel.ProfileSettings.EnabledSchemaIds;
        ParsedSharedYaml? sharedYaml = File.Exists(rimeMintCustomPath)
            ? ParseSharedYaml(RepositoryContext.ReadUtf8(rimeMintCustomPath))
            : null;
        ParsedWeaselYaml? weaselYaml = File.Exists(weaselCustomPath)
            ? ParseWeaselYaml(
                RepositoryContext.ReadUtf8(weaselCustomPath),
                File.Exists(Path.Combine(currentModel.SyncSettings.WindowsTargetRoot, "build", "weasel.yaml"))
                    ? RepositoryContext.ReadUtf8(Path.Combine(currentModel.SyncSettings.WindowsTargetRoot, "build", "weasel.yaml"))
                    : null)
            : null;
        IReadOnlyList<string> dictionaryOrder = File.Exists(rimeMintDictionaryPath)
            ? ParseDictionaryOrder(RepositoryContext.ReadUtf8(rimeMintDictionaryPath))
            : currentModel.DictionarySettings.DictionaryOrder;
        IReadOnlyList<CustomEntry> customEntries = File.Exists(customSimpleTablePath)
            ? ParseSimpleTableEntries(RepositoryContext.ReadUtf8(customSimpleTablePath))
            : File.Exists(customSimplePath)
            ? ParseCustomEntries(RepositoryContext.ReadUtf8(customSimplePath))
            : currentModel.DictionarySettings.CustomEntries;
        IReadOnlyList<string> userFacingDictionaryOrder = NormalizeUserFacingDictionaryIds(dictionaryOrder);

        IReadOnlyList<string> fuzzyRules = sharedYaml?.FuzzyRules ?? [];
        IReadOnlyList<string> normalizedRules = fuzzyRules
            .Select(rule => FuzzyRegexToSimple.TryGetValue(rule, out string? simple) ? simple : rule)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IReadOnlyList<string> additionalRules = normalizedRules.Where(rule => !DefaultFuzzyRules.Contains(rule, StringComparer.Ordinal)).ToArray();
        bool fuzzyEnabled = normalizedRules.Count > 0;
        string fuzzyPresetId = normalizedRules.Any(rule => DefaultFuzzyRules.Contains(rule, StringComparer.Ordinal)) ? "cn_common" : string.Empty;

        return new ConfigModel
        {
            ConfigVersion = currentModel.ConfigVersion,
            ProfileSettings = new ProfileSettings
            {
                EnabledSchemaIds = enabledSchemaIds,
                WindowsDefaultSchemaId = currentModel.ProfileSettings.WindowsDefaultSchemaId,
                AndroidDefaultSchemaId = currentModel.ProfileSettings.AndroidDefaultSchemaId,
            },
            FuzzyPinyinSettings = new FuzzyPinyinSettings
            {
                PresetId = fuzzyPresetId,
                TargetSchemaIds = fuzzyEnabled ? ["rime_mint"] : currentModel.FuzzyPinyinSettings.TargetSchemaIds,
            },
            PersonalizationSettings = currentModel.PersonalizationSettings,
            DictionarySettings = new DictionarySettings
            {
                EnabledDictionaryIds = userFacingDictionaryOrder,
                DictionaryOrder = userFacingDictionaryOrder,
                CustomEntries = customEntries,
            },
            ModelSettings = new ModelSettings
            {
                EnabledModelIds = currentModel.ModelSettings.EnabledModelIds,
                ActiveModelId = currentModel.ModelSettings.ActiveModelId,
                ModelRoot = currentModel.ModelSettings.ModelRoot,
                ModelVersions = currentModel.ModelSettings.ModelVersions,
            },
            SyncSettings = currentModel.SyncSettings,
            AndroidSettings = currentModel.AndroidSettings,
            WindowsSettings = new WindowsSettings
            {
                DpiScaleMode = currentModel.WindowsSettings.DpiScaleMode,
            },
        };
    }

    public string ExportLatestSnapshot(string outputDirectory)
    {
        string? snapshotId = _repositoryContext.ResolveStateReference("latest_successful_snapshot.txt")
            ?? _repositoryContext.ResolveStateReference("latest_generated_snapshot.txt");
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            throw new InvalidOperationException("当前没有可导出的快照。");
        }

        string sourceDirectory = Path.Combine(_repositoryContext.SnapshotsRoot, snapshotId);
        string destinationPath = Path.Combine(outputDirectory, $"{snapshotId}.zip");
        ZipDirectory(sourceDirectory, destinationPath);
        return destinationPath;
    }

    public string ExportSnapshot(string snapshotId, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            throw new InvalidOperationException("未提供可导出的快照标识。");
        }

        string sourceDirectory = Path.Combine(_repositoryContext.SnapshotsRoot, snapshotId);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"未找到待导出的快照目录：{snapshotId}");
        }

        string destinationPath = Path.Combine(outputDirectory, $"{snapshotId}.zip");
        ZipDirectory(sourceDirectory, destinationPath);
        return destinationPath;
    }

    public string ExportBackup(string backupId, string outputDirectory)
    {
        string sourceDirectory = Path.Combine(_repositoryContext.BackupsRoot, backupId);
        string destinationPath = Path.Combine(outputDirectory, $"{backupId}.zip");
        ZipDirectory(sourceDirectory, destinationPath);
        return destinationPath;
    }

    public string ExportLatestDiagnostic(string outputDirectory)
    {
        string sourcePath = Path.Combine(_repositoryContext.StateRoot, "last_diagnostic.json");
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException("当前没有可导出的诊断结果。");
        }

        string destinationPath = Path.Combine(outputDirectory, "diagnostic_report.json");
        FileHelper.CopyFileWithBackoff(sourcePath, destinationPath, overwrite: true);
        return destinationPath;
    }

    public string ExportCurrentUserData(string outputDirectory, ConfigModel model)
    {
        Directory.CreateDirectory(outputDirectory);
        string userDataRoot = Path.Combine(outputDirectory, "user_data");
        Directory.CreateDirectory(userDataRoot);

        string customEntriesPath = Path.Combine(userDataRoot, "custom_entries.json");
        RepositoryContext.WriteUtf8(
            customEntriesPath,
            JsonSerializer.Serialize(model.DictionarySettings.CustomEntries, JsonOptions));

        string targetRoot = RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot);
        string userDictionaryRoot = Path.Combine(userDataRoot, "user_dict_exports");
        Directory.CreateDirectory(userDictionaryRoot);
        foreach ((string fileName, string content) in ExportUserDictionaryFiles(targetRoot))
        {
            RepositoryContext.WriteUtf8(Path.Combine(userDictionaryRoot, fileName), content);
        }

        return userDataRoot;
    }

    public string ExportUserConfigToml(string outputPath, ConfigModel model)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        string configJson = JsonSerializer.Serialize(model, JsonOptions);
        Dictionary<string, string> userDictionaryFiles = ExportUserDictionaryFiles(
            RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot));

        StringBuilder builder = new();
        builder.AppendLine("format_version = 1");
        builder.AppendLine("export_kind = \"rimekit_windows_user_config\"");
        builder.AppendLine($"exported_at = \"{DateTimeOffset.UtcNow:O}\"");
        builder.AppendLine($"config_model_base64 = \"{Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson))}\"");

        foreach ((string fileName, string content) in userDictionaryFiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine("[[user_dictionary_files]]");
            builder.AppendLine($"file_name_base64 = \"{Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName))}\"");
            builder.AppendLine($"content_base64 = \"{Convert.ToBase64String(Encoding.UTF8.GetBytes(content))}\"");
        }

        RepositoryContext.WriteUtf8(fullOutputPath, builder.ToString());
        return fullOutputPath;
    }

    public (ConfigModel ConfigModel, IReadOnlyDictionary<string, string> UserDictionaryFiles) ImportUserConfigToml(string sourcePath)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"未找到用户配置文件：{fullSourcePath}");
        }

        string content = RepositoryContext.ReadUtf8(fullSourcePath).ReplaceLineEndings("\n");
        string exportKind = ReadTomlBasicString(content, "export_kind");
        if (!string.Equals(exportKind, "rimekit_windows_user_config", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("当前 TOML 文件不是 RimeKit Windows 用户配置导出文件。");
        }

        string configModelBase64 = ReadTomlBasicString(content, "config_model_base64");
        ConfigModel? importedModel = JsonSerializer.Deserialize<ConfigModel>(
            Encoding.UTF8.GetString(Convert.FromBase64String(configModelBase64)),
            JsonOptions);
        if (importedModel is null)
        {
            throw new InvalidOperationException("无法从 TOML 中解析正式配置模型。");
        }

        Dictionary<string, string> userDictionaryFiles = new(StringComparer.OrdinalIgnoreCase);
        string[] blocks = Regex.Split(content, @"(?m)^\[\[user_dictionary_files\]\]\n");
        foreach (string block in blocks.Skip(1))
        {
            string fileName = Encoding.UTF8.GetString(Convert.FromBase64String(ReadTomlBasicString(block, "file_name_base64")));
            string fileContent = Encoding.UTF8.GetString(Convert.FromBase64String(ReadTomlBasicString(block, "content_base64")));
            string normalizedFileName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, normalizedFileName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"用户词典文件名不合法：{fileName}");
            }

            userDictionaryFiles[normalizedFileName] = fileContent;
        }

        return (importedModel, userDictionaryFiles);
    }

    public ConfigModel ImportCustomEntries(string sourcePath, ConfigModel currentModel)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"未找到自定义词条文件：{fullSourcePath}");
        }

        IReadOnlyList<CustomEntry>? importedEntries = JsonSerializer.Deserialize<IReadOnlyList<CustomEntry>>(
            RepositoryContext.ReadUtf8(fullSourcePath),
            JsonOptions);
        if (importedEntries is null)
        {
            throw new InvalidOperationException("无法解析自定义词条 JSON。");
        }

        return new ConfigModel
        {
            ConfigVersion = currentModel.ConfigVersion,
            ProfileSettings = currentModel.ProfileSettings,
            FuzzyPinyinSettings = currentModel.FuzzyPinyinSettings,
            PersonalizationSettings = currentModel.PersonalizationSettings,
            DictionarySettings = new DictionarySettings
            {
                EnabledDictionaryIds = currentModel.DictionarySettings.EnabledDictionaryIds,
                DictionaryOrder = currentModel.DictionarySettings.DictionaryOrder,
                CustomEntries = importedEntries,
            },
            ModelSettings = currentModel.ModelSettings,
            SyncSettings = currentModel.SyncSettings,
            AndroidSettings = currentModel.AndroidSettings,
            WindowsSettings = currentModel.WindowsSettings,
        };
    }

    public string ImportUserDictionaryDirectory(string sourceDirectory, string targetRoot)
    {
        string fullSourceDirectory = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(fullSourceDirectory))
        {
            throw new DirectoryNotFoundException($"未找到用户词典目录：{fullSourceDirectory}");
        }

        Directory.CreateDirectory(targetRoot);
        int importedCount = 0;
        foreach (string file in Directory.GetFiles(fullSourceDirectory, "*.userdb.txt", SearchOption.TopDirectoryOnly))
        {
            try
            {
                string destinationPath = Path.Combine(targetRoot, Path.GetFileName(file));
                FileHelper.CopyFileWithBackoff(file, destinationPath, overwrite: true);
                importedCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ImportUserDict] 复制失败: {file} — {ex.Message}");
            }
        }

        if (importedCount == 0)
        {
            throw new InvalidOperationException("用户词典目录中没有 *.userdb.txt 文件。");
        }

        return targetRoot;
    }

    public void ImportUserDictionaryFiles(IReadOnlyDictionary<string, string> userDictionaryFiles, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach ((string fileName, string content) in userDictionaryFiles)
        {
            string normalizedFileName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, normalizedFileName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"用户词典文件名不合法：{fileName}");
            }

            try
            {
                RepositoryContext.WriteUtf8(Path.Combine(targetRoot, normalizedFileName), content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ImportUserDict] 写入失败: {normalizedFileName} — {ex.Message}");
            }
        }
    }

    public string PublishLatestSnapshotToSharedRoot(string sharedRoot, string? snapshotId = null)
    {
        string fullSharedRoot = Path.GetFullPath(sharedRoot);
        Directory.CreateDirectory(fullSharedRoot);
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            return ExportLatestSnapshot(fullSharedRoot);
        }

        string sourceDirectory = Path.Combine(_repositoryContext.SnapshotsRoot, snapshotId);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"未找到待发布的快照目录：{snapshotId}");
        }

        string destinationPath = Path.Combine(fullSharedRoot, $"{snapshotId}.zip");
        ZipDirectory(sourceDirectory, destinationPath);
        return destinationPath;
    }

    public ImportedSnapshot StageLatestSnapshotImportFromSharedRoot(string sharedRoot)
    {
        string fullSharedRoot = Path.GetFullPath(sharedRoot);
        if (!Directory.Exists(fullSharedRoot))
        {
            throw new DirectoryNotFoundException($"未找到同步快照根目录：{fullSharedRoot}");
        }

        string latestSnapshotPath = Directory.GetFiles(fullSharedRoot, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new FileNotFoundException("同步快照根目录中没有可导入的同步快照。");

        return StageSnapshotImport(latestSnapshotPath);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string file in Directory.GetFiles(sourceDirectory))
        {
            try
            {
                string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(file));
                FileHelper.CopyFileWithBackoff(file, destinationPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CopyDirectory] 复制失败: {file} — {ex.Message}");
            }
        }

        foreach (string directory in Directory.GetDirectories(sourceDirectory))
        {
            string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, destinationPath);
        }
    }

    private static void ZipDirectory(string sourceDirectory, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        string backupPath = destinationPath + ".bak";
        if (File.Exists(destinationPath))
        {
            if (File.Exists(backupPath))
            {
                FileHelper.DeleteFileWithBackoff(backupPath);
            }
            FileHelper.CopyFileWithBackoff(destinationPath, backupPath, overwrite: true);
        }
        try
        {
            ZipFile.CreateFromDirectory(sourceDirectory, destinationPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            if (File.Exists(backupPath))
            {
                FileHelper.DeleteFileWithBackoff(backupPath);
            }
        }
        catch (IOException)
        {
            if (File.Exists(backupPath) && !File.Exists(destinationPath))
            {
                FileHelper.CopyFileWithBackoff(backupPath, destinationPath, overwrite: true);
                FileHelper.DeleteFileWithBackoff(backupPath);
            }
            throw;
        }
    }

    public ImportedSnapshot StageSnapshotImport(string sourcePath)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath) && !Directory.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"未找到同步快照：{fullSourcePath}");
        }

        string importRoot = Path.Combine(_repositoryContext.WorkspaceRoot, "imports", RepositoryContext.CreateOperationId("snapshot-import"));
        Directory.CreateDirectory(importRoot);

        string extractedRoot;
        if (Directory.Exists(fullSourcePath))
        {
            extractedRoot = Path.Combine(importRoot, Path.GetFileName(fullSourcePath));
            CopyDirectory(fullSourcePath, extractedRoot);
        }
        else
        {
            ZipFile.ExtractToDirectory(fullSourcePath, importRoot);
            string configSnapshotAtRoot = Path.Combine(importRoot, "config_snapshot.json");
            extractedRoot = File.Exists(configSnapshotAtRoot)
                ? importRoot
                : Directory.GetDirectories(importRoot).Single();
        }

        string configSnapshotPath = Path.Combine(extractedRoot, "config_snapshot.json");
        string syncManifestPath = Path.Combine(extractedRoot, "sync_manifest.json");
        string windowsDirectory = Path.Combine(extractedRoot, "windows");
        string androidDirectory = Path.Combine(extractedRoot, "android");
        if (!File.Exists(configSnapshotPath) || !File.Exists(syncManifestPath) ||
            !Directory.Exists(windowsDirectory) || !Directory.Exists(androidDirectory))
        {
            throw new InvalidOperationException("同步快照结构不完整，缺少正式快照文件或平台目标包。");
        }

        using JsonDocument configSnapshot = JsonDocument.Parse(RepositoryContext.ReadUtf8(configSnapshotPath));
        string snapshotId = configSnapshot.RootElement.GetProperty("snapshot_id").GetString()
            ?? throw new InvalidOperationException("同步快照缺少 snapshot_id。");
        ConfigModel? configModel = JsonSerializer.Deserialize<ConfigModel>(
            configSnapshot.RootElement.GetProperty("config_model").GetRawText(),
            JsonOptions);
        if (configModel is null)
        {
            throw new InvalidOperationException("无法解析同步快照内的正式配置模型。");
        }

        Dictionary<string, string> windowsFiles = Directory.GetFiles(windowsDirectory)
            .Where(path => !IsBinaryRuntimeFile(Path.GetFileName(path) ?? string.Empty))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => Path.GetFileName(path) ?? throw new InvalidOperationException("Windows 目标包存在匿名文件。"),
                RepositoryContext.ReadUtf8,
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, byte[]> windowsBinaryFiles = Directory.GetFiles(windowsDirectory)
            .Where(path => IsBinaryRuntimeFile(Path.GetFileName(path) ?? string.Empty))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => Path.GetFileName(path) ?? throw new InvalidOperationException("Windows 目标包存在匿名文件。"),
                RepositoryContext.ReadBytes,
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> userDictionaryFiles = Directory.Exists(Path.Combine(extractedRoot, "user_data", "user_dict_exports"))
            ? Directory.GetFiles(Path.Combine(extractedRoot, "user_data", "user_dict_exports"))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    path => Path.GetFileName(path) ?? throw new InvalidOperationException("用户词典同步载荷存在匿名文件。"),
                    RepositoryContext.ReadUtf8,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string snapshotDestination = Path.Combine(_repositoryContext.SnapshotsRoot, snapshotId);
        string snapshotOld = snapshotDestination + ".old";
        if (Directory.Exists(snapshotDestination))
        {
            if (Directory.Exists(snapshotOld))
            {
                FileHelper.DeleteDirectoryWithBackoff(snapshotOld);
            }
            Directory.Move(snapshotDestination, snapshotOld);
        }
        try
        {
            CopyDirectory(extractedRoot, snapshotDestination);
            if (Directory.Exists(snapshotOld))
            {
                FileHelper.DeleteDirectoryWithBackoff(snapshotOld);
            }
        }
        catch (IOException)
        {
            if (Directory.Exists(snapshotOld) && !Directory.Exists(snapshotDestination))
            {
                Directory.Move(snapshotOld, snapshotDestination);
            }
            throw;
        }

        return new ImportedSnapshot
        {
            SnapshotId = snapshotId,
            SnapshotDirectory = snapshotDestination,
            ConfigModel = configModel,
            WindowsOutputFiles = windowsFiles,
            WindowsBinaryOutputFiles = windowsBinaryFiles,
            UserDictionaryFiles = userDictionaryFiles,
        };
    }

    private static Dictionary<string, string> ExportUserDictionaryFiles(string targetRoot)
    {
        if (!Directory.Exists(targetRoot))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return Directory.GetFiles(targetRoot, "*.userdb.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                path => Path.GetFileName(path) ?? throw new InvalidOperationException("Windows 目标目录存在匿名用户词典文件。"),
                RepositoryContext.ReadUtf8,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadTomlBasicString(string content, string key)
    {
        Match match = Regex.Match(
            content,
            $@"(?m)^{Regex.Escape(key)}\s*=\s*""(?<value>[^""]*)""\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidOperationException($"用户配置 TOML 缺少字段：{key}");
        }

        return match.Groups["value"].Value;
    }

    private void ApplyInstalledResourceOverlays(
        ConfigModel model,
        IDictionary<string, string> windowsFiles,
        IDictionary<string, string> androidFiles,
        IDictionary<string, byte[]> windowsBinaryFiles,
        IDictionary<string, byte[]> androidBinaryFiles)
    {
        HashSet<string> reservedFileNames = new(
            windowsFiles.Keys.Concat(androidFiles.Keys),
            StringComparer.OrdinalIgnoreCase);

        foreach ((string relativePath, string content) in LoadInstalledResourceFiles(model, "windows", reservedFileNames))
        {
            windowsFiles[relativePath] = content;
        }

        foreach ((string relativePath, byte[] content) in LoadInstalledBinaryResourceFiles(model, "windows", reservedFileNames))
        {
            windowsBinaryFiles[relativePath] = content;
        }

        foreach ((string relativePath, string content) in LoadInstalledResourceFiles(model, "android", reservedFileNames))
        {
            androidFiles[relativePath] = content;
        }

        foreach ((string relativePath, byte[] content) in LoadInstalledBinaryResourceFiles(model, "android", reservedFileNames))
        {
            androidBinaryFiles[relativePath] = content;
        }
    }

    private Dictionary<string, string> LoadInstalledResourceFiles(ConfigModel model, string platform, IReadOnlySet<string> reservedFileNames)
    {
        HashSet<string> enabledResourceIds = new(StringComparer.OrdinalIgnoreCase);
        enabledResourceIds.UnionWith(model.DictionarySettings.EnabledDictionaryIds);
        enabledResourceIds.UnionWith(model.ProfileSettings.EnabledSchemaIds);
        enabledResourceIds.UnionWith(model.ModelSettings.EnabledModelIds);

        Dictionary<string, string> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstalledResourceState state in LoadInstalledResourceStates())
        {
            if (string.IsNullOrWhiteSpace(state.InstallPath) || !Directory.Exists(state.InstallPath))
            {
                continue;
            }

            bool shouldIncludeState = string.Equals(state.ResourceKind, "dictionary", StringComparison.OrdinalIgnoreCase) ||
                                      enabledResourceIds.Contains(state.ResourceId);
            if (!shouldIncludeState)
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(state.InstallPath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(state.InstallPath, file).Replace('\\', '/');
                string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                string fileName = Path.GetFileName(normalizedRelativePath);

                if (!ShouldIncludeInstalledResourceFile(platform, state.ResourceKind, normalizedRelativePath, fileName, reservedFileNames))
                {
                    continue;
                }

                if (IsBinaryRuntimeFile(fileName))
                {
                    continue;
                }

                string fileContent = RepositoryContext.ReadUtf8(file).ReplaceLineEndings("\r\n");
                fileContent = FixDictYamlName(fileContent, fileName);
                results[normalizedRelativePath] = fileContent;
            }
        }

        return results;
    }

    private Dictionary<string, byte[]> LoadInstalledBinaryResourceFiles(ConfigModel model, string platform, IReadOnlySet<string> reservedFileNames)
    {
        HashSet<string> enabledResourceIds = new(StringComparer.OrdinalIgnoreCase);
        enabledResourceIds.UnionWith(model.DictionarySettings.EnabledDictionaryIds);
        enabledResourceIds.UnionWith(model.ProfileSettings.EnabledSchemaIds);
        enabledResourceIds.UnionWith(model.ModelSettings.EnabledModelIds);

        Dictionary<string, byte[]> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstalledResourceState state in LoadInstalledResourceStates())
        {
            if (string.IsNullOrWhiteSpace(state.InstallPath) || !Directory.Exists(state.InstallPath))
            {
                continue;
            }

            bool shouldIncludeState = string.Equals(state.ResourceKind, "dictionary", StringComparison.OrdinalIgnoreCase) ||
                                      enabledResourceIds.Contains(state.ResourceId);
            if (!shouldIncludeState)
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(state.InstallPath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(state.InstallPath, file).Replace('\\', '/');
                string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                string fileName = Path.GetFileName(normalizedRelativePath);

                if (!ShouldIncludeInstalledResourceFile(platform, state.ResourceKind, normalizedRelativePath, fileName, reservedFileNames) ||
                    !IsBinaryRuntimeFile(fileName))
                {
                    continue;
                }

                byte[] bytes = RepositoryContext.ReadBytes(file);
                results[normalizedRelativePath] = bytes;

            }
        }

        return results;
    }

    private static bool ShouldIncludeInstalledResourceFile(
        string platform,
        string resourceKind,
        string relativePath,
        string fileName,
        IReadOnlySet<string> reservedFileNames)
    {
        string extension = Path.GetExtension(fileName);
        string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(extension))
        {
            if (string.Equals(fileName, "rime.lua", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        if (reservedFileNames.Contains(fileName) ||
            reservedFileNames.Contains(relativePath) ||
            fileName.EndsWith(".custom.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(resourceKind, "dictionary", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.EndsWith(".dict.yaml", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(resourceKind, "schema", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedRelativePath.StartsWith($"opencc{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedRelativePath.StartsWith($"lua{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                       fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            }

            if (normalizedRelativePath.StartsWith($"dicts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.EndsWith(".dict.yaml", StringComparison.OrdinalIgnoreCase) ||
                       fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            }

            if (fileName.EndsWith(".schema.yaml", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".dict.yaml", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] sharedRuntimeFiles =
            [
                "default.yaml",
                "symbols.yaml",
                "terra_symbols.yaml",
                "rime.lua",
            ];
            if (sharedRuntimeFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (platform == "windows" &&
                string.Equals(fileName, "weasel.yaml", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        if (string.Equals(resourceKind, "model", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.EndsWith(".gram", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "grammar.yaml", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsBinaryRuntimeFile(string fileName)
    {
        return fileName.EndsWith(".gram", StringComparison.OrdinalIgnoreCase);
    }

    private static string FixDictYamlName(string content, string targetFileName)
    {
        string expectedName = Path.GetFileNameWithoutExtension(targetFileName);
        if (expectedName.EndsWith(".dict", StringComparison.OrdinalIgnoreCase))
        {
            expectedName = expectedName[..^5];
        }
        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        StringBuilder builder = new();
        bool inHeader = false;
        bool headerEnded = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (!inHeader)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    builder.AppendLine(line);
                    continue;
                }
                if (trimmed == "---")
                {
                    inHeader = true;
                    builder.AppendLine(line);
                    continue;
                }
            }

            if (inHeader && !headerEnded)
            {
                if (line.Trim().StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"name: {expectedName}");
                    continue;
                }
                if (line.Trim() == "...")
                {
                    headerEnded = true;
                }
            }
            builder.AppendLine(line);
        }
        return builder.ToString().TrimEnd('\r', '\n');
    }

    private IEnumerable<InstalledResourceState> LoadInstalledResourceStates()
    {
        string statePath = _repositoryContext.InstalledResourcesStatePath;
        if (!File.Exists(statePath))
        {
            return [];
        }

        IReadOnlyList<InstalledResourceState>? states = JsonSerializer.Deserialize<IReadOnlyList<InstalledResourceState>>(RepositoryContext.ReadUtf8(statePath), JsonOptions);
        if (states is null)
            throw new InvalidOperationException($"无法解析已安装资源状态文件：{statePath}");

        string repoRoot = Path.GetFullPath(_repositoryContext.RepositoryRoot);
        return states.Select(item => new InstalledResourceState
        {
            ResourceId = item.ResourceId,
            DisplayName = item.DisplayName,
            ResourceKind = item.ResourceKind,
            Source = item.Source,
            SourceClass = item.SourceClass,
            InstallPath = ResolveInstallPath(item.InstallPath, repoRoot),
            InstalledVersion = item.InstalledVersion,
            InstalledAt = item.InstalledAt,
            Note = item.Note,
        });
    }

    private static string ResolveInstallPath(string storedPath, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return string.Empty;

        string repoRoot = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
        int wsIndex = storedPath.IndexOf(
            $"workspace{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        if (wsIndex >= 0)
        {
            string relative = storedPath.Substring(wsIndex);
            return Path.GetFullPath(Path.Combine(repoRoot, relative));
        }

        if (!Path.IsPathRooted(storedPath))
            return Path.GetFullPath(Path.Combine(repoRoot, storedPath));

        return storedPath;
    }

    private static object[] BuildResourcePayloads(
        IReadOnlyDictionary<string, string> windowsFiles,
        IReadOnlyDictionary<string, byte[]> windowsBinaryFiles,
        IReadOnlyDictionary<string, string> androidFiles,
        IReadOnlyDictionary<string, byte[]> androidBinaryFiles,
        object resolvedFeaturePresets)
    {
        string featurePresetJson = JsonSerializer.Serialize(resolvedFeaturePresets, JsonOptions);
        List<object> payloads =
        [
            new { payload_id = "rime_mint", payload_kind = "schema", path = "windows/rime_mint.custom.yaml", sha256 = RepositoryContext.ComputeSha256(windowsFiles["rime_mint.custom.yaml"]) },
            new { payload_id = "t9", payload_kind = "schema", path = "android/t9.custom.yaml", sha256 = RepositoryContext.ComputeSha256(androidFiles["t9.custom.yaml"]) },
            new { payload_id = "moetype", payload_kind = "dictionary", path = "windows/rime_mint.custom.dict.yaml", sha256 = RepositoryContext.ComputeSha256(windowsFiles["rime_mint.custom.dict.yaml"]) },
            new { payload_id = "sogou_network_popular_words", payload_kind = "dictionary", path = "windows/rime_mint.custom.dict.yaml", sha256 = RepositoryContext.ComputeSha256(windowsFiles["rime_mint.custom.dict.yaml"]) },
            new { payload_id = "custom_simple_dictionary", payload_kind = "dictionary", path = "windows/dicts/custom_simple.dict.yaml", sha256 = RepositoryContext.ComputeSha256(windowsFiles[Path.Combine("dicts", "custom_simple.dict.yaml")]) },
            new { payload_id = "default_custom_template", payload_kind = "template", path = "windows/default.custom.yaml", sha256 = RepositoryContext.ComputeSha256(windowsFiles["default.custom.yaml"]) },
            new { payload_id = "rime_mint_custom_template", payload_kind = "template", path = "windows/rime_mint.custom.yaml", sha256 = RepositoryContext.ComputeSha256(windowsFiles["rime_mint.custom.yaml"]) },
            new { payload_id = "t9_custom_template", payload_kind = "template", path = "android/t9.custom.yaml", sha256 = RepositoryContext.ComputeSha256(androidFiles["t9.custom.yaml"]) },
            new { payload_id = "weasel_custom_template", payload_kind = "template", path = "windows/weasel.custom.yaml", sha256 = RepositoryContext.ComputeSha256(windowsFiles["weasel.custom.yaml"]) },
            new { payload_id = "android_apply_manifest_template", payload_kind = "template", path = "android/android_apply_manifest.json", sha256 = RepositoryContext.ComputeSha256(androidFiles["android_apply_manifest.json"]) },
            new { payload_id = "fuzzy_pinyin_preset", payload_kind = "preset", path = "generation_summary.json#/resolved_feature_presets/fuzzy_pinyin", sha256 = RepositoryContext.ComputeSha256(featurePresetJson + "|fuzzy_pinyin") },
            new { payload_id = "symbol_profile_preset", payload_kind = "preset", path = "generation_summary.json#/resolved_feature_presets/symbol_profile", sha256 = RepositoryContext.ComputeSha256(featurePresetJson + "|symbol_profile") },
            new { payload_id = "preedit_profile_preset", payload_kind = "preset", path = "generation_summary.json#/resolved_feature_presets/preedit_profile", sha256 = RepositoryContext.ComputeSha256(featurePresetJson + "|preedit_profile") },
        ];

        HashSet<string> knownPaths = new(payloads.Select(item =>
        {
            JsonElement element = JsonSerializer.SerializeToElement(item, JsonOptions);
            return element.GetProperty("path").GetString() ?? string.Empty;
        }), StringComparer.OrdinalIgnoreCase);

        AppendDynamicResourcePayloads(payloads, knownPaths, "windows", windowsFiles);
        AppendDynamicBinaryResourcePayloads(payloads, knownPaths, "windows", windowsBinaryFiles);
        AppendDynamicResourcePayloads(payloads, knownPaths, "android", androidFiles);
        AppendDynamicBinaryResourcePayloads(payloads, knownPaths, "android", androidBinaryFiles);
        return payloads.ToArray();
    }

    private static void AppendDynamicResourcePayloads(
        List<object> payloads,
        ISet<string> knownPaths,
        string platform,
        IReadOnlyDictionary<string, string> files)
    {
        foreach ((string relativePath, string content) in files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            string normalizedPath = $"{platform}/{relativePath.Replace('\\', '/')}";
            if (knownPaths.Contains(normalizedPath) || !ShouldIncludeDynamicPayload(relativePath))
            {
                continue;
            }

            payloads.Add(new
            {
                payload_id = BuildDynamicPayloadId(platform, relativePath),
                payload_kind = ResolveDynamicPayloadKind(relativePath),
                path = normalizedPath,
                sha256 = RepositoryContext.ComputeSha256(content),
            });
            knownPaths.Add(normalizedPath);
        }
    }

    private static void AppendDynamicBinaryResourcePayloads(
        List<object> payloads,
        ISet<string> knownPaths,
        string platform,
        IReadOnlyDictionary<string, byte[]> files)
    {
        foreach ((string relativePath, byte[] content) in files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            string normalizedPath = $"{platform}/{relativePath.Replace('\\', '/')}";
            if (knownPaths.Contains(normalizedPath) || !ShouldIncludeDynamicPayload(relativePath))
            {
                continue;
            }

            payloads.Add(new
            {
                payload_id = BuildDynamicPayloadId(platform, relativePath),
                payload_kind = ResolveDynamicPayloadKind(relativePath),
                path = normalizedPath,
                sha256 = RepositoryContext.ComputeSha256(content),
            });
            knownPaths.Add(normalizedPath);
        }
    }

    private static bool ShouldIncludeDynamicPayload(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        return fileName.EndsWith(".dict.yaml", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".schema.yaml", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".gram", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "grammar.yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDynamicPayloadKind(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (fileName.EndsWith(".gram", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "grammar.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return "model";
        }

        if (fileName.EndsWith(".schema.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return "schema";
        }

        return "dictionary";
    }

    private static string BuildDynamicPayloadId(string platform, string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').Replace('/', '_').Replace('.', '_');
        return $"{platform}_{normalized}";
    }

    private static string[] BuildAndroidSuccessChecks()
    {
        return
        [
            "carrier_available",
            "rime_plugin_available",
            "sync_root_authorized",
            "android_import_root_authorized",
            "required_schema_selected",
            "keyboard_layout_applied",
            "t9_delivery_completed",
        ];
    }

    private static string[] BuildWindowsSuccessChecks()
    {
        return
        [
            "weasel_available",
            "target_files_written",
            "deployer_completed",
            "font_resolved",
            "candidate_layout_applied",
        ];
    }

    private static object[] BuildDeliveryPlan()
    {
        return
        [
            new { platform = "android", phase = WorkflowPhases.Detect, task_id = "android_detect_environment", manual_required = true },
            new { platform = "android", phase = WorkflowPhases.Configure, task_id = "android_configure_model", manual_required = false },
            new { platform = "android", phase = WorkflowPhases.Generate, task_id = "android_generate_targets", manual_required = false },
            new { platform = "android", phase = WorkflowPhases.Backup, task_id = "android_backup_import_root", manual_required = false },
            new { platform = "android", phase = WorkflowPhases.Apply, task_id = "android_apply_import_bundle", manual_required = false },
            new { platform = "android", phase = WorkflowPhases.Deploy, task_id = "android_deploy_carrier", manual_required = true },
            new { platform = "android", phase = WorkflowPhases.Recheck, task_id = "android_recheck_runtime", manual_required = false },
            new { platform = "android", phase = WorkflowPhases.Diagnose, task_id = "android_diagnose_result", manual_required = false },
            new { platform = "windows", phase = WorkflowPhases.Detect, task_id = "windows_detect_environment", manual_required = false },
            new { platform = "windows", phase = WorkflowPhases.Configure, task_id = "windows_configure_model", manual_required = false },
            new { platform = "windows", phase = WorkflowPhases.Generate, task_id = "windows_generate_targets", manual_required = false },
            new { platform = "windows", phase = WorkflowPhases.Backup, task_id = "windows_backup_target_root", manual_required = false },
            new { platform = "windows", phase = WorkflowPhases.Apply, task_id = "windows_apply_target_bundle", manual_required = false },
            new { platform = "windows", phase = WorkflowPhases.Deploy, task_id = "windows_deploy_carrier", manual_required = false },
            new { platform = "windows", phase = WorkflowPhases.Recheck, task_id = "windows_recheck_runtime", manual_required = false },
            new { platform = "windows", phase = WorkflowPhases.Diagnose, task_id = "windows_diagnose_result", manual_required = false },
        ];
    }

    private static string ComputeCollectionHash(IReadOnlyDictionary<string, string> files)
    {
        if (files.Count == 0)
        {
            return RepositoryContext.ComputeSha256(string.Empty);
        }

        string canonical = string.Join(
            "\n",
            files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key}:{RepositoryContext.ComputeSha256(item.Value)}"));
        return RepositoryContext.ComputeSha256(canonical);
    }

    private static string ReadOrGenerateWeaselCustom(string targetRoot)
    {
        string customPath = Path.Combine(targetRoot, "weasel.custom.yaml");
        if (File.Exists(customPath))
            return File.ReadAllText(customPath, Encoding.UTF8);
        return "patch:\r\n";
    }

    private static string ReadOrGenerateRimeMintCustom(string targetRoot, ConfigModel model)
    {
        string customPath = Path.Combine(targetRoot, "rime_mint.custom.yaml");
        if (File.Exists(customPath))
            return File.ReadAllText(customPath, Encoding.UTF8);
        return "patch:\r\n";
    }

    private static string RenderDefaultCustomYaml(ConfigModel model)
    {
        StringBuilder builder = new();
        builder.AppendLine("patch:");
        builder.AppendLine("  schema_list:");
        foreach (string schemaId in model.ProfileSettings.EnabledSchemaIds)
        {
            builder.AppendLine($"    - schema: \"{schemaId}\"");
        }

        return builder.ToString();
    }

    private static string PrepareWindowsRuntimeContent(string fileName, string content)
    {
        if (!string.Equals(fileName, "default.custom.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        List<string> schemaLines = content
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.None)
            .Where(line => line.Contains("schema:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("schema: \"t9\"", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<string> runtimeLines =
        [
            "patch:",
            "  schema_list:",
        ];
        runtimeLines.AddRange(schemaLines);
        runtimeLines.Add("  \"switcher/save_options\": []");
        return string.Join("\r\n", runtimeLines) + "\r\n";
    }

    private string ReadMintCustomYamlOrEmpty(string targetRoot)
    {
        string path = Path.Combine(targetRoot, "rime_mint.custom.yaml");
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "patch:\r\n";
    }

    private static string RenderT9CustomYaml(ConfigModel model)
    {
        StringBuilder builder = new();
        builder.AppendLine("patch:");
        builder.AppendLine("  \"style/candidate_list_layout\": \"\"");
        return builder.ToString();
    }

    private string RenderRimeMintDictionaryYaml(ConfigModel model, string dictionaryName)
    {
        StringBuilder builder = new();
        IReadOnlySet<string> installedDictionaryIds = LoadInstalledResourceStates()
            .Where(state => string.Equals(state.ResourceKind, "dictionary", StringComparison.OrdinalIgnoreCase))
            .Where(state => !string.IsNullOrWhiteSpace(state.InstallPath) && Directory.Exists(state.InstallPath))
            .Where(state => Directory.GetFiles(state.InstallPath, "*.dict.yaml", SearchOption.AllDirectories).Length > 0)
            .Select(state => state.ResourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] baseImportTables =
        [
            "dicts/rime_mint.chars",
            "dicts/rime_mint.base",
            "dicts/rime_mint.correlation",
            "dicts/rime_mint.compatible",
            "dicts/rime_mint.ext",
            "dicts/other_kaomoji",
            "dicts/rime_ice.others",
        ];

        builder.AppendLine("---");
        builder.AppendLine($"name: {dictionaryName}");
        builder.AppendLine("version: \"tracked_by_snapshot\"");
        builder.AppendLine("sort: by_weight");
        builder.AppendLine("use_preset_vocabulary: false");
        builder.AppendLine("import_tables:");
        HashSet<string> emittedDictionaryIds = new(StringComparer.OrdinalIgnoreCase);
        if (model.DictionarySettings.CustomEntries.Count > 0)
        {
            builder.AppendLine("  - \"dicts/custom_simple\"");
            emittedDictionaryIds.Add("custom_simple");
        }

        foreach (string baseImport in baseImportTables)
        {
            builder.AppendLine($"  - \"{baseImport}\"");
        }

        foreach (string dictionaryId in model.DictionarySettings.DictionaryOrder)
        {
            if (string.Equals(dictionaryId, "custom_simple", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!installedDictionaryIds.Contains(dictionaryId))
            {
                continue;
            }

            if (!emittedDictionaryIds.Add(dictionaryId))
            {
                continue;
            }

            builder.AppendLine($"  - \"{dictionaryId}\"");
        }

        foreach (string dictionaryId in installedDictionaryIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(dictionaryId, "custom_simple", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!emittedDictionaryIds.Add(dictionaryId))
            {
                continue;
            }

            builder.AppendLine($"  - \"{dictionaryId}\"");
        }

        return builder.ToString();
    }

    private string? ResolveInstalledGrammarName(ModelSettings modelSettings)
    {
        if (string.IsNullOrWhiteSpace(modelSettings.ActiveModelId) ||
            !modelSettings.EnabledModelIds.Contains(modelSettings.ActiveModelId, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        (string expectedFileName, string grammarLanguageName) = modelSettings.ActiveModelId switch
        {
            "wanxiang_lts_zh_hans" => ("wanxiang-lts-zh-hans.gram", "wanxiang-lts-zh-hans"),
            _ => throw new InvalidOperationException($"不支持的语法模型: '{modelSettings.ActiveModelId}'。" +
                " 支持的模型: wanxiang_lts_zh_hans。"),
        };
        if (string.IsNullOrWhiteSpace(expectedFileName))
        {
            return null;
        }

        InstalledResourceState? state = LoadInstalledResourceStates()
            .FirstOrDefault(item =>
                string.Equals(item.ResourceKind, "model", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ResourceId, modelSettings.ActiveModelId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.InstallPath) &&
                Directory.Exists(item.InstallPath) &&
                File.Exists(Path.Combine(item.InstallPath, expectedFileName)));
        if (state is null)
        {
            return null;
        }

        return grammarLanguageName;
    }

    private static string RenderCustomSimpleDictionaryYaml(ConfigModel model)
    {
        StringBuilder builder = new();
        builder.AppendLine("---");
        builder.AppendLine("name: custom_simple");
        builder.AppendLine("version: \"tracked_by_snapshot\"");
        builder.AppendLine("sort: by_weight");
        builder.AppendLine("...");
        foreach (CustomEntry entry in model.DictionarySettings.CustomEntries)
        {
            int effectiveWeight = Math.Max(1, entry.Weight) + CustomEntryWeightBoost;
            builder.AppendLine($"{entry.Text}\t{entry.Code}\t{effectiveWeight}");
        }

        return builder.ToString();
    }

    private static string RenderRimeMintSimpleTable(ConfigModel model)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Rime table");
        builder.AppendLine("# encoding: utf-8");
        builder.AppendLine("# 根据官方自定义文本方式生成");
        builder.AppendLine("#@/db_name rime_mint.simple.txt");
        builder.AppendLine("#@/db_type tabledb");
        builder.AppendLine();
        foreach (CustomEntry entry in model.DictionarySettings.CustomEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Text) || string.IsNullOrWhiteSpace(entry.Code))
            {
                continue;
            }

            builder.AppendLine($"{entry.Text}\t{entry.Code}");
        }

        return builder.ToString();
    }

    private string ReadWeaselCustomYamlOrEmpty(string targetRoot)
    {
        string path = Path.Combine(targetRoot, "weasel.custom.yaml");
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "patch:\r\n";
    }

    private static string RenderAndroidApplyManifest(ConfigModel model)
    {
        return JsonSerializer.Serialize(new
        {
            platform = "android",
            carrier_id = "fcitx5_android_rime",
            required_schema_id = model.ProfileSettings.AndroidDefaultSchemaId,
            keyboard_layout = model.AndroidSettings.KeyboardLayout,
            candidate_text_size = model.AndroidSettings.CandidateTextSize,
            candidate_view_height = model.AndroidSettings.CandidateViewHeight,
            manual_steps = new object[]
            {
                new { step_id = "grant_android_import_root", title = "授权 Android 导入源目录", next_action = "完成授权后返回应用重新检测" },
                new { step_id = "confirm_android_import", title = "在承载器中确认导入", next_action = "完成导入后返回应用重新检测" },
            },
            recheck_items = new[] { "required_schema_selected", "keyboard_layout_applied", "delivery_completed" },
            delivery_mode = "import_and_manual_confirm",
        }, JsonOptions);
    }

    private static IReadOnlyList<string> ResolveFuzzyRules(ConfigModel model)
    {
        List<string> rules = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(model.FuzzyPinyinSettings.PresetId))
        {
            foreach (string canonical in DefaultFuzzyRules)
            {
                string regex = FuzzySimpleToRegex[canonical];
                rules.Add(regex);
                seen.Add(regex);
            }
        }

        return rules;
    }

    private static IReadOnlyList<string> ParseEnabledSchemaIds(string content)
    {
        return content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- schema:", StringComparison.Ordinal))
            .Select(line => line.Split(':', 2)[1].Trim().Trim('"'))
            .ToArray();
    }

    private static IReadOnlyList<string> MergeImportedSchemaIds(
        IReadOnlyList<string> importedSchemaIds,
        string windowsDefaultSchemaId,
        string androidDefaultSchemaId)
    {
        List<string> merged = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string schemaId in importedSchemaIds)
        {
            if (string.IsNullOrWhiteSpace(schemaId) || !seen.Add(schemaId))
            {
                continue;
            }

            merged.Add(schemaId);
        }

        foreach (string requiredSchemaId in new[] { windowsDefaultSchemaId, androidDefaultSchemaId })
        {
            if (string.IsNullOrWhiteSpace(requiredSchemaId) || !seen.Add(requiredSchemaId))
            {
                continue;
            }

            merged.Add(requiredSchemaId);
        }

        return merged;
    }

    private static ParsedSharedYaml ParseSharedYaml(string content)
    {
        string[] lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        List<string> fuzzyRules = [];
        bool readingFuzzy = false;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line == "speller/algebra:" ||
                line == "\"speller/algebra\":" ||
                line == "speller/algebra/+:" ||
                line == "\"speller/algebra/+\":")
            {
                readingFuzzy = true;
                continue;
            }

            if (readingFuzzy && line.StartsWith("- ", StringComparison.Ordinal))
            {
                string rule = line[2..].Trim().Trim('"');
                if (rule.StartsWith("derive/", StringComparison.OrdinalIgnoreCase)
                    && !rule.Contains("[nl]ve", StringComparison.OrdinalIgnoreCase))
                {
                    fuzzyRules.Add(rule);
                }
                continue;
            }

            if (readingFuzzy && !line.StartsWith("- ", StringComparison.Ordinal))
            {
                readingFuzzy = false;
            }
        }

        return new ParsedSharedYaml
        {
            PageSize = ParseNullableInt(lines, "menu/page_size"),
            CandidateLayout = ParseQuotedValue(lines, "style/candidate_list_layout"),
            ShowEmojiComments = ParseNullableBool(lines, "translator/always_show_comments"),
            SimplificationMode = ParseSwitchReset(lines, 4),
            FullShapeEnabled = ParseSwitchReset(lines, 2) == "1",
            AsciiPunctEnabled = ParseSwitchReset(lines, 5) == "1",
            EmojiSuggestionEnabled = ParseSwitchReset(lines, 1) == "1",
            ToneDisplayEnabled = ParseSwitchReset(lines, 3) == "1",
            FuzzyRules = fuzzyRules,
        };
    }

    internal static ParsedWeaselYaml ParseWeaselTemplateYaml(string content)
    {
        string[] lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        return ParseWeaselYaml(string.Join("\n", FlattenYamlLines(lines)), null);
    }

    internal static string[] FlattenYamlLines(string[] lines)
    {
        List<string> result = [];
        Stack<(string key, int indent)> pathStack = new();
        bool inSwitches = false;
        int switchesIndent = -1;
        foreach (string l in lines)
        {
            string line = l;
            string trimmed = line.TrimEnd();
            int indent = line.Length - line.TrimStart().Length;
            string content = trimmed.TrimStart();
            if (string.IsNullOrEmpty(trimmed))
            {
                result.Add(line);
                continue;
            }
            if (content.StartsWith('#'))
            {
                result.Add(line);
                continue;
            }
            if (inSwitches)
            {
                if (indent <= switchesIndent && content.Length > 0 && !content.StartsWith('-'))
                {
                    inSwitches = false;
                }
                else
                {
                    result.Add(line);
                    continue;
                }
            }
            if (content.StartsWith("switches:"))
            {
                inSwitches = true;
                switchesIndent = indent;
                result.Add(line);
                continue;
            }
            while (pathStack.Count > 0 && indent <= pathStack.Peek().indent)
            {
                pathStack.Pop();
            }
            if (content.StartsWith('-'))
            {
                result.Add(line);
                continue;
            }
            int colonIdx = content.IndexOf(':');
            if (colonIdx < 0)
            {
                result.Add(line);
                continue;
            }
            string rawKey = content[..colonIdx].Trim();
            string key = rawKey.Trim('"', '\'');
            string rawRest = colonIdx + 1 < content.Length ? content[(colonIdx + 1)..] : "";
            int commentIdx = rawRest.IndexOf('#');
            string? rest = (commentIdx >= 0 ? rawRest[..commentIdx] : rawRest).Trim();
            if (string.IsNullOrEmpty(rest))
            {
                pathStack.Push((key, indent));
                continue;
            }
            string prefix = pathStack.Count > 0
                ? string.Join("/", pathStack.Reverse().Select(s => s.key)) + "/"
                : "";
            result.Add(prefix + key + ": " + rest);
        }
        return result.ToArray();
    }

    internal static ParsedSchemaDefaults ParseSchemaTemplateYaml(string content)
    {
        string[] rawLines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        string[] lines = FlattenYamlLines(rawLines);
        Dictionary<string, int> switchResets = ParseSchemaSwitches(lines);
        return new ParsedSchemaDefaults
        {
            PageSize = ParseNullableInt(lines, "menu/page_size"),
            FullShapeEnabled = switchResets.TryGetValue("full_shape", out int fs) ? fs == 1 : null,
            AsciiPunctEnabled = switchResets.TryGetValue("ascii_punct", out int ap) ? ap == 1 : null,
            EmojiSuggestionEnabled = switchResets.TryGetValue("emoji_suggestion", out int es) ? es == 1 : null,
            ToneDisplayEnabled = switchResets.TryGetValue("tone_display", out int td) ? td == 1 : null,
            SimplificationMode = switchResets.TryGetValue("transcription", out int tr) ? (tr == 1 ? "traditional" : "simplified") : null,
            CandidateLayout = ParseQuotedValue(lines, "style/candidate_list_layout"),
            ShowEmojiComments = ParseBooleanValue(lines, "translator/always_show_comments"),
            EnableUserDict = ParseBooleanValue(lines, "translator/enable_user_dict"),
        };
    }

    private static Dictionary<string, int> ParseSchemaSwitches(string[] lines)
    {
        Dictionary<string, int> result = new();
        bool inSwitches = false;
        string? currentName = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (!inSwitches)
            {
                if (line == "switches:")
                {
                    inSwitches = true;
                }
                continue;
            }
            if (line.StartsWith("  - name:", StringComparison.Ordinal) || line.StartsWith("- name:", StringComparison.Ordinal))
            {
                string? name = ParseSchemaSwitchName(line);
                if (name is not null)
                {
                    currentName = name;
                }
                continue;
            }
            if (currentName is not null && line.TrimStart().StartsWith("reset:", StringComparison.Ordinal))
            {
                string[] parts = line.Split(':', 2);
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int reset))
                {
                    result[currentName] = reset;
                }
                currentName = null;
                continue;
            }
            if (line.Length > 0 && !line.StartsWith(" ", StringComparison.Ordinal) && !line.StartsWith("-", StringComparison.Ordinal))
            {
                inSwitches = false;
            }
        }
        return result;
    }

    private static string? ParseSchemaSwitchName(string line)
    {
        string[] parts = line.Split(':', 2);
        if (parts.Length < 2)
            return null;
        string name = parts[1];
        int commentIdx = name.IndexOf('#');
        if (commentIdx >= 0)
            name = name[..commentIdx];
        name = name.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static ParsedWeaselYaml ParseWeaselYaml(string content, string? buildWeaselContent)
    {
        string[] lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        string? colorScheme = ParseQuotedValue(lines, "style/color_scheme");
        string? colorSchemeDark = ParseQuotedValue(lines, "style/color_scheme_dark");
        IReadOnlyDictionary<string, WeaselSchemeColors> schemeColors = ParseWeaselSchemeColors(buildWeaselContent);
        return new ParsedWeaselYaml
        {
            ColorScheme = colorScheme,
            ColorSchemeDark = colorSchemeDark,
            FontFace = ParseQuotedValue(lines, "style/font_face"),
            FontPoint = ParseNullableInt(lines, "style/font_point"),
            LabelFontFace = ParseQuotedValue(lines, "style/label_font_face"),
            LabelFontPoint = ParseNullableInt(lines, "style/label_font_point"),
            CommentFontFace = ParseQuotedValue(lines, "style/comment_font_face"),
            CommentFontPoint = ParseNullableInt(lines, "style/comment_font_point"),
            ShowEmojiComments = ParseWeaselShowEmojiComments(
                lines,
                colorScheme,
                colorSchemeDark,
                schemeColors,
                ParseNullableInt(lines, "style/comment_font_point")),
            ShowNotification = ParseNullableBool(lines, "show_notifications"),
            NotificationTimeMs = ParseNullableInt(lines, "show_notifications_time"),
            GlobalAscii = ParseNullableBool(lines, "global_ascii"),
            InlinePreedit = ParseNullableBool(lines, "style/inline_preedit"),
            PreeditType = ParseQuotedValue(lines, "style/preedit_type"),
            Fullscreen = ParseNullableBool(lines, "style/fullscreen"),
            VerticalText = ParseNullableBool(lines, "style/vertical_text"),
            VerticalTextLeftToRight = ParseNullableBool(lines, "style/vertical_text_left_to_right"),
            VerticalTextWithWrap = ParseNullableBool(lines, "style/vertical_text_with_wrap"),
            VerticalAutoReverse = ParseNullableBool(lines, "style/vertical_auto_reverse"),
            LabelFormat = ParseQuotedValue(lines, "style/label_format"),
            MarkText = ParseQuotedValue(lines, "style/mark_text"),
            AsciiTipFollowCursor = ParseNullableBool(lines, "style/ascii_tip_follow_cursor"),
            EnhancedPosition = ParseNullableBool(lines, "style/enhanced_position"),
            DisplayTrayIcon = ParseNullableBool(lines, "style/display_tray_icon"),
            AntialiasMode = ParseQuotedValue(lines, "style/antialias_mode"),
            CandidateAbbreviateLength = ParseNullableInt(lines, "style/candidate_abbreviate_length"),
            PagingOnScroll = ParseNullableBool(lines, "style/paging_on_scroll"),
            HoverType = ParseQuotedValue(lines, "style/hover_type"),
            ClickToCapture = ParseNullableBool(lines, "style/click_to_capture"),
            LayoutBaseline = ParseNullableInt(lines, "style/layout/baseline"),
            LayoutLineSpacing = ParseNullableInt(lines, "style/layout/linespacing"),
            LayoutAlignType = ParseQuotedValue(lines, "style/layout/align_type"),
            LayoutMaxHeight = ParseNullableInt(lines, "style/layout/max_height"),
            LayoutMaxWidth = ParseNullableInt(lines, "style/layout/max_width"),
            LayoutMinHeight = ParseNullableInt(lines, "style/layout/min_height"),
            LayoutMinWidth = ParseNullableInt(lines, "style/layout/min_width"),
            LayoutBorderWidth = ParseNullableInt(lines, "style/layout/border_width"),
            LayoutMarginX = ParseNullableInt(lines, "style/layout/margin_x"),
            LayoutMarginY = ParseNullableInt(lines, "style/layout/margin_y"),
            LayoutSpacing = ParseNullableInt(lines, "style/layout/spacing"),
            LayoutCandidateSpacing = ParseNullableInt(lines, "style/layout/candidate_spacing"),
            LayoutHiliteSpacing = ParseNullableInt(lines, "style/layout/hilite_spacing"),
            LayoutHilitePadding = ParseNullableInt(lines, "style/layout/hilite_padding"),
            LayoutHilitePaddingX = ParseNullableInt(lines, "style/layout/hilite_padding_x"),
            LayoutHilitePaddingY = ParseNullableInt(lines, "style/layout/hilite_padding_y"),
            LayoutShadowRadius = ParseNullableInt(lines, "style/layout/shadow_radius"),
            LayoutShadowOffsetX = ParseNullableInt(lines, "style/layout/shadow_offset_x"),
            LayoutShadowOffsetY = ParseNullableInt(lines, "style/layout/shadow_offset_y"),
            LayoutCornerRadius = ParseNullableInt(lines, "style/layout/corner_radius"),
            CandidateLayout = ParseQuotedValue(lines, "style/candidate_list_layout"),
        };
    }

    private static IReadOnlyList<string> ParseDictionaryOrder(string content)
    {
        return content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim().Trim('"'))
            .Select(NormalizeImportedDictionaryId)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line!)
            .ToArray();
    }

    private static string? ExtractInlineBindingValue(string body, string key)
    {
        Match match = Regex.Match(
            body,
            $@"\b{Regex.Escape(key)}\s*:\s*(?:""(?<value>[^""]+)""|(?<value>[^,}}]+))",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static IReadOnlyList<CustomEntry> ParseCustomEntries(string content)
    {
        List<CustomEntry> entries = [];
        bool readingEntries = false;
        foreach (string rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (line == "...")
            {
                readingEntries = true;
                continue;
            }

            if (!readingEntries || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            entries.Add(new CustomEntry
            {
                Text = parts[0],
                Code = parts[1],
                Weight = int.TryParse(parts[2], out int weight) ? weight : 1,
            });
        }

        return entries;
    }

    private static IReadOnlyList<CustomEntry> ParseSimpleTableEntries(string content)
    {
        List<CustomEntry> entries = [];
        foreach (string rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            entries.Add(new CustomEntry
            {
                Text = parts[0],
                Code = parts[1],
                Weight = 1,
            });
        }

        return entries;
    }

    private static string? ParseQuotedValue(IEnumerable<string> lines, string key)
    {
        string[] prefixes =
        [
            $"{key}:",
            $"\"{key}\":",
        ];
        string? raw = lines.FirstOrDefault(line =>
        {
            string trimmed = line.Trim();
            return prefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.Ordinal));
        });
        return raw?.Split(':', 2)[1].Trim().Trim('"');
    }

    private static bool? ParseBooleanValue(IEnumerable<string> lines, string key)
    {
        string? raw = ParseQuotedValue(lines, key);
        if (raw is null)
            return null;
        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        throw new InvalidOperationException(
            $"模板字段 '{key}' 的值 '{raw}' 不是有效的布尔值 (true/false)。请检查模板文件是否正确。");
    }

    private static bool? ParseWeaselShowEmojiComments(
        IReadOnlyList<string> lines,
        string? colorScheme,
        string? colorSchemeDark,
        IReadOnlyDictionary<string, WeaselSchemeColors> schemeColors,
        int? commentFontPoint)
    {
        if (commentFontPoint is <= 1)
        {
            return false;
        }

        string[] schemes = [.. new[] { colorScheme, colorSchemeDark }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (schemes.Length == 0)
        {
            return null;
        }

        bool hiddenForAnyScheme = false;
        foreach (string scheme in schemes)
        {
            string? commentColor = ParseQuotedValue(lines, $"preset_color_schemes/{scheme}/comment_text_color");
            string? hilitedCommentColor = ParseQuotedValue(lines, $"preset_color_schemes/{scheme}/hilited_comment_text_color");
            if (IsCommentColorHidden(commentColor, hilitedCommentColor, scheme, schemeColors))
            {
                hiddenForAnyScheme = true;
                continue;
            }

            return true;
        }

        return hiddenForAnyScheme ? false : true;
    }

    private static bool IsCommentColorHidden(
        string? commentColor,
        string? hilitedCommentColor,
        string scheme,
        IReadOnlyDictionary<string, WeaselSchemeColors> schemeColors)
    {
        if (string.IsNullOrWhiteSpace(commentColor) || string.IsNullOrWhiteSpace(hilitedCommentColor))
        {
            return false;
        }

        string normalizedComment = commentColor.Trim();
        string normalizedHilitedComment = hilitedCommentColor.Trim();
        if ((string.Equals(normalizedComment, "0", StringComparison.Ordinal) || string.Equals(normalizedComment, "0x00000000", StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(normalizedHilitedComment, "0", StringComparison.Ordinal) || string.Equals(normalizedHilitedComment, "0x00000000", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!schemeColors.TryGetValue(scheme, out WeaselSchemeColors? colors) || colors is null)
        {
            return false;
        }

        return string.Equals(normalizedComment, colors.CommentHiddenColor, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(normalizedHilitedComment, colors.HilitedCommentHiddenColor, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyDictionary<string, WeaselSchemeColors> LoadWeaselSchemeColors(string windowsTargetRoot)
    {
        string buildWeaselPath = Path.Combine(RepositoryContext.ExpandPath(windowsTargetRoot), "build", "weasel.yaml");
        if (!File.Exists(buildWeaselPath))
        {
            return new Dictionary<string, WeaselSchemeColors>(StringComparer.OrdinalIgnoreCase);
        }

        return ParseWeaselSchemeColors(RepositoryContext.ReadUtf8(buildWeaselPath));
    }

    private static IReadOnlyDictionary<string, WeaselSchemeColors> ParseWeaselSchemeColors(string? buildWeaselContent)
    {
        Dictionary<string, WeaselSchemeColors> result = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(buildWeaselContent))
        {
            return result;
        }

        string[] lines = buildWeaselContent.Split(["\r\n", "\n"], StringSplitOptions.None);
        string? currentScheme = null;
        string? backColor = null;
        string? candidateBackColor = null;
        string? hilitedBackColor = null;
        string? hilitedCandidateBackColor = null;

        void CommitCurrent()
        {
            if (string.IsNullOrWhiteSpace(currentScheme) || string.IsNullOrWhiteSpace(backColor))
            {
                return;
            }

            string commentHiddenColor = candidateBackColor ?? backColor!;
            string hilitedCommentHiddenColor = hilitedCandidateBackColor ?? hilitedBackColor ?? commentHiddenColor;
            result[currentScheme!] = new WeaselSchemeColors
            {
                CommentHiddenColor = commentHiddenColor,
                HilitedCommentHiddenColor = hilitedCommentHiddenColor,
            };
        }

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (rawLine.StartsWith("  ", StringComparison.Ordinal) &&
                !rawLine.StartsWith("    ", StringComparison.Ordinal) &&
                line.EndsWith(":", StringComparison.Ordinal) &&
                !string.Equals(line, "style:", StringComparison.Ordinal) &&
                !string.Equals(line, "preset_color_schemes:", StringComparison.Ordinal))
            {
                CommitCurrent();
                currentScheme = line[..^1];
                backColor = null;
                candidateBackColor = null;
                hilitedBackColor = null;
                hilitedCandidateBackColor = null;
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentScheme))
            {
                continue;
            }

            if (line.StartsWith("back_color:", StringComparison.Ordinal))
            {
                backColor = line.Split(':', 2)[1].Trim();
                continue;
            }

            if (line.StartsWith("candidate_back_color:", StringComparison.Ordinal))
            {
                candidateBackColor = line.Split(':', 2)[1].Trim();
                continue;
            }

            if (line.StartsWith("hilited_back_color:", StringComparison.Ordinal))
            {
                hilitedBackColor = line.Split(':', 2)[1].Trim();
                continue;
            }

            if (line.StartsWith("hilited_candidate_back_color:", StringComparison.Ordinal))
            {
                hilitedCandidateBackColor = line.Split(':', 2)[1].Trim();
            }
        }

        CommitCurrent();
        return result;
    }

    private static int? ParseNullableInt(IEnumerable<string> lines, string key)
    {
        string? raw = ParseQuotedValue(lines, key);
        return int.TryParse(raw, out int value) ? value : null;
    }

    private static bool? ParseNullableBool(IEnumerable<string> lines, string key)
    {
        string? raw = ParseQuotedValue(lines, key);
        return bool.TryParse(raw, out bool value) ? value : null;
    }

    private static string? ParseSwitchReset(IReadOnlyList<string> lines, int switchIndex)
    {
        string? raw = ParseQuotedValue(lines, $"switches/@{switchIndex}/reset");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (switchIndex == 4)
        {
            return raw == "1" ? "traditional" : "simplified";
        }

        return raw;
    }

    private static IReadOnlyList<string> NormalizeUserFacingDictionaryIds(IReadOnlyList<string> dictionaryIds)
    {
        return dictionaryIds
            .Where(dictionaryId => !string.Equals(dictionaryId, "custom_simple", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeImportedDictionaryId(string value)
    {
        if (string.Equals(value, "dicts/custom_simple", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "custom_simple", StringComparison.OrdinalIgnoreCase))
        {
            return "custom_simple";
        }

        if (value.StartsWith("dicts/rime_mint.", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("dicts/rime_ice.", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "dicts/other_kaomoji", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }
}

internal sealed class GeneratedArtifacts
{
    public string SnapshotId { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> WindowsOutputFiles { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyDictionary<string, byte[]> WindowsBinaryOutputFiles { get; init; } =
        new Dictionary<string, byte[]>();

    public IReadOnlyDictionary<string, string> UserDictionaryFiles { get; init; } =
        new Dictionary<string, string>();
}

internal sealed class ImportedSnapshot
{
    public string SnapshotId { get; init; } = string.Empty;

    public string SnapshotDirectory { get; init; } = string.Empty;

    public ConfigModel ConfigModel { get; init; } = ConfigModel.CreateDefault();

    public IReadOnlyDictionary<string, string> WindowsOutputFiles { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyDictionary<string, byte[]> WindowsBinaryOutputFiles { get; init; } =
        new Dictionary<string, byte[]>();

    public IReadOnlyDictionary<string, string> UserDictionaryFiles { get; init; } =
        new Dictionary<string, string>();
}

internal sealed class ParsedSharedYaml
{
    public int? PageSize { get; init; }

    public string? CandidateLayout { get; init; }

    public bool? ShowEmojiComments { get; init; }

    public string? SimplificationMode { get; init; }

    public bool? FullShapeEnabled { get; init; }

    public bool? AsciiPunctEnabled { get; init; }

    public bool? EmojiSuggestionEnabled { get; init; }

    public bool? ToneDisplayEnabled { get; init; }






    public IReadOnlyList<string> FuzzyRules { get; init; } = [];
}

public sealed class ParsedWeaselYaml
{
    public string? ColorScheme { get; init; }

    public string? ColorSchemeDark { get; init; }

    public string? FontFace { get; init; }

    public int? FontPoint { get; init; }

    public string? LabelFontFace { get; init; }

    public int? LabelFontPoint { get; init; }

    public string? CommentFontFace { get; init; }

    public int? CommentFontPoint { get; init; }

    public bool? ShowEmojiComments { get; init; }

    public bool? ShowNotification { get; init; }

    public int? NotificationTimeMs { get; init; }

    public bool? GlobalAscii { get; init; }

    public bool? InlinePreedit { get; init; }

    public string? PreeditType { get; init; }

    public bool? Fullscreen { get; init; }

    public bool? VerticalText { get; init; }

    public bool? VerticalTextLeftToRight { get; init; }

    public bool? VerticalTextWithWrap { get; init; }

    public bool? VerticalAutoReverse { get; init; }

    public string? LabelFormat { get; init; }

    public string? MarkText { get; init; }

    public bool? AsciiTipFollowCursor { get; init; }

    public bool? EnhancedPosition { get; init; }

    public bool? DisplayTrayIcon { get; init; }

    public string? AntialiasMode { get; init; }

    public int? CandidateAbbreviateLength { get; init; }

    public bool? PagingOnScroll { get; init; }

    public string? HoverType { get; init; }

    public bool? ClickToCapture { get; init; }

    public int? LayoutBaseline { get; init; }

    public int? LayoutLineSpacing { get; init; }

    public string? LayoutAlignType { get; init; }

    public int? LayoutMaxHeight { get; init; }

    public int? LayoutMaxWidth { get; init; }

    public int? LayoutMinHeight { get; init; }

    public int? LayoutMinWidth { get; init; }

    public int? LayoutBorderWidth { get; init; }

    public int? LayoutMarginX { get; init; }

    public int? LayoutMarginY { get; init; }

    public int? LayoutSpacing { get; init; }

    public int? LayoutCandidateSpacing { get; init; }

    public int? LayoutHiliteSpacing { get; init; }

    public int? LayoutHilitePadding { get; init; }

    public int? LayoutHilitePaddingX { get; init; }

    public int? LayoutHilitePaddingY { get; init; }

    public int? LayoutShadowRadius { get; init; }

    public int? LayoutShadowOffsetX { get; init; }

    public int? LayoutShadowOffsetY { get; init; }

    public int? LayoutCornerRadius { get; init; }

    public string? CandidateLayout { get; init; }
}

internal sealed class WeaselSchemeColors
{
    public string CommentHiddenColor { get; init; } = "0x00000000";

    public string HilitedCommentHiddenColor { get; init; } = "0x00000000";
}
