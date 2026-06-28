package com.rimekit.android.artifacts

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

@RunWith(AndroidJUnit4::class)
class AndroidArtifactDeviceIntegrationTest {
    @Test
    fun applyRecheckAndRestoreBackup_shouldWorkOnDeviceFileRoots() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidArtifactService(context)
        val importRoot = createCleanDirectory(context.filesDir, "device-import-root")
        val importRootUri = importRoot.toURI().toString()
        File(importRoot, "legacy.userdb.txt").writeText("legacy-payload")

        val generated = service.generate(
            configModel = AndroidConfigModel.createDefault().copy(
                androidImportRoot = importRootUri,
            ),
            importRootUri = importRootUri,
        )

        val backup = service.backupImportRoot(importRootUri)
        service.applyImportBundle(importRootUri, generated)

        val recheck = service.recheckImportRoot(importRootUri)
        assertTrue("缺少文件: ${recheck.missingFiles}", recheck.missingFiles.isEmpty())
        assertTrue(File(importRoot, "android_apply_manifest.json").exists())

        File(importRoot, "android_apply_manifest.json").delete()
        service.restoreBackup(importRootUri, backup.backupId)

        assertTrue(File(importRoot, "legacy.userdb.txt").exists())
        assertTrue(!File(importRoot, "android_apply_manifest.json").exists())
    }

    @Test
    fun publishImportAndImportRuntime_shouldWorkOnDeviceFileRoots() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidArtifactService(context)
        val importRoot = createCleanDirectory(context.filesDir, "device-sync-import-root")
        val sharedRoot = createCleanDirectory(context.filesDir, "device-sync-shared-root")
        val importRootUri = importRoot.toURI().toString()
        val sharedRootUri = sharedRoot.toURI().toString()
        File(importRoot, "device.userdb.txt").writeText("device-user-payload")

        val sourceModel = AndroidConfigModel.createDefault().copy(
            candidateLayout = "horizontal",
            showEmojiComments = false,
            androidImportRoot = importRootUri,
            sharedSyncRoot = sharedRootUri,
        )

        service.generate(
            configModel = sourceModel,
            importRootUri = importRootUri,
        )

        val zipName = service.publishLatestSnapshotToSharedRoot(sharedRootUri)
        assertTrue(
            "共享根目录缺少快照文件: $zipName; 当前文件=${sharedRoot.listFiles().orEmpty().mapNotNull(File::getName)}",
            File(sharedRoot, zipName).exists(),
        )

        val staged = service.importLatestSnapshotFromSharedRoot(sharedRootUri)
        service.backupImportRoot(importRootUri)
        service.applyStagedSyncSnapshot(importRootUri, staged)

        val imported = service.importRuntimeToConfig(importRootUri, AndroidConfigModel.createDefault())
        assertEquals("horizontal", imported.candidateLayout)
        assertEquals(false, imported.showEmojiComments)
        assertEquals("t9", imported.androidDefaultSchemaId)
        assertEquals("9_key", imported.keyboardLayout)
        assertEquals("device-user-payload", staged.userDictionaryFiles["device.userdb.txt"])
    }

    @Test
    fun importWindowsSnapshotFromHostSharedRoot_shouldSucceed() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidArtifactService(context)
        val importRoot = createCleanDirectory(context.filesDir, "host-sync-import-root")
        val sharedRoot = File(context.filesDir, "host-shared-root").apply { mkdirs() }
        val sharedRootUri = sharedRoot.toURI().toString()
        val importRootUri = importRoot.toURI().toString()

        val zipFiles = sharedRoot.listFiles().orEmpty().filter { it.isFile && it.name.endsWith(".zip", ignoreCase = true) }
        assumeTrue("host-shared-root 中没有 Windows 快照 zip。", zipFiles.isNotEmpty())

        val staged = service.importLatestSnapshotFromSharedRoot(sharedRootUri)
        service.backupImportRoot(importRootUri)
        service.applyStagedSyncSnapshot(importRootUri, staged)

        val imported = service.importRuntimeToConfig(importRootUri, AndroidConfigModel.createDefault())
        assertEquals("t9", imported.androidDefaultSchemaId)
        assertTrue(staged.androidFiles.containsKey("android_apply_manifest.json"))
        assertTrue(staged.windowsFiles.containsKey("weasel.custom.yaml"))
        assertTrue(staged.userDictionaryFiles.isNotEmpty())
    }

    private fun createCleanDirectory(
        parent: File,
        name: String,
    ): File {
        return File(parent, name).apply {
            deleteRecursively()
            mkdirs()
        }
    }
}
