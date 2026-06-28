package com.rimekit.android.artifacts

/**
 * Android 侧配置模型校验结果。
 */
data class AndroidConfigValidationIssue(
    val code: String,
    val detail: String,
    val conflictScope: String? = null,
)

/**
 * Android 侧正式配置模型校验器。
 */
object AndroidConfigValidator {
    private val supportedPreeditModes = setOf("upstream_default", "raw_code", "translated_code")
    private val supportedCustomPhraseModes = setOf("disabled", "simple_code_only", "full_phrase")
    private val supportedCandidateLayouts = setOf("vertical", "horizontal")
    private val supportedSimplificationModes = setOf("simplified", "traditional", "opencc_switchable")

    fun validate(
        model: AndroidConfigModel,
        catalog: AndroidValidationCatalog,
    ): List<AndroidConfigValidationIssue> {
        val issues = mutableListOf<AndroidConfigValidationIssue>()

        if (model.configVersion != 1) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_VERSION_UNSUPPORTED",
                detail = "配置模型版本不受支持：${model.configVersion}",
                conflictScope = "config_model",
            )
        }

        if (model.enabledSchemaIds.isEmpty()) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "enabled_schema_ids 不允许为空。",
                conflictScope = "config_model",
            )
        }

        model.enabledSchemaIds.forEach { schemaId ->
            if (schemaId !in catalog.schemaIds) {
                issues += AndroidConfigValidationIssue(
                    code = "RESOURCE_MANIFEST_INVALID",
                    detail = "启用的方案未在正式资源清单中定义：$schemaId",
                    conflictScope = "formal_resource",
                )
            }
        }

        if (model.windowsDefaultSchemaId != "rime_mint") {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "Windows 默认方案必须固定为 rime_mint。",
                conflictScope = "config_model",
            )
        }

        if (model.androidDefaultSchemaId != "t9") {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "Android 默认方案必须固定为 t9。",
                conflictScope = "config_model",
            )
        }

        if (model.windowsDefaultSchemaId !in model.enabledSchemaIds || model.androidDefaultSchemaId !in model.enabledSchemaIds) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "平台默认方案必须包含在 enabled_schema_ids 中。",
                conflictScope = "config_model",
            )
        }

        if (model.candidatePageSize != null && model.candidatePageSize <= 0) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "candidate_settings.page_size 如设置，必须大于 0。",
                conflictScope = "config_model",
            )
        }

        if (model.candidateLayout !in supportedCandidateLayouts) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "candidate_settings.layout 不合法：${model.candidateLayout}",
                conflictScope = "config_model",
            )
        }

        if (model.simplificationMode !in supportedSimplificationModes) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "simplification_mode 不合法：${model.simplificationMode}",
                conflictScope = "config_model",
            )
        }

        if (!model.fuzzyEnabled) {
            if (model.fuzzyPresetId.isNotBlank()) {
                issues += AndroidConfigValidationIssue(
                    code = "CONFIG_MODEL_SCHEMA_INVALID",
                    detail = "模糊拼音关闭时 preset_id 必须为空字符串。",
                    conflictScope = "config_model",
                )
            }
            if (model.fuzzyAdditionalRules.isNotEmpty()) {
                issues += AndroidConfigValidationIssue(
                    code = "CONFIG_MODEL_SCHEMA_INVALID",
                    detail = "模糊拼音关闭时 additional_rules 必须为空数组。",
                    conflictScope = "config_model",
                )
            }
        }
        else {
            if (model.fuzzyPresetId.isNotBlank() && model.fuzzyPresetId !in catalog.fuzzyPresetIds) {
                issues += AndroidConfigValidationIssue(
                    code = "FEATURE_PRESET_INVALID",
                    detail = "模糊拼音预设未在正式预设清单中定义：${model.fuzzyPresetId}",
                    conflictScope = "formal_resource",
                )
            }
            if (model.fuzzyTargetSchemaIds.isEmpty()) {
                issues += AndroidConfigValidationIssue(
                    code = "CONFIG_MODEL_SCHEMA_INVALID",
                    detail = "模糊拼音启用时必须至少指定一个 target_schema_id。",
                    conflictScope = "config_model",
                )
            }
        }

        model.fuzzyTargetSchemaIds.forEach { schemaId ->
            if (schemaId !in model.enabledSchemaIds) {
                issues += AndroidConfigValidationIssue(
                    code = "CONFIG_MODEL_SCHEMA_INVALID",
                    detail = "模糊拼音目标方案必须属于 enabled_schema_ids：$schemaId",
                    conflictScope = "config_model",
                )
            }
        }

        if (model.symbolProfileId !in catalog.symbolProfileIds) {
            issues += AndroidConfigValidationIssue(
                code = "FEATURE_PRESET_INVALID",
                detail = "symbol_profile_id 未在正式预设清单中定义：${model.symbolProfileId}",
                conflictScope = "formal_resource",
            )
        }

        if (model.preeditFormatMode !in supportedPreeditModes || model.preeditFormatMode !in catalog.preeditProfileIds) {
            issues += AndroidConfigValidationIssue(
                code = "FEATURE_PRESET_INVALID",
                detail = "preedit_format_mode 未在正式预设清单中定义：${model.preeditFormatMode}",
                conflictScope = "formal_resource",
            )
        }

        if (model.customPhraseMode !in supportedCustomPhraseModes) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "custom_phrase_mode 不合法：${model.customPhraseMode}",
                conflictScope = "config_model",
            )
        }

        if (model.commentStyleVariant.isBlank()) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "comment_style_variant 不允许为空。",
                conflictScope = "config_model",
            )
        }

        model.enabledDictionaryIds.forEach { dictionaryId ->
            if (dictionaryId !in catalog.dictionaryIds) {
                issues += AndroidConfigValidationIssue(
                    code = "RESOURCE_MANIFEST_INVALID",
                    detail = "启用的词库未在正式资源清单中定义：$dictionaryId",
                    conflictScope = "formal_resource",
                )
            }
        }

        if (model.enabledDictionaryIds.toSet() != model.dictionaryOrder.toSet()) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "dictionary_order 必须完整覆盖 enabled_dictionary_ids，且不得包含未启用词库。",
                conflictScope = "config_model",
            )
        }

        val customEntryKeys = mutableSetOf<String>()
        model.customEntries.forEach { entry ->
            if (entry.text.isBlank() || entry.code.isBlank()) {
                issues += AndroidConfigValidationIssue(
                    code = "CONFIG_MODEL_SCHEMA_INVALID",
                    detail = "自定义词条必须同时包含词条文本和编码：${entry.text.ifBlank { "<empty>" }} / ${entry.code.ifBlank { "<empty>" }}",
                    conflictScope = "config_model",
                )
            }
            if (entry.weight <= 0) {
                issues += AndroidConfigValidationIssue(
                    code = "CONFIG_MODEL_SCHEMA_INVALID",
                    detail = "自定义词条权重必须为正整数：${entry.text} / ${entry.code}",
                    conflictScope = "config_model",
                )
            }

            val key = "${entry.text}\t${entry.code}"
            if (!customEntryKeys.add(key)) {
                issues += AndroidConfigValidationIssue(
                    code = "CONFIG_MODEL_SCHEMA_INVALID",
                    detail = "custom_entries 中存在重复词条：${entry.text} / ${entry.code}",
                    conflictScope = "config_model",
                )
            }
        }

        model.enabledModelIds.forEach { modelId ->
            if (modelId !in catalog.modelIds) {
                issues += AndroidConfigValidationIssue(
                    code = "RESOURCE_MANIFEST_INVALID",
                    detail = "启用的模型未在正式资源清单中定义：$modelId",
                    conflictScope = "formal_resource",
                )
            }
        }

        if (model.activeModelId.isNotBlank() && model.activeModelId !in model.enabledModelIds) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "active_model_id 非空时必须属于 enabled_model_ids。",
                conflictScope = "config_model",
            )
        }

        if (model.collocationMaxLength < 0 ||
            model.collocationMinLength < 0 ||
            model.maxHomophones < 0 ||
            model.maxHomographs < 0
        ) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "模型调优字段不得为负数。",
                conflictScope = "config_model",
            )
        }

        if (model.snapshotRetentionLimit <= 0) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "snapshot_retention_limit 必须大于 0。",
                conflictScope = "config_model",
            )
        }

        if (model.keyboardLayout != "9_key") {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "Android 键盘布局必须固定为 9_key。",
                conflictScope = "config_model",
            )
        }

        if (model.candidateTextSize <= 0 || model.candidateViewHeight <= 0) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "Android 候选字号和候选区高度都必须为正整数。",
                conflictScope = "config_model",
            )
        }

        if (model.windowsFontPoint < 0) {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "Windows font_point 不允许为负数。",
                conflictScope = "config_model",
            )
        }

        if (model.windowsDpiScaleMode != "per_monitor_v2") {
            issues += AndroidConfigValidationIssue(
                code = "CONFIG_MODEL_SCHEMA_INVALID",
                detail = "Windows DPI 模式必须固定为 per_monitor_v2。",
                conflictScope = "config_model",
            )
        }

        return issues
    }
}
