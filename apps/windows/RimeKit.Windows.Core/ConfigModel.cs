using System.Text.Json.Serialization;

namespace RimeKit.Windows.Core;

public sealed class ConfigModel
{
    [JsonPropertyName("config_version")]
    public int ConfigVersion { get; init; } = 1;

    [JsonPropertyName("profile_settings")]
    public ProfileSettings ProfileSettings { get; init; } = new();

    [JsonPropertyName("fuzzy_pinyin_settings")]
    public FuzzyPinyinSettings FuzzyPinyinSettings { get; init; } = new();

    [JsonPropertyName("personalization_settings")]
    public PersonalizationSettings PersonalizationSettings { get; init; } = new();

    [JsonPropertyName("dictionary_settings")]
    public DictionarySettings DictionarySettings { get; init; } = new();

    [JsonPropertyName("model_settings")]
    public ModelSettings ModelSettings { get; init; } = new();

    [JsonPropertyName("sync_settings")]
    public SyncSettings SyncSettings { get; init; } = new();

    [JsonPropertyName("android_settings")]
    public AndroidSettings AndroidSettings { get; init; } = new();

    [JsonPropertyName("windows_settings")]
    public WindowsSettings WindowsSettings { get; init; } = new();

    public static ConfigModel CreateDefault()
    {
        return new ConfigModel();
    }
}

public sealed class ProfileSettings
{
    [JsonPropertyName("enabled_schema_ids")]
    public IReadOnlyList<string> EnabledSchemaIds { get; init; } = ["rime_mint"];

    [JsonPropertyName("windows_default_schema_id")]
    public string WindowsDefaultSchemaId { get; init; } = "rime_mint";

    [JsonPropertyName("android_default_schema_id")]
    public string AndroidDefaultSchemaId { get; init; } = "t9";
}

public sealed class FuzzyPinyinSettings
{
    [JsonPropertyName("preset_id")]
    public string PresetId { get; init; } = string.Empty;

    [JsonPropertyName("target_schema_ids")]
    public IReadOnlyList<string> TargetSchemaIds { get; init; } = ["rime_mint"];
}

public sealed class PersonalizationSettings
{
    [JsonPropertyName("symbol_profile_id")]
    public string SymbolProfileId { get; init; } = "default";

    [JsonPropertyName("preedit_format_mode")]
    public string PreeditFormatMode { get; init; } = "upstream_default";

    [JsonPropertyName("comment_style_variant")]
    public string CommentStyleVariant { get; init; } = "default";
}

public sealed class DictionarySettings
{
    [JsonPropertyName("enabled_dictionary_ids")]
    public IReadOnlyList<string> EnabledDictionaryIds { get; init; } = [];

    [JsonPropertyName("dictionary_order")]
    public IReadOnlyList<string> DictionaryOrder { get; init; } = [];

    [JsonPropertyName("custom_entries")]
    public IReadOnlyList<CustomEntry> CustomEntries { get; init; } = [];
}

public sealed class CustomEntry
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("weight")]
    public int Weight { get; init; } = 1;
}

public sealed class ModelSettings
{
    [JsonPropertyName("enabled_model_ids")]
    public IReadOnlyList<string> EnabledModelIds { get; init; } = [];

    [JsonPropertyName("active_model_id")]
    public string ActiveModelId { get; init; } = string.Empty;

    [JsonPropertyName("model_root")]
    public string ModelRoot { get; init; } = "%APPDATA%\\Rime";

    [JsonPropertyName("model_versions")]
    public IReadOnlyDictionary<string, string> ModelVersions { get; init; } =
        new Dictionary<string, string>();
}

public sealed class SyncSettings
{
    [JsonPropertyName("android_import_root")]
    public string AndroidImportRoot { get; init; } = string.Empty;

    [JsonPropertyName("windows_target_root")]
    public string WindowsTargetRoot { get; init; } = "%APPDATA%\\Rime";

    [JsonPropertyName("export_root")]
    public string ExportRoot { get; init; } = string.Empty;

    [JsonPropertyName("backup_root")]
    public string BackupRoot { get; init; } = string.Empty;

    [JsonPropertyName("snapshot_retention_limit")]
    public int SnapshotRetentionLimit { get; init; } = 20;
}

public sealed class AndroidSettings
{
    [JsonPropertyName("keyboard_layout")]
    public string KeyboardLayout { get; init; } = "9_key";

    [JsonPropertyName("candidate_text_size")]
    public int CandidateTextSize { get; init; } = 22;

    [JsonPropertyName("candidate_view_height")]
    public int CandidateViewHeight { get; init; } = 32;
}

public sealed class WindowsSettings
{
    [JsonPropertyName("dpi_scale_mode")]
    public string DpiScaleMode { get; init; } = "per_monitor_v2";
}
