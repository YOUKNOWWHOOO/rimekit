package com.rimekit.android.artifacts

import androidx.test.core.app.ApplicationProvider
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import java.io.File

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class AndroidArtifactServiceTest {
    @Test
    fun generate_shouldEmitFormalArtifactContracts() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidArtifactService(context)

        val artifacts = service.generate(AndroidConfigModel.createDefault())
        val snapshotRoot = File(artifacts.snapshotDirectoryName)

        val configSnapshot = JSONObject(File(snapshotRoot, "config_snapshot.json").readText())
        assertTrue(configSnapshot.has("snapshot_id"))
        assertTrue(configSnapshot.has("created_at"))
        assertTrue(configSnapshot.has("config_model"))
        assertTrue(configSnapshot.has("resolved_resources"))
        assertTrue(configSnapshot.has("resolved_feature_presets"))

        val generationSummary = JSONObject(File(snapshotRoot, "generation_summary.json").readText())
        assertTrue(generationSummary.has("resource_versions"))
        assertTrue(generationSummary.has("generated_files_by_platform"))
        assertTrue(generationSummary.has("shared_output_summary"))

        val androidApplyManifest = JSONObject(File(File(snapshotRoot, "android"), "android_apply_manifest.json").readText())
        assertEquals("android", androidApplyManifest.getString("platform"))
        assertTrue(androidApplyManifest.has("carrier_id"))
        assertTrue(androidApplyManifest.has("required_schema_id"))
        assertTrue(androidApplyManifest.has("keyboard_layout"))
        assertTrue(androidApplyManifest.has("manual_steps"))
        assertTrue(androidApplyManifest.has("recheck_items"))
        assertTrue(androidApplyManifest.has("delivery_mode"))

        val syncManifest = JSONObject(File(snapshotRoot, "sync_manifest.json").readText())
        assertTrue(syncManifest.has("config_payload"))
        assertTrue(syncManifest.has("resource_payloads"))
        assertTrue(syncManifest.has("user_data_payloads"))
        assertTrue(syncManifest.has("platform_targets"))
        assertTrue(syncManifest.has("delivery_plan"))
        assertTrue(syncManifest.has("success_criteria"))
        assertTrue(syncManifest.has("consistency_hashes"))
    }

    @Test
    fun backupAndRestore_shouldRecoverImportRootAndConfigModel() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidArtifactService(context)
        val configRepository = AndroidConfigRepository(context)
        val importRoot = createCleanDirectory(context.cacheDir, "artifact-backup-restore-import-root")
        File(importRoot, "custom_simple.dict.yaml").writeText("...\r\n薄荷\tbohe\t1\r\n")
        File(importRoot, "demo.userdb.txt").writeText("user-payload")
        val importRootUri = importRoot.toURI().toString()

        val originalModel = AndroidConfigModel.createDefault().copy(
            candidateLayout = "horizontal",
            androidImportRoot = importRootUri,
            sharedSyncRoot = createCleanDirectory(context.cacheDir, "artifact-backup-restore-sync-root").toURI().toString(),
        )
        configRepository.save(originalModel)

        val backup = service.backupImportRoot(importRootUri)

        File(importRoot, "custom_simple.dict.yaml").writeText("...\r\n已修改\tgaixie\t2\r\n")
        File(importRoot, "temp.txt").writeText("noise")
        configRepository.save(AndroidConfigModel.createDefault())

        service.restoreBackup(importRootUri, backup.backupId)

        val restoredFiles = importRoot.listFiles().orEmpty().mapNotNull(File::getName).sorted()
        assertEquals(listOf("custom_simple.dict.yaml", "demo.userdb.txt"), restoredFiles)
        assertTrue(File(importRoot, "custom_simple.dict.yaml").readText().contains("薄荷\tbohe\t1"))
        assertEquals("horizontal", configRepository.load().candidateLayout)
    }

    @Test
    fun publishAndImportLatestSnapshotFromSharedRoot_shouldRoundTrip() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidArtifactService(context)
        val importRoot = createCleanDirectory(context.cacheDir, "artifact-sync-roundtrip-import-root")
        val sharedRoot = createCleanDirectory(context.cacheDir, "artifact-sync-roundtrip-shared-root")
        File(importRoot, "demo.userdb.txt").writeText("user-payload")
        val importRootUri = importRoot.toURI().toString()
        val sharedRootUri = sharedRoot.toURI().toString()

        val artifacts = service.generate(
            configModel = AndroidConfigModel.createDefault().copy(
                androidImportRoot = importRootUri,
                sharedSyncRoot = sharedRootUri,
            ),
            importRootUri = importRootUri,
        )

        val publishedFileName = service.publishLatestSnapshotToSharedRoot(sharedRootUri)
        assertTrue(File(sharedRoot, publishedFileName).exists())

        val stagedSnapshot = service.importLatestSnapshotFromSharedRoot(sharedRootUri)

        assertEquals(artifacts.snapshotId, stagedSnapshot.snapshotId)
        assertTrue(stagedSnapshot.androidFiles.containsKey("android_apply_manifest.json"))
        assertTrue(stagedSnapshot.windowsFiles.containsKey("weasel.custom.yaml"))
        assertEquals("user-payload", stagedSnapshot.userDictionaryFiles["demo.userdb.txt"])
        assertTrue(File(File(context.filesDir, "snapshots"), stagedSnapshot.snapshotId).exists())
    }

    @Test
    fun importRuntimeToConfig_shouldRecoverRuntimeStateIntoFormalModel() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidArtifactService(context)
        val importRoot = createCleanDirectory(context.cacheDir, "artifact-import-runtime-root")
        File(importRoot, "demo.userdb.txt").writeText("user-payload")
        val importRootUri = importRoot.toURI().toString()

        val sourceModel = AndroidConfigModel.createDefault().copy(
            candidateLayout = "horizontal",
            showEmojiComments = false,
            simplificationMode = "traditional",
            fuzzyEnabled = true,
            fuzzyPresetId = "cn_common",
            fuzzyTargetSchemaIds = listOf("rime_mint", "t9"),
            customEntries = listOf(
                AndroidCustomEntry(
                    text = "薄荷输入法",
                    code = "bohe",
                    weight = 5,
                ),
            ),
            androidImportRoot = importRootUri,
        )

        val generated = service.generate(
            configModel = sourceModel,
            importRootUri = importRootUri,
        )
        service.applyImportBundle(importRootUri, generated)

        val importedModel = service.importRuntimeToConfig(
            importRootUri = importRootUri,
            currentModel = AndroidConfigModel.createDefault(),
        )

        assertEquals("horizontal", importedModel.candidateLayout)
        assertEquals(false, importedModel.showEmojiComments)
        assertEquals("traditional", importedModel.simplificationMode)
        assertEquals(true, importedModel.fuzzyEnabled)
        assertEquals(listOf("rime_mint", "t9"), importedModel.fuzzyTargetSchemaIds)
        assertEquals("薄荷输入法", importedModel.customEntries.single().text)
        assertEquals("t9", importedModel.androidDefaultSchemaId)
        assertEquals("9_key", importedModel.keyboardLayout)
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
