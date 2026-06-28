package com.rimekit.android.exports

import android.content.Context
import android.net.Uri
import com.rimekit.android.artifacts.AndroidArtifactState
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/**
 * Android 导出服务。
 */
class AndroidExportService(
    private val context: Context,
) {
    fun exportConfigModel(
        uri: Uri,
        configJson: String,
    ) {
        writeText(uri, configJson)
    }

    fun exportLatestDiagnostic(uri: Uri) {
        val file = File(File(context.filesDir, "state"), "last_diagnostic.json")
        require(file.exists()) { "当前没有可导出的诊断结果。" }
        writeText(uri, file.readText())
    }

    fun exportLatestSnapshot(
        uri: Uri,
        artifactState: AndroidArtifactState,
    ) {
        val snapshotId = requireNotNull(artifactState.latestSnapshotId) { "当前没有可导出的快照。" }
        val directory = File(File(context.filesDir, "snapshots"), snapshotId)
        require(directory.exists()) { "快照目录不存在：$snapshotId" }
        zipDirectory(uri, directory)
    }

    fun exportLatestBackup(
        uri: Uri,
        artifactState: AndroidArtifactState,
    ) {
        val backupId = requireNotNull(artifactState.latestBackupId) { "当前没有可导出的备份。" }
        exportBackupById(uri, backupId)
    }

    fun exportBackupById(
        uri: Uri,
        backupId: String,
    ) {
        val directory = File(File(context.filesDir, "backups"), backupId)
        require(directory.exists()) { "备份目录不存在：$backupId" }
        zipDirectory(uri, directory)
    }

    fun exportResourceManifest(uri: Uri) {
        val payload = context.assets.open("resource_manifest.json").bufferedReader().use { reader -> reader.readText() }
        writeText(uri, payload)
    }

    fun exportResourceUpdateReport(uri: Uri) {
        val file = File(File(context.filesDir, "state"), "last_resource_update_report.json")
        require(file.exists()) { "当前没有可导出的资源更新检查结果。" }
        writeText(uri, file.readText())
    }

    private fun writeText(
        uri: Uri,
        content: String,
    ) {
        context.contentResolver.openOutputStream(uri, "wt").use { stream ->
            requireNotNull(stream) { "无法打开导出目标。" }
            stream.writer().use { writer ->
                writer.write(content)
            }
        }
    }

    private fun zipDirectory(
        uri: Uri,
        root: File,
    ) {
        context.contentResolver.openOutputStream(uri, "w").use { stream ->
            requireNotNull(stream) { "无法打开导出目标。" }
            ZipOutputStream(stream).use { zip ->
                addDirectory(zip, root, root)
            }
        }
    }

    private fun addDirectory(
        zip: ZipOutputStream,
        root: File,
        current: File,
    ) {
        current.listFiles()?.sortedBy { file -> file.name }?.forEach { file ->
            val relativePath = root.toPath().relativize(file.toPath()).toString().replace('\\', '/')
            if (file.isDirectory) {
                addDirectory(zip, root, file)
            } else {
                zip.putNextEntry(ZipEntry(relativePath))
                file.inputStream().use { input ->
                    input.copyTo(zip)
                }
                zip.closeEntry()
            }
        }
    }
}
