package com.rimekit.android.diagnostics

/**
 * Android 诊断项。
 */
data class AndroidDiagnosticFinding(
    val code: String,
    val severity: String,
    val summary: String,
    val detail: String,
    val displayKind: String,
    val autoActionKind: String,
    val entryPointKind: String,
    val conflictScope: String? = null,
    val relatedTaskId: String? = null,
)

data class AndroidEntryPoint(
    val kind: String,
    val label: String,
    val target: String? = null,
)

/**
 * Android 结构化诊断报告。
 */
data class AndroidDiagnosticReport(
    val platform: String = "android",
    val phase: String,
    val status: String,
    val findings: List<AndroidDiagnosticFinding>,
    val nextAction: String,
    val snapshotId: String?,
    val targetStateMutated: Boolean,
    val rollbackAvailable: Boolean,
    val rollbackRecommended: Boolean,
    val displayKind: String,
    val entryPoints: List<AndroidEntryPoint>,
    val backupId: String? = null,
)
