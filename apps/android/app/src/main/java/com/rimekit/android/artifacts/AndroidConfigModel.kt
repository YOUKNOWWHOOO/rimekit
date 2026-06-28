package com.rimekit.android.artifacts

import org.json.JSONArray
import org.json.JSONObject

/**
 * Android 当前使用的正式配置模型视图。
 *
 * 当前页面层仍消费扁平字段，但持久化与同步快照统一落盘为冻结基线要求的嵌套正式结构。
 */
data class AndroidConfigModel(
    val configVersion: Int = 1,
    val enabledSchemaIds: List<String> = listOf("rime_mint", "t9"),
    val windowsDefaultSchemaId: String = "rime_mint",
    val androidDefaultSchemaId: String = "t9",
    val candidatePageSize: Int? = null,
    val candidateLayout: String = "vertical",
    val showEmojiComments: Boolean = true,
    val simplificationMode: String = "simplified",
    val fullShapeEnabled: Boolean = false,
    val asciiPunctEnabled: Boolean = false,
    val emojiSuggestionEnabled: Boolean = true,
    val toneDisplayEnabled: Boolean = true,
    val fuzzyEnabled: Boolean = false,
    val fuzzyPresetId: String = "",
    val fuzzyTargetSchemaIds: List<String> = listOf("rime_mint"),
    val fuzzyAdditionalRules: List<String> = emptyList(),
    val symbolProfileId: String = "default",
    val preeditFormatMode: String = "upstream_default",
    val customPhraseMode: String = "simple_code_only",
    val commentStyleVariant: String = "default",
    val enabledDictionaryIds: List<String> = listOf(
        "moetype",
        "sogou_network_popular_words",
        "custom_simple",
    ),
    val dictionaryOrder: List<String> = listOf(
        "moetype",
        "sogou_network_popular_words",
        "custom_simple",
    ),
    val customEntries: List<AndroidCustomEntry> = emptyList(),
    val enabledModelIds: List<String> = emptyList(),
    val activeModelId: String = "",
    val modelRoot: String = "",
    val modelVersions: Map<String, String> = emptyMap(),
    val contextualSuggestionsEnabled: Boolean = false,
    val collocationMaxLength: Int = 0,
    val collocationMinLength: Int = 0,
    val maxHomophones: Int = 0,
    val maxHomographs: Int = 0,
    val sharedSyncRoot: String = "",
    val androidImportRoot: String = "",
    val windowsTargetRoot: String = "%APPDATA%\\Rime",
    val exportRoot: String = "",
    val backupRoot: String = "",
    val snapshotRetentionLimit: Int = 20,
    val keyboardLayout: String = "9_key",
    val candidateTextSize: Int = 22,
    val candidateViewHeight: Int = 32,
    val windowsFontFace: String = "",
    val windowsFontPoint: Int = 0,
    val windowsDpiScaleMode: String = "per_monitor_v2",
    val windowsShowNotification: Boolean = false,
) {
    fun toFormalJson(): JSONObject {
        return JSONObject()
            .put("config_version", configVersion)
            .put(
                "profile_settings",
                JSONObject()
                    .put("enabled_schema_ids", enabledSchemaIds.toStringJsonArray())
                    .put("windows_default_schema_id", windowsDefaultSchemaId)
                    .put("android_default_schema_id", androidDefaultSchemaId),
            )
            .put(
                "candidate_settings",
                JSONObject()
                    .apply {
                        if (candidatePageSize != null) {
                            put("page_size", candidatePageSize)
                        }
                    }
                    .put("layout", candidateLayout)
                    .put("show_emoji_comments", showEmojiComments),
            )
            .put(
                "behavior_settings",
                JSONObject()
                    .put("simplification_mode", simplificationMode)
                    .put("full_shape_enabled", fullShapeEnabled)
                    .put("ascii_punct_enabled", asciiPunctEnabled)
                    .put("emoji_suggestion_enabled", emojiSuggestionEnabled)
                    .put("tone_display_enabled", toneDisplayEnabled),
            )
            .put(
                "fuzzy_pinyin_settings",
                JSONObject()
                    .put("enabled", fuzzyEnabled)
                    .put("preset_id", fuzzyPresetId)
                    .put("target_schema_ids", fuzzyTargetSchemaIds.toStringJsonArray())
                    .put("additional_rules", fuzzyAdditionalRules.toStringJsonArray()),
            )
            .put(
                "personalization_settings",
                JSONObject()
                    .put("symbol_profile_id", symbolProfileId)
                    .put("preedit_format_mode", preeditFormatMode)
                    .put("custom_phrase_mode", customPhraseMode)
                    .put("comment_style_variant", commentStyleVariant),
            )
            .put(
                "dictionary_settings",
                JSONObject()
                    .put("enabled_dictionary_ids", enabledDictionaryIds.toStringJsonArray())
                    .put("dictionary_order", dictionaryOrder.toStringJsonArray())
                    .put("custom_entries", customEntries.toCustomEntryJsonArray()),
            )
            .put(
                "model_settings",
                JSONObject()
                    .put("enabled_model_ids", enabledModelIds.toStringJsonArray())
                    .put("active_model_id", activeModelId)
                    .put("model_root", modelRoot)
                    .put("model_versions", JSONObject(modelVersions))
                    .put("contextual_suggestions_enabled", contextualSuggestionsEnabled)
                    .put("collocation_max_length", collocationMaxLength)
                    .put("collocation_min_length", collocationMinLength)
                    .put("max_homophones", maxHomophones)
                    .put("max_homographs", maxHomographs),
            )
            .put(
                "sync_settings",
                JSONObject()
                    .put("android_import_root", androidImportRoot)
                    .put("windows_target_root", windowsTargetRoot)
                    .put("export_root", exportRoot)
                    .put("backup_root", backupRoot)
                    .put("snapshot_retention_limit", snapshotRetentionLimit),
            )
            .put(
                "android_settings",
                JSONObject()
                    .put("keyboard_layout", keyboardLayout)
                    .put("candidate_text_size", candidateTextSize)
                    .put("candidate_view_height", candidateViewHeight),
            )
            .put(
                "windows_settings",
                JSONObject()
                    .put("font_face", windowsFontFace)
                    .put("font_point", windowsFontPoint)
                    .put("dpi_scale_mode", windowsDpiScaleMode)
                    .put("show_notification", windowsShowNotification),
            )
    }

    companion object {
        fun createDefault(): AndroidConfigModel = AndroidConfigModel()

        fun fromStoredJson(json: JSONObject): AndroidConfigModel {
            if (json.has("profile_settings")) {
                return fromFormalJson(json)
            }

            return AndroidConfigModel(
                configVersion = json.optInt("config_version", 1),
                enabledSchemaIds = json.optJSONArray("enabled_schema_ids").toStringList(listOf("rime_mint", "t9")),
                windowsDefaultSchemaId = json.optString("windows_default_schema_id", "rime_mint"),
                androidDefaultSchemaId = json.optString("android_default_schema_id", "t9"),
                candidatePageSize = json.optIntOrNull("candidate_page_size"),
                candidateLayout = json.optString("candidate_layout", "vertical"),
                showEmojiComments = json.optBoolean("show_emoji_comments", true),
                simplificationMode = json.optString("simplification_mode", "simplified"),
                fullShapeEnabled = json.optBoolean("full_shape_enabled", false),
                asciiPunctEnabled = json.optBoolean("ascii_punct_enabled", false),
                emojiSuggestionEnabled = json.optBoolean("emoji_suggestion_enabled", true),
                toneDisplayEnabled = json.optBoolean("tone_display_enabled", true),
                fuzzyEnabled = json.optBoolean("fuzzy_enabled", false),
                fuzzyPresetId = json.optString("fuzzy_preset_id", ""),
                fuzzyTargetSchemaIds = json.optJSONArray("fuzzy_target_schema_ids").toStringList(listOf("rime_mint")),
                fuzzyAdditionalRules = json.optJSONArray("fuzzy_additional_rules").toStringList(emptyList()),
                symbolProfileId = json.optString("symbol_profile_id", "default"),
                preeditFormatMode = json.optString("preedit_format_mode", "upstream_default"),
                customPhraseMode = json.optString("custom_phrase_mode", "simple_code_only"),
                commentStyleVariant = json.optString("comment_style_variant", "default"),
                enabledDictionaryIds = json.optJSONArray("enabled_dictionary_ids").toStringList(
                    listOf("moetype", "sogou_network_popular_words", "custom_simple"),
                ),
                dictionaryOrder = json.optJSONArray("dictionary_order").toStringList(
                    listOf("moetype", "sogou_network_popular_words", "custom_simple"),
                ),
                customEntries = json.optJSONArray("custom_entries").toCustomEntries(),
                enabledModelIds = json.optJSONArray("enabled_model_ids").toStringList(emptyList()),
                activeModelId = json.optString("active_model_id", ""),
                modelRoot = json.optString("model_root", ""),
                modelVersions = json.optJSONObject("model_versions").toStringMap(),
                contextualSuggestionsEnabled = json.optBoolean("contextual_suggestions_enabled", false),
                collocationMaxLength = json.optInt("collocation_max_length", 0),
                collocationMinLength = json.optInt("collocation_min_length", 0),
                maxHomophones = json.optInt("max_homophones", 0),
                maxHomographs = json.optInt("max_homographs", 0),
                sharedSyncRoot = json.optString("shared_sync_root", ""),
                androidImportRoot = json.optString("android_import_root", ""),
                windowsTargetRoot = json.optString("windows_target_root", "%APPDATA%\\Rime"),
                exportRoot = json.optString("export_root", ""),
                backupRoot = json.optString("backup_root", ""),
                snapshotRetentionLimit = json.optInt("snapshot_retention_limit", 20),
                keyboardLayout = json.optString("keyboard_layout", "9_key"),
                candidateTextSize = json.optInt("candidate_text_size", 22),
                candidateViewHeight = json.optInt("candidate_view_height", 32),
                windowsFontFace = json.optString("windows_font_face", ""),
                windowsFontPoint = json.optInt("windows_font_point", 0),
                windowsDpiScaleMode = json.optString("windows_dpi_scale_mode", "per_monitor_v2"),
                windowsShowNotification = json.optBoolean("windows_show_notification", false),
            )
        }

        private fun fromFormalJson(json: JSONObject): AndroidConfigModel {
            val profile = json.optJSONObject("profile_settings") ?: JSONObject()
            val candidate = json.optJSONObject("candidate_settings") ?: JSONObject()
            val behavior = json.optJSONObject("behavior_settings") ?: JSONObject()
            val fuzzy = json.optJSONObject("fuzzy_pinyin_settings") ?: JSONObject()
            val personalization = json.optJSONObject("personalization_settings") ?: JSONObject()
            val dictionaries = json.optJSONObject("dictionary_settings") ?: JSONObject()
            val model = json.optJSONObject("model_settings") ?: JSONObject()
            val sync = json.optJSONObject("sync_settings") ?: JSONObject()
            val android = json.optJSONObject("android_settings") ?: JSONObject()
            val windows = json.optJSONObject("windows_settings") ?: JSONObject()

            return AndroidConfigModel(
                configVersion = json.optInt("config_version", 1),
                enabledSchemaIds = profile.optJSONArray("enabled_schema_ids").toStringList(listOf("rime_mint", "t9")),
                windowsDefaultSchemaId = profile.optString("windows_default_schema_id", "rime_mint"),
                androidDefaultSchemaId = profile.optString("android_default_schema_id", "t9"),
                candidatePageSize = candidate.optIntOrNull("page_size"),
                candidateLayout = candidate.optString("layout", "vertical"),
                showEmojiComments = candidate.optBoolean("show_emoji_comments", true),
                simplificationMode = behavior.optString("simplification_mode", "simplified"),
                fullShapeEnabled = behavior.optBoolean("full_shape_enabled", false),
                asciiPunctEnabled = behavior.optBoolean("ascii_punct_enabled", false),
                emojiSuggestionEnabled = behavior.optBoolean("emoji_suggestion_enabled", true),
                toneDisplayEnabled = behavior.optBoolean("tone_display_enabled", true),
                fuzzyEnabled = fuzzy.optBoolean("enabled", false),
                fuzzyPresetId = fuzzy.optString("preset_id", ""),
                fuzzyTargetSchemaIds = fuzzy.optJSONArray("target_schema_ids").toStringList(listOf("rime_mint")),
                fuzzyAdditionalRules = fuzzy.optJSONArray("additional_rules").toStringList(emptyList()),
                symbolProfileId = personalization.optString("symbol_profile_id", "default"),
                preeditFormatMode = personalization.optString("preedit_format_mode", "upstream_default"),
                customPhraseMode = personalization.optString("custom_phrase_mode", "simple_code_only"),
                commentStyleVariant = personalization.optString("comment_style_variant", "default"),
                enabledDictionaryIds = dictionaries.optJSONArray("enabled_dictionary_ids").toStringList(
                    listOf("moetype", "sogou_network_popular_words", "custom_simple"),
                ),
                dictionaryOrder = dictionaries.optJSONArray("dictionary_order").toStringList(
                    listOf("moetype", "sogou_network_popular_words", "custom_simple"),
                ),
                customEntries = dictionaries.optJSONArray("custom_entries").toCustomEntries(),
                enabledModelIds = model.optJSONArray("enabled_model_ids").toStringList(emptyList()),
                activeModelId = model.optString("active_model_id", ""),
                modelRoot = model.optString("model_root", ""),
                modelVersions = model.optJSONObject("model_versions").toStringMap(),
                contextualSuggestionsEnabled = model.optBoolean("contextual_suggestions_enabled", false),
                collocationMaxLength = model.optInt("collocation_max_length", 0),
                collocationMinLength = model.optInt("collocation_min_length", 0),
                maxHomophones = model.optInt("max_homophones", 0),
                maxHomographs = model.optInt("max_homographs", 0),
                sharedSyncRoot = sync.optString("shared_sync_root", ""),
                androidImportRoot = sync.optString("android_import_root", ""),
                windowsTargetRoot = sync.optString("windows_target_root", "%APPDATA%\\Rime"),
                exportRoot = sync.optString("export_root", ""),
                backupRoot = sync.optString("backup_root", ""),
                snapshotRetentionLimit = sync.optInt("snapshot_retention_limit", 20),
                keyboardLayout = android.optString("keyboard_layout", "9_key"),
                candidateTextSize = android.optInt("candidate_text_size", 22),
                candidateViewHeight = android.optInt("candidate_view_height", 32),
                windowsFontFace = windows.optString("font_face", ""),
                windowsFontPoint = windows.optInt("font_point", 0),
                windowsDpiScaleMode = windows.optString("dpi_scale_mode", "per_monitor_v2"),
                windowsShowNotification = windows.optBoolean("show_notification", false),
            )
        }

        private fun JSONObject.optIntOrNull(key: String): Int? {
            return if (has(key) && !isNull(key)) {
                optInt(key)
            } else {
                null
            }
        }

        private fun JSONArray?.toStringList(defaultValue: List<String>): List<String> {
            if (this == null) {
                return defaultValue
            }
            return buildList {
                for (index in 0 until length()) {
                    add(optString(index))
                }
            }
        }

        private fun JSONArray?.toCustomEntries(): List<AndroidCustomEntry> {
            if (this == null) {
                return emptyList()
            }
            return buildList {
                for (index in 0 until length()) {
                    val item = optJSONObject(index) ?: continue
                    add(
                        AndroidCustomEntry(
                            text = item.optString("text"),
                            code = item.optString("code"),
                            weight = item.optInt("weight", 1),
                        ),
                    )
                }
            }
        }

        private fun JSONObject?.toStringMap(): Map<String, String> {
            if (this == null) {
                return emptyMap()
            }
            return keys().asSequence().associateWith { key -> optString(key) }
        }
    }
}

data class AndroidCustomEntry(
    val text: String,
    val code: String,
    val weight: Int,
)

private fun List<String>.toStringJsonArray(): JSONArray {
    return JSONArray().also { array ->
        forEach(array::put)
    }
}

private fun List<AndroidCustomEntry>.toCustomEntryJsonArray(): JSONArray {
    return JSONArray().also { array ->
        forEach { entry ->
            array.put(
                JSONObject()
                    .put("text", entry.text)
                    .put("code", entry.code)
                    .put("weight", entry.weight),
            )
        }
    }
}
