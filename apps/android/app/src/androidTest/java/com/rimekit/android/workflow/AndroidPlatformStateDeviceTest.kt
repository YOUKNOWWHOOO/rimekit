package com.rimekit.android.workflow

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

@RunWith(AndroidJUnit4::class)
class AndroidPlatformStateDeviceTest {
    @Test
    fun snapshot_shouldReflectAuthorizedDirectoriesAndConfirmedRuntimeState() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val prefs = context.getSharedPreferences("android_platform_state", android.content.Context.MODE_PRIVATE)
        val syncRoot = File(context.filesDir, "device-test-sync-root").apply { mkdirs() }
        val importRoot = File(context.filesDir, "device-test-import-root").apply { mkdirs() }
        File(importRoot, "android_apply_manifest.json").writeText(
            """
            {
              "required_schema_id": "t9",
              "keyboard_layout": "9_key"
            }
            """.trimIndent(),
        )
        prefs.edit()
            .putString("sync_root_uri", syncRoot.absolutePath)
            .putString("import_root_uri", importRoot.absolutePath)
            .putBoolean("delivery_completed", true)
            .apply()

        val service = AndroidPlatformServiceImpl(context)

        val snapshot = service.readSnapshot()

        assertEquals(ProbeState.Present, snapshot.carrierState)
        assertEquals(ProbeState.Present, snapshot.rimePluginState)
        assertEquals(PermissionState.Granted, snapshot.syncRootPermission)
        assertEquals(PermissionState.Granted, snapshot.importRootPermission)
        assertEquals(ImeState.Enabled, snapshot.imeEnabledState)
        assertEquals(ImeState.Enabled, snapshot.imeSelectedState)
        assertEquals(ManualConfirmationState.Confirmed, snapshot.deliveryConfirmation)
        assertEquals(ProbeState.Present, snapshot.requiredSchemaApplied)
        assertEquals(ProbeState.Present, snapshot.keyboardLayoutApplied)
        assertTrue(snapshot.syncRootUri?.contains("device-test-sync-root") == true)
        assertTrue(snapshot.importRootUri?.contains("device-test-import-root") == true)
    }
}
