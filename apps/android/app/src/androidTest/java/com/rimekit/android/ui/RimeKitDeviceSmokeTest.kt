package com.rimekit.android.ui

import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rimekit.android.MainActivity
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class RimeKitDeviceSmokeTest {
    @get:Rule
    val composeRule = createAndroidComposeRule<MainActivity>()

    @Test
    fun detectSection_shouldExposeFormalEnvironmentActions() {
        clickLastNodeWithText("检测")

        assertNodeExists("环境检测")
        assertAnyNodeExists("安装 Fcitx5", "打开 Fcitx5")
        assertAnyNodeExists("安装 Rime 插件", "打开 Rime 插件")
        assertNodeExists("打开输入法设置")
        assertNodeExists("切换当前输入法")
    }

    @Test
    fun deploySection_shouldExposeApplyDeployRecheckRollbackActions() {
        clickLastNodeWithText("部署")

        assertNodeExists("导入与部署闭环")
        assertNodeExists("生成")
        assertNodeExists("备份")
        assertNodeExists("应用")
        assertNodeExists("部署")
        assertNodeExists("回检")
        assertNodeExists("回滚")
    }

    @Test
    fun backupSection_shouldExposeBackupAndRollbackEntry() {
        clickLastNodeWithText("备份")

        assertNodeExists("备份与回滚")
        assertNodeExists("按最近备份回滚")
    }

    @Test
    fun diagnoseSection_shouldExposeConflictRecoveryAndAdvancedDiagnostic() {
        clickLastNodeWithText("诊断")

        assertNodeExists("诊断")
        assertNodeExists("高级诊断摘要")
        assertNodeExists("导入运行态")
        assertNodeExists("以当前配置覆盖")
    }

    @Test
    fun syncSection_shouldExposeSyncAndExportActions() {
        clickLastNodeWithText("同步")

        assertNodeExists("导出配置")
        assertNodeExists("导出诊断")
        assertNodeExists("导出快照")
    }

    @Test
    fun resourcesSection_shouldExposeResourceUpdateAndExportActions() {
        clickLastNodeWithText("词库模型")

        assertNodeExists("导出正式资源清单")
        assertNodeExists("导出资源更新检查结果")
    }

    private fun clickLastNodeWithText(text: String) {
        val nodes = composeRule.onAllNodesWithText(text)
        val lastIndex = nodes.fetchSemanticsNodes().lastIndex
        nodes[lastIndex].performClick()
    }

    private fun assertNodeExists(text: String) {
        assertTrue(
            "未找到节点：$text",
            composeRule.onAllNodesWithText(text).fetchSemanticsNodes().isNotEmpty(),
        )
    }

    private fun assertAnyNodeExists(vararg texts: String) {
        val matched = texts.any { text ->
            composeRule.onAllNodesWithText(text).fetchSemanticsNodes().isNotEmpty()
        }
        assertTrue("未找到任一节点：${texts.joinToString(" / ")}", matched)
    }
}
