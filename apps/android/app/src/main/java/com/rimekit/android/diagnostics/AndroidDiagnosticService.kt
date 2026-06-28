package com.rimekit.android.diagnostics

import android.content.Context
import com.rimekit.android.artifacts.AndroidArtifactState
import com.rimekit.android.artifacts.AndroidConfigValidationIssue
import com.rimekit.android.workflow.AndroidAutoActionKinds
import com.rimekit.android.workflow.AndroidDisplayKinds
import com.rimekit.android.workflow.AndroidEntryPointKinds
import com.rimekit.android.workflow.AndroidFinding
import com.rimekit.android.workflow.AndroidPlatformSnapshot
import com.rimekit.android.workflow.AndroidWorkflowPhase
import com.rimekit.android.workflow.AndroidWorkflowStatus
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.Instant

/**
 * Android 诊断报告生成与留痕服务。
 */
class AndroidDiagnosticService(
    private val context: Context,
    private val errorCodesLoader: () -> String = {
        context.assets.open("error_codes.json").bufferedReader().use { reader -> reader.readText() }
    },
    private val taskManifestLoader: () -> String = {
        context.assets.open("android_tasks.json").bufferedReader().use { reader -> reader.readText() }
    },
) {
    private val errorCodeDefinitions: Map<String, AndroidErrorCodeDefinition> by lazy {
        loadErrorCodeDefinitions()
    }
    private val taskDefinitions: Map<String, AndroidTaskDefinition> by lazy {
        loadTaskDefinitions()
    }

    fun buildFailureReport(
        phase: AndroidWorkflowPhase,
        taskId: String?,
        code: String,
        detail: String,
        artifactState: AndroidArtifactState,
        conflictScope: String? = null,
    ): AndroidDiagnosticReport {
        val definition = definitionFor(code)
        val taskDefinition = taskId?.let(taskDefinitions::get)
        val finding = AndroidDiagnosticFinding(
            code = code,
            severity = definition.severity,
            summary = definition.summary,
            detail = detail,
            displayKind = taskDefinition?.messageKind ?: definition.displayKind,
            autoActionKind = taskDefinition?.autoActionKind ?: definition.autoActionKind,
            entryPointKind = definition.entryPointKind,
            conflictScope = conflictScope,
            relatedTaskId = taskId,
        )
        val targetStateMutated = when (phase) {
            AndroidWorkflowPhase.Apply,
            AndroidWorkflowPhase.Deploy,
            AndroidWorkflowPhase.Recheck,
                -> artifactState.latestAppliedSnapshotId != null
            AndroidWorkflowPhase.Rollback -> artifactState.latestBackupId != null
            else -> false
        }
        val rollbackAvailable = artifactState.latestBackupId != null
        val rollbackRecommended = targetStateMutated && rollbackAvailable && phase != AndroidWorkflowPhase.Rollback

        return AndroidDiagnosticReport(
            phase = phase.id,
            status = AndroidWorkflowStatus.Failed.id,
            findings = listOf(finding),
            nextAction = definition.nextAction,
            snapshotId = artifactState.latestSnapshotId,
            targetStateMutated = targetStateMutated,
            rollbackAvailable = rollbackAvailable,
            rollbackRecommended = rollbackRecommended,
            displayKind = finding.displayKind,
            entryPoints = buildEntryPoints(listOf(finding), rollbackAvailable, rollbackRecommended),
            backupId = artifactState.latestBackupId,
        )
    }

    fun buildBlockedReport(
        phase: AndroidWorkflowPhase,
        issues: List<AndroidConfigValidationIssue>,
        artifactState: AndroidArtifactState,
        nextAction: String? = null,
    ): AndroidDiagnosticReport {
        val findings = issues.map { issue ->
            val definition = definitionFor(issue.code)
            AndroidDiagnosticFinding(
                code = issue.code,
                severity = definition.severity,
                summary = definition.summary,
                detail = issue.detail,
                displayKind = definition.displayKind,
                autoActionKind = definition.autoActionKind,
                entryPointKind = definition.entryPointKind,
                conflictScope = issue.conflictScope,
            )
        }
        val displayKind = findings.firstOrNull()?.displayKind ?: AndroidDisplayKinds.ExplicitError
        val rollbackAvailable = artifactState.latestBackupId != null
        return AndroidDiagnosticReport(
            phase = phase.id,
            status = AndroidWorkflowStatus.Blocked.id,
            findings = findings,
            nextAction = nextAction ?: findings.firstOrNull()?.let { definitionFor(it.code).nextAction }.orEmpty(),
            snapshotId = artifactState.latestSnapshotId,
            targetStateMutated = artifactState.latestAppliedSnapshotId != null,
            rollbackAvailable = rollbackAvailable,
            rollbackRecommended = false,
            displayKind = displayKind,
            entryPoints = buildEntryPoints(findings, rollbackAvailable, false),
            backupId = artifactState.latestBackupId,
        )
    }

    fun loadLastReport(): AndroidDiagnosticReport? {
        val stateRoot = File(context.filesDir, "state")
        val file = File(stateRoot, "last_diagnostic.json")
        if (!file.exists()) {
            return null
        }
        val json = JSONObject(file.readText())
        val findings = buildList {
            val array = json.optJSONArray("findings") ?: JSONArray()
            for (index in 0 until array.length()) {
                val item = array.getJSONObject(index)
                add(
                    AndroidDiagnosticFinding(
                        code = item.optString("code"),
                        severity = item.optString("severity"),
                        summary = item.optString("summary"),
                        detail = item.optString("detail"),
                        displayKind = item.optString("display_kind", AndroidDisplayKinds.ExplicitError),
                        autoActionKind = item.optString("auto_action_kind", AndroidAutoActionKinds.None),
                        entryPointKind = item.optString("entry_point_kind", AndroidEntryPointKinds.None),
                        conflictScope = item.optString("conflict_scope").takeIf(String::isNotBlank),
                        relatedTaskId = item.optString("related_task_id").takeIf(String::isNotBlank),
                    ),
                )
            }
        }
        val entryPoints = buildList {
            val array = json.optJSONArray("entry_points") ?: JSONArray()
            for (index in 0 until array.length()) {
                val item = array.getJSONObject(index)
                add(
                    AndroidEntryPoint(
                        kind = item.optString("kind"),
                        label = item.optString("label"),
                        target = item.optString("target").takeIf(String::isNotBlank),
                    ),
                )
            }
        }
        return AndroidDiagnosticReport(
            platform = json.optString("platform", "android"),
            phase = json.optString("phase"),
            status = json.optString("status"),
            findings = findings,
            nextAction = json.optString("next_action"),
            snapshotId = json.optString("snapshot_id").takeIf(String::isNotBlank),
            targetStateMutated = json.optBoolean("target_state_mutated"),
            rollbackAvailable = json.optBoolean("rollback_available"),
            rollbackRecommended = json.optBoolean("rollback_recommended"),
            displayKind = json.optString("display_kind", AndroidDisplayKinds.None),
            entryPoints = entryPoints,
            backupId = json.optString("backup_id").takeIf(String::isNotBlank),
        )
    }

    fun buildReport(
        phase: AndroidWorkflowPhase,
        status: AndroidWorkflowStatus,
        findings: List<AndroidFinding>,
        snapshot: AndroidPlatformSnapshot,
        artifactState: AndroidArtifactState,
        operationMessage: String?,
    ): AndroidDiagnosticReport {
        val diagnosticFindings = findings.map { finding ->
            val definition = definitionFor(finding.code)
            AndroidDiagnosticFinding(
                code = finding.code,
                severity = definition.severity,
                summary = finding.summary,
                detail = finding.detail,
                displayKind = finding.displayKind,
                autoActionKind = finding.autoActionKind,
                entryPointKind = finding.entryPointKind,
                conflictScope = if (finding.code == "STATE_MISMATCH") "runtime_state" else null,
            )
        }

        val targetStateMutated = artifactState.latestAppliedSnapshotId != null
        val rollbackAvailable = artifactState.latestBackupId != null
        val rollbackRecommended = status == AndroidWorkflowStatus.Failed && rollbackAvailable
        val nextAction = when {
            status == AndroidWorkflowStatus.Completed && diagnosticFindings.isEmpty() ->
                "当前 Android 闭环已完成。"
            operationMessage != null -> operationMessage
            diagnosticFindings.isNotEmpty() -> definitionFor(diagnosticFindings.first().code).nextAction
            snapshot.importRootUri == null -> "先完成 Android 导入源目录授权。"
            else -> "继续执行下一正式阶段。"
        }
        val displayKind = when {
            diagnosticFindings.isNotEmpty() -> diagnosticFindings.first().displayKind
            status == AndroidWorkflowStatus.ManualActionRequired -> AndroidDisplayKinds.ExplicitPrompt
            else -> AndroidDisplayKinds.None
        }

        return AndroidDiagnosticReport(
            phase = phase.id,
            status = status.id,
            findings = diagnosticFindings,
            nextAction = nextAction,
            snapshotId = artifactState.latestSnapshotId,
            targetStateMutated = targetStateMutated,
            rollbackAvailable = rollbackAvailable,
            rollbackRecommended = rollbackRecommended,
            displayKind = displayKind,
            entryPoints = buildEntryPoints(diagnosticFindings, rollbackAvailable, rollbackRecommended),
            backupId = artifactState.latestBackupId,
        )
    }

    fun persist(report: AndroidDiagnosticReport) {
        val diagnosticsRoot = File(context.filesDir, "diagnostics").apply { mkdirs() }
        val stateRoot = File(context.filesDir, "state").apply { mkdirs() }
        val logRoot = File(context.filesDir, "logs").apply { mkdirs() }
        val fileName = "${Instant.now().toString().replace(':', '-')}-android-diagnostic.json"
        val target = File(diagnosticsRoot, fileName)
        val payload = report.toJson()
        target.writeText(payload)
        File(stateRoot, "last_diagnostic.json").writeText(payload)

        val phaseLogRoot = File(logRoot, report.phase).apply { mkdirs() }
        val logFile = File(phaseLogRoot, "${Instant.now().toString().replace(':', '-')}-android-${report.phase}.log")
        logFile.writeText(
            JSONObject()
                .put("timestamp", Instant.now().toString())
                .put("platform", report.platform)
                .put("phase", report.phase)
                .put("status", report.status)
                .put("message_kind", report.displayKind)
                .put("next_action", report.nextAction)
                .put("snapshot_id", report.snapshotId)
                .put("backup_id", report.backupId)
                .put("target_state_mutated", report.targetStateMutated)
                .put("rollback_available", report.rollbackAvailable)
                .put("rollback_recommended", report.rollbackRecommended)
                .put("entry_points", entryPointsToJson(report.entryPoints))
                .put("findings", findingsToJson(report.findings))
                .toString(2),
        )
    }

    private fun AndroidDiagnosticReport.toJson(): String {
        return JSONObject()
            .put("platform", platform)
            .put("phase", phase)
            .put("status", status)
            .put("findings", findingsToJson(findings))
            .put("next_action", nextAction)
            .put("snapshot_id", snapshotId)
            .put("backup_id", backupId)
            .put("target_state_mutated", targetStateMutated)
            .put("rollback_available", rollbackAvailable)
            .put("rollback_recommended", rollbackRecommended)
            .put("display_kind", displayKind)
            .put("entry_points", entryPointsToJson(entryPoints))
            .toString(2)
    }

    private fun findingsToJson(findings: List<AndroidDiagnosticFinding>): JSONArray {
        val array = JSONArray()
        findings.forEach { finding ->
            array.put(
                JSONObject()
                    .put("code", finding.code)
                    .put("severity", finding.severity)
                    .put("summary", finding.summary)
                    .put("detail", finding.detail)
                    .put("display_kind", finding.displayKind)
                    .put("auto_action_kind", finding.autoActionKind)
                    .put("entry_point_kind", finding.entryPointKind)
                    .apply {
                        if (!finding.conflictScope.isNullOrBlank()) {
                            put("conflict_scope", finding.conflictScope)
                        }
                        if (!finding.relatedTaskId.isNullOrBlank()) {
                            put("related_task_id", finding.relatedTaskId)
                        }
                    },
            )
        }
        return array
    }

    private fun entryPointsToJson(entryPoints: List<AndroidEntryPoint>): JSONArray {
        val array = JSONArray()
        entryPoints.forEach { entryPoint ->
            array.put(
                JSONObject()
                    .put("kind", entryPoint.kind)
                    .put("label", entryPoint.label)
                    .apply {
                        if (!entryPoint.target.isNullOrBlank()) {
                            put("target", entryPoint.target)
                        }
                    },
            )
        }
        return array
    }

    private fun buildEntryPoints(
        findings: List<AndroidDiagnosticFinding>,
        rollbackAvailable: Boolean,
        rollbackRecommended: Boolean,
    ): List<AndroidEntryPoint> {
        val entryPoints = linkedMapOf<String, AndroidEntryPoint>()
        findings.forEach { finding ->
            val taskEntryPoints = finding.relatedTaskId?.let(taskDefinitions::get)?.entryPoints
            if (!taskEntryPoints.isNullOrEmpty()) {
                taskEntryPoints.forEach { kind ->
                    if (kind != AndroidEntryPointKinds.None && !entryPoints.containsKey(kind)) {
                        entryPoints[kind] = AndroidEntryPoint(
                            kind = kind,
                            label = entryPointLabel(kind),
                            target = entryPointTarget(kind),
                        )
                    }
                }
            } else {
                val kind = finding.entryPointKind
                if (kind != AndroidEntryPointKinds.None && !entryPoints.containsKey(kind)) {
                    entryPoints[kind] = AndroidEntryPoint(
                        kind = kind,
                        label = entryPointLabel(kind),
                        target = entryPointTarget(kind),
                    )
                }
            }
        }
        if ((rollbackAvailable || rollbackRecommended) && !entryPoints.containsKey(AndroidEntryPointKinds.Rollback)) {
            entryPoints[AndroidEntryPointKinds.Rollback] = AndroidEntryPoint(
                kind = AndroidEntryPointKinds.Rollback,
                label = entryPointLabel(AndroidEntryPointKinds.Rollback),
            )
        }
        return entryPoints.values.toList()
    }

    private fun loadErrorCodeDefinitions(): Map<String, AndroidErrorCodeDefinition> {
        val payload = errorCodesLoader()
        val root = JSONObject(payload)
        val array = root.getJSONArray("codes")
        val definitions = mutableMapOf<String, AndroidErrorCodeDefinition>()
        for (index in 0 until array.length()) {
            val item = array.getJSONObject(index)
            definitions[item.getString("code")] = AndroidErrorCodeDefinition(
                summary = item.getString("default_summary"),
                nextAction = item.getString("recommended_next_action"),
                severity = item.getString("severity"),
                displayKind = item.getString("display_kind"),
                autoActionKind = item.getString("auto_action_kind"),
                entryPointKind = item.getString("entry_point_kind"),
            )
        }
        return definitions
    }

    private fun loadTaskDefinitions(): Map<String, AndroidTaskDefinition> {
        val payload = taskManifestLoader()
        val root = JSONObject(payload)
        val array = root.getJSONArray("tasks")
        val definitions = mutableMapOf<String, AndroidTaskDefinition>()
        for (index in 0 until array.length()) {
            val item = array.getJSONObject(index)
            definitions[item.getString("task_id")] = AndroidTaskDefinition(
                messageKind = item.getString("message_kind"),
                autoActionKind = item.getString("auto_action_kind"),
                entryPoints = buildList {
                    val entryPoints = item.getJSONArray("entry_points")
                    for (entryIndex in 0 until entryPoints.length()) {
                        add(entryPoints.getString(entryIndex))
                    }
                },
            )
        }
        return definitions
    }

    private fun definitionFor(code: String): AndroidErrorCodeDefinition {
        return errorCodeDefinitions[code] ?: AndroidErrorCodeDefinition(
            summary = code,
            nextAction = "请根据当前诊断结果继续处理。",
            severity = "fatal",
            displayKind = AndroidDisplayKinds.ExplicitError,
            autoActionKind = AndroidAutoActionKinds.None,
            entryPointKind = AndroidEntryPointKinds.None,
        )
    }

    private fun entryPointLabel(kind: String): String {
        return when (kind) {
            AndroidEntryPointKinds.InstallUrl -> "打开安装入口"
            AndroidEntryPointKinds.SettingsDeepLink -> "打开系统设置"
            AndroidEntryPointKinds.InputMethodPicker -> "打开输入法选择器"
            AndroidEntryPointKinds.DirectoryAuthorization -> "补齐目录授权"
            AndroidEntryPointKinds.DeployConfirmation -> "完成部署确认"
            AndroidEntryPointKinds.DirectoryOpen -> "打开目录"
            AndroidEntryPointKinds.LogsOpen -> "打开日志"
            AndroidEntryPointKinds.Retry -> "重新检测或重试"
            AndroidEntryPointKinds.Rollback -> "执行回滚"
            else -> kind
        }
    }

    private fun entryPointTarget(kind: String): String? {
        return when (kind) {
            AndroidEntryPointKinds.InstallUrl -> "https://f-droid.org/packages/org.fcitx.fcitx5.android/"
            else -> null
        }
    }
}

private data class AndroidErrorCodeDefinition(
    val summary: String,
    val nextAction: String,
    val severity: String,
    val displayKind: String,
    val autoActionKind: String,
    val entryPointKind: String,
)

private data class AndroidTaskDefinition(
    val messageKind: String,
    val autoActionKind: String,
    val entryPoints: List<String>,
)
