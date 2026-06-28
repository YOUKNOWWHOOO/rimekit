package com.rimekit.android.workflow

import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import java.io.File

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class AndroidPlatformServiceStateTest {
    @Test
    fun localDirectoryUris_shouldBeRecognizedAsGrantedPermissions() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val prefs = context.getSharedPreferences("android_platform_state", android.content.Context.MODE_PRIVATE)
        val syncRoot = File(context.filesDir, "robolectric-sync-root").apply { mkdirs() }
        val importRoot = File(context.filesDir, "robolectric-import-root").apply { mkdirs() }
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

        assertEquals(PermissionState.Granted, snapshot.syncRootPermission)
        assertEquals(PermissionState.Granted, snapshot.importRootPermission)
        assertEquals(ProbeState.Present, snapshot.requiredSchemaApplied)
        assertEquals(ProbeState.Present, snapshot.keyboardLayoutApplied)
        assertEquals(ManualConfirmationState.Confirmed, snapshot.deliveryConfirmation)
    }

    @Test
    fun onApplicationResumed_shouldNotAutoConfirmDeliveryCompletion() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val prefs = context.getSharedPreferences("android_platform_state", android.content.Context.MODE_PRIVATE)
        prefs.edit().clear().apply()

        val service = AndroidPlatformServiceImpl(context)

        service.markDeliveryConfirmationPending()
        service.onApplicationResumed()

        val snapshot = service.readSnapshot()
        assertEquals(ManualConfirmationState.Missing, snapshot.deliveryConfirmation)
    }
}
