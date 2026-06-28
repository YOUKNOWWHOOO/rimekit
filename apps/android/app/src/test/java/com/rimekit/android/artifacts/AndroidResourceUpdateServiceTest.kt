package com.rimekit.android.artifacts

import androidx.test.core.app.ApplicationProvider
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class AndroidResourceUpdateServiceTest {
    @Test
    fun checkForUpdates_shouldPersistStructuredReportForNonHttpSources() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val repository = AndroidResourceManifestRepository(context)
        val service = AndroidResourceUpdateService(
            context = context,
            manifestRepository = repository,
            manifestLoader = {
                AndroidResourceManifest(
                    schemas = listOf(
                        AndroidResourceItem(
                            id = "local_schema",
                            displayName = "本地方案",
                            sourceClass = "product_fixed_decision",
                            source = "local-only",
                        ),
                    ),
                    dictionaries = emptyList(),
                    models = emptyList(),
                )
            },
        )

        val report = service.checkForUpdates()
        val json = JSONObject(report)

        assertTrue(json.has("checked_at"))
        assertEquals(3, json.getJSONArray("items").length())
        val first = json.getJSONArray("items").getJSONObject(0)
        assertEquals("local_schema", first.getString("id"))
        assertTrue(!first.getBoolean("reachable"))
        assertTrue(first.getString("note").contains("HTTP"))
        val ids = buildList {
            val items = json.getJSONArray("items")
            for (index in 0 until items.length()) {
                add(items.getJSONObject(index).getString("id"))
            }
        }
        assertTrue(ids.contains("android_fcitx5_carrier"))
        assertTrue(ids.contains("android_rime_plugin"))
        assertEquals(report, service.loadLastReport())
    }
}
