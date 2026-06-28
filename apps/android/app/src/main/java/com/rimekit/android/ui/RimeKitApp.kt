package com.rimekit.android.ui

import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.view.inputmethod.InputMethodManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts.CreateDocument
import androidx.activity.result.contract.ActivityResultContracts.OpenDocument
import androidx.activity.result.contract.ActivityResultContracts.OpenDocumentTree
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.ui.text.input.KeyboardType
import com.rimekit.android.workflow.AndroidDisplayKinds
import com.rimekit.android.workflow.AndroidFinding
import com.rimekit.android.workflow.AndroidManualStep
import com.rimekit.android.workflow.AndroidPermissionKind
import com.rimekit.android.workflow.AndroidPhaseState
import com.rimekit.android.workflow.AndroidPlatformSnapshot
import com.rimekit.android.workflow.ManualConfirmationState
import com.rimekit.android.workflow.ProbeState
import com.rimekit.android.workflow.PermissionState
import com.rimekit.android.workflow.ImeState
import com.rimekit.android.workflow.AndroidRuntimeConfirmationKind
import com.rimekit.android.workflow.AndroidWorkflowUiState
import com.rimekit.android.workflow.AndroidWorkflowViewModel
import com.rimekit.android.workflow.WorkflowSection
import com.rimekit.android.diagnostics.AndroidDiagnosticReport

/**
 * Android 当前正式页面入口。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun RimeKitApp(
    workflowViewModel: AndroidWorkflowViewModel = viewModel(),
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val uiState by workflowViewModel.uiState.collectAsStateWithLifecycle()
    val configSurfaceSummary = remember(context) { loadConfigSurfaceSummary(context) }
    val taskDefinitions = remember(context) { loadAndroidTaskDefinitions(context) }
    val surfaceTags = remember(context) { loadAndroidSurfaceTags(context) }
    val pendingBackupExportIdState = androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf<String?>(null) }
    val syncRootLauncher = rememberLauncherForActivityResult(OpenDocumentTree()) { uri ->
        if (uri != null) {
            workflowViewModel.persistGrantedUri(AndroidPermissionKind.SyncRoot, uri)
        }
    }
    val importRootLauncher = rememberLauncherForActivityResult(OpenDocumentTree()) { uri ->
        if (uri != null) {
            workflowViewModel.persistGrantedUri(AndroidPermissionKind.ImportRoot, uri)
        }
    }
    val exportConfigLauncher = rememberLauncherForActivityResult(CreateDocument("application/json")) { uri ->
        if (uri != null) {
            workflowViewModel.exportConfigModel(uri)
        }
    }
    val exportDiagnosticLauncher = rememberLauncherForActivityResult(CreateDocument("application/json")) { uri ->
        if (uri != null) {
            workflowViewModel.exportLatestDiagnostic(uri)
        }
    }
    val exportSnapshotLauncher = rememberLauncherForActivityResult(CreateDocument("application/zip")) { uri ->
        if (uri != null) {
            workflowViewModel.exportLatestSnapshot(uri)
        }
    }
    val exportBackupLauncher = rememberLauncherForActivityResult(CreateDocument("application/zip")) { uri ->
        if (uri != null) {
            workflowViewModel.exportLatestBackup(uri)
        }
    }
    val exportSpecificBackupLauncher = rememberLauncherForActivityResult(CreateDocument("application/zip")) { uri ->
        val backupId = pendingBackupExportIdState.value
        if (uri != null && !backupId.isNullOrBlank()) {
            workflowViewModel.exportBackupById(uri, backupId)
        }
    }
    val exportResourceManifestLauncher = rememberLauncherForActivityResult(CreateDocument("application/json")) { uri ->
        if (uri != null) {
            workflowViewModel.exportResourceManifest(uri)
        }
    }
    val exportResourceUpdateReportLauncher = rememberLauncherForActivityResult(CreateDocument("application/json")) { uri ->
        if (uri != null) {
            workflowViewModel.exportResourceUpdateReport(uri)
        }
    }
    val importSnapshotLauncher = rememberLauncherForActivityResult(OpenDocument()) { uri ->
        if (uri != null) {
            workflowViewModel.importSyncSnapshot(uri)
        }
    }
    val openInputMethodSettings = remember(workflowViewModel) { { workflowViewModel.openInputMethodSettings() } }
    val showInputMethodPicker = remember(workflowViewModel) { { workflowViewModel.showInputMethodPicker() } }
    val openFcitxInstallOrApp = remember(workflowViewModel) { { workflowViewModel.openCarrierInstallOrApp() } }
    val openRimePluginInstallOrApp = remember(workflowViewModel) { { workflowViewModel.openRimePluginInstallOrApp() } }
    val openFcitxUninstallOrSettings = remember(workflowViewModel) { { workflowViewModel.openCarrierUninstallOrSettings() } }
    val openRimePluginUninstallOrSettings = remember(workflowViewModel) { { workflowViewModel.openRimePluginUninstallOrSettings() } }
    val openResourceSource = remember(workflowViewModel) { { source: String -> workflowViewModel.openResourceSource(source) } }

    DisposableEffect(lifecycleOwner, workflowViewModel) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                workflowViewModel.onApplicationResumed()
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose {
            lifecycleOwner.lifecycle.removeObserver(observer)
        }
    }

    MaterialTheme {
        Scaffold(
            topBar = {
                TopAppBar(
                    title = { Text("韵匣（RimeKit）") },
                )
            },
            bottomBar = {
                NavigationBar {
                    WorkflowSection.entries.forEach { section ->
                        NavigationBarItem(
                            selected = uiState.selectedSection == section,
                            onClick = { workflowViewModel.selectSection(section) },
                            icon = {},
                            label = { Text(section.title) },
                        )
                    }
                }
            },
        ) { paddingValues ->
            WorkflowContent(
                uiState = uiState,
                configSurfaceSummary = configSurfaceSummary,
                taskDefinitions = taskDefinitions,
                surfaceTags = surfaceTags,
                paddingValues = paddingValues,
                onRefresh = workflowViewModel::refreshWorkflow,
                onGrantSyncRoot = { syncRootLauncher.launch(null) },
                onGrantImportRoot = { importRootLauncher.launch(null) },
                onClearSyncRoot = { workflowViewModel.clearGrantedUri(AndroidPermissionKind.SyncRoot) },
                onClearImportRoot = { workflowViewModel.clearGrantedUri(AndroidPermissionKind.ImportRoot) },
                onOpenInputMethodSettings = openInputMethodSettings,
                onShowInputMethodPicker = showInputMethodPicker,
                onOpenFcitxInstallOrApp = openFcitxInstallOrApp,
                onOpenRimePluginInstallOrApp = openRimePluginInstallOrApp,
                onOpenFcitxUninstallOrSettings = openFcitxUninstallOrSettings,
                onOpenRimePluginUninstallOrSettings = openRimePluginUninstallOrSettings,
                onGenerate = workflowViewModel::generateArtifacts,
                onBackup = workflowViewModel::backupImportRoot,
                onApply = workflowViewModel::applyImportBundle,
                onDeploy = workflowViewModel::deployImportBundle,
                onRecheck = workflowViewModel::recheckImportBundle,
                onRollback = workflowViewModel::rollbackImportBundle,
                onSetDeliveryConfirmed = { confirmed ->
                    workflowViewModel.setRuntimeConfirmation(
                        AndroidRuntimeConfirmationKind.DeliveryCompleted,
                        confirmed,
                    )
                },
                onSetSchemaConfirmed = { confirmed ->
                    workflowViewModel.setRuntimeConfirmation(
                        AndroidRuntimeConfirmationKind.RequiredSchemaSelected,
                        confirmed,
                    )
                },
                onSetKeyboardConfirmed = { confirmed ->
                    workflowViewModel.setRuntimeConfirmation(
                        AndroidRuntimeConfirmationKind.KeyboardLayoutApplied,
                        confirmed,
                    )
                },
                onUpdateConfig = workflowViewModel::updateConfigModel,
                onSaveConfig = workflowViewModel::saveConfigModel,
                onImportSnapshot = { importSnapshotLauncher.launch(arrayOf("application/zip", "application/octet-stream")) },
                onImportLatestFromSyncRoot = workflowViewModel::importLatestSnapshotFromSharedRoot,
                onExportConfig = { exportConfigLauncher.launch("rimekit-config.json") },
                onExportDiagnostic = { exportDiagnosticLauncher.launch("rimekit-diagnostic.json") },
                onExportSnapshot = { exportSnapshotLauncher.launch("rimekit-snapshot.zip") },
                onExportBackup = { exportBackupLauncher.launch("rimekit-backup.zip") },
                onExportBackupById = { backupId ->
                    pendingBackupExportIdState.value = backupId
                    exportSpecificBackupLauncher.launch("rimekit-backup-$backupId.zip")
                },
                onExportResourceManifest = { exportResourceManifestLauncher.launch("rimekit-resource-manifest.json") },
                onExportResourceUpdateReport = { exportResourceUpdateReportLauncher.launch("rimekit-resource-update-report.json") },
                onCheckResourceUpdates = workflowViewModel::checkResourceUpdates,
                onOpenResourceSource = openResourceSource,
                onPublishLatestToSyncRoot = workflowViewModel::publishLatestSnapshotToSharedRoot,
                onImportRuntime = workflowViewModel::importRuntimeToConfigModel,
                onOverrideWithGui = workflowViewModel::overrideRuntimeWithGui,
                onRollbackById = workflowViewModel::rollbackImportBundle,
            )
        }
    }
}

@Composable
private fun WorkflowContent(
    uiState: AndroidWorkflowUiState,
    configSurfaceSummary: String,
    taskDefinitions: Map<String, AndroidTaskUiDefinition>,
    surfaceTags: Map<String, String>,
    paddingValues: PaddingValues,
    onRefresh: () -> Unit,
    onGrantSyncRoot: () -> Unit,
    onGrantImportRoot: () -> Unit,
    onClearSyncRoot: () -> Unit,
    onClearImportRoot: () -> Unit,
    onOpenInputMethodSettings: () -> Unit,
    onShowInputMethodPicker: () -> Unit,
    onOpenFcitxInstallOrApp: () -> Unit,
    onOpenRimePluginInstallOrApp: () -> Unit,
    onOpenFcitxUninstallOrSettings: () -> Unit,
    onOpenRimePluginUninstallOrSettings: () -> Unit,
    onGenerate: () -> Unit,
    onBackup: () -> Unit,
    onApply: () -> Unit,
    onDeploy: () -> Unit,
    onRecheck: () -> Unit,
    onRollback: () -> Unit,
    onSetDeliveryConfirmed: (Boolean) -> Unit,
    onSetSchemaConfirmed: (Boolean) -> Unit,
    onSetKeyboardConfirmed: (Boolean) -> Unit,
    onUpdateConfig: (com.rimekit.android.artifacts.AndroidConfigModel) -> Unit,
    onSaveConfig: () -> Unit,
    onImportSnapshot: () -> Unit,
    onImportLatestFromSyncRoot: () -> Unit,
    onExportConfig: () -> Unit,
    onExportDiagnostic: () -> Unit,
    onExportSnapshot: () -> Unit,
    onExportBackup: () -> Unit,
    onExportBackupById: (String) -> Unit,
    onExportResourceManifest: () -> Unit,
    onExportResourceUpdateReport: () -> Unit,
    onCheckResourceUpdates: () -> Unit,
    onOpenResourceSource: (String) -> Unit,
    onPublishLatestToSyncRoot: () -> Unit,
    onImportRuntime: () -> Unit,
    onOverrideWithGui: () -> Unit,
    onRollbackById: (String) -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(paddingValues)
            .padding(horizontal = 16.dp)
            .verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        StatusCard(
            title = "当前阶段",
            content = phaseTitle(uiState.currentPhase),
        )
        StatusCard(
            title = "当前结果",
            content = statusTitle(uiState.currentStatus),
        )
        StatusCard(
            title = "下一步动作",
            content = uiState.nextAction,
        )
        Button(
            onClick = onRefresh,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("刷新阶段状态")
        }
        if (uiState.operationMessage != null) {
            StatusCard(
                title = "最近操作",
                content = uiState.operationMessage,
            )
        }
        PhaseTimelineCard(uiState.phaseStates)

        when (uiState.selectedSection) {
            WorkflowSection.Welcome -> SectionCard(
                title = "欢迎",
                body = buildString {
                    appendLine("Android 客户端当前已经接入正式阶段模型、目录授权、导入源写入、备份回滚、同步快照导入导出和结构化诊断。主流程请按“检测 -> 配置/词库模型 -> 同步或部署 -> 回检 -> 诊断”推进。")
                    appendLine()
                    append(configSurfaceSummary)
                },
            )
            WorkflowSection.Detect -> DetectSection(
                snapshot = uiState.platformSnapshot,
                findings = uiState.findings,
                taskDefinitions = taskDefinitions,
                onOpenInputMethodSettings = onOpenInputMethodSettings,
                onShowInputMethodPicker = onShowInputMethodPicker,
                onOpenFcitxInstallOrApp = onOpenFcitxInstallOrApp,
                onOpenRimePluginInstallOrApp = onOpenRimePluginInstallOrApp,
                onOpenFcitxUninstallOrSettings = onOpenFcitxUninstallOrSettings,
                onOpenRimePluginUninstallOrSettings = onOpenRimePluginUninstallOrSettings,
            )
            WorkflowSection.Authorize -> AuthorizationSection(
                snapshot = uiState.platformSnapshot,
                manualSteps = uiState.manualSteps,
                onGrantSyncRoot = onGrantSyncRoot,
                onGrantImportRoot = onGrantImportRoot,
                onClearSyncRoot = onClearSyncRoot,
                onClearImportRoot = onClearImportRoot,
            )
            WorkflowSection.Configure -> ConfigureSection(
                configModel = uiState.configModel,
                surfaceTags = surfaceTags,
                onUpdateConfig = onUpdateConfig,
                onSaveConfig = onSaveConfig,
            )
            WorkflowSection.Resources -> ResourcesSection(
                configModel = uiState.configModel,
                resourceManifest = uiState.resourceManifest,
                surfaceTags = surfaceTags,
                onUpdateConfig = onUpdateConfig,
                onSaveConfig = onSaveConfig,
                onExportResourceManifest = onExportResourceManifest,
                onExportResourceUpdateReport = onExportResourceUpdateReport,
                resourceUpdateReport = uiState.resourceUpdateReport,
                onCheckResourceUpdates = onCheckResourceUpdates,
                onOpenResourceSource = onOpenResourceSource,
            )
            WorkflowSection.Sync -> SyncSection(
                artifactState = uiState.artifactState,
                configModel = uiState.configModel,
                onImportSnapshot = onImportSnapshot,
                onImportLatestFromSyncRoot = onImportLatestFromSyncRoot,
                onExportConfig = onExportConfig,
                onExportSnapshot = onExportSnapshot,
                onExportDiagnostic = onExportDiagnostic,
                onPublishLatestToSyncRoot = onPublishLatestToSyncRoot,
            )
            WorkflowSection.Backup -> BackupSection(
                artifactState = uiState.artifactState,
                backupEntries = uiState.backupEntries,
                onExportBackup = onExportBackup,
                onExportBackupById = onExportBackupById,
                onRollback = { onRollback() },
                onRollbackById = onRollbackById,
            )
            WorkflowSection.Deploy -> DeploySection(
                phaseStates = uiState.phaseStates,
                artifactState = uiState.artifactState,
                taskDefinitions = taskDefinitions,
                isBusy = uiState.isBusy,
                deployInstructions = uiState.deployInstructions,
                onGenerate = onGenerate,
                onBackup = onBackup,
                onApply = onApply,
                onDeploy = onDeploy,
                onRecheck = onRecheck,
                onRollback = onRollback,
                snapshot = uiState.platformSnapshot,
                onSetDeliveryConfirmed = onSetDeliveryConfirmed,
                onSetSchemaConfirmed = onSetSchemaConfirmed,
                onSetKeyboardConfirmed = onSetKeyboardConfirmed,
            )
            WorkflowSection.Diagnose -> DiagnoseSection(
                findings = uiState.findings,
                snapshot = uiState.platformSnapshot,
                manualSteps = uiState.manualSteps,
                deployInstructions = uiState.deployInstructions,
                diagnosticReport = uiState.diagnosticReport,
                resourceManifest = uiState.resourceManifest,
                configModel = uiState.configModel,
                artifactState = uiState.artifactState,
                onImportRuntime = onImportRuntime,
                onOverrideWithGui = onOverrideWithGui,
                onGrantSyncRoot = onGrantSyncRoot,
                onGrantImportRoot = onGrantImportRoot,
                onClearSyncRoot = onClearSyncRoot,
                onClearImportRoot = onClearImportRoot,
            )
        }
    }
}

@Composable
private fun SyncSection(
    artifactState: com.rimekit.android.artifacts.AndroidArtifactState,
    configModel: com.rimekit.android.artifacts.AndroidConfigModel,
    onImportSnapshot: () -> Unit,
    onImportLatestFromSyncRoot: () -> Unit,
    onExportConfig: () -> Unit,
    onExportSnapshot: () -> Unit,
    onExportDiagnostic: () -> Unit,
    onPublishLatestToSyncRoot: () -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text("同步与导出", style = MaterialTheme.typography.titleMedium)
            Text(
                "最近快照: ${artifactState.latestSnapshotId ?: "无"}",
                style = MaterialTheme.typography.bodyMedium,
            )
            Text(
                "同步快照根目录: ${configModel.sharedSyncRoot.ifBlank { "未写入配置草稿" }}",
                style = MaterialTheme.typography.bodySmall,
            )
            Text(
                "Android 导入源目录: ${configModel.androidImportRoot.ifBlank { "未写入配置草稿" }}",
                style = MaterialTheme.typography.bodySmall,
            )
            Text(
                "Windows 目标目录（只读摘要）: ${configModel.windowsTargetRoot}",
                style = MaterialTheme.typography.bodySmall,
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Button(onClick = onImportSnapshot, modifier = Modifier.weight(1f)) {
                    Text("导入快照")
                }
                Button(onClick = onImportLatestFromSyncRoot, modifier = Modifier.weight(1f)) {
                    Text("导入同步根目录最新快照")
                }
            }
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Button(onClick = onPublishLatestToSyncRoot, modifier = Modifier.weight(1f)) {
                    Text("发布最新快照到同步根目录")
                }
                Button(onClick = onExportConfig, modifier = Modifier.weight(1f)) {
                    Text("导出配置")
                }
            }
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Button(onClick = onExportSnapshot, modifier = Modifier.weight(1f)) {
                    Text("导出快照")
                }
                Button(onClick = onExportDiagnostic, modifier = Modifier.weight(1f)) {
                    Text("导出诊断")
                }
            }
        }
    }
}

@Composable
private fun BackupSection(
    artifactState: com.rimekit.android.artifacts.AndroidArtifactState,
    backupEntries: List<com.rimekit.android.artifacts.AndroidBackupEntry>,
    onExportBackup: () -> Unit,
    onExportBackupById: (String) -> Unit,
    onRollback: () -> Unit,
    onRollbackById: (String) -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text("备份与回滚", style = MaterialTheme.typography.titleMedium)
            Text(
                "最近备份: ${artifactState.latestBackupId ?: "无"}",
                style = MaterialTheme.typography.bodyMedium,
            )
            Button(
                onClick = onExportBackup,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("导出备份")
            }
            Button(
                onClick = onRollback,
                enabled = artifactState.latestBackupId != null,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("按最近备份回滚")
            }
            if (backupEntries.isNotEmpty()) {
                SectionCard(
                    title = "备份列表",
                    body = backupEntries.joinToString(separator = "\n\n") { entry ->
                        buildString {
                            appendLine("备份 ID: ${entry.backupId}")
                            appendLine("关联快照: ${entry.snapshotId ?: "none"}")
                            append("创建时间: ${entry.createdAt ?: "unknown"}")
                        }
                    },
                )
                backupEntries.forEach { entry ->
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Button(
                            onClick = { onExportBackupById(entry.backupId) },
                            modifier = Modifier.weight(1f),
                        ) {
                            Text("导出 ${entry.backupId}")
                        }
                        Button(
                            onClick = { onRollbackById(entry.backupId) },
                            modifier = Modifier.weight(1f),
                        ) {
                            Text("回滚到 ${entry.backupId}")
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ConfigureSection(
    configModel: com.rimekit.android.artifacts.AndroidConfigModel,
    surfaceTags: Map<String, String>,
    onUpdateConfig: (com.rimekit.android.artifacts.AndroidConfigModel) -> Unit,
    onSaveConfig: () -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = "Android 配置页 [${surfaceTag(surfaceTags, "profile_management")}, ${surfaceTag(surfaceTags, "candidate_presentation")}, ${surfaceTag(surfaceTags, "input_behavior")}]",
                style = MaterialTheme.typography.titleMedium,
            )
            SectionCard(
                title = "方案与默认方案 [${surfaceTag(surfaceTags, "profile_management")}]",
                body = buildString {
                    appendLine("启用方案: ${configModel.enabledSchemaIds.joinToString()}")
                    appendLine("Windows 默认方案（固定）: ${configModel.windowsDefaultSchemaId}")
                    append("Android 默认方案（固定）: ${configModel.androidDefaultSchemaId}")
                },
            )
            Button(
                onClick = {
                    onUpdateConfig(
                        configModel.copy(
                            enabledSchemaIds = listOf("rime_mint", "t9"),
                            windowsDefaultSchemaId = "rime_mint",
                            androidDefaultSchemaId = "t9",
                        ),
                    )
                },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("恢复固定方案约束")
            }
            ConfigToggle(
                title = "启用薄荷主方案（rime_mint）",
                checked = "rime_mint" in configModel.enabledSchemaIds,
                enabled = false,
                onCheckedChange = { _ -> },
            )
            ConfigToggle(
                title = "启用九键方案（t9）",
                checked = "t9" in configModel.enabledSchemaIds,
                enabled = false,
                onCheckedChange = { _ -> },
            )
            Text(
                text = "默认方案固定为 t9，键盘布局固定为中文 9 键（9_key）。",
                style = MaterialTheme.typography.bodyMedium,
            )
            SingleChoiceSegmentedButtonRow(
                modifier = Modifier.fillMaxWidth(),
            ) {
                listOf("vertical", "horizontal").forEachIndexed { index, layout ->
                    SegmentedButton(
                        selected = configModel.candidateLayout == layout,
                        onClick = {
                            onUpdateConfig(configModel.copy(candidateLayout = layout))
                        },
                        shape = androidx.compose.material3.SegmentedButtonDefaults.itemShape(
                            index = index,
                            count = 2,
                        ),
                    ) {
                        Text(if (layout == "vertical") "竖排候选" else "横排候选")
                    }
                }
            }
            NumberField(
                label = "候选数（留空表示不覆写）",
                value = configModel.candidatePageSize?.toString().orEmpty(),
                onValueChange = { value ->
                    if (value.isBlank()) {
                        onUpdateConfig(configModel.copy(candidatePageSize = null))
                    } else {
                        value.toIntOrNull()?.let { number ->
                            onUpdateConfig(configModel.copy(candidatePageSize = number))
                        }
                    }
                },
            )
            ConfigToggle(
                title = "启用模糊拼音",
                checked = configModel.fuzzyEnabled,
                onCheckedChange = { checked ->
                    onUpdateConfig(
                        configModel.copy(
                            fuzzyEnabled = checked,
                            fuzzyPresetId = if (checked) "cn_common" else "",
                            fuzzyAdditionalRules = if (checked) configModel.fuzzyAdditionalRules else emptyList(),
                            fuzzyTargetSchemaIds = if (checked && configModel.fuzzyTargetSchemaIds.isEmpty()) {
                                listOf("rime_mint")
                            } else {
                                configModel.fuzzyTargetSchemaIds
                            },
                        ),
                    )
                },
            )
            if (configModel.fuzzyEnabled) {
                SectionCard(
                    title = "模糊拼音目标方案 [${surfaceTag(surfaceTags, "fuzzy_pinyin")}]",
                    body = "当前目标: ${configModel.fuzzyTargetSchemaIds.joinToString()}",
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Button(
                        onClick = {
                            val targets = configModel.fuzzyTargetSchemaIds.toMutableSet()
                            if (!targets.add("rime_mint")) {
                                targets.remove("rime_mint")
                            }
                            onUpdateConfig(configModel.copy(fuzzyTargetSchemaIds = targets.toList().sorted()))
                        },
                        modifier = Modifier.weight(1f),
                    ) {
                        Text(if ("rime_mint" in configModel.fuzzyTargetSchemaIds) "薄荷主方案: 已启用" else "薄荷主方案: 未启用")
                    }
                    Button(
                        onClick = {
                            val targets = configModel.fuzzyTargetSchemaIds.toMutableSet()
                            if (!targets.add("t9")) {
                                targets.remove("t9")
                            }
                            onUpdateConfig(configModel.copy(fuzzyTargetSchemaIds = targets.toList().sorted()))
                        },
                        modifier = Modifier.weight(1f),
                    ) {
                        Text(if ("t9" in configModel.fuzzyTargetSchemaIds) "九键方案: 已启用" else "九键方案: 未启用")
                    }
                }
            }
            ConfigToggle(
                title = "Emoji 注释",
                checked = configModel.showEmojiComments,
                onCheckedChange = { checked ->
                    onUpdateConfig(configModel.copy(showEmojiComments = checked))
                },
            )
            ConfigToggle(
                title = "全角",
                checked = configModel.fullShapeEnabled,
                onCheckedChange = { checked ->
                    onUpdateConfig(configModel.copy(fullShapeEnabled = checked))
                },
            )
            ConfigToggle(
                title = "ASCII 标点",
                checked = configModel.asciiPunctEnabled,
                onCheckedChange = { checked ->
                    onUpdateConfig(configModel.copy(asciiPunctEnabled = checked))
                },
            )
            SingleChoiceSegmentedButtonRow(
                modifier = Modifier.fillMaxWidth(),
            ) {
                listOf(
                    "simplified" to "简体",
                    "traditional" to "繁体",
                    "opencc_switchable" to "OpenCC 切换",
                ).forEachIndexed { index, option ->
                    SegmentedButton(
                        selected = configModel.simplificationMode == option.first,
                        onClick = {
                            onUpdateConfig(configModel.copy(simplificationMode = option.first))
                        },
                        shape = androidx.compose.material3.SegmentedButtonDefaults.itemShape(
                            index = index,
                            count = 3,
                        ),
                    ) {
                        Text(option.second)
                    }
                }
            }
            ConfigToggle(
                title = "Emoji 联想",
                checked = configModel.emojiSuggestionEnabled,
                onCheckedChange = { checked ->
                    onUpdateConfig(configModel.copy(emojiSuggestionEnabled = checked))
                },
            )
            ConfigToggle(
                title = "声调显示",
                checked = configModel.toneDisplayEnabled,
                onCheckedChange = { checked ->
                    onUpdateConfig(configModel.copy(toneDisplayEnabled = checked))
                },
            )
            SectionCard(
                title = "个性化输入 [${surfaceTag(surfaceTags, "personalization")}]",
                body = buildString {
                    appendLine("符号预设: ${configModel.symbolProfileId}")
                    appendLine("预编辑显示: ${configModel.preeditFormatMode}")
                    append("自定义词条模式: ${configModel.customPhraseMode}")
                },
            )
            OutlinedTextField(
                value = configModel.symbolProfileId,
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(symbolProfileId = value))
                },
                label = { Text("符号预设 ID") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            OutlinedTextField(
                value = configModel.commentStyleVariant,
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(commentStyleVariant = value))
                },
                label = { Text("注释样式变体") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            SingleChoiceSegmentedButtonRow(
                modifier = Modifier.fillMaxWidth(),
            ) {
                listOf(
                    "upstream_default" to "上游默认",
                    "raw_code" to "原始编码",
                    "translated_code" to "翻译编码",
                ).forEachIndexed { index, option ->
                    SegmentedButton(
                        selected = configModel.preeditFormatMode == option.first,
                        onClick = {
                            onUpdateConfig(configModel.copy(preeditFormatMode = option.first))
                        },
                        shape = androidx.compose.material3.SegmentedButtonDefaults.itemShape(
                            index = index,
                            count = 3,
                        ),
                    ) {
                        Text(option.second)
                    }
                }
            }
            SingleChoiceSegmentedButtonRow(
                modifier = Modifier.fillMaxWidth(),
            ) {
                listOf(
                    "disabled" to "关闭",
                    "simple_code_only" to "简码",
                    "full_phrase" to "整句",
                ).forEachIndexed { index, option ->
                    SegmentedButton(
                        selected = configModel.customPhraseMode == option.first,
                        onClick = {
                            onUpdateConfig(configModel.copy(customPhraseMode = option.first))
                        },
                        shape = androidx.compose.material3.SegmentedButtonDefaults.itemShape(
                            index = index,
                            count = 3,
                        ),
                    ) {
                        Text(option.second)
                    }
                }
            }
            NumberField(
                label = "候选字号",
                value = configModel.candidateTextSize.toString(),
                onValueChange = { value ->
                    value.toIntOrNull()?.let { number ->
                        onUpdateConfig(configModel.copy(candidateTextSize = number))
                    }
                },
            )
            NumberField(
                label = "候选区高度",
                value = configModel.candidateViewHeight.toString(),
                onValueChange = { value ->
                    value.toIntOrNull()?.let { number ->
                        onUpdateConfig(configModel.copy(candidateViewHeight = number))
                    }
                },
            )
            OutlinedTextField(
                value = configModel.sharedSyncRoot,
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(sharedSyncRoot = value))
                },
                label = { Text("同步快照根目录") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            OutlinedTextField(
                value = configModel.exportRoot,
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(exportRoot = value))
                },
                label = { Text("导出目录") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            OutlinedTextField(
                value = configModel.backupRoot,
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(backupRoot = value))
                },
                label = { Text("备份目录") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            OutlinedTextField(
                value = configModel.windowsTargetRoot,
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(windowsTargetRoot = value))
                },
                label = { Text("Windows 目标目录（共享字段）") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            NumberField(
                label = "快照保留上限",
                value = configModel.snapshotRetentionLimit.toString(),
                onValueChange = { value ->
                    value.toIntOrNull()?.let { number ->
                        onUpdateConfig(configModel.copy(snapshotRetentionLimit = number))
                    }
                },
            )
            Button(
                onClick = onSaveConfig,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("保存配置")
            }
            SectionCard(
                title = "Windows 专属字段只读摘要 [${surfaceTag(surfaceTags, "windows_presentation")}]",
                body = buildString {
                    appendLine("字体: ${configModel.windowsFontFace.ifBlank { "跟随上游默认" }}")
                    appendLine("字号: ${if (configModel.windowsFontPoint > 0) configModel.windowsFontPoint else "跟随上游默认"}")
                    appendLine("DPI 模式: ${configModel.windowsDpiScaleMode}")
                    append("状态变化通知: ${if (configModel.windowsShowNotification) "开启" else "关闭"}")
                },
            )
        }
    }
}

@Composable
private fun ResourcesSection(
    configModel: com.rimekit.android.artifacts.AndroidConfigModel,
    resourceManifest: com.rimekit.android.artifacts.AndroidResourceManifest,
    surfaceTags: Map<String, String>,
    onUpdateConfig: (com.rimekit.android.artifacts.AndroidConfigModel) -> Unit,
    onSaveConfig: () -> Unit,
    onExportResourceManifest: () -> Unit,
    onExportResourceUpdateReport: () -> Unit,
    resourceUpdateReport: String?,
    onCheckResourceUpdates: () -> Unit,
    onOpenResourceSource: (String) -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = "词库与模型 [${surfaceTag(surfaceTags, "dictionary_management")}, ${surfaceTag(surfaceTags, "model_management")}]",
                style = MaterialTheme.typography.titleMedium,
            )
            Button(
                onClick = onExportResourceManifest,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("导出正式资源清单")
            }
            Button(
                onClick = onCheckResourceUpdates,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("检查资源更新")
            }
            Button(
                onClick = onExportResourceUpdateReport,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("导出资源更新检查结果")
            }
            Button(
                onClick = { onOpenResourceSource("https://github.com/Mintimate/oh-my-rime") },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("打开薄荷方案来源")
            }
            Button(
                onClick = { onOpenResourceSource("https://github.com/suiginko/moetype") },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("打开萌典词库来源")
            }
            Button(
                onClick = { onOpenResourceSource("https://pinyin.sogou.com/dict/detail/index/4") },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("打开搜狗新词来源")
            }
            if (!resourceUpdateReport.isNullOrBlank()) {
                SectionCard(
                    title = "资源更新检查结果 [${surfaceTag(surfaceTags, "dictionary_management")}]",
                    body = resourceUpdateReport,
                )
            }
            ResourceManifestCard(
                title = "正式方案资源",
                items = resourceManifest.schemas,
            )
            ResourceManifestCard(
                title = "正式词库资源",
                items = resourceManifest.dictionaries,
            )
            if (resourceManifest.models.isNotEmpty()) {
                ResourceManifestCard(
                    title = "正式模型资源",
                    items = resourceManifest.models,
                )
            }
            SectionCard(
                title = "正式词库顺序",
                body = configModel.dictionaryOrder.joinToString(separator = " -> "),
            )
            listOf(
                "moetype" to "萌典词库",
                "sogou_network_popular_words" to "搜狗网络流行新词",
                "custom_simple" to "自定义简码词条",
            ).forEach { option ->
                DictionaryItemRow(
                    title = option.second,
                    dictionaryId = option.first,
                    configModel = configModel,
                    onUpdateConfig = onUpdateConfig,
                )
            }
            SectionCard(
                title = "自定义词条摘要",
                body = "当前自定义词条数量: ${configModel.customEntries.size}\n格式：每行 `词条<TAB>编码<TAB>权重`。",
            )
            OutlinedTextField(
                value = serializeCustomEntries(configModel.customEntries),
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(customEntries = parseCustomEntries(value)))
                },
                label = { Text("自定义词条") },
                modifier = Modifier.fillMaxWidth(),
                minLines = 5,
            )
            ConfigToggle(
                title = "上下文联想",
                checked = configModel.contextualSuggestionsEnabled,
                onCheckedChange = { checked ->
                    onUpdateConfig(configModel.copy(contextualSuggestionsEnabled = checked))
                },
            )
            OutlinedTextField(
                value = configModel.modelRoot,
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(modelRoot = value))
                },
                label = { Text("模型根目录") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            OutlinedTextField(
                value = configModel.enabledModelIds.joinToString(","),
                onValueChange = { value ->
                    onUpdateConfig(
                        configModel.copy(
                            enabledModelIds = value.split(',')
                                .map(String::trim)
                                .filter(String::isNotBlank),
                        ),
                    )
                },
                label = { Text("启用模型 ID（逗号分隔）") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            OutlinedTextField(
                value = configModel.activeModelId,
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(activeModelId = value.trim()))
                },
                label = { Text("当前模型 ID") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            OutlinedTextField(
                value = serializeModelVersions(configModel.modelVersions),
                onValueChange = { value ->
                    onUpdateConfig(configModel.copy(modelVersions = parseModelVersions(value)))
                },
                label = { Text("模型版本（每行 model_id=version）") },
                modifier = Modifier.fillMaxWidth(),
                minLines = 4,
            )
            NumberField(
                label = "最大搭配长度",
                value = configModel.collocationMaxLength.toString(),
                onValueChange = { value ->
                    value.toIntOrNull()?.let { number ->
                        onUpdateConfig(configModel.copy(collocationMaxLength = number))
                    }
                },
            )
            NumberField(
                label = "最小搭配长度",
                value = configModel.collocationMinLength.toString(),
                onValueChange = { value ->
                    value.toIntOrNull()?.let { number ->
                        onUpdateConfig(configModel.copy(collocationMinLength = number))
                    }
                },
            )
            NumberField(
                label = "最大同音词数",
                value = configModel.maxHomophones.toString(),
                onValueChange = { value ->
                    value.toIntOrNull()?.let { number ->
                        onUpdateConfig(configModel.copy(maxHomophones = number))
                    }
                },
            )
            NumberField(
                label = "最大同形词数",
                value = configModel.maxHomographs.toString(),
                onValueChange = { value ->
                    value.toIntOrNull()?.let { number ->
                        onUpdateConfig(configModel.copy(maxHomographs = number))
                    }
                },
            )
            Button(
                onClick = onSaveConfig,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("保存词库与模型配置")
            }
        }
    }
}

@Composable
private fun ResourceManifestCard(
    title: String,
    items: List<com.rimekit.android.artifacts.AndroidResourceItem>,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(title, style = MaterialTheme.typography.titleMedium)
            if (items.isEmpty()) {
                Text("当前没有登记的正式资源。", style = MaterialTheme.typography.bodySmall)
            } else {
                items.forEach { item ->
                    Text(
                        text = buildString {
                            appendLine("${item.displayName} (${item.id})")
                            appendLine("来源分类: ${item.sourceClass}")
                            appendLine("来源: ${item.source}")
                            item.sourceType?.let { appendLine("来源类型: $it") }
                            item.versionOrUpdatedAt?.let { append("版本/更新时间: $it") }
                        },
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
            }
        }
    }
}

@Composable
private fun DictionaryItemRow(
    title: String,
    dictionaryId: String,
    configModel: com.rimekit.android.artifacts.AndroidConfigModel,
    onUpdateConfig: (com.rimekit.android.artifacts.AndroidConfigModel) -> Unit,
) {
    val enabled = dictionaryId in configModel.enabledDictionaryIds
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(title, style = MaterialTheme.typography.bodyMedium)
                Checkbox(
                    checked = enabled,
                    onCheckedChange = { checked ->
                        val enabledIds = configModel.enabledDictionaryIds.toMutableList()
                        val order = configModel.dictionaryOrder.toMutableList()
                        if (checked) {
                            if (dictionaryId !in enabledIds) {
                                enabledIds += dictionaryId
                            }
                            if (dictionaryId !in order) {
                                order += dictionaryId
                            }
                        } else {
                            enabledIds.remove(dictionaryId)
                            order.remove(dictionaryId)
                        }
                        onUpdateConfig(
                            configModel.copy(
                                enabledDictionaryIds = enabledIds,
                                dictionaryOrder = order,
                            ),
                        )
                    },
                )
            }
            if (enabled) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Button(
                        onClick = {
                            val order = configModel.dictionaryOrder.toMutableList()
                            val index = order.indexOf(dictionaryId)
                            if (index > 0) {
                                order.removeAt(index)
                                order.add(index - 1, dictionaryId)
                                onUpdateConfig(configModel.copy(dictionaryOrder = order))
                            }
                        },
                        modifier = Modifier.weight(1f),
                    ) {
                        Text("上移")
                    }
                    Button(
                        onClick = {
                            val order = configModel.dictionaryOrder.toMutableList()
                            val index = order.indexOf(dictionaryId)
                            if (index in 0 until order.lastIndex) {
                                order.removeAt(index)
                                order.add(index + 1, dictionaryId)
                                onUpdateConfig(configModel.copy(dictionaryOrder = order))
                            }
                        },
                        modifier = Modifier.weight(1f),
                    ) {
                        Text("下移")
                    }
                }
            }
        }
    }
}

@Composable
private fun ConfigToggle(
    title: String,
    checked: Boolean,
    enabled: Boolean = true,
    onCheckedChange: (Boolean) -> Unit,
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(title, style = MaterialTheme.typography.bodyMedium)
        Checkbox(
            checked = checked,
            enabled = enabled,
            onCheckedChange = onCheckedChange,
        )
    }
}

@Composable
private fun NumberField(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        label = { Text(label) },
        modifier = Modifier.fillMaxWidth(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        singleLine = true,
    )
}

private fun serializeCustomEntries(
    entries: List<com.rimekit.android.artifacts.AndroidCustomEntry>,
): String {
    return entries.joinToString(separator = "\n") { entry ->
        "${entry.text}\t${entry.code}\t${entry.weight}"
    }
}

private fun parseCustomEntries(
    rawValue: String,
): List<com.rimekit.android.artifacts.AndroidCustomEntry> {
    return rawValue.lineSequence()
        .map(String::trim)
        .filter(String::isNotBlank)
        .map { line ->
            val parts = line.split('\t')
            val text = parts.getOrNull(0).orEmpty()
            val code = parts.getOrNull(1).orEmpty()
            val weight = parts.getOrNull(2)?.toIntOrNull() ?: 0
            com.rimekit.android.artifacts.AndroidCustomEntry(
                text = text,
                code = code,
                weight = weight,
            )
        }
        .toList()
}

private fun serializeModelVersions(
    versions: Map<String, String>,
): String {
    return versions.entries
        .sortedBy { (key, _) -> key }
        .joinToString(separator = "\n") { (key, value) -> "$key=$value" }
}

private fun parseModelVersions(
    rawValue: String,
): Map<String, String> {
    return rawValue.lineSequence()
        .map(String::trim)
        .filter(String::isNotBlank)
        .mapNotNull { line ->
            val key = line.substringBefore('=', missingDelimiterValue = "").trim()
            val value = line.substringAfter('=', missingDelimiterValue = "").trim()
            if (key.isBlank() || value.isBlank()) {
                null
            } else {
                key to value
            }
        }
        .toMap()
}

@Composable
private fun PhaseTimelineCard(
    phaseStates: List<AndroidPhaseState>,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = "正式阶段",
                style = MaterialTheme.typography.titleMedium,
            )
            phaseStates.forEach { phaseState ->
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.Top,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Text(
                        text = phaseState.phase.title,
                        modifier = Modifier.size(width = 96.dp, height = 20.dp),
                        style = MaterialTheme.typography.labelMedium,
                    )
                    Column(
                        verticalArrangement = Arrangement.spacedBy(2.dp),
                    ) {
                        Text(
                            text = statusTitle(phaseState.status.id),
                            style = MaterialTheme.typography.labelLarge,
                        )
                        Text(
                            text = phaseState.summary,
                            style = MaterialTheme.typography.bodySmall,
                        )
                        Text(
                            text = phaseState.nextAction,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
        }
    }
}

@Composable
internal fun DetectSection(
    snapshot: AndroidPlatformSnapshot,
    findings: List<AndroidFinding>,
    taskDefinitions: Map<String, AndroidTaskUiDefinition>,
    onOpenInputMethodSettings: () -> Unit,
    onShowInputMethodPicker: () -> Unit,
    onOpenFcitxInstallOrApp: () -> Unit,
    onOpenRimePluginInstallOrApp: () -> Unit,
    onOpenFcitxUninstallOrSettings: () -> Unit,
    onOpenRimePluginUninstallOrSettings: () -> Unit,
) {
    Column(
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        SectionCard(
            title = "环境检测",
            body = buildString {
                appendLine("Android 承载器: ${probeStateLabel(snapshot.carrierState)}")
                appendLine("Android 承载器版本: ${snapshot.carrierVersion ?: "未安装"}")
                appendLine("Rime 插件: ${probeStateLabel(snapshot.rimePluginState)}")
                appendLine("Rime 插件版本: ${snapshot.rimePluginVersion ?: "未安装"}")
                appendLine("同步快照根目录授权: ${permissionStateLabel(snapshot.syncRootPermission)}")
                appendLine("Android 导入源目录授权: ${permissionStateLabel(snapshot.importRootPermission)}")
                appendLine("输入法已启用: ${imeStateLabel(snapshot.imeEnabledState)}")
                appendLine("输入法已选中: ${imeStateLabel(snapshot.imeSelectedState)}")
                appendLine("承载器更新来源: ${snapshot.carrierUpdateSource ?: "未配置"}")
                append("插件更新来源: ${snapshot.rimePluginUpdateSource ?: "未配置"}")
            },
        )
        if (findings.isNotEmpty()) {
            FindingsCard(findings)
        }
        TaskActionRow(
            left = AndroidTaskActionButtonState(
                task = taskDefinitions["android_request_carrier_install"],
                buttonLabel = if (snapshot.carrierState == ProbeState.Present) "打开 Fcitx5" else "安装 Fcitx5",
                onClick = onOpenFcitxInstallOrApp,
            ),
            right = AndroidTaskActionButtonState(
                task = taskDefinitions["android_request_rime_plugin_install"],
                buttonLabel = if (snapshot.rimePluginState == ProbeState.Present) "打开 Rime 插件" else "安装 Rime 插件",
                onClick = onOpenRimePluginInstallOrApp,
            ),
        )
        TaskActionRow(
            left = AndroidTaskActionButtonState(
                task = taskDefinitions["android_open_ime_settings"],
                buttonLabel = "打开输入法设置",
                onClick = onOpenInputMethodSettings,
            ),
            right = AndroidTaskActionButtonState(
                task = taskDefinitions["android_open_ime_picker"],
                buttonLabel = "切换当前输入法",
                onClick = onShowInputMethodPicker,
            ),
        )
        TaskActionRow(
            left = AndroidTaskActionButtonState(
                task = taskDefinitions["android_request_carrier_uninstall"],
                buttonLabel = "卸载 Fcitx5",
                onClick = onOpenFcitxUninstallOrSettings,
                enabled = snapshot.carrierState == ProbeState.Present,
            ),
            right = AndroidTaskActionButtonState(
                task = taskDefinitions["android_request_rime_plugin_uninstall"],
                buttonLabel = "卸载 Rime 插件",
                onClick = onOpenRimePluginUninstallOrSettings,
                enabled = snapshot.rimePluginState == ProbeState.Present,
            ),
        )
    }
}

private const val FCITX_PACKAGE_NAME = "org.fcitx.fcitx5.android"
private const val RIME_PLUGIN_PACKAGE_NAME = "org.fcitx.fcitx5.android.plugin.rime"

@Composable
private fun AuthorizationSection(
    snapshot: AndroidPlatformSnapshot,
    manualSteps: List<AndroidManualStep>,
    onGrantSyncRoot: () -> Unit,
    onGrantImportRoot: () -> Unit,
    onClearSyncRoot: () -> Unit,
    onClearImportRoot: () -> Unit,
) {
    Column(
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        PermissionCard(
            title = "同步快照根目录",
            status = snapshot.syncRootPermission.name,
            uri = snapshot.syncRootUri,
            onGrant = onGrantSyncRoot,
            onClear = onClearSyncRoot,
        )
        PermissionCard(
            title = "Android 导入源目录",
            status = snapshot.importRootPermission.name,
            uri = snapshot.importRootUri,
            onGrant = onGrantImportRoot,
            onClear = onClearImportRoot,
        )
        if (manualSteps.isNotEmpty()) {
            Card(
                modifier = Modifier.fillMaxWidth(),
            ) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Text(
                        text = "手动步骤",
                        style = MaterialTheme.typography.titleMedium,
                    )
                    manualSteps.forEach { step ->
                        Text(
                            text = "${step.title}\n${step.nextAction}",
                            style = MaterialTheme.typography.bodyMedium,
                        )
                    }
                }
            }
        }
    }
}

@Composable
internal fun DeploySection(
    phaseStates: List<AndroidPhaseState>,
    artifactState: com.rimekit.android.artifacts.AndroidArtifactState,
    taskDefinitions: Map<String, AndroidTaskUiDefinition>,
    isBusy: Boolean,
    deployInstructions: String?,
    snapshot: AndroidPlatformSnapshot,
    onGenerate: () -> Unit,
    onBackup: () -> Unit,
    onApply: () -> Unit,
    onDeploy: () -> Unit,
    onRecheck: () -> Unit,
    onRollback: () -> Unit,
    onSetDeliveryConfirmed: (Boolean) -> Unit,
    onSetSchemaConfirmed: (Boolean) -> Unit,
    onSetKeyboardConfirmed: (Boolean) -> Unit,
) {
    val deployRelated = phaseStates.filter {
        it.phase.id in listOf("generate", "backup", "apply", "deploy", "recheck", "rollback")
    }
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = "导入与部署闭环",
                style = MaterialTheme.typography.titleMedium,
            )
            deployRelated.forEach { item ->
                Text(
                    text = "${item.phase.id} · ${item.status.id}\n${item.nextAction}",
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
            Text(
                text = "最近快照: ${artifactState.latestSnapshotId ?: "无"}\n最近备份: ${artifactState.latestBackupId ?: "无"}",
                style = MaterialTheme.typography.bodyMedium,
            )
            if (artifactState.generatedFileNames.isNotEmpty()) {
                Text(
                    text = "最近生成文件: ${artifactState.generatedFileNames.joinToString()}",
                    style = MaterialTheme.typography.bodySmall,
                )
            }
            if (!deployInstructions.isNullOrBlank()) {
                Text(
                    text = "最新应用清单:\n$deployInstructions",
                    style = MaterialTheme.typography.bodySmall,
                )
            }
            TaskActionRow(
                left = AndroidTaskActionButtonState(taskDefinitions["android_generate_targets"], "生成", onGenerate, !isBusy),
                center = AndroidTaskActionButtonState(taskDefinitions["android_backup_import_root"], "备份", onBackup, !isBusy),
                right = AndroidTaskActionButtonState(taskDefinitions["android_apply_import_bundle"], "应用", onApply, !isBusy),
            )
            TaskActionRow(
                left = AndroidTaskActionButtonState(taskDefinitions["android_deploy_carrier"], "部署", onDeploy, !isBusy),
                center = AndroidTaskActionButtonState(taskDefinitions["android_recheck_runtime"], "回检", onRecheck, !isBusy),
                right = AndroidTaskActionButtonState(taskDefinitions["android_rollback_import_root"], "回滚", onRollback, !isBusy),
            )
            ConfirmationCard(
                title = "已返回应用等待回检",
                status = manualConfirmationStateLabel(snapshot.deliveryConfirmation),
                onConfirm = { onSetDeliveryConfirmed(true) },
                onClear = { onSetDeliveryConfirmed(false) },
            )
            ReadOnlyStatusCard(
                title = "导入源默认方案为 t9",
                status = snapshot.requiredSchemaApplied.name,
            )
            ReadOnlyStatusCard(
                title = "导入源键盘布局为 9_key",
                status = snapshot.keyboardLayoutApplied.name,
            )
        }
    }
}

@Composable
private fun TaskActionRow(
    left: AndroidTaskActionButtonState,
    center: AndroidTaskActionButtonState? = null,
    right: AndroidTaskActionButtonState? = null,
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        TaskActionButton(left, Modifier.weight(1f))
        if (center != null) {
            TaskActionButton(center, Modifier.weight(1f))
        }
        if (right != null) {
            TaskActionButton(right, Modifier.weight(1f))
        }
    }
}

@Composable
private fun TaskActionButton(
    state: AndroidTaskActionButtonState,
    modifier: Modifier = Modifier,
) {
    Card(modifier = modifier) {
        Column(
            modifier = Modifier.padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(
                text = state.task?.title ?: state.buttonLabel,
                style = MaterialTheme.typography.bodyMedium,
            )
            if (state.task != null) {
                Text(
                    text = "入口: ${state.task.entryPoints.joinToString { entryPointKindLabel(it) }}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Button(
                onClick = state.onClick,
                enabled = state.enabled,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(state.buttonLabel)
            }
        }
    }
}

@Composable
private fun ConfirmationCard(
    title: String,
    status: String,
    onConfirm: () -> Unit,
    onClear: () -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = title,
                style = MaterialTheme.typography.titleMedium,
            )
            Text(
                text = "当前状态: ${probeStateLabel(status)}",
                style = MaterialTheme.typography.bodyMedium,
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Button(
                    onClick = onConfirm,
                    modifier = Modifier.weight(1f),
                ) {
                    Text("已确认")
                }
                Button(
                    onClick = onClear,
                    modifier = Modifier.weight(1f),
                ) {
                    Text("重置")
                }
            }
        }
    }
}

@Composable
private fun ReadOnlyStatusCard(
    title: String,
    status: String,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = title,
                style = MaterialTheme.typography.titleMedium,
            )
            Text(
                text = "当前状态: ${probeStateLabel(status)}",
                style = MaterialTheme.typography.bodyMedium,
            )
        }
    }
}

@Composable
private fun DiagnoseSection(
    findings: List<AndroidFinding>,
    snapshot: AndroidPlatformSnapshot,
    manualSteps: List<AndroidManualStep>,
    deployInstructions: String?,
    diagnosticReport: AndroidDiagnosticReport?,
    resourceManifest: com.rimekit.android.artifacts.AndroidResourceManifest,
    configModel: com.rimekit.android.artifacts.AndroidConfigModel,
    artifactState: com.rimekit.android.artifacts.AndroidArtifactState,
    onImportRuntime: () -> Unit,
    onOverrideWithGui: () -> Unit,
    onGrantSyncRoot: () -> Unit,
    onGrantImportRoot: () -> Unit,
    onClearSyncRoot: () -> Unit,
    onClearImportRoot: () -> Unit,
) {
    Column(
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (findings.isEmpty()) {
            SectionCard(
                title = "诊断",
                body = diagnosticReport?.let { report ->
                    buildString {
                        appendLine("当前阶段: ${phaseTitle(report.phase)}")
                        appendLine("当前结果: ${statusTitle(report.status)}")
                        appendLine("下一步: ${report.nextAction}")
                        append("最近快照: ${report.snapshotId ?: "none"}")
                    }
                } ?: "当前没有新的诊断项，可先执行检测、生成、同步或部署动作。"
            )
        } else {
            FindingsCard(findings)
        }
        if (diagnosticReport != null) {
            DiagnosticReportCard(diagnosticReport)
        }
        SectionCard(
            title = "高级诊断摘要",
            body = buildString {
                appendLine("最近快照: ${artifactState.latestSnapshotId ?: "none"}")
                appendLine("最近备份: ${artifactState.latestBackupId ?: "none"}")
                appendLine("最近应用快照: ${artifactState.latestAppliedSnapshotId ?: "none"}")
                appendLine("最近回检摘要: ${artifactState.lastRecheckSummary ?: "none"}")
                appendLine("同步快照根目录: ${configModel.sharedSyncRoot.ifBlank { snapshot.syncRootUri ?: "未配置" }}")
                appendLine("Android 导入源目录: ${configModel.androidImportRoot.ifBlank { snapshot.importRootUri ?: "未配置" }}")
                appendLine("Windows 目标目录: ${configModel.windowsTargetRoot}")
                appendLine("正式方案数: ${resourceManifest.schemas.size}")
                appendLine("正式词库数: ${resourceManifest.dictionaries.size}")
                append("正式模型数: ${resourceManifest.models.size}")
            },
        )
        Card(
            modifier = Modifier.fillMaxWidth(),
        ) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Text(
                    text = "冲突恢复",
                    style = MaterialTheme.typography.titleMedium,
                )
                Text(
                    text = "当配置模型、目标配置或运行态不一致时，只允许显式选择 import_runtime 或 override_with_gui。",
                    style = MaterialTheme.typography.bodyMedium,
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Button(
                        onClick = onImportRuntime,
                        modifier = Modifier.weight(1f),
                    ) {
                        Text("导入运行态")
                    }
                    Button(
                        onClick = onOverrideWithGui,
                        modifier = Modifier.weight(1f),
                    ) {
                        Text("以当前配置覆盖")
                    }
                }
            }
        }
        if (!deployInstructions.isNullOrBlank()) {
            SectionCard(
                title = "部署引导",
                body = deployInstructions,
            )
        }
        if (manualSteps.isNotEmpty()) {
            AuthorizationSection(
                snapshot = snapshot,
                manualSteps = manualSteps,
                onGrantSyncRoot = onGrantSyncRoot,
                onGrantImportRoot = onGrantImportRoot,
                onClearSyncRoot = onClearSyncRoot,
                onClearImportRoot = onClearImportRoot,
            )
        }
    }
}

@Composable
private fun DiagnosticReportCard(
    report: AndroidDiagnosticReport,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(
                text = "结构化诊断",
                style = MaterialTheme.typography.titleMedium,
            )
            Text("当前阶段: ${phaseTitle(report.phase)}", style = MaterialTheme.typography.bodySmall)
            Text("当前结果: ${statusTitle(report.status)}", style = MaterialTheme.typography.bodySmall)
            Text("显式反馈: ${displayKindLabel(report.displayKind)}", style = MaterialTheme.typography.bodySmall)
            Text("快照引用: ${report.snapshotId ?: "none"}", style = MaterialTheme.typography.bodySmall)
            Text("备份引用: ${report.backupId ?: "none"}", style = MaterialTheme.typography.bodySmall)
            Text("目标状态已修改: ${yesNo(report.targetStateMutated)}", style = MaterialTheme.typography.bodySmall)
            Text("可回滚: ${yesNo(report.rollbackAvailable)}", style = MaterialTheme.typography.bodySmall)
            Text("建议回滚: ${yesNo(report.rollbackRecommended)}", style = MaterialTheme.typography.bodySmall)
            Text("下一步动作: ${report.nextAction}", style = MaterialTheme.typography.bodySmall)
            if (report.entryPoints.isNotEmpty()) {
                Text("可用入口：", style = MaterialTheme.typography.bodySmall)
                report.entryPoints.forEach { entryPoint ->
                    Text(
                        text = "- ${entryPointKindLabel(entryPoint.kind)}: ${entryPoint.label}${entryPoint.target?.let { " ($it)" } ?: ""}",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
            }
        }
    }
}

@Composable
private fun PermissionCard(
    title: String,
    status: String,
    uri: String?,
    onGrant: () -> Unit,
    onClear: () -> Unit,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = title,
                style = MaterialTheme.typography.titleMedium,
            )
            Text(
                text = "状态: ${permissionStateLabel(status)}",
                style = MaterialTheme.typography.bodyMedium,
            )
            Text(
                text = "当前目录: ${uri ?: "未授权"}",
                style = MaterialTheme.typography.bodySmall,
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Button(
                    onClick = onGrant,
                    modifier = Modifier.weight(1f),
                ) {
                    Text("选择目录")
                }
                Button(
                    onClick = onClear,
                    modifier = Modifier.weight(1f),
                ) {
                    Text("清除授权")
                }
            }
        }
    }
}

@Composable
private fun FindingsCard(
    findings: List<AndroidFinding>,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(
                text = "诊断项",
                style = MaterialTheme.typography.titleMedium,
            )
            findings.forEach { finding ->
                Text(
                    text = "[${displayKindLabel(finding.displayKind)}] [${finding.code}] ${finding.summary}\n${finding.detail}\n自动动作: ${autoActionKindLabel(finding.autoActionKind)}\n入口类型: ${entryPointKindLabel(finding.entryPointKind)}",
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
        }
    }
}

private fun displayKindLabel(displayKind: String): String {
    return when (displayKind) {
        AndroidDisplayKinds.ExplicitWarning -> "显式告警"
        AndroidDisplayKinds.ExplicitPrompt -> "显式提示"
        AndroidDisplayKinds.ExplicitError -> "显式报错"
        else -> "无"
    }
}

private fun phaseTitle(phaseId: String): String {
    return when (phaseId) {
        "detect" -> "检测"
        "configure" -> "配置"
        "generate" -> "生成"
        "backup" -> "备份"
        "apply" -> "应用"
        "deploy" -> "部署"
        "recheck" -> "回检"
        "rollback" -> "回滚"
        "diagnose" -> "诊断"
        else -> phaseId
    }
}

private fun statusTitle(statusId: String): String {
    return when (statusId) {
        "ready" -> "就绪"
        "manual_action_required" -> "等待手动步骤"
        "blocked" -> "阻塞"
        "failed" -> "失败"
        "completed" -> "完成"
        else -> statusId
    }
}

private fun probeStateLabel(state: ProbeState): String {
    return when (state) {
        ProbeState.Unknown -> "未知"
        ProbeState.Present -> "已满足"
        ProbeState.Missing -> "缺失"
    }
}

private fun probeStateLabel(state: String): String {
    return when (state) {
        "Unknown", "unknown" -> "未知"
        "Present", "present" -> "已满足"
        "Missing", "missing" -> "缺失"
        else -> state
    }
}

private fun manualConfirmationStateLabel(state: ManualConfirmationState): String {
    return when (state) {
        ManualConfirmationState.Unknown -> "未知"
        ManualConfirmationState.Confirmed -> "已确认"
        ManualConfirmationState.Missing -> "待确认"
    }
}

private fun permissionStateLabel(state: PermissionState): String {
    return when (state) {
        PermissionState.Unknown -> "未知"
        PermissionState.Granted -> "已授权"
        PermissionState.Missing -> "未授权"
    }
}

private fun permissionStateLabel(state: String): String {
    return when (state) {
        "Unknown", "unknown" -> "未知"
        "Granted", "granted" -> "已授权"
        "Missing", "missing" -> "未授权"
        else -> state
    }
}

private fun imeStateLabel(state: ImeState): String {
    return when (state) {
        ImeState.Unknown -> "未知"
        ImeState.Enabled -> "已满足"
        ImeState.Missing -> "未满足"
    }
}

private fun autoActionKindLabel(kind: String): String {
    return when (kind) {
        "detect_only" -> "仅检测"
        "install_request" -> "安装请求"
        "reinstall_request" -> "重新安装请求"
        "repair_check" -> "修复检查"
        "open_settings" -> "打开设置"
        "open_picker" -> "打开选择器"
        "open_directory" -> "打开目录"
        "open_logs" -> "打开日志"
        "retry_execution" -> "重试执行"
        "none" -> "无自动动作"
        else -> kind
    }
}

private fun entryPointKindLabel(kind: String): String {
    return when (kind) {
        "install_url" -> "安装入口"
        "installer_launch" -> "安装器启动"
        "uninstall_launch" -> "卸载入口"
        "settings_deep_link" -> "设置深链"
        "input_method_picker" -> "输入法选择器"
        "directory_authorization" -> "目录授权"
        "deploy_confirmation" -> "部署确认"
        "directory_open" -> "打开目录"
        "logs_open" -> "打开日志"
        "retry" -> "重新检测/重试"
        "rollback" -> "回滚"
        "none" -> "无入口"
        else -> kind
    }
}

private fun yesNo(value: Boolean): String = if (value) "是" else "否"

private fun loadConfigSurfaceSummary(context: android.content.Context): String {
    return runCatching {
        val payload = context.assets.open("config_surface_registry.json").bufferedReader().use { reader -> reader.readText() }
        val root = org.json.JSONObject(payload)
        val surfaces = root.getJSONArray("surfaces")
        var editable = 0
        var readonly = 0
        var explicitPrompt = 0
        val editableExamples = mutableListOf<String>()
        val readonlyExamples = mutableListOf<String>()

        for (index in 0 until surfaces.length()) {
            val surface = surfaces.getJSONObject(index)
            val displayName = surface.getString("display_name")
            val editPlatforms = surface.getJSONArray("edit_platforms")
            val readonlyPlatforms = surface.getJSONArray("readonly_platforms")
            val feedbackContract = surface.getJSONObject("feedback_contract")

            if ((0 until editPlatforms.length()).any { editPlatforms.getString(it) == "android" }) {
                editable++
                if (editableExamples.size < 4) {
                    editableExamples += displayName
                }
            }
            if ((0 until readonlyPlatforms.length()).any { readonlyPlatforms.getString(it) == "android" }) {
                readonly++
                if (readonlyExamples.size < 4) {
                    readonlyExamples += displayName
                }
            }
            if (feedbackContract.getString("display_kind") == AndroidDisplayKinds.ExplicitPrompt) {
                explicitPrompt++
            }
        }

        buildString {
            appendLine("正式配置面摘要：")
            appendLine("Android 可编辑配置面数: $editable")
            appendLine("Android 只读配置面数: $readonly")
            appendLine("需显式提示的配置面数: $explicitPrompt")
            appendLine("示例可编辑面: ${editableExamples.joinToString("、")}")
            append("示例只读面: ${if (readonlyExamples.isEmpty()) "无" else readonlyExamples.joinToString("、")}")
        }
    }.getOrElse { error ->
        "正式配置面摘要读取失败：${error.message ?: "未知错误"}"
    }
}

internal data class AndroidTaskUiDefinition(
    val taskId: String,
    val title: String,
    val entryPoints: List<String>,
)

internal data class AndroidTaskActionButtonState(
    val task: AndroidTaskUiDefinition?,
    val buttonLabel: String,
    val onClick: () -> Unit,
    val enabled: Boolean = true,
)

private fun loadAndroidTaskDefinitions(context: android.content.Context): Map<String, AndroidTaskUiDefinition> {
    return runCatching {
        val payload = context.assets.open("android_tasks.json").bufferedReader().use { reader -> reader.readText() }
        val root = org.json.JSONObject(payload)
        val tasks = root.getJSONArray("tasks")
        buildMap {
            for (index in 0 until tasks.length()) {
                val task = tasks.getJSONObject(index)
                val entryPoints = task.getJSONArray("entry_points")
                put(
                    task.getString("task_id"),
                    AndroidTaskUiDefinition(
                        taskId = task.getString("task_id"),
                        title = task.getString("title"),
                        entryPoints = buildList {
                            for (entryIndex in 0 until entryPoints.length()) {
                                add(entryPoints.getString(entryIndex))
                            }
                        },
                    ),
                )
            }
        }
    }.getOrElse { emptyMap() }
}

private fun loadAndroidSurfaceTags(context: android.content.Context): Map<String, String> {
    return runCatching {
        val payload = context.assets.open("config_surface_registry.json").bufferedReader().use { reader -> reader.readText() }
        val root = org.json.JSONObject(payload)
        val surfaces = root.getJSONArray("surfaces")
        buildMap {
            for (index in 0 until surfaces.length()) {
                val surface = surfaces.getJSONObject(index)
                put(surface.getString("surface_id"), surface.getString("surface_id"))
            }
        }
    }.getOrElse { emptyMap() }
}

private fun surfaceTag(surfaceTags: Map<String, String>, surfaceId: String): String {
    return surfaceTags[surfaceId] ?: surfaceId
}

@Composable
private fun StatusCard(
    title: String,
    content: String,
) {
    SectionCard(
        title = title,
        body = content,
    )
}

@Composable
private fun SectionCard(
    title: String,
    body: String,
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(
                text = title,
                style = MaterialTheme.typography.titleMedium,
            )
            Text(
                text = body,
                style = MaterialTheme.typography.bodyMedium,
            )
        }
    }
}
