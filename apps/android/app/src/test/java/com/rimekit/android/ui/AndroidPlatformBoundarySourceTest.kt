package com.rimekit.android.ui

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class AndroidPlatformBoundarySourceTest {
    @Test
    fun uiLayer_shouldNotDirectlyLaunchPlatformIntents() {
        val source = File(resolveRepoRoot(), "apps/android/app/src/main/java/com/rimekit/android/ui/RimeKitApp.kt").readText()

        assertFalse("RimeKitApp 不应继续直接 startActivity。", source.contains("startActivity(", ignoreCase = false))
        assertFalse("RimeKitApp 不应继续直接声明 ACTION_DELETE。", source.contains("ACTION_DELETE", ignoreCase = false))
        assertFalse("RimeKitApp 不应继续直接声明 ACTION_APPLICATION_DETAILS_SETTINGS。", source.contains("ACTION_APPLICATION_DETAILS_SETTINGS", ignoreCase = false))
        assertFalse("RimeKitApp 不应继续直接声明 ACTION_INPUT_METHOD_SETTINGS。", source.contains("ACTION_INPUT_METHOD_SETTINGS", ignoreCase = false))
        assertFalse("RimeKitApp 不应继续直接获取 InputMethodManager。", source.contains("getSystemService(InputMethodManager::class.java)", ignoreCase = false))
        assertFalse("RimeKitApp 不应继续把资源来源失败归类为 EXPORT_FAILED。", source.contains("EXPORT_FAILED", ignoreCase = false))
    }

    @Test
    fun platformService_shouldOwnPlatformIntentLaunching() {
        val source = File(resolveRepoRoot(), "apps/android/app/src/main/java/com/rimekit/android/workflow/AndroidPlatformService.kt").readText()

        assertTrue("AndroidPlatformService 应提供 openInputMethodSettings。", source.contains("fun openInputMethodSettings()", ignoreCase = false))
        assertTrue("AndroidPlatformService 应提供 showInputMethodPicker。", source.contains("fun showInputMethodPicker()", ignoreCase = false))
        assertTrue("AndroidPlatformService 应提供 openPackageOrUrl。", source.contains("fun openPackageOrUrl(", ignoreCase = false))
        assertTrue("AndroidPlatformService 应提供 openPackageUninstallOrDetails。", source.contains("fun openPackageUninstallOrDetails(", ignoreCase = false))
        assertTrue("AndroidPlatformService 应提供 openExternalUrl。", source.contains("fun openExternalUrl(", ignoreCase = false))
        assertTrue("AndroidPlatformService 实现应持有 startActivity。", source.contains("context.startActivity(", ignoreCase = false))
    }

    @Test
    fun uiLayer_shouldRouteResourceLinksThroughViewModel() {
        val source = File(resolveRepoRoot(), "apps/android/app/src/main/java/com/rimekit/android/ui/RimeKitApp.kt").readText()

        assertTrue("RimeKitApp 应通过 ViewModel 打开资源来源。", source.contains("workflowViewModel.openResourceSource(source)", ignoreCase = false))
    }

    private fun resolveRepoRoot(): File {
        val userDir = requireNotNull(System.getProperty("user.dir")) { "缺少 user.dir" }
        var current: File? = File(userDir)
        while (current != null) {
            if (File(current, "shared/spec/resource_manifest.json").exists()) {
                return current
            }
            current = current.parentFile
        }
        error("无法定位仓库根目录。")
    }
}
