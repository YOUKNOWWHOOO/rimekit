package com.rimekit.android.ui

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import com.rimekit.android.artifacts.AndroidArtifactState
import com.rimekit.android.workflow.AndroidPlatformSnapshot
import com.rimekit.android.workflow.AndroidWorkflowPhase
import com.rimekit.android.workflow.AndroidWorkflowStatus
import com.rimekit.android.workflow.ManualConfirmationState
import com.rimekit.android.workflow.ProbeState
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class RimeKitUiRenderingTest {
    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun detectSection_shouldRenderTaskDrivenActionTitles() {
        composeRule.setContent {
            DetectSection(
                snapshot = AndroidPlatformSnapshot(
                    carrierState = ProbeState.Missing,
                    rimePluginState = ProbeState.Missing,
                ),
                findings = emptyList(),
                taskDefinitions = mapOf(
                    "android_request_carrier_install" to AndroidTaskUiDefinition(
                        taskId = "android_request_carrier_install",
                        title = "发起 Android 承载器安装请求",
                        entryPoints = listOf("install_url", "retry"),
                    ),
                    "android_request_rime_plugin_install" to AndroidTaskUiDefinition(
                        taskId = "android_request_rime_plugin_install",
                        title = "发起 Android Rime 插件安装请求",
                        entryPoints = listOf("install_url", "retry"),
                    ),
                    "android_open_ime_settings" to AndroidTaskUiDefinition(
                        taskId = "android_open_ime_settings",
                        title = "打开 Android 输入法设置",
                        entryPoints = listOf("settings_deep_link", "retry"),
                    ),
                    "android_open_ime_picker" to AndroidTaskUiDefinition(
                        taskId = "android_open_ime_picker",
                        title = "打开 Android 输入法选择器",
                        entryPoints = listOf("input_method_picker", "retry"),
                    ),
                    "android_request_carrier_uninstall" to AndroidTaskUiDefinition(
                        taskId = "android_request_carrier_uninstall",
                        title = "发起 Android 承载器卸载入口",
                        entryPoints = listOf("uninstall_launch", "retry"),
                    ),
                    "android_request_rime_plugin_uninstall" to AndroidTaskUiDefinition(
                        taskId = "android_request_rime_plugin_uninstall",
                        title = "发起 Android Rime 插件卸载入口",
                        entryPoints = listOf("uninstall_launch", "retry"),
                    ),
                ),
                onOpenInputMethodSettings = {},
                onShowInputMethodPicker = {},
                onOpenFcitxInstallOrApp = {},
                onOpenRimePluginInstallOrApp = {},
                onOpenFcitxUninstallOrSettings = {},
                onOpenRimePluginUninstallOrSettings = {},
            )
        }

        composeRule.onAllNodesWithText("发起 Android 承载器安装请求").assertCountEquals(1)
        composeRule.onAllNodesWithText("发起 Android Rime 插件安装请求").assertCountEquals(1)
        composeRule.onAllNodesWithText("打开 Android 输入法设置").assertCountEquals(1)
        composeRule.onAllNodesWithText("打开 Android 输入法选择器").assertCountEquals(1)
        composeRule.onAllNodesWithText("发起 Android 承载器卸载入口").assertCountEquals(1)
        composeRule.onAllNodesWithText("发起 Android Rime 插件卸载入口").assertCountEquals(1)
    }

    @Test
    fun deploySection_shouldRenderTaskDrivenActionTitles() {
        composeRule.setContent {
            DeploySection(
                phaseStates = listOf(
                    com.rimekit.android.workflow.AndroidPhaseState(
                        phase = AndroidWorkflowPhase.Generate,
                        status = AndroidWorkflowStatus.Ready,
                        summary = "生成",
                        nextAction = "执行生成",
                    ),
                ),
                artifactState = AndroidArtifactState(),
                taskDefinitions = mapOf(
                    "android_generate_targets" to AndroidTaskUiDefinition(
                        taskId = "android_generate_targets",
                        title = "生成 Android 目标包、应用清单与同步清单",
                        entryPoints = listOf("retry"),
                    ),
                    "android_backup_import_root" to AndroidTaskUiDefinition(
                        taskId = "android_backup_import_root",
                        title = "备份 Android 导入源目录与资源状态",
                        entryPoints = listOf("retry"),
                    ),
                    "android_apply_import_bundle" to AndroidTaskUiDefinition(
                        taskId = "android_apply_import_bundle",
                        title = "刷新 Android 导入源目录与应用清单",
                        entryPoints = listOf("retry", "rollback"),
                    ),
                    "android_deploy_carrier" to AndroidTaskUiDefinition(
                        taskId = "android_deploy_carrier",
                        title = "触发 Android 导入、设置引导与必要确认",
                        entryPoints = listOf("deploy_confirmation", "retry"),
                    ),
                    "android_recheck_runtime" to AndroidTaskUiDefinition(
                        taskId = "android_recheck_runtime",
                        title = "返回后回检运行态",
                        entryPoints = listOf("retry", "rollback"),
                    ),
                    "android_rollback_import_root" to AndroidTaskUiDefinition(
                        taskId = "android_rollback_import_root",
                        title = "按备份恢复 Android 导入源并重新进入导入闭环",
                        entryPoints = listOf("rollback"),
                    ),
                ),
                isBusy = false,
                deployInstructions = null,
                snapshot = AndroidPlatformSnapshot(
                    deliveryConfirmation = ManualConfirmationState.Missing,
                ),
                onGenerate = {},
                onBackup = {},
                onApply = {},
                onDeploy = {},
                onRecheck = {},
                onRollback = {},
                onSetDeliveryConfirmed = {},
                onSetSchemaConfirmed = {},
                onSetKeyboardConfirmed = {},
            )
        }

        composeRule.onNodeWithText("生成 Android 目标包、应用清单与同步清单").assertIsDisplayed()
        composeRule.onNodeWithText("刷新 Android 导入源目录与应用清单").assertIsDisplayed()
        composeRule.onNodeWithText("触发 Android 导入、设置引导与必要确认").assertIsDisplayed()
        composeRule.onNodeWithText("按备份恢复 Android 导入源并重新进入导入闭环").assertIsDisplayed()
        composeRule.onAllNodesWithText("当前状态: 待确认").assertCountEquals(1)
        composeRule.onAllNodesWithText("已确认").assertCountEquals(1)
        composeRule.onAllNodesWithText("重置").assertCountEquals(1)
    }
}
