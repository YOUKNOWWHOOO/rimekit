package com.rimekit.android.artifacts

import org.junit.Assert.assertTrue
import org.junit.Test

class AndroidResourceManifestRepositoryTest {
    @Test
    fun load_shouldExposeSchemasDictionariesAndCatalog() {
        val payload =
            """
            {
              "schemas": [
                { "schema_id": "rime_mint", "display_name": "薄荷主方案", "source_class": "official_current", "source_url": "https://example.com/rime_mint" },
                { "schema_id": "t9", "display_name": "九键方案", "source_class": "official_current", "source_url": "https://example.com/t9" }
              ],
              "dictionaries": [
                { "dictionary_id": "moetype", "display_name": "萌典词库", "source_class": "product_fixed_decision", "source": "https://example.com/moetype", "source_type": "git_repository", "version_or_updated_at": "tracked_by_snapshot" },
                { "dictionary_id": "custom_simple", "display_name": "自定义简码词条", "source_class": "official_current", "source": "generated", "source_type": "generated", "version_or_updated_at": "tracked_by_snapshot" }
              ],
              "models": [],
              "feature_presets": {
                "fuzzy_pinyin_presets": [{ "preset_id": "cn_common" }],
                "symbol_profiles": [{ "preset_id": "default" }],
                "preedit_profiles": [{ "preset_id": "upstream_default" }, { "preset_id": "raw_code" }]
              }
            }
            """.trimIndent()

        val manifest = AndroidResourceManifestRepository.parseManifest(payload)
        val catalog = AndroidResourceManifestRepository.parseValidationCatalog(payload, manifest)

        assertTrue(manifest.schemas.any { it.id == "rime_mint" })
        assertTrue(manifest.schemas.any { it.id == "t9" })
        assertTrue(manifest.dictionaries.any { it.id == "moetype" })
        assertTrue(catalog.schemaIds.contains("rime_mint"))
        assertTrue(catalog.dictionaryIds.contains("custom_simple"))
        assertTrue(catalog.fuzzyPresetIds.contains("cn_common"))
        assertTrue(catalog.symbolProfileIds.contains("default"))
        assertTrue(catalog.preeditProfileIds.contains("upstream_default"))
    }
}
