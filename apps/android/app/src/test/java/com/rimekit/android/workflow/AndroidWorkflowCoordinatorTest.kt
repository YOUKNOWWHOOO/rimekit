package com.rimekit.android.workflow

import com.rimekit.android.artifacts.AndroidArtifactState
import com.rimekit.android.artifacts.AndroidConfigModel
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AndroidWorkflowCoordinatorTest {
    @Test
    fun detect_shouldRequireManualActionsWhenPermissionsOrImeMissing() {
        val coordinator = AndroidWorkflowCoordinator(
            platformService = FakePlatformService(
                snapshot = AndroidPlatformSnapshot(
                    carrierState = ProbeState.Present,
                    rimePluginState = ProbeState.Present,
                    syncRootPermission = PermissionState.Missing,
                    importRootPermission = PermissionState.Missing,
                    imeEnabledState = ImeState.Missing,
                    imeSelectedState = ImeState.Missing,
                ),
            ),
        )

        val state = coordinator.buildUiState(
            selectedSection = WorkflowSection.Authorize,
            artifactState = AndroidArtifactState(),
            configModel = AndroidConfigModel.createDefault(),
        )

        assertEquals("detect", state.currentPhase)
        assertEquals("manual_action_required", state.currentStatus)
        assertTrue(state.manualSteps.any { it.stepId == "grant_sync_root_permission" })
        assertTrue(state.manualSteps.any { it.stepId == "grant_import_root_permission" })
        assertTrue(state.manualSteps.any { it.stepId == "enable_android_ime" })
        assertTrue(state.manualSteps.any { it.stepId == "select_android_ime" })
    }

    @Test
    fun recheckAndDiagnose_shouldCompleteAfterSuccessfulDelivery() {
        val coordinator = AndroidWorkflowCoordinator(
            platformService = FakePlatformService(
                snapshot = AndroidPlatformSnapshot(
                    carrierState = ProbeState.Present,
                    rimePluginState = ProbeState.Present,
                    syncRootPermission = PermissionState.Granted,
                    importRootPermission = PermissionState.Granted,
                    imeEnabledState = ImeState.Enabled,
                    imeSelectedState = ImeState.Enabled,
                    requiredSchemaApplied = ProbeState.Present,
                    keyboardLayoutApplied = ProbeState.Present,
                    deliveryConfirmation = ManualConfirmationState.Confirmed,
                ),
            ),
        )

        val state = coordinator.buildUiState(
            selectedSection = WorkflowSection.Diagnose,
            artifactState = AndroidArtifactState(
                latestSnapshotId = "snapshot-1",
                latestBackupId = "backup-1",
                latestAppliedSnapshotId = "snapshot-1",
                lastRecheckSummary = "回检通过：导入源文件完整，且已记录用户确认完成导入、切换 t9 并确认中文9键到位。",
            ),
            configModel = AndroidConfigModel.createDefault(),
        )

        assertTrue(state.phaseStates.any { it.phase == AndroidWorkflowPhase.Recheck && it.status == AndroidWorkflowStatus.Completed })
        assertTrue(state.phaseStates.any { it.phase == AndroidWorkflowPhase.Diagnose && it.status == AndroidWorkflowStatus.Completed })
        assertEquals("diagnose", state.currentPhase)
        assertEquals("completed", state.currentStatus)
    }

    @Test
    fun recheckFailure_shouldSurfaceStateMismatch() {
        val coordinator = AndroidWorkflowCoordinator(
            platformService = FakePlatformService(
                snapshot = AndroidPlatformSnapshot(
                    carrierState = ProbeState.Present,
                    rimePluginState = ProbeState.Present,
                    syncRootPermission = PermissionState.Granted,
                    importRootPermission = PermissionState.Granted,
                    imeEnabledState = ImeState.Enabled,
                    imeSelectedState = ImeState.Enabled,
                ),
            ),
        )

        val state = coordinator.buildUiState(
            selectedSection = WorkflowSection.Diagnose,
            artifactState = AndroidArtifactState(
                latestSnapshotId = "snapshot-1",
                latestBackupId = "backup-1",
                latestAppliedSnapshotId = "snapshot-1",
                lastRecheckSummary = "回检失败：尚未记录用户已完成承载器导入确认",
            ),
            configModel = AndroidConfigModel.createDefault(),
        )

        assertTrue(state.findings.any { it.code == "ANDROID_RECHECK_FAILED" })
        assertTrue(state.findings.any { it.code == "STATE_MISMATCH" })
        assertTrue(state.phaseStates.any { it.phase == AndroidWorkflowPhase.Recheck && it.status == AndroidWorkflowStatus.Failed })
    }
}

private class FakePlatformService(
    private val snapshot: AndroidPlatformSnapshot,
) : AndroidPlatformService {
    override fun readSnapshot(): AndroidPlatformSnapshot = snapshot

    override fun persistGrantedUri(
        kind: AndroidPermissionKind,
        uri: android.net.Uri,
    ) {
        error("测试替身不应写入 URI")
    }

    override fun clearGrantedUri(kind: AndroidPermissionKind) {
        error("测试替身不应清除 URI")
    }

    override fun setRuntimeConfirmation(
        kind: AndroidRuntimeConfirmationKind,
        confirmed: Boolean,
    ) {
        error("测试替身不应修改运行态确认")
    }

    override fun markDeliveryConfirmationPending() {
        error("测试替身不应标记等待返回确认")
    }

    override fun onApplicationResumed() {
        error("测试替身不应自动把返回应用当成导入确认")
    }

    override fun openInputMethodSettings(): Result<Unit> {
        error("测试替身不应打开输入法设置")
    }

    override fun showInputMethodPicker(): Result<Unit> {
        error("测试替身不应弹出输入法选择器")
    }

    override fun openPackageOrUrl(
        packageName: String,
        url: String,
    ): Result<Unit> {
        error("测试替身不应打开安装入口")
    }

    override fun openPackageUninstallOrDetails(packageName: String): Result<Unit> {
        error("测试替身不应打开卸载入口")
    }

    override fun openExternalUrl(url: String): Result<Unit> {
        error("测试替身不应打开外部资源链接")
    }
}
