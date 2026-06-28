package com.rimekit.android.workflow

import com.rimekit.android.artifacts.AndroidArtifactState
import com.rimekit.android.artifacts.AndroidBackupEntry
import com.rimekit.android.artifacts.AndroidConfigModel
import com.rimekit.android.artifacts.AndroidResourceManifest
import com.rimekit.android.diagnostics.AndroidDiagnosticReport

/**
 * Android 正式阶段枚举。
 */
enum class AndroidWorkflowPhase(
    val id: String,
    val title: String,
) {
    Detect("detect", "检测"),
    Configure("configure", "配置"),
    Generate("generate", "生成"),
    Backup("backup", "备份"),
    Apply("apply", "应用"),
    Deploy("deploy", "部署"),
    Recheck("recheck", "回检"),
    Rollback("rollback", "回滚"),
    Diagnose("diagnose", "诊断"),
}

/**
 * 正式结果语义。
 */
enum class AndroidWorkflowStatus(val id: String) {
    Ready("ready"),
    ManualActionRequired("manual_action_required"),
    Blocked("blocked"),
    Failed("failed"),
    Completed("completed"),
}

object AndroidDisplayKinds {
    const val ExplicitWarning = "explicit_warning"
    const val ExplicitError = "explicit_error"
    const val ExplicitPrompt = "explicit_prompt"
    const val None = "none"
}

object AndroidAutoActionKinds {
    const val None = "none"
    const val DetectOnly = "detect_only"
    const val InstallRequest = "install_request"
    const val ReinstallRequest = "reinstall_request"
    const val RepairCheck = "repair_check"
    const val OpenSettings = "open_settings"
    const val OpenPicker = "open_picker"
    const val OpenDirectory = "open_directory"
    const val OpenLogs = "open_logs"
    const val RetryExecution = "retry_execution"
}

object AndroidEntryPointKinds {
    const val None = "none"
    const val InstallUrl = "install_url"
    const val InstallerLaunch = "installer_launch"
    const val UninstallLaunch = "uninstall_launch"
    const val SettingsDeepLink = "settings_deep_link"
    const val InputMethodPicker = "input_method_picker"
    const val DirectoryAuthorization = "directory_authorization"
    const val DeployConfirmation = "deploy_confirmation"
    const val DirectoryOpen = "directory_open"
    const val LogsOpen = "logs_open"
    const val Retry = "retry"
    const val Rollback = "rollback"
}

/**
 * Android 页面导航分区。
 */
enum class WorkflowSection(val title: String) {
    Welcome("欢迎"),
    Detect("检测"),
    Authorize("授权"),
    Configure("配置"),
    Resources("词库模型"),
    Sync("同步"),
    Backup("备份"),
    Deploy("部署"),
    Diagnose("诊断"),
}

/**
 * 平台探测状态。
 */
enum class ProbeState {
    Unknown,
    Present,
    Missing,
}

/**
 * Android 目录授权状态。
 */
enum class PermissionState {
    Unknown,
    Granted,
    Missing,
}

/**
 * Android 输入法状态。
 */
enum class ImeState {
    Unknown,
    Enabled,
    Missing,
}

/**
 * Android 手动确认状态。
 *
 * 这类状态只表示“用户是否已经在应用内确认完成某个手动步骤”，
 * 不是平台真实探测得到的运行态事实。
 */
enum class ManualConfirmationState {
    Unknown,
    Confirmed,
    Missing,
}

/**
 * Android 平台事实快照。
 */
data class AndroidPlatformSnapshot(
    val carrierState: ProbeState = ProbeState.Unknown,
    val rimePluginState: ProbeState = ProbeState.Unknown,
    val carrierVersion: String? = null,
    val rimePluginVersion: String? = null,
    val syncRootPermission: PermissionState = PermissionState.Unknown,
    val importRootPermission: PermissionState = PermissionState.Unknown,
    val imeEnabledState: ImeState = ImeState.Unknown,
    val imeSelectedState: ImeState = ImeState.Unknown,
    val requiredSchemaApplied: ProbeState = ProbeState.Missing,
    val keyboardLayoutApplied: ProbeState = ProbeState.Unknown,
    val deliveryConfirmation: ManualConfirmationState = ManualConfirmationState.Unknown,
    val syncRootUri: String? = null,
    val importRootUri: String? = null,
    val carrierUpdateSource: String? = null,
    val rimePluginUpdateSource: String? = null,
)

/**
 * 诊断项。
 */
data class AndroidFinding(
    val code: String,
    val summary: String,
    val detail: String,
    val displayKind: String = AndroidDisplayKinds.ExplicitError,
    val autoActionKind: String = AndroidAutoActionKinds.None,
    val entryPointKind: String = AndroidEntryPointKinds.None,
)

/**
 * 手动步骤。
 */
data class AndroidManualStep(
    val stepId: String,
    val title: String,
    val nextAction: String,
    val entryPointKind: String = AndroidEntryPointKinds.None,
)

/**
 * 单阶段状态。
 */
data class AndroidPhaseState(
    val phase: AndroidWorkflowPhase,
    val status: AndroidWorkflowStatus,
    val summary: String,
    val nextAction: String,
)

/**
 * Android 页面统一状态。
 */
data class AndroidWorkflowUiState(
    val selectedSection: WorkflowSection = WorkflowSection.Welcome,
    val currentPhase: String = AndroidWorkflowPhase.Detect.id,
    val currentStatus: String = AndroidWorkflowStatus.Ready.id,
    val nextAction: String = "执行检测，开始建立 Android 承载器、授权、导入与回检的真实平台状态。",
    val phaseStates: List<AndroidPhaseState> = emptyList(),
    val findings: List<AndroidFinding> = emptyList(),
    val manualSteps: List<AndroidManualStep> = emptyList(),
    val platformSnapshot: AndroidPlatformSnapshot = AndroidPlatformSnapshot(),
    val artifactState: AndroidArtifactState = AndroidArtifactState(),
    val backupEntries: List<AndroidBackupEntry> = emptyList(),
    val isBusy: Boolean = false,
    val operationMessage: String? = null,
    val deployInstructions: String? = null,
    val configModel: AndroidConfigModel = AndroidConfigModel.createDefault(),
    val resourceManifest: AndroidResourceManifest = AndroidResourceManifest(
        schemas = emptyList(),
        dictionaries = emptyList(),
        models = emptyList(),
    ),
    val resourceUpdateReport: String? = null,
    val diagnosticReport: AndroidDiagnosticReport? = null,
)
