package com.rimekit.android.artifacts

import android.content.Context
import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream
import java.security.MessageDigest
import java.time.Instant
import java.time.format.DateTimeFormatter
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

/**
 * Android 正式产物流服务。
 */
class AndroidArtifactService(
    private val context: Context,
) {
    private val artifactPrefs = context.getSharedPreferences("android_artifacts", Context.MODE_PRIVATE)
    private val configRepository = AndroidConfigRepository(context)

    fun loadArtifactState(): AndroidArtifactState {
        val generated = artifactPrefs.getStringSet(KEY_GENERATED_FILES, emptySet()).orEmpty().toList().sorted()
        return AndroidArtifactState(
            latestSnapshotId = artifactPrefs.getString(KEY_LATEST_SNAPSHOT_ID, null),
            latestBackupId = artifactPrefs.getString(KEY_LATEST_BACKUP_ID, null),
            generatedFileNames = generated,
            latestAppliedSnapshotId = artifactPrefs.getString(KEY_LATEST_APPLIED_SNAPSHOT_ID, null),
            lastRecheckSummary = artifactPrefs.getString(KEY_LAST_RECHECK_SUMMARY, null),
        )
    }

    fun listBackups(): List<AndroidBackupEntry> {
        return backupsRoot().listFiles()
            ?.filter(File::isDirectory)
            ?.sortedByDescending(File::getName)
            ?.map { directory ->
                val manifestFile = directory.resolve("backup_manifest.json")
                val manifest = if (manifestFile.exists()) JSONObject(manifestFile.readText()) else JSONObject()
                AndroidBackupEntry(
                    backupId = directory.name,
                    snapshotId = manifest.optString("snapshot_id").takeIf(String::isNotBlank),
                    createdAt = manifest.optString("created_at").takeIf(String::isNotBlank),
                )
            }
            .orEmpty()
    }

    fun generate(
        configModel: AndroidConfigModel = AndroidConfigModel.createDefault(),
        importRootUri: String? = null,
    ): AndroidGeneratedArtifacts {
        val snapshotId = createOperationId("android")
        val snapshotDirectory = File(snapshotsRoot(), snapshotId).apply { mkdirs() }
        val androidDirectory = File(snapshotDirectory, "android").apply { mkdirs() }
        val windowsDirectory = File(snapshotDirectory, "windows").apply { mkdirs() }
        val userDataDirectory = File(snapshotDirectory, "user_data").apply { mkdirs() }
        val userDictionaryDirectory = File(userDataDirectory, "user_dict_exports").apply { mkdirs() }

        val windowsFiles = buildWindowsFiles(configModel)
        val androidFiles = buildAndroidFiles(configModel)
        val userDictionaryFiles = exportUserDictionaryFiles(importRootUri)
        writeFiles(windowsDirectory, windowsFiles)
        writeFiles(androidDirectory, androidFiles)
        writeFiles(userDictionaryDirectory, userDictionaryFiles)

        val customEntriesJson = renderCustomEntriesJson(configModel)
        File(userDataDirectory, "custom_entries.json").writeText(customEntriesJson)

        val configSnapshotJson = renderConfigSnapshotJson(snapshotId, configModel)
        val generationSummaryJson = renderGenerationSummaryJson(snapshotId, configModel, androidFiles, windowsFiles)
        val syncManifestJson = renderSyncManifestJson(
            snapshotId = snapshotId,
            model = configModel,
            configSnapshotJson = configSnapshotJson,
            customEntriesJson = customEntriesJson,
            androidFiles = androidFiles,
            windowsFiles = windowsFiles,
            userDictionaryFiles = userDictionaryFiles,
        )

        File(snapshotDirectory, "config_snapshot.json").writeText(configSnapshotJson)
        File(snapshotDirectory, "generation_summary.json").writeText(generationSummaryJson)
        File(snapshotDirectory, "sync_manifest.json").writeText(syncManifestJson)

        persistLatestSnapshot(snapshotId, androidFiles.keys + userDictionaryFiles.keys)

        return AndroidGeneratedArtifacts(
            snapshotId = snapshotId,
            snapshotDirectoryName = snapshotDirectory.absolutePath,
            androidFiles = androidFiles,
            windowsFiles = windowsFiles,
            userDictionaryFiles = userDictionaryFiles,
        )
    }

    fun backupImportRoot(importRootUri: String): AndroidBackupResult {
        val backupId = createOperationId("android-backup")
        val backupDirectory = File(backupsRoot(), backupId).apply { mkdirs() }
        val platformTargetsDirectory = File(backupDirectory, "platform_targets").apply { mkdirs() }
        val resourceStateDirectory = File(backupDirectory, "resource_state").apply { mkdirs() }
        localRootOrNull(importRootUri)?.let { localRoot ->
            localRoot.listFiles().orEmpty().forEach { file ->
                if (file.isFile) {
                    file.copyTo(File(platformTargetsDirectory, file.name), overwrite = true)
                }
            }
        } ?: run {
            val root = requireRoot(importRootUri)
            root.listFiles().forEach { file ->
                if (file.isFile) {
                    file.uri.copyInto(File(platformTargetsDirectory, file.name.orEmpty()))
                }
            }
        }

        File(resourceStateDirectory, "current_config_model.json").writeText(configRepository.loadRawJson())
        val latestSnapshotId = artifactPrefs.getString(KEY_LATEST_SNAPSHOT_ID, null)
        if (!latestSnapshotId.isNullOrBlank()) {
            val latestUserDataRoot = File(File(snapshotsRoot(), latestSnapshotId), "user_data")
            if (latestUserDataRoot.exists()) {
                copyDirectory(latestUserDataRoot, File(resourceStateDirectory, "user_data"))
            }
        }

        val manifest = JSONObject()
            .put("backup_id", backupId)
            .put("created_at", Instant.now().toString())
            .put("platform", "android")
            .put("snapshot_id", latestSnapshotId)
            .put("platform_targets", JSONArray().put("platform_targets"))
            .put("resource_state", JSONArray().put("resource_state"))
            .put("snapshot_ref", latestSnapshotId)
            .put("restorable", true)
            .put("includes_user_data", File(resourceStateDirectory, "user_data").exists())
            .toString(2)
        File(backupDirectory, "backup_manifest.json").writeText(manifest)
        File(backupDirectory, "snapshot_ref.txt").writeText(latestSnapshotId.orEmpty())

        artifactPrefs.edit()
            .putString(KEY_LATEST_BACKUP_ID, backupId)
            .apply()

        return AndroidBackupResult(
            backupId = backupId,
            backupDirectoryName = backupDirectory.absolutePath,
        )
    }

    fun applyImportBundle(
        importRootUri: String,
        generatedArtifacts: AndroidGeneratedArtifacts,
    ) {
        applyFilesToImportRoot(
            importRootUri = importRootUri,
            snapshotId = generatedArtifacts.snapshotId,
            androidFiles = generatedArtifacts.androidFiles,
            userDictionaryFiles = generatedArtifacts.userDictionaryFiles,
        )
    }

    fun stageSyncSnapshotImport(uri: Uri): AndroidStagedSyncSnapshot {
        val importedRoot = extractSnapshotPackage(uri)
        return createStagedSyncSnapshot(importedRoot)
    }

    fun stageSyncSnapshotImport(file: File): AndroidStagedSyncSnapshot {
        require(file.exists()) { "同步快照文件不存在：${file.absolutePath}" }
        val importedRoot = extractSnapshotPackage(file.inputStream())
        return createStagedSyncSnapshot(importedRoot)
    }

    private fun createStagedSyncSnapshot(importedRoot: File): AndroidStagedSyncSnapshot {
        val snapshotId = importedRoot.name
        val configSnapshot = File(importedRoot, "config_snapshot.json")
        val androidDirectory = File(importedRoot, "android")
        val windowsDirectory = File(importedRoot, "windows")
        val syncManifest = File(importedRoot, "sync_manifest.json")
        require(configSnapshot.exists()) { "同步快照缺少 config_snapshot.json。" }
        require(syncManifest.exists()) { "同步快照缺少 sync_manifest.json。" }
        require(androidDirectory.exists()) { "同步快照缺少 android 目标包。" }
        require(windowsDirectory.exists()) { "同步快照缺少 windows 目标包。" }

        val configJson = JSONObject(configSnapshot.readText())
        val configModel = AndroidConfigModel.fromStoredJson(configJson.getJSONObject("config_model"))
        val androidFiles = readFiles(androidDirectory)
        val windowsFiles = readFiles(windowsDirectory)
        require(androidFiles.containsKey("android_apply_manifest.json")) { "同步快照缺少 android_apply_manifest.json。" }

        val userDictionaryFiles = readFiles(File(importedRoot, "user_data/user_dict_exports"))
        persistLatestSnapshot(snapshotId, androidFiles.keys + userDictionaryFiles.keys)

        return AndroidStagedSyncSnapshot(
            snapshotId = snapshotId,
            snapshotDirectoryName = importedRoot.absolutePath,
            configModel = configModel,
            androidFiles = androidFiles,
            windowsFiles = windowsFiles,
            userDictionaryFiles = userDictionaryFiles,
        )
    }

    fun applyStagedSyncSnapshot(
        importRootUri: String,
        snapshot: AndroidStagedSyncSnapshot,
    ) {
        applyFilesToImportRoot(
            importRootUri = importRootUri,
            snapshotId = snapshot.snapshotId,
            androidFiles = snapshot.androidFiles,
            userDictionaryFiles = snapshot.userDictionaryFiles,
        )
    }

    fun importRuntimeToConfig(
        importRootUri: String,
        currentModel: AndroidConfigModel,
    ): AndroidConfigModel {
        val root = requireRoot(importRootUri)
        val currentFiles = root.listFiles()
            .filter { file -> file.isFile }
            .associate { file ->
                val name = requireNotNull(file.name) { "导入源存在匿名文件。" }
                name to file.uri.readText()
            }

        val schemaIds = parseEnabledSchemaIds(currentFiles["default.custom.yaml"]) ?: currentModel.enabledSchemaIds
        val runtimeModel = currentModel.copy(
            enabledSchemaIds = schemaIds,
            windowsDefaultSchemaId = currentModel.windowsDefaultSchemaId,
            androidDefaultSchemaId = parseAndroidManifest(currentFiles["android_apply_manifest.json"])?.requiredSchemaId
                ?: currentModel.androidDefaultSchemaId,
        )

        val rimeMintFields = parseSharedYaml(currentFiles["rime_mint.custom.yaml"])
        val t9Fields = parseSharedYaml(currentFiles["t9.custom.yaml"])
        val dictionaryOrder = parseDictionaryOrder(currentFiles["rime_mint.dict.yaml"]) ?: currentModel.dictionaryOrder
        val customEntries = parseCustomEntries(currentFiles["custom_simple.dict.yaml"])
        val androidManifest = parseAndroidManifest(currentFiles["android_apply_manifest.json"])

        val mergedFuzzyTargets = buildList {
            if (rimeMintFields?.fuzzyRules?.isNotEmpty() == true) {
                add("rime_mint")
            }
            if (t9Fields?.fuzzyRules?.isNotEmpty() == true) {
                add("t9")
            }
        }
        val fuzzyRules = when {
            !rimeMintFields?.fuzzyRules.isNullOrEmpty() -> rimeMintFields.fuzzyRules
            !t9Fields?.fuzzyRules.isNullOrEmpty() -> t9Fields.fuzzyRules
            else -> emptyList()
        }
        val normalizedRules = fuzzyRules!!.map { rule -> FUZZY_REGEX_TO_SIMPLE[rule] ?: rule }.distinct()
        val additionalRules = normalizedRules.filterNot(DEFAULT_FUZZY_RULES::contains)
        val fuzzyPresetId = if (normalizedRules.any(DEFAULT_FUZZY_RULES::contains)) "cn_common" else ""

        return runtimeModel.copy(
            candidateLayout = rimeMintFields?.candidateLayout ?: t9Fields?.candidateLayout ?: currentModel.candidateLayout,
            showEmojiComments = rimeMintFields?.showEmojiComments ?: t9Fields?.showEmojiComments ?: currentModel.showEmojiComments,
            simplificationMode = rimeMintFields?.simplificationMode ?: t9Fields?.simplificationMode ?: currentModel.simplificationMode,
            fullShapeEnabled = rimeMintFields?.fullShapeEnabled ?: t9Fields?.fullShapeEnabled ?: currentModel.fullShapeEnabled,
            asciiPunctEnabled = rimeMintFields?.asciiPunctEnabled ?: t9Fields?.asciiPunctEnabled ?: currentModel.asciiPunctEnabled,
            emojiSuggestionEnabled = rimeMintFields?.emojiSuggestionEnabled ?: t9Fields?.emojiSuggestionEnabled
                ?: currentModel.emojiSuggestionEnabled,
            toneDisplayEnabled = rimeMintFields?.toneDisplayEnabled ?: t9Fields?.toneDisplayEnabled ?: currentModel.toneDisplayEnabled,
            fuzzyEnabled = fuzzyRules.isNotEmpty(),
            fuzzyPresetId = fuzzyPresetId,
            fuzzyTargetSchemaIds = if (mergedFuzzyTargets.isNotEmpty()) mergedFuzzyTargets else currentModel.fuzzyTargetSchemaIds,
            fuzzyAdditionalRules = additionalRules,
            dictionaryOrder = dictionaryOrder,
            enabledDictionaryIds = dictionaryOrder,
            customEntries = if (customEntries.isNotEmpty()) customEntries else currentModel.customEntries,
            contextualSuggestionsEnabled = rimeMintFields?.contextualSuggestionsEnabled
                ?: t9Fields?.contextualSuggestionsEnabled
                ?: currentModel.contextualSuggestionsEnabled,
            collocationMaxLength = rimeMintFields?.collocationMaxLength ?: t9Fields?.collocationMaxLength
                ?: currentModel.collocationMaxLength,
            collocationMinLength = rimeMintFields?.collocationMinLength ?: t9Fields?.collocationMinLength
                ?: currentModel.collocationMinLength,
            maxHomophones = rimeMintFields?.maxHomophones ?: t9Fields?.maxHomophones ?: currentModel.maxHomophones,
            maxHomographs = rimeMintFields?.maxHomographs ?: t9Fields?.maxHomographs ?: currentModel.maxHomographs,
            keyboardLayout = androidManifest?.keyboardLayout ?: currentModel.keyboardLayout,
            candidateTextSize = androidManifest?.candidateTextSize ?: currentModel.candidateTextSize,
            candidateViewHeight = androidManifest?.candidateViewHeight ?: currentModel.candidateViewHeight,
        )
    }

    fun restoreBackup(
        importRootUri: String,
        backupId: String,
    ) {
        val backupDirectory = File(backupsRoot(), backupId)
        val platformTargetsDirectory = File(backupDirectory, "platform_targets")
        val resourceStateDirectory = File(backupDirectory, "resource_state")
        val snapshotRefPath = File(backupDirectory, "snapshot_ref.txt")
        require(platformTargetsDirectory.exists()) { "未找到可恢复的 Android 备份目录：$backupId" }

        localRootOrNull(importRootUri)?.let { localRoot ->
            localRoot.mkdirs()
            localRoot.listFiles().orEmpty().forEach { file ->
                if (file.isFile) {
                    file.delete()
                }
            }
            platformTargetsDirectory.listFiles()?.forEach { file ->
                if (file.isFile) {
                    file.copyTo(File(localRoot, file.name), overwrite = true)
                }
            }
        } ?: run {
            val root = requireRoot(importRootUri)
            root.listFiles().forEach { file ->
                if (file.isFile) {
                    file.delete()
                }
            }
            platformTargetsDirectory.listFiles()?.forEach { file ->
                if (file.isFile) {
                    root.findFile(file.name)?.delete()
                    val target = root.createFile(resolveMimeType(file.name), file.name)
                        ?: error("无法恢复导入源文件：${file.name}")
                    target.uri.writeFrom(file, binary = false)
                }
            }
        }

        val currentConfig = File(resourceStateDirectory, "current_config_model.json")
        if (currentConfig.exists()) {
            configRepository.saveRawJson(currentConfig.readText())
        }

        val restoredSnapshotId = snapshotRefPath.takeIf(File::exists)
            ?.readText()
            ?.trim()
            ?.takeIf(String::isNotBlank)
        val restoredFileNames = platformTargetsDirectory.listFiles()
            ?.filter(File::isFile)
            ?.mapNotNull(File::getName)
            ?.sorted()
            ?.toSet()
            ?: emptySet()

        artifactPrefs.edit().apply {
            if (restoredSnapshotId != null) {
                putString(KEY_LATEST_SNAPSHOT_ID, restoredSnapshotId)
                putString(KEY_LATEST_APPLIED_SNAPSHOT_ID, restoredSnapshotId)
            }
            putStringSet(KEY_GENERATED_FILES, restoredFileNames)
            remove(KEY_LAST_RECHECK_SUMMARY)
        }.apply()
    }

    fun recheckImportRoot(
        importRootUri: String,
    ): AndroidRecheckResult {
        val expectedFiles = artifactPrefs.getStringSet(KEY_GENERATED_FILES, emptySet()).orEmpty().toList().sorted()
        val actualNames = localRootOrNull(importRootUri)?.let { localRoot ->
            localRoot.listFiles().orEmpty().map(File::getName).toSet()
        } ?: requireRoot(importRootUri).listFiles().mapNotNull { file -> file.name }.toSet()
        val missingFiles = expectedFiles.filterNot(actualNames::contains)

        return AndroidRecheckResult(
            missingFiles = missingFiles,
            expectedFiles = expectedFiles,
        )
    }

    fun saveRecheckSummary(summary: String) {
        artifactPrefs.edit()
            .putString(KEY_LAST_RECHECK_SUMMARY, summary)
            .apply()
    }

    fun clearDeployTransientState() {
        artifactPrefs.edit()
            .remove(KEY_LAST_RECHECK_SUMMARY)
            .apply()
    }

    fun latestApplyManifestText(): String? {
        val latestSnapshotId = artifactPrefs.getString(KEY_LATEST_SNAPSHOT_ID, null) ?: return null
        val manifestFile = File(File(File(snapshotsRoot(), latestSnapshotId), "android"), "android_apply_manifest.json")
        return manifestFile.takeIf(File::exists)?.readText()
    }

    fun ensureLatestSnapshot(
        configModel: AndroidConfigModel,
        importRootUri: String? = null,
    ): String {
        val latestSnapshotId = artifactPrefs.getString(KEY_LATEST_SNAPSHOT_ID, null)
        if (!latestSnapshotId.isNullOrBlank()) {
            val snapshotRoot = File(snapshotsRoot(), latestSnapshotId)
            val configSnapshotFile = File(snapshotRoot, "config_snapshot.json")
            if (configSnapshotFile.exists()) {
                val snapshotJson = JSONObject(configSnapshotFile.readText())
                val storedConfig = snapshotJson.optJSONObject("config_model")?.toString()
                if (storedConfig == configModel.toFormalJson().toString()) {
                    return latestSnapshotId
                }
            }
        }

        return generate(configModel = configModel, importRootUri = importRootUri).snapshotId
    }

    fun exportLatestSnapshotToFile(
        targetFile: File,
        configModel: AndroidConfigModel,
        importRootUri: String? = null,
    ): String {
        val snapshotId = ensureLatestSnapshot(configModel, importRootUri)
        val snapshotDirectory = File(snapshotsRoot(), snapshotId)
        require(snapshotDirectory.exists()) { "快照目录不存在：$snapshotId" }
        zipDirectory(snapshotDirectory, targetFile)
        return snapshotId
    }

    fun publishLatestSnapshotToSharedRoot(sharedSyncRootUri: String): String {
        val latestSnapshotId = artifactPrefs.getString(KEY_LATEST_SNAPSHOT_ID, null)
            ?: error("当前没有可发布到同步根目录的快照。")
        val snapshotDirectory = File(snapshotsRoot(), latestSnapshotId)
        require(snapshotDirectory.exists()) { "快照目录不存在：$latestSnapshotId" }

        val fileName = "$latestSnapshotId.zip"
        val tempZip = File(importsRoot(), "$fileName.tmp")
        if (tempZip.exists()) {
            tempZip.delete()
        }
        zipDirectory(snapshotDirectory, tempZip)
        localRootOrNull(sharedSyncRootUri)?.let { localRoot ->
            localRoot.mkdirs()
            File(localRoot, fileName).delete()
            tempZip.copyTo(File(localRoot, fileName), overwrite = true)
        } ?: run {
            val sharedRoot = requireRoot(sharedSyncRootUri)
            sharedRoot.findFile(fileName)?.delete()
            val target = sharedRoot.createFile("application/zip", fileName)
                ?: error("无法在同步根目录创建快照文件：$fileName")
            target.uri.writeFrom(tempZip, binary = true)
        }
        tempZip.delete()
        return fileName
    }

    fun importLatestSnapshotFromSharedRoot(sharedSyncRootUri: String): AndroidStagedSyncSnapshot {
        localRootOrNull(sharedSyncRootUri)?.let { localRoot ->
            val latestSnapshot = localRoot.listFiles().orEmpty()
                .filter { file -> file.isFile && file.name.endsWith(".zip", ignoreCase = true) }
                .maxByOrNull(File::getName)
                ?: error("同步根目录中没有可导入的同步快照。")
            return stageSyncSnapshotImport(latestSnapshot.toURI().toString().let(Uri::parse))
        }

        val sharedRoot = requireRoot(sharedSyncRootUri)
        val latestSnapshot = sharedRoot.listFiles()
            .filter { file -> file.isFile && file.name?.endsWith(".zip", ignoreCase = true) == true }
            .maxByOrNull { file -> file.name.orEmpty() }
            ?: error("同步根目录中没有可导入的同步快照。")
        return stageSyncSnapshotImport(latestSnapshot.uri)
    }

    private fun buildWindowsFiles(model: AndroidConfigModel): Map<String, String> {
        return linkedMapOf(
            "default.custom.yaml" to renderDefaultCustomYaml(model),
            "rime_mint.custom.yaml" to renderRimeMintCustomYaml(model),
            "rime_mint.dict.yaml" to renderRimeMintDictionaryYaml(model),
            "custom_simple.dict.yaml" to renderCustomSimpleDictionaryYaml(model),
            "weasel.custom.yaml" to renderWeaselCustomYaml(model),
        )
    }

    private fun buildAndroidFiles(model: AndroidConfigModel): Map<String, String> {
        val windowsFiles = buildWindowsFiles(model)
        return linkedMapOf(
            "default.custom.yaml" to requireNotNull(windowsFiles["default.custom.yaml"]),
            "rime_mint.custom.yaml" to requireNotNull(windowsFiles["rime_mint.custom.yaml"]),
            "t9.custom.yaml" to renderT9CustomYaml(model),
            "rime_mint.dict.yaml" to requireNotNull(windowsFiles["rime_mint.dict.yaml"]),
            "custom_simple.dict.yaml" to requireNotNull(windowsFiles["custom_simple.dict.yaml"]),
            "android_apply_manifest.json" to renderAndroidApplyManifest(model),
        )
    }

    private fun writeFiles(
        directory: File,
        files: Map<String, String>,
    ) {
        files.forEach { (name, content) ->
            File(directory, name).writeText(content)
        }
    }

    private fun applyFilesToImportRoot(
        importRootUri: String,
        snapshotId: String,
        androidFiles: Map<String, String>,
        userDictionaryFiles: Map<String, String>,
    ) {
        localRootOrNull(importRootUri)?.let { localRoot ->
            localRoot.mkdirs()
            androidFiles.forEach { (name, content) ->
                File(localRoot, name).writeText(content)
            }
            userDictionaryFiles.forEach { (name, content) ->
                File(localRoot, name).writeText(content)
            }
        } ?: run {
            val root = requireRoot(importRootUri)
            androidFiles.forEach { (name, content) ->
                root.findFile(name)?.delete()
                val target = root.createFile(resolveMimeType(name), name)
                    ?: error("无法创建导入源文件：$name")
                context.contentResolver.openOutputStream(target.uri, "wt").use { stream ->
                    requireNotNull(stream) { "无法打开导入源输出流：$name" }
                    stream.writer().use { writer ->
                        writer.write(content)
                    }
                }
            }
            userDictionaryFiles.forEach { (name, content) ->
                root.findFile(name)?.delete()
                val target = root.createFile(resolveMimeType(name), name)
                    ?: error("无法创建用户词典同步载荷：$name")
                context.contentResolver.openOutputStream(target.uri, "wt").use { stream ->
                    requireNotNull(stream) { "无法打开用户词典输出流：$name" }
                    stream.writer().use { writer ->
                        writer.write(content)
                    }
                }
            }
        }
        artifactPrefs.edit()
            .putString(KEY_LATEST_APPLIED_SNAPSHOT_ID, snapshotId)
            .remove(KEY_LAST_RECHECK_SUMMARY)
            .apply()
    }

    private fun persistLatestSnapshot(
        snapshotId: String,
        generatedAndroidFiles: Collection<String>,
    ) {
        artifactPrefs.edit()
            .putString(KEY_LATEST_SNAPSHOT_ID, snapshotId)
            .putStringSet(KEY_GENERATED_FILES, generatedAndroidFiles.toSet())
            .apply()
    }

    private fun exportUserDictionaryFiles(importRootUri: String?): Map<String, String> {
        if (importRootUri.isNullOrBlank()) {
            return emptyMap()
        }

        localRootOrNull(importRootUri)?.let { localRoot ->
            return localRoot.listFiles().orEmpty()
                .filter { file -> file.isFile && file.name.endsWith(".userdb.txt", ignoreCase = true) }
                .sortedBy(File::getName)
                .associate { file -> file.name to file.readText() }
        }

        val root = requireRoot(importRootUri)
        return root.listFiles()
            .filter { file -> file.isFile && file.name?.endsWith(".userdb.txt", ignoreCase = true) == true }
            .sortedBy { file -> file.name }
            .associate { file ->
                val fileName = requireNotNull(file.name) { "Android 导入源存在匿名用户词典文件。" }
                fileName to file.uri.readText()
            }
    }

    private fun requireRoot(importRootUri: String): DocumentFile {
        val uri = Uri.parse(importRootUri)
        if (uri.scheme.equals("file", ignoreCase = true)) {
            val localRoot = requireLocalFile(uri)
            require(localRoot.isDirectory) { "本地导入源路径不是目录：$localRoot" }
            return DocumentFile.fromFile(localRoot)
        }
        return requireNotNull(DocumentFile.fromTreeUri(context, uri)) {
            "无法解析 Android 导入源目录。"
        }
    }

    private fun Uri.copyInto(target: File) {
        asLocalFileOrNull()?.let { source ->
            target.parentFile?.mkdirs()
            source.copyTo(target, overwrite = true)
            return
        }

        context.contentResolver.openInputStream(this).use { input ->
            requireNotNull(input) { "无法读取导入源备份文件：$this" }
            target.outputStream().use { output ->
                input.copyTo(output)
            }
        }
    }

    private fun Uri.writeFrom(
        source: File,
        binary: Boolean,
    ) {
        asLocalFileOrNull()?.let { target ->
            target.parentFile?.mkdirs()
            source.copyTo(target, overwrite = true)
            return
        }

        context.contentResolver.openOutputStream(this, if (binary) "w" else "wt").use { output ->
            requireNotNull(output) { "无法写入导入源文件：$this" }
            source.inputStream().use { input ->
                input.copyTo(output)
            }
        }
    }

    private fun zipDirectory(
        sourceDirectory: File,
        targetFile: File,
    ) {
        targetFile.parentFile?.mkdirs()
        ZipOutputStream(FileOutputStream(targetFile)).use { zip ->
            addDirectory(zip, sourceDirectory, sourceDirectory)
        }
    }

    private fun addDirectory(
        zip: ZipOutputStream,
        root: File,
        current: File,
    ) {
        current.listFiles()
            ?.sortedBy(File::getName)
            ?.forEach { file ->
                val relativePath = root.toPath().relativize(file.toPath()).toString().replace('\\', '/')
                if (file.isDirectory) {
                    addDirectory(zip, root, file)
                } else {
                    zip.putNextEntry(ZipEntry(relativePath))
                    file.inputStream().use { input -> input.copyTo(zip) }
                    zip.closeEntry()
                }
            }
    }

    private fun Uri.readText(): String {
        asLocalFileOrNull()?.let { localFile ->
            return localFile.readText()
        }

        context.contentResolver.openInputStream(this).use { input ->
            requireNotNull(input) { "无法读取导入源文件：$this" }
            return input.bufferedReader().use { reader -> reader.readText() }
        }
    }

    private fun Uri.asLocalFileOrNull(): File? {
        if (!scheme.equals("file", ignoreCase = true)) {
            return null
        }
        return requireLocalFile(this)
    }

    private fun localRootOrNull(rootUri: String): File? {
        return Uri.parse(rootUri).asLocalFileOrNull()
    }

    private fun requireLocalFile(uri: Uri): File {
        val path = requireNotNull(uri.path) { "本地文件 URI 缺少路径：$uri" }
        return File(path)
    }

    private fun snapshotsRoot(): File = File(context.filesDir, "snapshots").apply { mkdirs() }

    private fun backupsRoot(): File = File(context.filesDir, "backups").apply { mkdirs() }

    private fun importsRoot(): File = File(context.cacheDir, "sync_imports").apply { mkdirs() }

    private fun createOperationId(suffix: String): String {
        val timestamp = DateTimeFormatter.ofPattern("yyyyMMdd'T'HHmmss'Z'")
            .format(Instant.now().atOffset(java.time.ZoneOffset.UTC))
        return "$timestamp-$suffix"
    }

    private fun resolveMimeType(name: String): String {
        return when {
            name.endsWith(".json", ignoreCase = true) -> "application/json"
            else -> "text/plain"
        }
    }

    private fun renderDefaultCustomYaml(model: AndroidConfigModel): String {
        return buildString {
            appendLine("patch:")
            appendLine("  schema_list:")
            model.enabledSchemaIds.forEach { schemaId ->
                appendLine("    - schema: \"$schemaId\"")
            }
        }
    }

    private fun renderRimeMintCustomYaml(model: AndroidConfigModel): String {
        return buildString {
            appendLine("patch:")
            if (model.candidatePageSize != null) {
                appendLine("  menu/page_size: ${model.candidatePageSize}")
            }
            appendLine("  style/candidate_list_layout: \"${model.candidateLayout}\"")
            appendLine("  menu/inline_preedit: ${model.showEmojiComments}")
            appendLine("  punctuator/full_shape: ${model.fullShapeEnabled}")
            appendLine("  recognizer/patterns/punct: \"${if (model.asciiPunctEnabled) "[[:punct:]]+" else ""}\"")
            appendLine("  translator/contextual_suggestions: ${model.contextualSuggestionsEnabled}")
            appendLine("  translator/collocation_max_length: ${model.collocationMaxLength}")
            appendLine("  translator/collocation_min_length: ${model.collocationMinLength}")
            appendLine("  translator/max_homophones: ${model.maxHomophones}")
            appendLine("  translator/max_homographs: ${model.maxHomographs}")
            appendLine("  switches:")
            appendLine("    - name: simplification")
            appendLine("      reset: \"${model.simplificationMode}\"")
            appendLine("    - name: emoji_suggestion")
            appendLine("      reset: ${if (model.emojiSuggestionEnabled) 1 else 0}")
            appendLine("    - name: tone_display")
            appendLine("      reset: ${if (model.toneDisplayEnabled) 1 else 0}")
            if (model.fuzzyEnabled && model.fuzzyTargetSchemaIds.contains("rime_mint")) {
                appendLine("  speller/algebra:")
                resolveFuzzyRules(model).forEach { rule ->
                    appendLine("    - \"$rule\"")
                }
            }
        }
    }

    private fun renderT9CustomYaml(model: AndroidConfigModel): String {
        return buildString {
            appendLine("patch:")
            appendLine("  style/candidate_list_layout: \"${model.candidateLayout}\"")
            appendLine("  menu/inline_preedit: ${model.showEmojiComments}")
            appendLine("  punctuator/full_shape: ${model.fullShapeEnabled}")
            appendLine("  recognizer/patterns/punct: \"${if (model.asciiPunctEnabled) "[[:punct:]]+" else ""}\"")
            appendLine("  translator/contextual_suggestions: ${model.contextualSuggestionsEnabled}")
            appendLine("  translator/collocation_max_length: ${model.collocationMaxLength}")
            appendLine("  translator/collocation_min_length: ${model.collocationMinLength}")
            appendLine("  translator/max_homophones: ${model.maxHomophones}")
            appendLine("  translator/max_homographs: ${model.maxHomographs}")
            appendLine("  switches:")
            appendLine("    - name: simplification")
            appendLine("      reset: \"${model.simplificationMode}\"")
            appendLine("    - name: emoji_suggestion")
            appendLine("      reset: ${if (model.emojiSuggestionEnabled) 1 else 0}")
            appendLine("    - name: tone_display")
            appendLine("      reset: ${if (model.toneDisplayEnabled) 1 else 0}")
            if (model.fuzzyEnabled && model.fuzzyTargetSchemaIds.contains("t9")) {
                appendLine("  speller/algebra:")
                resolveFuzzyRules(model).forEach { rule ->
                    appendLine("    - \"$rule\"")
                }
            }
        }
    }

    private fun renderRimeMintDictionaryYaml(model: AndroidConfigModel): String {
        return buildString {
            appendLine("---")
            appendLine("name: rime_mint")
            appendLine("version: \"tracked_by_snapshot\"")
            appendLine("sort: by_weight")
            appendLine("use_preset_vocabulary: true")
            appendLine("import_tables:")
            model.dictionaryOrder.forEach { dictionaryId ->
                appendLine("  - \"$dictionaryId\"")
            }
        }
    }

    private fun renderCustomSimpleDictionaryYaml(model: AndroidConfigModel): String {
        return buildString {
            appendLine("---")
            appendLine("name: custom_simple")
            appendLine("version: \"tracked_by_snapshot\"")
            appendLine("sort: by_weight")
            appendLine("use_preset_vocabulary: true")
            appendLine("...")
            model.customEntries.forEach { entry ->
                appendLine("${entry.text}\t${entry.code}\t${entry.weight}")
            }
        }
    }

    private fun renderWeaselCustomYaml(model: AndroidConfigModel): String {
        return buildString {
            appendLine("patch:")
            appendLine("  style/layout/type: \"${model.candidateLayout}\"")
            appendLine("  show_notifications: ${model.windowsShowNotification}")
            if (model.windowsFontFace.isNotBlank()) {
                appendLine("  style/font_face: \"${model.windowsFontFace}\"")
            }
            if (model.windowsFontPoint > 0) {
                appendLine("  style/font_point: ${model.windowsFontPoint}")
            }
        }
    }

    private fun renderAndroidApplyManifest(model: AndroidConfigModel): String {
        return JSONObject()
            .put("platform", "android")
            .put("carrier_id", "fcitx5_android_rime")
            .put("required_schema_id", model.androidDefaultSchemaId)
            .put("keyboard_layout", model.keyboardLayout)
            .put("candidate_text_size", model.candidateTextSize)
            .put("candidate_view_height", model.candidateViewHeight)
            .put(
                "manual_steps",
                JSONArray()
                    .put(
                        JSONObject()
                            .put("step_id", "grant_android_import_root")
                            .put("title", "授权 Android 导入源目录")
                            .put("next_action", "完成授权后返回应用重新检测"),
                    )
                    .put(
                        JSONObject()
                            .put("step_id", "confirm_android_import")
                            .put("title", "在承载器中确认导入")
                            .put("next_action", "完成导入后返回应用重新检测"),
                    ),
            )
            .put(
                "recheck_items",
                JSONArray()
                    .put("carrier_available")
                    .put("rime_plugin_available")
                    .put("sync_root_authorized")
                    .put("android_import_root_authorized")
                    .put("required_schema_selected")
                    .put("keyboard_layout_applied")
                    .put("t9_delivery_completed"),
            )
            .put("delivery_mode", "import_and_manual_confirm")
            .toString(2)
    }

    private fun renderCustomEntriesJson(model: AndroidConfigModel): String {
        return JSONArray().also { array ->
            model.customEntries.forEach { entry ->
                array.put(
                    JSONObject()
                        .put("text", entry.text)
                        .put("code", entry.code)
                        .put("weight", entry.weight),
                )
            }
        }.toString(2)
    }

    private fun renderConfigSnapshotJson(
        snapshotId: String,
        model: AndroidConfigModel,
    ): String {
        return JSONObject()
            .put("snapshot_id", snapshotId)
            .put("created_at", Instant.now().toString())
            .put("config_version", model.configVersion)
            .put("config_model", model.toFormalJson())
            .put(
                "resolved_resources",
                JSONArray()
                    .put(resourceObject("rime_mint", "schema", "official_current"))
                    .put(resourceObject("t9", "schema", "official_current"))
                    .put(resourceObject("moetype", "dictionary", "product_fixed_decision"))
                    .put(resourceObject("sogou_network_popular_words", "dictionary", "product_fixed_decision"))
                    .put(resourceObject("custom_simple", "dictionary", "official_current")),
            )
            .put(
                "resolved_feature_presets",
                JSONObject()
                    .put("fuzzy_pinyin", model.fuzzyPresetId)
                    .put("symbol_profile", model.symbolProfileId)
                    .put("preedit_profile", model.preeditFormatMode),
            )
            .toString(2)
    }

    private fun renderGenerationSummaryJson(
        snapshotId: String,
        model: AndroidConfigModel,
        androidFiles: Map<String, String>,
        windowsFiles: Map<String, String>,
    ): String {
        return JSONObject()
            .put("snapshot_id", snapshotId)
            .put("config_version", 1)
            .put(
                "resource_versions",
                JSONObject()
                    .put("rime_mint", "tracked_by_snapshot")
                    .put("t9", "tracked_by_snapshot")
                    .put("moetype", "tracked_by_snapshot")
                    .put("sogou_network_popular_words", "tracked_by_snapshot")
                    .put("custom_simple", "tracked_by_snapshot"),
            )
            .put(
                "resolved_defaults",
                JSONObject()
                    .put("windows_default_schema_id", model.windowsDefaultSchemaId)
                    .put("android_default_schema_id", model.androidDefaultSchemaId)
                    .put("keyboard_layout", model.keyboardLayout),
            )
            .put(
                "resolved_feature_presets",
                JSONObject()
                    .put("fuzzy_pinyin", model.fuzzyPresetId)
                    .put("symbol_profile", model.symbolProfileId)
                    .put("preedit_profile", model.preeditFormatMode),
            )
            .put(
                "generated_files_by_platform",
                JSONObject()
                    .put("android", JSONArray(androidFiles.keys.toList()))
                    .put("windows", JSONArray(windowsFiles.keys.toList())),
            )
            .put(
                "shared_output_summary",
                JSONObject()
                    .put(
                        "candidate",
                        JSONObject()
                            .put("page_size", model.candidatePageSize)
                            .put("layout", model.candidateLayout)
                            .put("show_emoji_comments", model.showEmojiComments),
                    )
                    .put(
                        "behavior",
                        JSONObject()
                            .put("simplification_mode", model.simplificationMode)
                            .put("full_shape_enabled", model.fullShapeEnabled)
                            .put("ascii_punct_enabled", model.asciiPunctEnabled),
                    )
                    .put(
                        "fuzzy_pinyin",
                        JSONObject()
                            .put("enabled", model.fuzzyEnabled)
                            .put("preset_id", model.fuzzyPresetId)
                            .put("target_schema_ids", JSONArray(model.fuzzyTargetSchemaIds)),
                    )
                    .put("shortcut_bindings", JSONArray())
                    .put(
                        "personalization",
                        JSONObject()
                            .put("preedit_format_mode", model.preeditFormatMode)
                            .put("custom_phrase_mode", model.customPhraseMode),
                    )
                    .put(
                        "dictionary",
                        JSONObject()
                            .put("enabled_dictionary_ids", JSONArray(model.enabledDictionaryIds))
                            .put("dictionary_order", JSONArray(model.dictionaryOrder))
                            .put("custom_entry_count", model.customEntries.size),
                    )
                    .put(
                        "model_tuning",
                        JSONObject()
                            .put("contextual_suggestions_enabled", model.contextualSuggestionsEnabled)
                            .put("collocation_max_length", model.collocationMaxLength)
                            .put("collocation_min_length", model.collocationMinLength)
                            .put("max_homophones", model.maxHomophones)
                            .put("max_homographs", model.maxHomographs),
                    ),
            )
            .toString(2)
    }

    private fun renderSyncManifestJson(
        snapshotId: String,
        model: AndroidConfigModel,
        configSnapshotJson: String,
        customEntriesJson: String,
        androidFiles: Map<String, String>,
        windowsFiles: Map<String, String>,
        userDictionaryFiles: Map<String, String>,
    ): String {
        val resolvedFeaturePresets = JSONObject()
            .put("fuzzy_pinyin", model.fuzzyPresetId)
            .put("symbol_profile", model.symbolProfileId)
            .put("preedit_profile", model.preeditFormatMode)

        return JSONObject()
            .put("snapshot_id", snapshotId)
            .put(
                "config_payload",
                JSONObject()
                    .put("config_version", 1)
                    .put("payload_path", "config_snapshot.json")
                    .put("sha256", computeSha256(configSnapshotJson)),
            )
            .put(
                "resource_payloads",
                buildResourcePayloads(
                    androidFiles = androidFiles,
                    windowsFiles = windowsFiles,
                    resolvedFeaturePresets = resolvedFeaturePresets,
                ),
            )
            .put(
                "user_data_payloads",
                JSONArray()
                    .put(payloadRef("custom_entries", "custom_entries", "user_data/custom_entries.json", customEntriesJson))
                    .put(
                        JSONObject()
                            .put("payload_id", "user_dict_export_directory")
                            .put("payload_kind", "user_dict_export")
                            .put("path", "user_data/user_dict_exports")
                            .put("sha256", computeCollectionHash(userDictionaryFiles)),
                    ),
            )
            .put(
                "platform_targets",
                JSONObject()
                    .put(
                        "android",
                        JSONObject()
                            .put("files", JSONArray(androidFiles.keys.toList()))
                            .put("success_checks", buildAndroidSuccessChecks()),
                    )
                    .put(
                        "windows",
                        JSONObject()
                            .put("files", JSONArray(windowsFiles.keys.toList()))
                            .put("success_checks", buildWindowsSuccessChecks()),
                    ),
            )
            .put(
                "delivery_plan",
                buildDeliveryPlan(),
            )
            .put(
                "success_criteria",
                JSONObject()
                    .put("config_model", successRule("config_snapshot.json"))
                    .put("formal_resource", successRule("resource_manifest + resolved_feature_presets + generated_template_files"))
                    .put("user_data", successRule("user_data/custom_entries.json + user_dict_exports/"))
                    .put("target_config", successRule("windows/*.yaml + android/*.yaml + android_apply_manifest.json"))
                    .put(
                        "runtime_state",
                        successRule(
                            "android carrier_available + rime_plugin_available + sync_root_authorized + android_import_root_authorized + required_schema_selected + keyboard_layout_applied + t9_delivery_completed; windows weasel_available + target_files_written + deployer_completed + font_resolved + candidate_layout_applied",
                        ),
                    ),
            )
            .put(
                "consistency_hashes",
                JSONObject()
                    .put("default.custom.yaml", computeSha256(windowsFiles["default.custom.yaml"].orEmpty()))
                    .put("rime_mint.custom.yaml", computeSha256(windowsFiles["rime_mint.custom.yaml"].orEmpty()))
                    .put("rime_mint.dict.yaml", computeSha256(windowsFiles["rime_mint.dict.yaml"].orEmpty()))
                    .put("custom_simple.dict.yaml", computeSha256(windowsFiles["custom_simple.dict.yaml"].orEmpty())),
            )
            .toString(2)
    }

    private fun buildResourcePayloads(
        androidFiles: Map<String, String>,
        windowsFiles: Map<String, String>,
        resolvedFeaturePresets: JSONObject,
    ): JSONArray {
        return JSONArray()
            .put(payloadRef("rime_mint", "schema", "windows/rime_mint.custom.yaml", windowsFiles["rime_mint.custom.yaml"].orEmpty()))
            .put(payloadRef("t9", "schema", "android/t9.custom.yaml", androidFiles["t9.custom.yaml"].orEmpty()))
            .put(payloadRef("moetype", "dictionary", "windows/rime_mint.dict.yaml", windowsFiles["rime_mint.dict.yaml"].orEmpty()))
            .put(
                payloadRef(
                    "sogou_network_popular_words",
                    "dictionary",
                    "windows/rime_mint.dict.yaml",
                    windowsFiles["rime_mint.dict.yaml"].orEmpty(),
                ),
            )
            .put(
                payloadRef(
                    "custom_simple_dictionary",
                    "dictionary",
                    "windows/custom_simple.dict.yaml",
                    windowsFiles["custom_simple.dict.yaml"].orEmpty(),
                ),
            )
            .put(
                payloadRef(
                    "default_custom_template",
                    "template",
                    "windows/default.custom.yaml",
                    windowsFiles["default.custom.yaml"].orEmpty(),
                ),
            )
            .put(
                payloadRef(
                    "rime_mint_custom_template",
                    "template",
                    "windows/rime_mint.custom.yaml",
                    windowsFiles["rime_mint.custom.yaml"].orEmpty(),
                ),
            )
            .put(payloadRef("t9_custom_template", "template", "android/t9.custom.yaml", androidFiles["t9.custom.yaml"].orEmpty()))
            .put(
                payloadRef(
                    "weasel_custom_template",
                    "template",
                    "windows/weasel.custom.yaml",
                    windowsFiles["weasel.custom.yaml"].orEmpty(),
                ),
            )
            .put(
                payloadRef(
                    "android_apply_manifest_template",
                    "template",
                    "android/android_apply_manifest.json",
                    androidFiles["android_apply_manifest.json"].orEmpty(),
                ),
            )
            .put(
                payloadRef(
                    "fuzzy_pinyin_preset",
                    "preset",
                    "generation_summary.json#/resolved_feature_presets/fuzzy_pinyin",
                    resolvedFeaturePresets.optString("fuzzy_pinyin"),
                ),
            )
            .put(
                payloadRef(
                    "symbol_profile_preset",
                    "preset",
                    "generation_summary.json#/resolved_feature_presets/symbol_profile",
                    resolvedFeaturePresets.optString("symbol_profile"),
                ),
            )
            .put(
                payloadRef(
                    "preedit_profile_preset",
                    "preset",
                    "generation_summary.json#/resolved_feature_presets/preedit_profile",
                    resolvedFeaturePresets.optString("preedit_profile"),
                ),
            )
    }

    private fun buildAndroidSuccessChecks(): JSONArray {
        return JSONArray()
            .put("carrier_available")
            .put("rime_plugin_available")
            .put("sync_root_authorized")
            .put("android_import_root_authorized")
            .put("required_schema_selected")
            .put("keyboard_layout_applied")
            .put("t9_delivery_completed")
    }

    private fun buildWindowsSuccessChecks(): JSONArray {
        return JSONArray()
            .put("weasel_available")
            .put("target_files_written")
            .put("deployer_completed")
            .put("font_resolved")
            .put("candidate_layout_applied")
    }

    private fun buildDeliveryPlan(): JSONArray {
        return JSONArray()
            .put(deliveryPlan("android", "detect", "android_detect_environment", true))
            .put(deliveryPlan("android", "configure", "android_configure_model", false))
            .put(deliveryPlan("android", "generate", "android_generate_targets", false))
            .put(deliveryPlan("android", "backup", "android_backup_import_root", false))
            .put(deliveryPlan("android", "apply", "android_apply_import_bundle", false))
            .put(deliveryPlan("android", "deploy", "android_deploy_carrier", true))
            .put(deliveryPlan("android", "recheck", "android_recheck_runtime", false))
            .put(deliveryPlan("android", "diagnose", "android_diagnose_result", false))
            .put(deliveryPlan("windows", "detect", "windows_detect_environment", false))
            .put(deliveryPlan("windows", "configure", "windows_configure_model", false))
            .put(deliveryPlan("windows", "generate", "windows_generate_targets", false))
            .put(deliveryPlan("windows", "backup", "windows_backup_target_root", false))
            .put(deliveryPlan("windows", "apply", "windows_apply_target_bundle", false))
            .put(deliveryPlan("windows", "deploy", "windows_deploy_carrier", false))
            .put(deliveryPlan("windows", "recheck", "windows_recheck_runtime", false))
            .put(deliveryPlan("windows", "diagnose", "windows_diagnose_result", false))
    }

    private fun resourceObject(
        resourceId: String,
        resourceKind: String,
        sourceClass: String,
    ): JSONObject {
        return JSONObject()
            .put("resource_id", resourceId)
            .put("resource_kind", resourceKind)
            .put("source_class", sourceClass)
            .put("version_or_updated_at", "tracked_by_snapshot")
    }

    private fun payloadRef(
        payloadId: String,
        payloadKind: String,
        path: String,
        content: String,
    ): JSONObject {
        return JSONObject()
            .put("payload_id", payloadId)
            .put("payload_kind", payloadKind)
            .put("path", path)
            .put("sha256", computeSha256(content))
    }

    private fun deliveryPlan(
        platform: String,
        phase: String,
        taskId: String,
        manualRequired: Boolean,
    ): JSONObject {
        return JSONObject()
            .put("platform", platform)
            .put("phase", phase)
            .put("task_id", taskId)
            .put("manual_required", manualRequired)
    }

    private fun successRule(comparisonBasis: String): JSONObject {
        return JSONObject()
            .put("required", true)
            .put("comparison_basis", comparisonBasis)
    }

    private fun resolveFuzzyRules(model: AndroidConfigModel): List<String> {
        val rules = mutableListOf<String>()
        val seen = mutableSetOf<String>()
        if (model.fuzzyPresetId.isNotBlank()) {
            for (canonical in DEFAULT_FUZZY_RULES) {
                val regex = FUZZY_SIMPLE_TO_REGEX[canonical] ?: canonical
                rules.add(regex)
                seen.add(regex)
            }
        }
        for (rule in model.fuzzyAdditionalRules) {
            val output = FUZZY_SIMPLE_TO_REGEX[rule] ?: rule
            if (seen.add(output)) {
                rules.add(output)
            }
        }
        return rules
    }

    private fun computeSha256(content: String): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(content.toByteArray(Charsets.UTF_8))
        return digest.joinToString(separator = "") { byte -> "%02x".format(byte) }
    }

    private fun computeCollectionHash(files: Map<String, String>): String {
        if (files.isEmpty()) {
            return computeSha256("")
        }

        val canonical = files.entries
            .sortedBy { entry -> entry.key }
            .joinToString(separator = "\n") { (name, content) ->
                "$name:${computeSha256(content)}"
            }
        return computeSha256(canonical)
    }

    private fun extractSnapshotPackage(uri: Uri): File {
        val input = context.contentResolver.openInputStream(uri)
        requireNotNull(input) { "无法读取同步快照。" }
        return extractSnapshotPackage(input)
    }

    private fun extractSnapshotPackage(input: InputStream): File {
        val tempImportRoot = File(importsRoot(), createOperationId("snapshot-import")).apply { mkdirs() }
        input.use { stream ->
            ZipInputStream(stream).use { zip ->
                while (true) {
                    val entry: ZipEntry = zip.nextEntry ?: break
                    val target = File(tempImportRoot, entry.name)
                    if (entry.isDirectory) {
                        target.mkdirs()
                    } else {
                        target.parentFile?.mkdirs()
                        FileOutputStream(target).use { output ->
                            zip.copyTo(output)
                        }
                    }
                    zip.closeEntry()
                }
            }
        }

        val roots = tempImportRoot.listFiles()?.filter(File::isDirectory).orEmpty()
        val extractedRoot = if (File(tempImportRoot, "config_snapshot.json").exists()) {
            tempImportRoot
        } else {
            require(roots.size == 1) { "同步快照压缩包结构不合法。" }
            roots.first()
        }

        val snapshotId = JSONObject(File(extractedRoot, "config_snapshot.json").readText()).optString("snapshot_id")
        require(snapshotId.isNotBlank()) { "同步快照缺少 snapshot_id。" }
        val finalSnapshotRoot = File(snapshotsRoot(), snapshotId)
        if (finalSnapshotRoot.exists()) {
            finalSnapshotRoot.deleteRecursively()
        }
        copyDirectory(extractedRoot, finalSnapshotRoot)
        tempImportRoot.deleteRecursively()
        return finalSnapshotRoot
    }

    private fun readFiles(root: File): Map<String, String> {
        if (!root.exists() || !root.isDirectory) {
            return emptyMap()
        }
        return root.listFiles()
            ?.filter(File::isFile)
            ?.sortedBy(File::getName)
            ?.associate { file -> file.name to file.readText() }
            .orEmpty()
    }

    private fun parseEnabledSchemaIds(content: String?): List<String>? {
        if (content.isNullOrBlank()) {
            return null
        }
        return content.lineSequence()
            .map(String::trim)
            .filter { line -> line.startsWith("- schema:") }
            .map { line -> line.substringAfter(':').trim().trim('"') }
            .toList()
            .takeIf(List<String>::isNotEmpty)
    }

    private fun parseSharedYaml(content: String?): ParsedSharedYaml? {
        if (content.isNullOrBlank()) {
            return null
        }
        val lines = content.lines()
        val fuzzyRules = mutableListOf<String>()
        var readingFuzzy = false
        lines.forEach { rawLine ->
            val line = rawLine.trim()
            when {
                line == "speller/algebra:" -> readingFuzzy = true
                readingFuzzy && line.startsWith("- ") -> fuzzyRules += line.removePrefix("- ").trim().trim('"')
                readingFuzzy && !line.startsWith("- ") -> readingFuzzy = false
            }
        }

        return ParsedSharedYaml(
            candidateLayout = parseQuotedValue(lines, "style/candidate_list_layout"),
            showEmojiComments = parseBooleanValue(lines, "menu/inline_preedit"),
            simplificationMode = parseSwitchReset(lines, "simplification"),
            fullShapeEnabled = parseBooleanValue(lines, "punctuator/full_shape"),
            asciiPunctEnabled = parseQuotedValue(lines, "recognizer/patterns/punct")?.isNotBlank(),
            emojiSuggestionEnabled = parseSwitchReset(lines, "emoji_suggestion")?.let { it == "1" },
            toneDisplayEnabled = parseSwitchReset(lines, "tone_display")?.let { it == "1" },
            contextualSuggestionsEnabled = parseBooleanValue(lines, "translator/contextual_suggestions"),
            collocationMaxLength = parseIntValue(lines, "translator/collocation_max_length"),
            collocationMinLength = parseIntValue(lines, "translator/collocation_min_length"),
            maxHomophones = parseIntValue(lines, "translator/max_homophones"),
            maxHomographs = parseIntValue(lines, "translator/max_homographs"),
            fuzzyRules = fuzzyRules,
        )
    }

    private fun parseDictionaryOrder(content: String?): List<String>? {
        if (content.isNullOrBlank()) {
            return null
        }
        return content.lineSequence()
            .map(String::trim)
            .filter { line -> line.startsWith("- ") }
            .map { line -> line.removePrefix("- ").trim().trim('"') }
            .toList()
            .takeIf(List<String>::isNotEmpty)
    }

    private fun parseCustomEntries(content: String?): List<AndroidCustomEntry> {
        if (content.isNullOrBlank()) {
            return emptyList()
        }
        return content.lineSequence()
            .dropWhile { line -> line.trim() != "..." }
            .drop(1)
            .map(String::trim)
            .filter(String::isNotBlank)
            .mapNotNull { line ->
                val parts = line.split('\t')
                if (parts.size < 3) {
                    null
                } else {
                    AndroidCustomEntry(
                        text = parts[0],
                        code = parts[1],
                        weight = parts[2].toIntOrNull() ?: 1,
                    )
                }
            }
            .toList()
    }

    private fun parseAndroidManifest(content: String?): ParsedAndroidManifest? {
        if (content.isNullOrBlank()) {
            return null
        }
        val json = JSONObject(content)
        return ParsedAndroidManifest(
            requiredSchemaId = json.optString("required_schema_id"),
            keyboardLayout = json.optString("keyboard_layout"),
            candidateTextSize = json.optInt("candidate_text_size"),
            candidateViewHeight = json.optInt("candidate_view_height"),
        )
    }

    private fun parseQuotedValue(
        lines: List<String>,
        key: String,
    ): String? {
        return lines.firstOrNull { it.trim().startsWith("$key:") }
            ?.substringAfter(':')
            ?.trim()
            ?.trim('"')
    }

    private fun parseBooleanValue(
        lines: List<String>,
        key: String,
    ): Boolean? {
        return parseQuotedValue(lines, key)?.lowercase()?.let { value ->
            when (value) {
                "true" -> true
                "false" -> false
                else -> null
            }
        }
    }

    private fun parseIntValue(
        lines: List<String>,
        key: String,
    ): Int? {
        return parseQuotedValue(lines, key)?.toIntOrNull()
    }

    private fun parseSwitchReset(
        lines: List<String>,
        switchName: String,
    ): String? {
        val index = lines.indexOfFirst { it.trim() == "- name: $switchName" }
        if (index == -1) {
            return null
        }
        return lines.drop(index + 1)
            .firstOrNull { it.trim().startsWith("reset:") }
            ?.substringAfter(':')
            ?.trim()
            ?.trim('"')
    }

    private fun copyDirectory(
        source: File,
        target: File,
    ) {
        if (source.isDirectory) {
            target.mkdirs()
            source.listFiles()?.forEach { child ->
                copyDirectory(child, File(target, child.name))
            }
            return
        }

        target.parentFile?.mkdirs()
        source.copyTo(target, overwrite = true)
    }

    private data class ParsedSharedYaml(
        val candidateLayout: String?,
        val showEmojiComments: Boolean?,
        val simplificationMode: String?,
        val fullShapeEnabled: Boolean?,
        val asciiPunctEnabled: Boolean?,
        val emojiSuggestionEnabled: Boolean?,
        val toneDisplayEnabled: Boolean?,
        val contextualSuggestionsEnabled: Boolean?,
        val collocationMaxLength: Int?,
        val collocationMinLength: Int?,
        val maxHomophones: Int?,
        val maxHomographs: Int?,
        val fuzzyRules: List<String>,
    )

    private data class ParsedAndroidManifest(
        val requiredSchemaId: String,
        val keyboardLayout: String,
        val candidateTextSize: Int,
        val candidateViewHeight: Int,
    )

    private companion object {
        private const val KEY_LATEST_SNAPSHOT_ID = "latest_snapshot_id"
        private const val KEY_LATEST_BACKUP_ID = "latest_backup_id"
        private const val KEY_GENERATED_FILES = "generated_files"
        private const val KEY_LATEST_APPLIED_SNAPSHOT_ID = "latest_applied_snapshot_id"
        private const val KEY_LAST_RECHECK_SUMMARY = "last_recheck_summary"

        private val DEFAULT_FUZZY_RULES = listOf(
            "derive/zh/z",
            "derive/ch/c",
            "derive/sh/s",
        )

        private val FUZZY_SIMPLE_TO_REGEX = mapOf(
            "derive/zh/z" to "derive/^zh([a-z]+)$/z$1/",
            "derive/ch/c" to "derive/^ch([a-z]+)$/c$1/",
            "derive/sh/s" to "derive/^sh([a-z]+)$/s$1/",
        )

        private val FUZZY_REGEX_TO_SIMPLE = mapOf(
            "derive/^zh([a-z]+)$/z$1/" to "derive/zh/z",
            "derive/^ch([a-z]+)$/c$1/" to "derive/ch/c",
            "derive/^sh([a-z]+)$/s$1/" to "derive/sh/s",
        )
    }
}
