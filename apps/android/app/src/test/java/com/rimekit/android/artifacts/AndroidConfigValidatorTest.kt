package com.rimekit.android.artifacts

import org.junit.Assert.assertTrue
import org.junit.Test
import org.json.JSONObject

class AndroidConfigValidatorTest {
    private val validationCatalog = AndroidValidationCatalog(
        schemaIds = setOf("rime_mint", "t9"),
        dictionaryIds = setOf("moetype", "sogou_network_popular_words", "custom_simple"),
        modelIds = emptySet(),
        fuzzyPresetIds = setOf("cn_common"),
        symbolProfileIds = setOf("default"),
        preeditProfileIds = setOf("upstream_default", "raw_code", "translated_code"),
    )

    @Test
    fun defaultModel_shouldPassValidation() {
        val issues = AndroidConfigValidator.validate(AndroidConfigModel.createDefault(), validationCatalog)

        assertTrue("默认配置模型不应触发校验错误。", issues.isEmpty())
    }

    @Test
    fun invalidPlatformDefaults_shouldBeRejected() {
        val model = AndroidConfigModel.createDefault().copy(
            androidDefaultSchemaId = "rime_mint",
            keyboardLayout = "full",
            windowsDpiScaleMode = "system_aware",
        )

        val issues = AndroidConfigValidator.validate(model, validationCatalog)
        val codes = issues.map(AndroidConfigValidationIssue::code)

        assertTrue(codes.contains("CONFIG_MODEL_SCHEMA_INVALID"))
        assertTrue(issues.any { it.detail.contains("Android 默认方案必须固定为 t9。") })
        assertTrue(issues.any { it.detail.contains("Android 键盘布局必须固定为 9_key。") })
        assertTrue(issues.any { it.detail.contains("Windows DPI 模式必须固定为 per_monitor_v2。") })
    }

    @Test
    fun invalidCustomEntriesAndFuzzySettings_shouldBeRejected() {
        val duplicateEntry = AndroidCustomEntry(
            text = "薄荷输入法",
            code = "bhsr",
            weight = 100,
        )
        val model = AndroidConfigModel.createDefault().copy(
            fuzzyEnabled = false,
            fuzzyPresetId = "cn_common",
            fuzzyAdditionalRules = listOf("derive/zh/z"),
            customEntries = listOf(
                duplicateEntry,
                duplicateEntry,
                AndroidCustomEntry(text = "", code = "abc", weight = 0),
            ),
        )

        val issues = AndroidConfigValidator.validate(model, validationCatalog)

        assertTrue(issues.any { it.detail.contains("模糊拼音关闭时 preset_id 必须为空字符串。") })
        assertTrue(issues.any { it.detail.contains("模糊拼音关闭时 additional_rules 必须为空数组。") })
        assertTrue(issues.any { it.detail.contains("custom_entries 中存在重复词条") })
        assertTrue(issues.any { it.detail.contains("自定义词条必须同时包含词条文本和编码") })
        assertTrue(issues.any { it.detail.contains("自定义词条权重必须为正整数") })
    }

    @Test
    fun invalidDictionaryOrderAndSchemas_shouldBeRejected() {
        val model = AndroidConfigModel.createDefault().copy(
            enabledSchemaIds = listOf("rime_mint"),
            windowsDefaultSchemaId = "rime_mint",
            androidDefaultSchemaId = "t9",
            enabledDictionaryIds = listOf("moetype", "custom_simple"),
            dictionaryOrder = listOf("moetype"),
            fuzzyEnabled = true,
            fuzzyTargetSchemaIds = listOf("t9"),
        )

        val issues = AndroidConfigValidator.validate(model, validationCatalog)

        assertTrue(issues.any { it.detail.contains("平台默认方案必须包含在 enabled_schema_ids 中。") })
        assertTrue(issues.any { it.detail.contains("dictionary_order 必须完整覆盖 enabled_dictionary_ids") })
        assertTrue(issues.any { it.detail.contains("模糊拼音目标方案必须属于 enabled_schema_ids") })
    }

    @Test
    fun legacyFlatConfig_shouldPreserveExtendedFields() {
        val legacyJson = JSONObject(
            """
            {
              "config_version": 1,
              "enabled_schema_ids": ["rime_mint", "t9"],
              "windows_default_schema_id": "t9",
              "android_default_schema_id": "t9",
              "candidate_page_size": 9,
              "candidate_layout": "horizontal",
              "show_emoji_comments": false,
              "enabled_model_ids": ["model_a"],
              "active_model_id": "model_a",
              "model_root": "C:/models",
              "model_versions": { "model_a": "2026-04-13" },
              "shared_sync_root": "content://sync-root",
              "android_import_root": "content://android-import",
              "windows_target_root": "C:/Users/demo/AppData/Rime",
              "export_root": "content://export-root",
              "backup_root": "content://backup-root",
              "snapshot_retention_limit": 30,
              "windows_font_face": "Microsoft YaHei",
              "windows_font_point": 12,
              "windows_dpi_scale_mode": "per_monitor_v2",
              "windows_show_notification": true
            }
            """.trimIndent(),
        )

        val model = AndroidConfigModel.fromStoredJson(legacyJson)

        assertTrue(model.windowsDefaultSchemaId == "t9")
        assertTrue(model.candidatePageSize == 9)
        assertTrue(model.enabledModelIds == listOf("model_a"))
        assertTrue(model.activeModelId == "model_a")
        assertTrue(model.modelRoot == "C:/models")
        assertTrue(model.modelVersions["model_a"] == "2026-04-13")
        assertTrue(model.sharedSyncRoot == "content://sync-root")
        assertTrue(model.androidImportRoot == "content://android-import")
        assertTrue(model.windowsTargetRoot == "C:/Users/demo/AppData/Rime")
        assertTrue(model.exportRoot == "content://export-root")
        assertTrue(model.backupRoot == "content://backup-root")
        assertTrue(model.snapshotRetentionLimit == 30)
        assertTrue(model.windowsFontFace == "Microsoft YaHei")
        assertTrue(model.windowsFontPoint == 12)
        assertTrue(model.windowsShowNotification)
    }

    @Test
    fun invalidPresetAndModelFields_shouldBeRejected() {
        val model = AndroidConfigModel.createDefault().copy(
            candidateLayout = "grid",
            simplificationMode = "unknown",
            fuzzyEnabled = true,
            fuzzyPresetId = "bad_preset",
            fuzzyTargetSchemaIds = emptyList(),
            symbolProfileId = "bad_symbol",
            preeditFormatMode = "bad_preedit",
            customPhraseMode = "bad_phrase_mode",
            commentStyleVariant = "",
            enabledModelIds = listOf("ghost_model"),
            activeModelId = "ghost_model",
            collocationMaxLength = -1,
            windowsFontPoint = -2,
        )

        val issues = AndroidConfigValidator.validate(model, validationCatalog)

        assertTrue(issues.any { it.detail.contains("candidate_settings.layout 不合法") })
        assertTrue(issues.any { it.detail.contains("simplification_mode 不合法") })
        assertTrue(issues.any { it.detail.contains("模糊拼音预设未在正式预设清单中定义") })
        assertTrue(issues.any { it.detail.contains("模糊拼音启用时必须至少指定一个 target_schema_id") })
        assertTrue(issues.any { it.detail.contains("symbol_profile_id 未在正式预设清单中定义") })
        assertTrue(issues.any { it.detail.contains("preedit_format_mode 未在正式预设清单中定义") })
        assertTrue(issues.any { it.detail.contains("custom_phrase_mode 不合法") })
        assertTrue(issues.any { it.detail.contains("comment_style_variant 不允许为空") })
        assertTrue(issues.any { it.detail.contains("启用的模型未在正式资源清单中定义") })
        assertTrue(issues.any { it.detail.contains("模型调优字段不得为负数") })
        assertTrue(issues.any { it.detail.contains("Windows font_point 不允许为负数") })
    }
}
