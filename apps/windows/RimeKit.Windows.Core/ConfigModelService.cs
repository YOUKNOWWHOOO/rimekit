using System.Text.Json;
using System.Text.Encodings.Web;

namespace RimeKit.Windows.Core;

internal sealed class ConfigModelService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly RepositoryContext _repositoryContext;

    public ConfigModelService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    public ConfigModel Load(string? configPath, bool allowDefault)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            if (!allowDefault)
            {
                throw new InvalidOperationException("当前命令要求显式提供配置模型文件。");
            }

            return ConfigModel.CreateDefault();
        }

        string fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"未找到配置模型文件：{fullPath}");
        }

        ConfigModel? model = JsonSerializer.Deserialize<ConfigModel>(RepositoryContext.ReadUtf8(fullPath));
        return model ?? throw new InvalidOperationException("无法解析配置模型文件。");
    }

    public string Save(string configPath, ConfigModel model)
    {
        string fullPath = Path.GetFullPath(configPath);
        string payload = JsonSerializer.Serialize(model, JsonOptions);
        RepositoryContext.WriteUtf8(fullPath, payload);
        return fullPath;
    }

    public IReadOnlyList<DiagnosticFinding> Validate(
        ConfigModel model,
        Func<string, string, string?, string?, string?, IReadOnlyList<string>?, DiagnosticFinding> createFinding)
    {
        List<DiagnosticFinding> findings = [];

        if (model.ConfigVersion != 1)
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_VERSION_UNSUPPORTED",
                $"配置模型版本不受支持：{model.ConfigVersion}",
                null,
                null,
                null,
                null));
        }

        if (model.ProfileSettings.EnabledSchemaIds.Count == 0 && model.ProfileSettings.WindowsDefaultSchemaId.Length > 0)
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "启用了默认方案但 enabled_schema_ids 为空，状态不一致。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        foreach (string schemaId in model.ProfileSettings.EnabledSchemaIds)
        {
            if (string.IsNullOrWhiteSpace(schemaId))
            {
                findings.Add(createFinding(
                    "CONFIG_MODEL_SCHEMA_INVALID",
                    "启用的方案列表中存在空白方案标识。",
                    null,
                    ConflictScopes.ConfigModel,
                    null,
                    null));
            }
        }

        if (!model.ProfileSettings.EnabledSchemaIds.Contains(model.ProfileSettings.WindowsDefaultSchemaId, StringComparer.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(model.ProfileSettings.WindowsDefaultSchemaId))
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "windows_default_schema_id 必须属于 enabled_schema_ids。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        if (_repositoryContext.SchemaIds.Contains("t9") &&
            !string.Equals(model.ProfileSettings.AndroidDefaultSchemaId, "t9", StringComparison.Ordinal))
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "Android 默认方案必须固定为 t9（当前硬编码约束；后续若扩展 Android 正式方案支持则需同步修订）。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        foreach (string dictionaryId in model.DictionarySettings.EnabledDictionaryIds)
        {
            if (!_repositoryContext.DictionaryIds.Contains(dictionaryId))
            {
                findings.Add(createFinding(
                    "RESOURCE_MANIFEST_INVALID",
                    $"启用的词库未在正式资源清单中定义：{dictionaryId}",
                    null,
                    ConflictScopes.FormalResource,
                    null,
                    null));
            }
        }

        HashSet<string> enabledDictionarySet = new(model.DictionarySettings.EnabledDictionaryIds, StringComparer.OrdinalIgnoreCase);
        HashSet<string> orderedDictionarySet = new(model.DictionarySettings.DictionaryOrder, StringComparer.OrdinalIgnoreCase);
        if (!enabledDictionarySet.SetEquals(orderedDictionarySet))
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "dictionary_order 必须完整覆盖 enabled_dictionary_ids，且不得包含未启用词库。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        HashSet<string> customEntryKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (CustomEntry entry in model.DictionarySettings.CustomEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Text) || string.IsNullOrWhiteSpace(entry.Code))
            {
                findings.Add(createFinding(
                    "CONFIG_MODEL_SCHEMA_INVALID",
                    $"自定义词条文本和编码都不允许为空：{entry.Text} / {entry.Code}",
                    null,
                    ConflictScopes.ConfigModel,
                    null,
                    null));
            }

            if (entry.Weight <= 0)
            {
                findings.Add(createFinding(
                    "CONFIG_MODEL_SCHEMA_INVALID",
                    $"自定义词条权重必须为正整数：{entry.Text} / {entry.Code}",
                    null,
                    ConflictScopes.ConfigModel,
                    null,
                    null));
            }

            string compositeKey = $"{entry.Text}\t{entry.Code}";
            if (!customEntryKeys.Add(compositeKey))
            {
                findings.Add(createFinding(
                    "CONFIG_MODEL_SCHEMA_INVALID",
                    $"custom_entries 中存在重复词条：{entry.Text} / {entry.Code}",
                    null,
                    ConflictScopes.ConfigModel,
                    null,
                    null));
            }
        }

        if (model.DictionarySettings.CustomEntries.Count > 0 &&
            !model.DictionarySettings.EnabledDictionaryIds.Contains("custom_simple", StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "自定义词条已添加但 custom_simple 未被启用——词条可能不会生效。建议启用 custom_simple 资源。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        if (model.FuzzyPinyinSettings.TargetSchemaIds.Count == 0)
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "fuzzy_pinyin_settings.target_schema_ids 不得为空。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        if (!_repositoryContext.SymbolProfileIds.Contains(model.PersonalizationSettings.SymbolProfileId))
        {
            findings.Add(createFinding(
                "FEATURE_PRESET_INVALID",
                $"symbol_profile_id 未在正式预设清单中定义：{model.PersonalizationSettings.SymbolProfileId}",
                null,
                ConflictScopes.FormalResource,
                null,
                null));
        }

        if (!_repositoryContext.PreeditProfileIds.Contains(model.PersonalizationSettings.PreeditFormatMode))
        {
            findings.Add(createFinding(
                "FEATURE_PRESET_INVALID",
                $"preedit_format_mode 未在正式预设清单中定义：{model.PersonalizationSettings.PreeditFormatMode}",
                null,
                ConflictScopes.FormalResource,
                null,
                null));
        }

        if (!string.IsNullOrWhiteSpace(model.ModelSettings.ActiveModelId) &&
            !model.ModelSettings.EnabledModelIds.Contains(model.ModelSettings.ActiveModelId, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "active_model_id 非空时必须属于 enabled_model_ids。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        foreach (string modelId in model.ModelSettings.EnabledModelIds)
        {
            if (!_repositoryContext.ModelIds.Contains(modelId))
            {
                findings.Add(createFinding(
                    "RESOURCE_MANIFEST_INVALID",
                    $"启用的模型未在正式资源清单中定义：{modelId}",
                    null,
                    ConflictScopes.FormalResource,
                    null,
                    null));
            }
        }

        if (model.SyncSettings.SnapshotRetentionLimit <= 0)
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "snapshot_retention_limit 必须大于 0。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        if (!string.Equals(model.AndroidSettings.KeyboardLayout, "9_key", StringComparison.Ordinal))
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "Android 键盘布局必须固定为 9_key。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        if (model.AndroidSettings.CandidateTextSize <= 0 || model.AndroidSettings.CandidateViewHeight <= 0)
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "Android 候选字号和候选区高度都必须为正整数。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        if (!string.Equals(model.WindowsSettings.DpiScaleMode, "per_monitor_v2", StringComparison.Ordinal))
        {
            findings.Add(createFinding(
                "CONFIG_MODEL_SCHEMA_INVALID",
                "Windows DPI 模式必须固定为 per_monitor_v2。",
                null,
                ConflictScopes.ConfigModel,
                null,
                null));
        }

        return findings;
    }
}
