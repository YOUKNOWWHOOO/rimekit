package com.rimekit.android.workflow

import android.app.Application
import android.net.Uri
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.rimekit.android.artifacts.AndroidArtifactService
import com.rimekit.android.artifacts.AndroidConfigModel
import com.rimekit.android.artifacts.AndroidConfigRepository
import com.rimekit.android.artifacts.AndroidConfigValidationIssue
import com.rimekit.android.artifacts.AndroidConfigValidator
import com.rimekit.android.artifacts.AndroidRecheckResult
import com.rimekit.android.artifacts.AndroidResourceManifestRepository
import com.rimekit.android.artifacts.AndroidResourceUpdateService
import com.rimekit.android.artifacts.AndroidStagedSyncSnapshot
import com.rimekit.android.artifacts.AndroidValidationCatalog
import com.rimekit.android.diagnostics.AndroidDiagnosticReport
import com.rimekit.android.diagnostics.AndroidDiagnosticService
import com.rimekit.android.exports.AndroidExportService
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/**
 * Android A4 阶段的页面状态入口。
 */
class AndroidWorkflowViewModel(
    application: Application,
) : AndroidViewModel(application) {
    private val platformService = AndroidPlatformServiceImpl(application.applicationContext)
    private val coordinator = AndroidWorkflowCoordinator(platformService)
    private val artifactService = AndroidArtifactService(application.applicationContext)
    private val configRepository = AndroidConfigRepository(application.applicationContext)
    private val resourceManifestRepository = AndroidResourceManifestRepository(application.applicationContext)
    private val resourceUpdateService = AndroidResourceUpdateService(application.applicationContext, resourceManifestRepository)
    private val validationCatalog: AndroidValidationCatalog = resourceManifestRepository.loadValidationCatalog()
    private val diagnosticService = AndroidDiagnosticService(application.applicationContext)
    private val exportService = AndroidExportService(application.applicationContext)
    private val _uiState = MutableStateFlow(
        buildAndPersistUiState(
            selectedSection = WorkflowSection.Welcome,
            artifactState = artifactService.loadArtifactState(),
            configModel = configRepository.load(),
            diagnosticReport = diagnosticService.loadLastReport(),
        ),
    )
    private val lanSyncService = AndroidLanSyncService(
        artifactService = artifactService,
        configRepository = configRepository,
        currentImportRootProvider = { _uiState.value.platformSnapshot.importRootUri },
        onImportedSnapshot = { message ->
            viewModelScope.launch(Dispatchers.Main) {
                _uiState.update { current ->
                    buildAndPersistUiState(
                        selectedSection = current.selectedSection,
                        artifactState = artifactService.loadArtifactState(),
                        isBusy = false,
                        operationMessage = message,
                        deployInstructions = artifactService.latestApplyManifestText(),
                        configModel = configRepository.load(),
                        diagnosticReport = diagnosticService.loadLastReport(),
                    )
                }
            }
        },
    )
    val uiState: StateFlow<AndroidWorkflowUiState> = _uiState.asStateFlow()

    init {
        lanSyncService.start()
    }

    fun selectSection(section: WorkflowSection) {
        _uiState.update {
            buildAndPersistUiState(
                selectedSection = section,
                artifactState = artifactService.loadArtifactState(),
                isBusy = it.isBusy,
                operationMessage = it.operationMessage,
                deployInstructions = it.deployInstructions,
                configModel = it.configModel,
                diagnosticReport = it.diagnosticReport,
            )
        }
    }

    fun refreshWorkflow() {
        _uiState.update { current ->
            buildAndPersistUiState(
                selectedSection = current.selectedSection,
                artifactState = artifactService.loadArtifactState(),
                isBusy = current.isBusy,
                operationMessage = current.operationMessage,
                deployInstructions = current.deployInstructions,
                configModel = current.configModel,
                diagnosticReport = current.diagnosticReport,
            )
        }
    }

    fun persistGrantedUri(
        kind: AndroidPermissionKind,
        uri: Uri,
    ) {
        platformService.persistGrantedUri(kind, uri)
        _uiState.update { current ->
            val updatedModel = when (kind) {
                AndroidPermissionKind.SyncRoot -> current.configModel.copy(sharedSyncRoot = uri.toString())
                AndroidPermissionKind.ImportRoot -> current.configModel.copy(androidImportRoot = uri.toString())
            }
            buildAndPersistUiState(
                selectedSection = current.selectedSection,
                artifactState = artifactService.loadArtifactState(),
                isBusy = false,
                operationMessage = "目录授权已写入当前配置草稿，保存后即可进入正式配置模型。",
                deployInstructions = current.deployInstructions,
                configModel = updatedModel,
                diagnosticReport = current.diagnosticReport,
            )
        }
    }

    fun clearGrantedUri(kind: AndroidPermissionKind) {
        platformService.clearGrantedUri(kind)
        _uiState.update { current ->
            val updatedModel = when (kind) {
                AndroidPermissionKind.SyncRoot -> current.configModel.copy(sharedSyncRoot = "")
                AndroidPermissionKind.ImportRoot -> current.configModel.copy(androidImportRoot = "")
            }
            buildAndPersistUiState(
                selectedSection = current.selectedSection,
                artifactState = artifactService.loadArtifactState(),
                isBusy = false,
                operationMessage = "目录授权已清除；如需持久化该变化，请重新保存配置模型。",
                deployInstructions = current.deployInstructions,
                configModel = updatedModel,
                diagnosticReport = current.diagnosticReport,
            )
        }
    }

    fun updateConfigModel(updatedModel: AndroidConfigModel) {
        _uiState.update { current ->
            current.copy(configModel = updatedModel)
        }
    }

    fun saveConfigModel() {
        val model = _uiState.value.configModel
        if (!ensureValidConfigModel(AndroidWorkflowPhase.Configure, model, WorkflowSection.Configure)) {
            return
        }
        configRepository.save(model)
        _uiState.update { current ->
            buildAndPersistUiState(
                selectedSection = current.selectedSection,
                artifactState = artifactService.loadArtifactState(),
                isBusy = false,
                operationMessage = "配置模型已保存。",
                deployInstructions = current.deployInstructions,
                configModel = model,
                diagnosticReport = current.diagnosticReport,
            )
        }
    }

    fun setRuntimeConfirmation(
        kind: AndroidRuntimeConfirmationKind,
        confirmed: Boolean,
    ) {
        platformService.setRuntimeConfirmation(kind, confirmed)
        refreshWorkflow()
    }

    fun recordPlatformActionFailure(
        phase: AndroidWorkflowPhase,
        taskId: String?,
        code: String,
        detail: String,
    ) {
        val artifactState = artifactService.loadArtifactState()
        val report = diagnosticService.buildFailureReport(
            phase = phase,
            taskId = taskId,
            code = code,
            detail = detail,
            artifactState = artifactState,
        )
        _uiState.update { current ->
            buildAndPersistUiState(
                selectedSection = current.selectedSection,
                artifactState = artifactState,
                isBusy = false,
                operationMessage = detail,
                deployInstructions = current.deployInstructions,
                configModel = current.configModel,
                diagnosticReport = current.diagnosticReport,
                reportOverride = report,
            )
        }
    }

    fun onApplicationResumed() {
        platformService.onApplicationResumed()
        refreshWorkflow()
    }

    fun recordInformationalActionIssue(detail: String) {
        _uiState.update { current ->
            buildAndPersistUiState(
                selectedSection = current.selectedSection,
                artifactState = artifactService.loadArtifactState(),
                isBusy = false,
                operationMessage = detail,
                deployInstructions = current.deployInstructions,
                configModel = current.configModel,
                diagnosticReport = current.diagnosticReport,
            )
        }
    }

    override fun onCleared() {
        lanSyncService.stop()
        super.onCleared()
    }

    fun openInputMethodSettings() {
        platformService.openInputMethodSettings().onFailure { error ->
            recordPlatformActionFailure(
                phase = AndroidWorkflowPhase.Detect,
                taskId = "android_open_ime_settings",
                code = "ANDROID_IME_SETTINGS_JUMP_FAILED",
                detail = "打开 Android 输入法设置失败：${error.message ?: "未知错误"}",
            )
        }
    }

    fun showInputMethodPicker() {
        platformService.showInputMethodPicker().onFailure { error ->
            recordPlatformActionFailure(
                phase = AndroidWorkflowPhase.Detect,
                taskId = "android_open_ime_picker",
                code = "ANDROID_IME_PICKER_UNAVAILABLE",
                detail = "弹出 Android 输入法选择器失败：${error.message ?: "未知错误"}",
            )
        }
    }

    fun openCarrierInstallOrApp() {
        platformService.openPackageOrUrl(
            packageName = "org.fcitx.fcitx5.android",
            url = "https://f-droid.org/packages/org.fcitx.fcitx5.android/",
        ).onFailure { error ->
            recordPlatformActionFailure(
                phase = AndroidWorkflowPhase.Detect,
                taskId = "android_request_carrier_install",
                code = "ANDROID_CARRIER_INSTALL_REQUEST_FAILED",
                detail = "发起 Android 承载器安装入口失败：${error.message ?: "未知错误"}",
            )
        }
    }

    fun openRimePluginInstallOrApp() {
        platformService.openPackageOrUrl(
            packageName = "org.fcitx.fcitx5.android.plugin.rime",
            url = "https://f-droid.org/packages/org.fcitx.fcitx5.android.plugin.rime/",
        ).onFailure { error ->
            recordPlatformActionFailure(
                phase = AndroidWorkflowPhase.Detect,
                taskId = "android_request_rime_plugin_install",
                code = "ANDROID_RIME_PLUGIN_INSTALL_REQUEST_FAILED",
                detail = "发起 Android Rime 插件安装入口失败：${error.message ?: "未知错误"}",
            )
        }
    }

    fun openCarrierUninstallOrSettings() {
        platformService.openPackageUninstallOrDetails("org.fcitx.fcitx5.android").onFailure { error ->
            recordPlatformActionFailure(
                phase = AndroidWorkflowPhase.Detect,
                taskId = "android_request_carrier_uninstall",
                code = "ANDROID_CARRIER_UNINSTALL_REQUEST_FAILED",
                detail = "发起 Android 承载器卸载入口失败：${error.message ?: "未知错误"}",
            )
        }
    }

    fun openRimePluginUninstallOrSettings() {
        platformService.openPackageUninstallOrDetails("org.fcitx.fcitx5.android.plugin.rime").onFailure { error ->
            recordPlatformActionFailure(
                phase = AndroidWorkflowPhase.Detect,
                taskId = "android_request_rime_plugin_uninstall",
                code = "ANDROID_RIME_PLUGIN_UNINSTALL_REQUEST_FAILED",
                detail = "发起 Android Rime 插件卸载入口失败：${error.message ?: "未知错误"}",
            )
        }
    }

    fun openResourceSource(source: String) {
        platformService.openExternalUrl(source).onFailure { error ->
            recordInformationalActionIssue("打开资源更新来源失败：${error.message ?: "未知错误"}")
        }
    }

    fun exportConfigModel(target: Uri) {
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "配置模型已导出。",
        ) {
            exportService.exportConfigModel(target, configRepository.loadRawJson())
        }
    }

    fun exportLatestDiagnostic(target: Uri) {
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "最新诊断已导出。",
        ) {
            exportService.exportLatestDiagnostic(target)
        }
    }

    fun exportLatestSnapshot(target: Uri) {
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "最新快照已导出。",
        ) {
            exportService.exportLatestSnapshot(target, artifactService.loadArtifactState())
        }
    }

    fun exportLatestBackup(target: Uri) {
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "最新备份已导出。",
        ) {
            exportService.exportLatestBackup(target, artifactService.loadArtifactState())
        }
    }

    fun exportBackupById(
        target: Uri,
        backupId: String,
    ) {
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "指定备份已导出。",
        ) {
            exportService.exportBackupById(target, backupId)
        }
    }

    fun exportResourceManifest(target: Uri) {
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "正式资源清单已导出。",
        ) {
            exportService.exportResourceManifest(target)
        }
    }

    fun exportResourceUpdateReport(target: Uri) {
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "资源更新检查结果已导出。",
        ) {
            exportService.exportResourceUpdateReport(target)
        }
    }

    fun checkResourceUpdates() {
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "正式资源更新检查已完成。",
        ) {
            resourceUpdateService.checkForUpdates()
        }
    }

    fun publishLatestSnapshotToSharedRoot() {
        val sharedSyncRoot = _uiState.value.configModel.sharedSyncRoot.ifBlank {
            _uiState.value.platformSnapshot.syncRootUri.orEmpty()
        }
        if (sharedSyncRoot.isBlank()) {
            _uiState.update { current ->
                current.copy(operationMessage = "缺少同步快照根目录授权，无法发布最新快照。")
            }
            return
        }
        if (!ensureValidConfigModel(AndroidWorkflowPhase.Generate, _uiState.value.configModel, WorkflowSection.Configure)) {
            return
        }

        runArtifactOperation(
            phase = AndroidWorkflowPhase.Generate,
            successMessage = "最新同步快照已生成并发布到同步根目录。",
        ) {
            artifactService.generate(
                configModel = _uiState.value.configModel,
                importRootUri = _uiState.value.platformSnapshot.importRootUri,
            )
            artifactService.publishLatestSnapshotToSharedRoot(sharedSyncRoot)
        }
    }

    fun importLatestSnapshotFromSharedRoot() {
        val importRootUri = _uiState.value.platformSnapshot.importRootUri ?: run {
            _uiState.update { current ->
                current.copy(operationMessage = "缺少 Android 导入源目录授权，无法导入同步快照。")
            }
            return
        }
        val sharedSyncRoot = _uiState.value.configModel.sharedSyncRoot.ifBlank {
            _uiState.value.platformSnapshot.syncRootUri.orEmpty()
        }
        if (sharedSyncRoot.isBlank()) {
            _uiState.update { current ->
                current.copy(operationMessage = "缺少同步快照根目录授权，无法读取最新快照。")
            }
            return
        }

        runArtifactOperation(
            phase = AndroidWorkflowPhase.Apply,
            successMessage = "同步根目录中的最新快照已导入导入源目录，请继续执行 deploy 并在返回后 recheck。",
            failureCode = "CONFIG_MODEL_SCHEMA_INVALID",
            onSuccess = { result ->
                val stagedSnapshot = result as AndroidStagedSyncSnapshot
                configRepository.save(stagedSnapshot.configModel)
                buildAndPersistUiState(
                    selectedSection = WorkflowSection.Deploy,
                    artifactState = artifactService.loadArtifactState(),
                    isBusy = false,
                    operationMessage = "同步根目录中的最新快照已导入导入源目录，请继续执行 deploy 并在返回后 recheck。",
                    deployInstructions = artifactService.latestApplyManifestText(),
                    configModel = stagedSnapshot.configModel,
                    diagnosticReport = diagnosticService.loadLastReport(),
                )
            },
        ) {
            val stagedSnapshot = artifactService.importLatestSnapshotFromSharedRoot(sharedSyncRoot)
            val issues = AndroidConfigValidator.validate(stagedSnapshot.configModel, validationCatalog)
            if (issues.isNotEmpty()) {
                throw IllegalStateException(
                    "导入的同步快照配置不合法：${issues.joinToString("；") { issue -> issue.detail }}",
                )
            }
            artifactService.backupImportRoot(importRootUri)
            artifactService.applyStagedSyncSnapshot(importRootUri, stagedSnapshot)
            stagedSnapshot
        }
    }

    fun importSyncSnapshot(source: Uri) {
        val importRootUri = _uiState.value.platformSnapshot.importRootUri ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "缺少 Android 导入源目录授权，无法导入同步快照。",
                )
            }
            return
        }
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Apply,
            successMessage = "同步快照已导入导入源目录，请继续执行 deploy 并在返回后 recheck。",
            failureCode = "CONFIG_MODEL_SCHEMA_INVALID",
            onSuccess = { result ->
                val stagedSnapshot = result as AndroidStagedSyncSnapshot
                configRepository.save(stagedSnapshot.configModel)
                buildAndPersistUiState(
                    selectedSection = WorkflowSection.Deploy,
                    artifactState = artifactService.loadArtifactState(),
                    isBusy = false,
                    operationMessage = "同步快照已导入导入源目录，请继续执行 deploy 并在返回后 recheck。",
                    deployInstructions = artifactService.latestApplyManifestText(),
                    configModel = stagedSnapshot.configModel,
                    diagnosticReport = diagnosticService.loadLastReport(),
                )
            },
        ) {
            val stagedSnapshot = artifactService.stageSyncSnapshotImport(source)
            val issues = AndroidConfigValidator.validate(stagedSnapshot.configModel, validationCatalog)
            if (issues.isNotEmpty()) {
                throw IllegalStateException(
                    "导入的同步快照配置不合法：${issues.joinToString("；") { issue -> issue.detail }}",
                )
            }
            artifactService.backupImportRoot(importRootUri)
            artifactService.applyStagedSyncSnapshot(importRootUri, stagedSnapshot)
            stagedSnapshot
        }
    }

    fun importRuntimeToConfigModel() {
        val importRootUri = _uiState.value.platformSnapshot.importRootUri ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "缺少 Android 导入源目录授权，无法导入当前运行态。",
                )
            }
            return
        }
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Diagnose,
            successMessage = "当前运行态已导入为新的配置模型基础。",
            onSuccess = { result ->
                val updatedModel = result as AndroidConfigModel
                buildAndPersistUiState(
                    selectedSection = WorkflowSection.Configure,
                    artifactState = artifactService.loadArtifactState(),
                    isBusy = false,
                    operationMessage = "当前运行态已导入为新的配置模型基础。",
                    configModel = updatedModel,
                    diagnosticReport = diagnosticService.loadLastReport(),
                )
            },
        ) {
            val importedModel = artifactService.importRuntimeToConfig(importRootUri, _uiState.value.configModel)
            configRepository.save(importedModel)
            importedModel
        }
    }

    fun overrideRuntimeWithGui() {
        val importRootUri = _uiState.value.platformSnapshot.importRootUri ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "缺少 Android 导入源目录授权，无法以当前配置覆盖运行态。",
                )
            }
            return
        }
        if (!ensureValidConfigModel(AndroidWorkflowPhase.Apply, _uiState.value.configModel, WorkflowSection.Configure)) {
            return
        }
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Apply,
            successMessage = "已按当前配置覆盖 Android 导入源目录，请继续执行 deploy 并在返回后 recheck。",
            onSuccess = {
                buildAndPersistUiState(
                    selectedSection = WorkflowSection.Deploy,
                    artifactState = artifactService.loadArtifactState(),
                    isBusy = false,
                    operationMessage = "已按当前配置覆盖 Android 导入源目录，请继续执行 deploy 并在返回后 recheck。",
                    deployInstructions = artifactService.latestApplyManifestText(),
                    configModel = _uiState.value.configModel,
                    diagnosticReport = diagnosticService.loadLastReport(),
                )
            },
        ) {
            val generatedArtifacts = artifactService.generate(_uiState.value.configModel)
            artifactService.backupImportRoot(importRootUri)
            artifactService.applyImportBundle(importRootUri, generatedArtifacts)
            generatedArtifacts
        }
    }

    fun generateArtifacts() {
        if (!ensureValidConfigModel(AndroidWorkflowPhase.Generate, _uiState.value.configModel, WorkflowSection.Configure)) {
            return
        }
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Generate,
            successMessage = "Android 目标包与同步快照已生成。",
        ) {
            artifactService.generate(
                configModel = _uiState.value.configModel,
                importRootUri = _uiState.value.platformSnapshot.importRootUri,
            )
        }
    }

    fun backupImportRoot() {
        val importRootUri = _uiState.value.platformSnapshot.importRootUri ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "缺少 Android 导入源目录授权，无法执行 backup。",
                )
            }
            return
        }
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Backup,
            successMessage = "Android 导入源目录备份已创建。",
        ) {
            artifactService.backupImportRoot(importRootUri)
        }
    }

    fun applyImportBundle() {
        val importRootUri = _uiState.value.platformSnapshot.importRootUri ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "缺少 Android 导入源目录授权，无法执行 apply。",
                )
            }
            return
        }
        if (!ensureValidConfigModel(AndroidWorkflowPhase.Apply, _uiState.value.configModel, WorkflowSection.Configure)) {
            return
        }
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Apply,
            successMessage = "Android 目标包已写入导入源目录。",
        ) {
            val generatedArtifacts = artifactService.generate(
                configModel = _uiState.value.configModel,
                importRootUri = importRootUri,
            )
            artifactService.backupImportRoot(importRootUri)
            artifactService.applyImportBundle(importRootUri, generatedArtifacts)
        }
    }

    fun deployImportBundle() {
        val instructions = artifactService.latestApplyManifestText() ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "缺少最新 Android 应用清单，无法进入 deploy。",
                )
            }
            return
        }
        platformService.markDeliveryConfirmationPending()
        platformService.setRuntimeConfirmation(AndroidRuntimeConfirmationKind.RequiredSchemaSelected, false)
        platformService.setRuntimeConfirmation(AndroidRuntimeConfirmationKind.KeyboardLayoutApplied, false)
        artifactService.clearDeployTransientState()

        _uiState.update { current ->
            buildAndPersistUiState(
                selectedSection = current.selectedSection,
                artifactState = artifactService.loadArtifactState(),
                isBusy = false,
                operationMessage = "请按 Android 应用清单完成承载器导入与必要确认，然后返回应用执行 recheck。",
                deployInstructions = instructions,
            )
        }
    }

    fun recheckImportBundle() {
        val importRootUri = _uiState.value.platformSnapshot.importRootUri ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "缺少 Android 导入源目录授权，无法执行 recheck。",
                )
            }
            return
        }
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Recheck,
            successMessage = "Android 导入源目录回检已完成。",
        ) {
            val result: AndroidRecheckResult = artifactService.recheckImportRoot(importRootUri)
            val snapshot = platformService.readSnapshot()
            val issues = mutableListOf<String>()
            if (result.missingFiles.isNotEmpty()) {
                issues += "导入源目录缺少文件：${result.missingFiles.joinToString()}"
            }
            if (snapshot.deliveryConfirmation != ManualConfirmationState.Confirmed) {
                issues += "尚未记录用户已完成承载器导入确认"
            }
            if (snapshot.requiredSchemaApplied != ProbeState.Present) {
                issues += "导入源中的 Android 应用清单未声明默认方案为 t9"
            }
            if (snapshot.keyboardLayoutApplied != ProbeState.Present) {
                issues += "导入源中的 Android 应用清单未声明键盘布局为 9_key"
            }
            if (issues.isEmpty()) {
                artifactService.saveRecheckSummary("回检通过：导入源文件完整，应用清单已声明 t9 + 9_key，且已记录用户确认完成导入。")
            } else {
                val summary = "回检失败：${issues.joinToString("；")}"
                artifactService.saveRecheckSummary(summary)
                error(summary)
            }
        }
    }

    fun rollbackImportBundle(backupIdOverride: String? = null) {
        val importRootUri = _uiState.value.platformSnapshot.importRootUri ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "缺少 Android 导入源目录授权，无法执行 rollback。",
                )
            }
            return
        }
        val backupId = backupIdOverride ?: artifactService.loadArtifactState().latestBackupId ?: run {
            _uiState.update { current ->
                current.copy(
                    operationMessage = "当前没有可用 Android 备份，无法执行 rollback。",
                )
            }
            return
        }
        runArtifactOperation(
            phase = AndroidWorkflowPhase.Rollback,
            successMessage = "Android 导入源目录已按最近备份恢复。",
            onSuccess = {
                buildAndPersistUiState(
                    selectedSection = WorkflowSection.Deploy,
                    artifactState = artifactService.loadArtifactState(),
                    isBusy = false,
                    operationMessage = "Android 导入源目录已恢复，请重新执行 deploy 并在返回后 recheck。",
                    deployInstructions = artifactService.latestApplyManifestText(),
                    configModel = _uiState.value.configModel,
                    diagnosticReport = diagnosticService.loadLastReport(),
                )
            },
        ) {
            artifactService.restoreBackup(importRootUri, backupId)
            platformService.setRuntimeConfirmation(AndroidRuntimeConfirmationKind.DeliveryCompleted, false)
            platformService.setRuntimeConfirmation(AndroidRuntimeConfirmationKind.RequiredSchemaSelected, false)
            platformService.setRuntimeConfirmation(AndroidRuntimeConfirmationKind.KeyboardLayoutApplied, false)
        }
    }

    private fun runArtifactOperation(
        phase: AndroidWorkflowPhase,
        successMessage: String,
        onSuccess: ((Any) -> AndroidWorkflowUiState)? = null,
        failureCode: String? = null,
        block: () -> Any,
    ) {
        _uiState.update { current ->
            buildAndPersistUiState(
                selectedSection = current.selectedSection,
                artifactState = artifactService.loadArtifactState(),
                isBusy = true,
                operationMessage = "正在执行 ${phase.id} ...",
                deployInstructions = current.deployInstructions,
            )
        }

        viewModelScope.launch(Dispatchers.IO) {
            runCatching {
                block()
            }.onSuccess { result ->
                _uiState.update { current ->
                    onSuccess?.invoke(result) ?: buildAndPersistUiState(
                        selectedSection = current.selectedSection,
                        artifactState = artifactService.loadArtifactState(),
                        isBusy = false,
                        operationMessage = successMessage,
                        deployInstructions = current.deployInstructions,
                        configModel = current.configModel,
                        diagnosticReport = current.diagnosticReport,
                    )
                }
            }.onFailure { error ->
                val artifactState = artifactService.loadArtifactState()
                val resolvedFailureCode = failureCode ?: resolveFailureCode(phase, error.message)
                val report = diagnosticService.buildFailureReport(
                    phase = phase,
                    taskId = null,
                    code = resolvedFailureCode,
                    detail = error.message ?: "${phase.id} 执行失败。",
                    artifactState = artifactState,
                    conflictScope = if (
                        resolvedFailureCode == "ANDROID_RECHECK_FAILED" ||
                        resolvedFailureCode == "STATE_MISMATCH"
                    ) {
                        "runtime_state"
                    } else {
                        null
                    },
                )
                _uiState.update { current ->
                    buildAndPersistUiState(
                        selectedSection = current.selectedSection,
                        artifactState = artifactState,
                        isBusy = false,
                        operationMessage = "${phase.id} 失败：${error.message}",
                        deployInstructions = current.deployInstructions,
                        reportOverride = report,
                    )
                }
            }
        }
    }

    private fun ensureValidConfigModel(
        phase: AndroidWorkflowPhase,
        model: AndroidConfigModel,
        selectedSection: WorkflowSection,
    ): Boolean {
        val issues = AndroidConfigValidator.validate(model, validationCatalog)
        if (issues.isEmpty()) {
            return true
        }

        publishValidationFailure(
            phase = phase,
            issues = issues,
            selectedSection = selectedSection,
            model = model,
        )
        return false
    }

    private fun publishValidationFailure(
        phase: AndroidWorkflowPhase,
        issues: List<AndroidConfigValidationIssue>,
        selectedSection: WorkflowSection,
        model: AndroidConfigModel,
    ) {
        val artifactState = artifactService.loadArtifactState()
        val report = diagnosticService.buildBlockedReport(
            phase = phase,
            issues = issues,
            artifactState = artifactState,
            nextAction = "先修正配置模型字段，再继续执行 ${phase.id}。",
        )
        _uiState.update { current ->
            buildAndPersistUiState(
                selectedSection = selectedSection,
                artifactState = artifactState,
                isBusy = false,
                operationMessage = "配置模型校验失败：${issues.first().detail}",
                deployInstructions = current.deployInstructions,
                configModel = model,
                diagnosticReport = current.diagnosticReport,
                reportOverride = report,
            )
        }
    }

    private fun buildAndPersistUiState(
        selectedSection: WorkflowSection,
        artifactState: com.rimekit.android.artifacts.AndroidArtifactState,
        isBusy: Boolean = false,
        operationMessage: String? = null,
        deployInstructions: String? = null,
        configModel: AndroidConfigModel = configRepository.load(),
        resourceManifest: com.rimekit.android.artifacts.AndroidResourceManifest = resourceManifestRepository.load(),
        resourceUpdateReport: String? = resourceUpdateService.loadLastReport(),
        backupEntries: List<com.rimekit.android.artifacts.AndroidBackupEntry> = artifactService.listBackups(),
        diagnosticReport: AndroidDiagnosticReport? = diagnosticService.loadLastReport(),
        reportOverride: AndroidDiagnosticReport? = null,
    ): AndroidWorkflowUiState {
        val uiState = coordinator.buildUiState(
            selectedSection = selectedSection,
            artifactState = artifactState,
            isBusy = isBusy,
            operationMessage = operationMessage,
            deployInstructions = deployInstructions,
            configModel = configModel,
            resourceManifest = resourceManifest,
            resourceUpdateReport = resourceUpdateReport,
            backupEntries = backupEntries,
            diagnosticReport = diagnosticReport,
        )
        val report: AndroidDiagnosticReport = reportOverride ?: diagnosticService.buildReport(
            phase = AndroidWorkflowPhase.entries.first { it.id == uiState.currentPhase },
            status = AndroidWorkflowStatus.entries.first { it.id == uiState.currentStatus },
            findings = uiState.findings,
            snapshot = uiState.platformSnapshot,
            artifactState = uiState.artifactState,
            operationMessage = uiState.operationMessage,
        )
        diagnosticService.persist(report)
        return uiState.copy(diagnosticReport = report)
    }

    private fun resolveFailureCode(
        phase: AndroidWorkflowPhase,
        detail: String?,
    ): String {
        val normalized = detail.orEmpty()
        return when (phase) {
            AndroidWorkflowPhase.Generate -> when {
                normalized.contains("用户词典", ignoreCase = true) -> "ANDROID_USER_DICT_EXPORT_FAILED"
                normalized.contains("应用清单", ignoreCase = true) -> "ANDROID_APPLY_MANIFEST_INVALID"
                else -> "CONFIG_GENERATION_FAILED"
            }
            AndroidWorkflowPhase.Backup -> "BACKUP_FAILED"
            AndroidWorkflowPhase.Apply -> when {
                normalized.contains("同步快照", ignoreCase = true) ||
                    normalized.contains("config_snapshot", ignoreCase = true) ||
                    normalized.contains("sync_manifest", ignoreCase = true) -> "SYNC_MANIFEST_INVALID"
                else -> "ANDROID_IMPORT_SOURCE_WRITE_FAILED"
            }
            AndroidWorkflowPhase.Recheck -> "ANDROID_RECHECK_FAILED"
            AndroidWorkflowPhase.Rollback -> "ROLLBACK_FAILED"
            AndroidWorkflowPhase.Diagnose -> if (normalized.contains("导出", ignoreCase = true)) {
                "EXPORT_FAILED"
            } else {
                "STATE_MISMATCH"
            }
            else -> "CONFIG_GENERATION_FAILED"
        }
    }
}
