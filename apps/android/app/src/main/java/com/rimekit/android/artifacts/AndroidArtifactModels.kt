package com.rimekit.android.artifacts

/**
 * Android 生成结果。
 */
data class AndroidGeneratedArtifacts(
    val snapshotId: String,
    val snapshotDirectoryName: String,
    val androidFiles: Map<String, String>,
    val windowsFiles: Map<String, String>,
    val userDictionaryFiles: Map<String, String>,
)

/**
 * Android 备份结果。
 */
data class AndroidBackupResult(
    val backupId: String,
    val backupDirectoryName: String,
)

data class AndroidBackupEntry(
    val backupId: String,
    val snapshotId: String?,
    val createdAt: String?,
)

/**
 * Android 产物流状态。
 */
data class AndroidArtifactState(
    val latestSnapshotId: String? = null,
    val latestBackupId: String? = null,
    val generatedFileNames: List<String> = emptyList(),
    val latestAppliedSnapshotId: String? = null,
    val lastRecheckSummary: String? = null,
)

data class AndroidRecheckResult(
    val missingFiles: List<String>,
    val expectedFiles: List<String>,
)

data class AndroidStagedSyncSnapshot(
    val snapshotId: String,
    val snapshotDirectoryName: String,
    val configModel: AndroidConfigModel,
    val androidFiles: Map<String, String>,
    val windowsFiles: Map<String, String>,
    val userDictionaryFiles: Map<String, String>,
)
