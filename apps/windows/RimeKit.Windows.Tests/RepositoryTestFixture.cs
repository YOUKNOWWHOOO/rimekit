using System.Text.Json;
using RimeKit.Windows.Core;
using RimeKit.Windows.Core.Utilities;

namespace RimeKit.Windows.Tests;

internal sealed class RepositoryTestFixture : IDisposable
{
    private readonly string _workspaceRoot;

    public RepositoryTestFixture()
    {
        string sourceRepositoryRoot = ResolveSourceRepositoryRoot();
        Environment.SetEnvironmentVariable(
            "RIMEKIT_WEASEL_ACTIVATOR_PATH",
            Path.Combine(
                sourceRepositoryRoot,
                "apps",
                "windows",
                "RimeKit.Windows.Activator",
                "bin",
                "Debug",
                "net10.0-windows",
                "RimeKit.Windows.Activator.exe"));
        _workspaceRoot = Path.Combine(
            sourceRepositoryRoot,
            "workspace",
            "windows-test-fixtures",
            Guid.NewGuid().ToString("N"));

        Environment.SetEnvironmentVariable("RIMEKIT_SOURCE_REPOSITORY_ROOT", _workspaceRoot);

        CopyDirectory(Path.Combine(sourceRepositoryRoot, "shared"), Path.Combine(_workspaceRoot, "shared"));

        string fakeDeployerPath = Path.Combine(_workspaceRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        File.WriteAllText(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

        string schemaContent = "schema_id: rime_mint\nswitches:\n  - name: ascii_mode\n    reset: 0\n  - name: emoji_suggestion\n    reset: 1\n  - name: full_shape\n    reset: 0\n  - name: tone_display\n    reset: 0\n  - name: transcription\n    reset: 0\n  - name: ascii_punct\n    reset: 0\nmenu:\n  page_size: 6\ntranslator:\n  dictionary: rime_mint\n  enable_user_dict: true\n";
        string appDataRime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");
        Directory.CreateDirectory(appDataRime);
        FileHelper.WriteTextWithVerification(Path.Combine(appDataRime, "rime_mint.schema.yaml"), schemaContent);
        FileHelper.WriteTextWithVerification(Path.Combine(appDataRime, "weasel.yaml"), "style/font_point: 12\nshow_notifications: true\n");
        FileHelper.WriteTextWithVerification(Path.Combine(appDataRime, "default.custom.yaml"), "patch:");
        FileHelper.WriteTextWithVerification(Path.Combine(appDataRime, "rime_mint.custom.yaml"), "patch:");
        FileHelper.WriteTextWithVerification(Path.Combine(appDataRime, "rime_mint.dict.yaml"), "");
    }

    public string RepositoryRoot => _workspaceRoot;

    public RepositoryContext CreateRepositoryContext()
    {
        return new RepositoryContext(_workspaceRoot);
    }

    public ArtifactService CreateArtifactService()
    {
        return new ArtifactService(CreateRepositoryContext());
    }

    public static ConfigModel CreateModelFromCase(JsonElement caseElement, string repositoryRoot)
    {
        ConfigModel model = ConfigModel.CreateDefault();
        if (caseElement.ValueKind != JsonValueKind.Object ||
            !caseElement.TryGetProperty("input_config", out JsonElement inputConfig) ||
            !inputConfig.TryGetProperty("overrides", out JsonElement overrides))
        {
            return new ConfigModel
            {
                ConfigVersion = model.ConfigVersion,
                ProfileSettings = model.ProfileSettings,                FuzzyPinyinSettings = model.FuzzyPinyinSettings,
                PersonalizationSettings = model.PersonalizationSettings,
                DictionarySettings = model.DictionarySettings,
                ModelSettings = model.ModelSettings,
                SyncSettings = new SyncSettings
                {
                    AndroidImportRoot = Path.Combine(repositoryRoot, "workspace", "android-import"),
                    WindowsTargetRoot = Path.Combine(repositoryRoot, "workspace", "windows-target"),
                    ExportRoot = Path.Combine(repositoryRoot, "exports"),
                    BackupRoot = Path.Combine(repositoryRoot, "backups"),
                    SnapshotRetentionLimit = 20,
                },
                AndroidSettings = model.AndroidSettings,
                WindowsSettings = model.WindowsSettings,            };
        }

        ProfileSettings profile = model.ProfileSettings;        FuzzyPinyinSettings fuzzy = model.FuzzyPinyinSettings;
        PersonalizationSettings personalization = model.PersonalizationSettings;
        DictionarySettings dictionaries = model.DictionarySettings;
        ModelSettings models = model.ModelSettings;
        AndroidSettings android = model.AndroidSettings;
        WindowsSettings windows = model.WindowsSettings;

        if (overrides.TryGetProperty("profile_settings", out JsonElement profileOverrides))
        {
            profile = new ProfileSettings
            {
                EnabledSchemaIds = ReadStringList(profileOverrides, "enabled_schema_ids") ?? profile.EnabledSchemaIds,
                WindowsDefaultSchemaId = ReadString(profileOverrides, "windows_default_schema_id") ?? profile.WindowsDefaultSchemaId,
                AndroidDefaultSchemaId = ReadString(profileOverrides, "android_default_schema_id") ?? profile.AndroidDefaultSchemaId,
            };
        }

        if (overrides.TryGetProperty("fuzzy_pinyin_settings", out JsonElement fuzzyOverrides))
        {
            fuzzy = new FuzzyPinyinSettings { PresetId = ReadString(fuzzyOverrides, "preset_id") ?? fuzzy.PresetId, TargetSchemaIds = ReadStringList(fuzzyOverrides, "target_schema_ids") ?? fuzzy.TargetSchemaIds };
        }

        if (overrides.TryGetProperty("personalization_settings", out JsonElement personalizationOverrides))
        {
            personalization = new PersonalizationSettings { SymbolProfileId = ReadString(personalizationOverrides, "symbol_profile_id") ?? personalization.SymbolProfileId, PreeditFormatMode = ReadString(personalizationOverrides, "preedit_format_mode") ?? personalization.PreeditFormatMode };
        }

        if (overrides.TryGetProperty("dictionary_settings", out JsonElement dictionaryOverrides))
        {
            dictionaries = new DictionarySettings
            {
                EnabledDictionaryIds = ReadStringList(dictionaryOverrides, "enabled_dictionary_ids") ?? dictionaries.EnabledDictionaryIds,
                DictionaryOrder = ReadStringList(dictionaryOverrides, "dictionary_order") ?? dictionaries.DictionaryOrder,
                CustomEntries = ReadCustomEntries(dictionaryOverrides, "custom_entries") ?? dictionaries.CustomEntries,
            };
        }

        if (overrides.TryGetProperty("model_settings", out JsonElement modelOverrides))
        {
            models = new ModelSettings { EnabledModelIds = ReadStringList(modelOverrides, "enabled_model_ids") ?? models.EnabledModelIds, ActiveModelId = ReadString(modelOverrides, "active_model_id") ?? models.ActiveModelId, ModelRoot = ReadString(modelOverrides, "model_root") ?? models.ModelRoot, ModelVersions = ReadStringDictionary(modelOverrides, "model_versions") ?? models.ModelVersions };
        }

        if (overrides.TryGetProperty("android_settings", out JsonElement androidOverrides))
        {
            android = new AndroidSettings
            {
                KeyboardLayout = ReadString(androidOverrides, "keyboard_layout") ?? android.KeyboardLayout,
                CandidateTextSize = ReadInt(androidOverrides, "candidate_text_size") ?? android.CandidateTextSize,
                CandidateViewHeight = ReadInt(androidOverrides, "candidate_view_height") ?? android.CandidateViewHeight,
            };
        }

        if (overrides.TryGetProperty("windows_settings", out JsonElement windowsOverrides))
        {
            windows = new WindowsSettings { DpiScaleMode = ReadString(windowsOverrides, "dpi_scale_mode") ?? windows.DpiScaleMode };
        }

        return new ConfigModel
        {
            ConfigVersion = model.ConfigVersion,
            ProfileSettings = profile,            FuzzyPinyinSettings = fuzzy,
            PersonalizationSettings = personalization,
            DictionarySettings = dictionaries,
            ModelSettings = models,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = Path.Combine(repositoryRoot, "workspace", "android-import"),
                WindowsTargetRoot = Path.Combine(repositoryRoot, "workspace", "windows-target"),
                ExportRoot = Path.Combine(repositoryRoot, "exports"),
                BackupRoot = Path.Combine(repositoryRoot, "backups"),
                SnapshotRetentionLimit = 20,
            },
            AndroidSettings = android,
            WindowsSettings = windows,
        };
    }

    public string ResolveConfigModelPath()
    {
        string dir = Path.Combine(_workspaceRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(dir);
        string configPath = Path.Combine(dir, "current_config_model.json");
        ConfigModel def = ConfigModel.CreateDefault();
        ConfigModel model = new()
        {
            ConfigVersion = def.ConfigVersion,
            ProfileSettings = def.ProfileSettings,
            FuzzyPinyinSettings = def.FuzzyPinyinSettings,
            PersonalizationSettings = def.PersonalizationSettings,
            DictionarySettings = def.DictionarySettings,
            ModelSettings = def.ModelSettings,
            SyncSettings = new SyncSettings
            {
                WindowsTargetRoot = Path.Combine(_workspaceRoot, "workspace", "windows-target"),
                SnapshotRetentionLimit = def.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = def.AndroidSettings,
            WindowsSettings = def.WindowsSettings,
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(model));
        return configPath;
    }

    public string GetTargetRoot(string configPath)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
        string root = doc.RootElement
            .GetProperty("sync_settings")
            .GetProperty("windows_target_root")
            .GetString() ?? string.Empty;
        return Environment.ExpandEnvironmentVariables(root);
    }

    public void EnsureRimeMintInstalled(WindowsWorkflowService workflowService, string configPath)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
        string root = doc.RootElement
            .GetProperty("sync_settings")
            .GetProperty("windows_target_root")
            .GetString() ?? string.Empty;
        string targetRoot = Environment.ExpandEnvironmentVariables(root);
        Directory.CreateDirectory(targetRoot);
    }

    public void EnsureWanxiangModelInstalled(WindowsWorkflowService workflowService, string configPath)
    {
        ConfigModel model = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(configPath))!;
        ConfigModel updated = new()
        {
            ConfigVersion = model.ConfigVersion,
            ProfileSettings = model.ProfileSettings,            FuzzyPinyinSettings = model.FuzzyPinyinSettings,
            PersonalizationSettings = model.PersonalizationSettings,
            DictionarySettings = model.DictionarySettings,
            ModelSettings = new ModelSettings
            {
                EnabledModelIds = ["wanxiang_lts_zh_hans"],
                ActiveModelId = "wanxiang_lts_zh_hans",
                ModelRoot = model.ModelSettings.ModelRoot,
                ModelVersions = model.ModelSettings.ModelVersions,
            },
            SyncSettings = model.SyncSettings,
            AndroidSettings = model.AndroidSettings,
            WindowsSettings = model.WindowsSettings,        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(updated));

        string modelInstallRoot = Path.Combine(_workspaceRoot, "workspace", "windows-installed-models", "wanxiang_lts_zh_hans");
        Directory.CreateDirectory(modelInstallRoot);
        File.WriteAllText(Path.Combine(modelInstallRoot, "wanxiang-lts-zh-hans.gram"), "dummy-gram");

        string stateDir = Path.Combine(_workspaceRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(stateDir);
        string statePath = Path.Combine(stateDir, "installed_resources.json");
        string stateJson = JsonSerializer.Serialize(new object[]
        {
            new
            {
                ResourceId = "wanxiang_lts_zh_hans",
                ResourceKind = "model",
                InstallPath = modelInstallRoot,
                InstalledVersion = "lts",
                SourceClass = "official_current",
            },
        });
        File.WriteAllText(statePath, stateJson);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_workspaceRoot))
        {
            return;
        }

        FileHelper.DeleteDirectoryWithBackoff(_workspaceRoot, maxRetries: 10, baseDelayMs: 200, maxDelayMs: 4000);
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

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string file in Directory.GetFiles(sourceDirectory))
        {
            FileHelper.CopyFileWithBackoff(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            ? property.GetBoolean()
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }

    private static IReadOnlyList<string>? ReadStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return property.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string>? ReadStringDictionary(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return property.EnumerateObject()
            .ToDictionary(item => item.Name, item => item.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CustomEntry>? ReadCustomEntries(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return property.EnumerateArray()
            .Select(item => new CustomEntry
            {
                Text = item.GetProperty("text").GetString() ?? string.Empty,
                Code = item.GetProperty("code").GetString() ?? string.Empty,
                Weight = item.GetProperty("weight").GetInt32(),
            })
            .ToArray();
    }
}
