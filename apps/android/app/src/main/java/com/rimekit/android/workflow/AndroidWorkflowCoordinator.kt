package com.rimekit.android.workflow

import com.rimekit.android.artifacts.AndroidArtifactState
import com.rimekit.android.artifacts.AndroidBackupEntry
import com.rimekit.android.artifacts.AndroidConfigModel
import com.rimekit.android.artifacts.AndroidResourceManifest
import com.rimekit.android.diagnostics.AndroidDiagnosticReport

/**
 * Android A4 当前阶段的状态编排器。
 */
class AndroidWorkflowCoordinator(
    private val platformService: AndroidPlatformService,
) {
    fun buildUiState(
        selectedSection: WorkflowSection,
        artifactState: AndroidArtifactState = AndroidArtifactState(),
        backupEntries: List<AndroidBackupEntry> = emptyList(),
        isBusy: Boolean = false,
        operationMessage: String? = null,
        deployInstructions: String? = null,
        configModel: AndroidConfigModel = AndroidConfigModel.createDefault(),
        resourceManifest: AndroidResourceManifest = AndroidResourceManifest(
            schemas = emptyList(),
            dictionaries = emptyList(),
            models = emptyList(),
        ),
        resourceUpdateReport: String? = null,
        diagnosticReport: AndroidDiagnosticReport? = null,
    ): AndroidWorkflowUiState {
        val snapshot = platformService.readSnapshot()
        val findings = buildFindings(snapshot, artifactState)
        val manualSteps = buildManualSteps(snapshot)
        val phaseStates = buildPhaseStates(snapshot, findings, manualSteps, artifactState)
        val diagnosePhase = phaseStates.last()
        val activePhase = if (
            diagnosePhase.phase == AndroidWorkflowPhase.Diagnose &&
            (diagnosePhase.status == AndroidWorkflowStatus.Completed || diagnosePhase.status == AndroidWorkflowStatus.Failed)
        ) {
            diagnosePhase
        } else {
            phaseStates.firstOrNull { it.status != AndroidWorkflowStatus.Completed }
                ?: diagnosePhase
        }

        return AndroidWorkflowUiState(
            selectedSection = selectedSection,
            currentPhase = activePhase.phase.id,
            currentStatus = activePhase.status.id,
            nextAction = activePhase.nextAction,
            phaseStates = phaseStates,
            findings = findings,
            manualSteps = manualSteps,
            platformSnapshot = snapshot,
            artifactState = artifactState,
            backupEntries = backupEntries,
            isBusy = isBusy,
            operationMessage = operationMessage,
            deployInstructions = deployInstructions,
            configModel = configModel,
            resourceManifest = resourceManifest,
            resourceUpdateReport = resourceUpdateReport,
            diagnosticReport = diagnosticReport,
        )
    }

    private fun buildFindings(
        snapshot: AndroidPlatformSnapshot,
        artifactState: AndroidArtifactState,
    ): List<AndroidFinding> {
        val findings = mutableListOf<AndroidFinding>()

        if (snapshot.carrierState == ProbeState.Missing) {
            findings += AndroidFinding(
                code = "ANDROID_CARRIER_MISSING",
                summary = "未检测到 Android 承载器",
                detail = "需要先安装 Fcitx5 for Android，并在返回应用后重新检测。",
                displayKind = AndroidDisplayKinds.ExplicitError,
                autoActionKind = AndroidAutoActionKinds.InstallRequest,
                entryPointKind = AndroidEntryPointKinds.InstallUrl,
            )
        }
        if (snapshot.rimePluginState == ProbeState.Missing) {
            findings += AndroidFinding(
                code = "ANDROID_RIME_PLUGIN_MISSING",
                summary = "未检测到 Android Rime 插件",
                detail = "需要先安装 Rime 插件，并在返回应用后重新检测。",
                displayKind = AndroidDisplayKinds.ExplicitError,
                autoActionKind = AndroidAutoActionKinds.InstallRequest,
                entryPointKind = AndroidEntryPointKinds.InstallUrl,
            )
        }
        if (snapshot.syncRootPermission == PermissionState.Missing ||
            snapshot.importRootPermission == PermissionState.Missing
        ) {
            findings += AndroidFinding(
                code = "ANDROID_PERMISSION_MISSING",
                summary = "缺少 Android 目录授权",
                detail = "同步快照根目录与 Android 导入源目录必须分别确认授权状态。",
                displayKind = AndroidDisplayKinds.ExplicitPrompt,
                autoActionKind = AndroidAutoActionKinds.None,
                entryPointKind = AndroidEntryPointKinds.DirectoryAuthorization,
            )
        }
        if (snapshot.imeEnabledState == ImeState.Missing) {
            findings += AndroidFinding(
                code = "ANDROID_IME_NOT_ENABLED",
                summary = "Android 输入法尚未启用",
                detail = "需要跳转系统输入法设置启用输入法，然后返回应用重新检测。",
                displayKind = AndroidDisplayKinds.ExplicitPrompt,
                autoActionKind = AndroidAutoActionKinds.OpenSettings,
                entryPointKind = AndroidEntryPointKinds.SettingsDeepLink,
            )
        }
        if (snapshot.imeSelectedState == ImeState.Missing) {
            findings += AndroidFinding(
                code = "ANDROID_IME_NOT_SELECTED",
                summary = "Android 输入法尚未切换为目标输入法",
                detail = "需要切换当前输入法，然后返回应用重新检测。",
                displayKind = AndroidDisplayKinds.ExplicitPrompt,
                autoActionKind = AndroidAutoActionKinds.OpenPicker,
                entryPointKind = AndroidEntryPointKinds.InputMethodPicker,
            )
        }
        if (artifactState.lastRecheckSummary?.startsWith("回检失败") == true) {
            findings += AndroidFinding(
                code = "ANDROID_RECHECK_FAILED",
                summary = "Android 回检未通过",
                detail = artifactState.lastRecheckSummary,
                displayKind = AndroidDisplayKinds.ExplicitError,
                autoActionKind = AndroidAutoActionKinds.DetectOnly,
                entryPointKind = AndroidEntryPointKinds.Retry,
            )
            findings += AndroidFinding(
                code = "STATE_MISMATCH",
                summary = "检测到正式状态冲突",
                detail = "请显式选择导入运行态或以当前配置覆盖。",
                displayKind = AndroidDisplayKinds.ExplicitError,
                autoActionKind = AndroidAutoActionKinds.None,
                entryPointKind = AndroidEntryPointKinds.None,
            )
        }

        return findings
    }

    private fun buildManualSteps(
        snapshot: AndroidPlatformSnapshot,
    ): List<AndroidManualStep> {
        val steps = mutableListOf<AndroidManualStep>()

        if (snapshot.syncRootPermission == PermissionState.Missing) {
            steps += AndroidManualStep(
                stepId = "grant_sync_root_permission",
                title = "授权同步快照根目录",
                nextAction = "完成目录授权后返回应用重新检测。",
                entryPointKind = AndroidEntryPointKinds.DirectoryAuthorization,
            )
        }
        if (snapshot.importRootPermission == PermissionState.Missing) {
            steps += AndroidManualStep(
                stepId = "grant_import_root_permission",
                title = "授权 Android 导入源目录",
                nextAction = "完成目录授权后返回应用重新检测。",
                entryPointKind = AndroidEntryPointKinds.DirectoryAuthorization,
            )
        }
        if (snapshot.imeEnabledState == ImeState.Missing) {
            steps += AndroidManualStep(
                stepId = "enable_android_ime",
                title = "启用 Android 输入法",
                nextAction = "从系统输入法设置返回后自动进入回检。",
                entryPointKind = AndroidEntryPointKinds.SettingsDeepLink,
            )
        }
        if (snapshot.imeSelectedState == ImeState.Missing) {
            steps += AndroidManualStep(
                stepId = "select_android_ime",
                title = "切换当前输入法",
                nextAction = "切换完成后返回应用自动重新检测。",
                entryPointKind = AndroidEntryPointKinds.InputMethodPicker,
            )
        }

        return steps
    }

    private fun buildPhaseStates(
        snapshot: AndroidPlatformSnapshot,
        findings: List<AndroidFinding>,
        manualSteps: List<AndroidManualStep>,
        artifactState: AndroidArtifactState,
    ): List<AndroidPhaseState> {
        val detectStatus = when {
            findings.any { it.code == "ANDROID_CARRIER_MISSING" || it.code == "ANDROID_RIME_PLUGIN_MISSING" } ->
                AndroidWorkflowStatus.Blocked
            manualSteps.isNotEmpty() -> AndroidWorkflowStatus.ManualActionRequired
            else -> AndroidWorkflowStatus.Completed
        }

        val configureStatus = when (detectStatus) {
            AndroidWorkflowStatus.Completed -> AndroidWorkflowStatus.Ready
            AndroidWorkflowStatus.Ready -> AndroidWorkflowStatus.Blocked
            AndroidWorkflowStatus.ManualActionRequired -> AndroidWorkflowStatus.Blocked
            AndroidWorkflowStatus.Blocked -> AndroidWorkflowStatus.Blocked
            AndroidWorkflowStatus.Failed -> AndroidWorkflowStatus.Blocked
        }

        val generateStatus = if (artifactState.latestSnapshotId != null) {
            AndroidWorkflowStatus.Completed
        } else if (configureStatus == AndroidWorkflowStatus.Ready || configureStatus == AndroidWorkflowStatus.Completed) {
            AndroidWorkflowStatus.Ready
        } else {
            AndroidWorkflowStatus.Blocked
        }

        val backupStatus = if (artifactState.latestBackupId != null) {
            AndroidWorkflowStatus.Completed
        } else if (artifactState.latestSnapshotId != null) {
            AndroidWorkflowStatus.Ready
        } else {
            AndroidWorkflowStatus.Blocked
        }

        val applyStatus = if (artifactState.latestAppliedSnapshotId != null) {
            AndroidWorkflowStatus.Completed
        } else if (artifactState.latestBackupId != null) {
            AndroidWorkflowStatus.Ready
        } else {
            AndroidWorkflowStatus.Blocked
        }

        val deployStatus = if (artifactState.latestAppliedSnapshotId == null) {
            AndroidWorkflowStatus.Blocked
        } else if (snapshot.deliveryConfirmation == ManualConfirmationState.Confirmed) {
            AndroidWorkflowStatus.Completed
        } else {
            AndroidWorkflowStatus.ManualActionRequired
        }

        val recheckStatus = when {
            artifactState.latestAppliedSnapshotId == null -> AndroidWorkflowStatus.Blocked
            artifactState.lastRecheckSummary?.startsWith("回检通过") == true -> AndroidWorkflowStatus.Completed
            artifactState.lastRecheckSummary?.startsWith("回检失败") == true -> AndroidWorkflowStatus.Failed
            snapshot.deliveryConfirmation == ManualConfirmationState.Confirmed &&
                snapshot.requiredSchemaApplied == ProbeState.Present &&
                snapshot.keyboardLayoutApplied == ProbeState.Present -> AndroidWorkflowStatus.Ready
            else -> AndroidWorkflowStatus.ManualActionRequired
        }

        val rollbackStatus = if (artifactState.latestBackupId != null) {
            AndroidWorkflowStatus.Ready
        } else {
            AndroidWorkflowStatus.Blocked
        }

        val diagnoseStatus = when {
            artifactState.lastRecheckSummary?.startsWith("回检通过") == true -> AndroidWorkflowStatus.Completed
            artifactState.lastRecheckSummary?.startsWith("回检失败") == true -> AndroidWorkflowStatus.Failed
            findings.isNotEmpty() -> AndroidWorkflowStatus.Blocked
            else -> AndroidWorkflowStatus.Ready
        }

        return listOf(
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Detect,
                status = detectStatus,
                summary = when (detectStatus) {
                    AndroidWorkflowStatus.Ready -> "尚未执行真实 Android 探测。"
                    AndroidWorkflowStatus.ManualActionRequired -> "探测已定位到需要用户完成的系统步骤。"
                    AndroidWorkflowStatus.Blocked -> "缺少承载器或插件，当前无法继续进入 Android 闭环。"
                    AndroidWorkflowStatus.Completed -> "Android 承载器、插件、授权和输入法前置条件已满足。"
                    AndroidWorkflowStatus.Failed -> "探测执行失败。"
                },
                nextAction = when (detectStatus) {
                    AndroidWorkflowStatus.Ready -> "执行检测，读取承载器、插件、授权与输入法状态。"
                    AndroidWorkflowStatus.ManualActionRequired -> "按下方手动步骤完成系统操作，返回后进入 recheck。"
                    AndroidWorkflowStatus.Blocked -> "先补齐 Android 承载器或 Rime 插件，再重新检测。"
                    AndroidWorkflowStatus.Completed -> "进入配置与生成阶段。"
                    AndroidWorkflowStatus.Failed -> "修复探测失败原因后重新检测。"
                },
            ),
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Configure,
                status = configureStatus,
                summary = "配置页负责编辑共享字段与 Android 专属字段；Windows 专属字段只读。",
                nextAction = if (configureStatus == AndroidWorkflowStatus.Ready) {
                    "在图形界面中保存完整配置模型。"
                } else {
                    "先完成 detect 阶段。"
                },
            ),
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Generate,
                status = generateStatus,
                summary = "生成 Android 目标包、应用清单和同步快照。",
                nextAction = if (generateStatus == AndroidWorkflowStatus.Ready) {
                    "执行 generate，把目标文件落到应用私有快照目录。"
                } else {
                    "先完成 detect / configure。"
                },
            ),
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Backup,
                status = backupStatus,
                summary = "备份当前 Android 导入源目录内容。",
                nextAction = if (backupStatus == AndroidWorkflowStatus.Ready) {
                    "执行 backup，为后续 apply / rollback 建立恢复点。"
                } else {
                    "先生成快照，再执行备份。"
                },
            ),
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Apply,
                status = applyStatus,
                summary = "把 Android 目标包写入导入源目录。",
                nextAction = if (applyStatus == AndroidWorkflowStatus.Ready) {
                    "执行 apply，把最新目标文件写入 Android 导入源目录。"
                } else {
                    "先完成 backup。"
                },
            ),
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Deploy,
                status = deployStatus,
                summary = "按 Android 应用清单引导用户完成承载器导入，并在应用内记录必要的手动确认。",
                nextAction = if (deployStatus == AndroidWorkflowStatus.ManualActionRequired) {
                    "根据应用清单在承载器内完成导入与必要确认。"
                } else if (deployStatus == AndroidWorkflowStatus.Completed) {
                    "已记录导入相关手动确认，可以进入 recheck。"
                } else {
                    "先完成 apply。"
                },
            ),
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Recheck,
                status = recheckStatus,
                summary = artifactState.lastRecheckSummary ?: "回检会核对导入源文件、应用清单中的默认方案与键盘布局，以及仍需用户确认的承载器导入完成状态。",
                nextAction = "在导入、部署或手动步骤返回后重新检测可探测状态，并仅把承载器导入完成保留为用户确认项。",
            ),
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Rollback,
                status = rollbackStatus,
                summary = "基于最近一次 Android 导入源备份恢复状态。",
                nextAction = "在 apply / deploy / recheck 失败后，基于最近备份恢复状态。",
            ),
            AndroidPhaseState(
                phase = AndroidWorkflowPhase.Diagnose,
                status = diagnoseStatus,
                summary = artifactState.lastRecheckSummary ?: "汇总当前闭环的阶段结果、错误码和下一步动作。",
                nextAction = "汇总错误码、阶段结果、下一步动作与回滚建议。",
            ),
        )
    }
}
