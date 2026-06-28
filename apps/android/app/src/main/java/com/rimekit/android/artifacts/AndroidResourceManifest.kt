package com.rimekit.android.artifacts

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

data class AndroidResourceItem(
    val id: String,
    val displayName: String,
    val sourceClass: String,
    val source: String,
    val sourceType: String? = null,
    val versionOrUpdatedAt: String? = null,
)

data class AndroidResourceManifest(
    val schemas: List<AndroidResourceItem>,
    val dictionaries: List<AndroidResourceItem>,
    val models: List<AndroidResourceItem>,
)

data class AndroidValidationCatalog(
    val schemaIds: Set<String>,
    val dictionaryIds: Set<String>,
    val modelIds: Set<String>,
    val fuzzyPresetIds: Set<String>,
    val symbolProfileIds: Set<String>,
    val preeditProfileIds: Set<String>,
)

class AndroidResourceManifestRepository(
    private val context: Context,
) {
    fun load(): AndroidResourceManifest {
        val payload = context.assets.open("resource_manifest.json").bufferedReader().use { reader -> reader.readText() }
        return parseManifest(payload)
    }

    fun loadValidationCatalog(): AndroidValidationCatalog {
        val manifest = load()
        val payload = context.assets.open("resource_manifest.json").bufferedReader().use { reader -> reader.readText() }
        return parseValidationCatalog(payload, manifest)
    }

    companion object {
        internal fun parseManifest(payload: String): AndroidResourceManifest {
            val json = JSONObject(payload)
            return AndroidResourceManifest(
                schemas = parseResourceItems(
                    jsonArray = json.getJSONArray("schemas"),
                    idKey = "schema_id",
                    sourceKey = "source_url",
                ),
                dictionaries = parseResourceItems(
                    jsonArray = json.getJSONArray("dictionaries"),
                    idKey = "dictionary_id",
                    sourceKey = "source",
                    sourceTypeKey = "source_type",
                    versionKey = "version_or_updated_at",
                ),
                models = parseResourceItems(
                    jsonArray = json.getJSONArray("models"),
                    idKey = "model_id",
                    sourceKey = "install_kind",
                ),
            )
        }

        internal fun parseValidationCatalog(
            payload: String,
            manifest: AndroidResourceManifest = parseManifest(payload),
        ): AndroidValidationCatalog {
            val json = JSONObject(payload)
            val featurePresets = json.getJSONObject("feature_presets")
            return AndroidValidationCatalog(
                schemaIds = manifest.schemas.map(AndroidResourceItem::id).toSet(),
                dictionaryIds = manifest.dictionaries.map(AndroidResourceItem::id).toSet(),
                modelIds = manifest.models.map(AndroidResourceItem::id).toSet(),
                fuzzyPresetIds = parseIdSet(featurePresets.getJSONArray("fuzzy_pinyin_presets"), "preset_id"),
                symbolProfileIds = parseIdSet(featurePresets.getJSONArray("symbol_profiles"), "preset_id"),
                preeditProfileIds = parseIdSet(featurePresets.getJSONArray("preedit_profiles"), "preset_id"),
            )
        }
    }
}

private fun parseResourceItems(
    jsonArray: JSONArray,
    idKey: String,
    sourceKey: String,
    sourceTypeKey: String? = null,
    versionKey: String? = null,
): List<AndroidResourceItem> {
    return buildList {
        for (index in 0 until jsonArray.length()) {
            val item = jsonArray.getJSONObject(index)
            add(
                AndroidResourceItem(
                    id = item.optString(idKey),
                    displayName = item.optString("display_name"),
                    sourceClass = item.optString("source_class"),
                    source = item.optString(sourceKey),
                    sourceType = sourceTypeKey?.let(item::optString)?.takeIf(String::isNotBlank),
                    versionOrUpdatedAt = versionKey?.let(item::optString)?.takeIf(String::isNotBlank),
                ),
            )
        }
    }
}

private fun parseIdSet(
    jsonArray: JSONArray,
    key: String,
): Set<String> {
    return buildSet {
        for (index in 0 until jsonArray.length()) {
            add(jsonArray.getJSONObject(index).optString(key))
        }
    }
}
