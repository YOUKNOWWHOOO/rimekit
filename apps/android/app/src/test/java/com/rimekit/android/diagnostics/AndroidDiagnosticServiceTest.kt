package com.rimekit.android.diagnostics

import androidx.test.core.app.ApplicationProvider
import com.rimekit.android.artifacts.AndroidArtifactState
import com.rimekit.android.workflow.AndroidWorkflowPhase
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class AndroidDiagnosticServiceTest {
    @Test
    fun buildFailureReport_shouldReuseTaskManifestMetadataForPlatformActionFailures() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidDiagnosticService(
            context = context,
            errorCodesLoader = {
                JSONObject()
                    .put(
                        "codes",
                        JSONArray().put(
                            JSONObject()
                                .put("code", "ANDROID_CARRIER_INSTALL_REQUEST_FAILED")
                                .put("default_summary", "Android 承载器安装请求发起失败")
                                .put("recommended_next_action", "检查安装入口后重试")
                                .put("severity", "fatal")
                                .put("display_kind", "explicit_error")
                                .put("auto_action_kind", "install_request")
                                .put("entry_point_kind", "install_url"),
                        ),
                    )
                    .toString()
            },
            taskManifestLoader = {
                JSONObject()
                    .put(
                        "tasks",
                        JSONArray().put(
                            JSONObject()
                                .put("task_id", "android_request_carrier_install")
                                .put("message_kind", "explicit_prompt")
                                .put("auto_action_kind", "install_request")
                                .put("entry_points", JSONArray().put("install_url").put("retry")),
                        ),
                    )
                    .toString()
            },
        )

        val report = service.buildFailureReport(
            phase = AndroidWorkflowPhase.Detect,
            taskId = "android_request_carrier_install",
            code = "ANDROID_CARRIER_INSTALL_REQUEST_FAILED",
            detail = "发起 Android 承载器安装入口失败：测试用例。",
            artifactState = AndroidArtifactState(),
        )

        assertEquals("explicit_prompt", report.displayKind)
        assertTrue(report.entryPoints.any { it.kind == "install_url" })
        assertEquals("android_request_carrier_install", report.findings.first().relatedTaskId)
        assertEquals("install_request", report.findings.first().autoActionKind)
        assertEquals("explicit_prompt", report.findings.first().displayKind)
    }

    @Test
    fun buildFailureReport_shouldSupportUninstallEntryMetadata() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val service = AndroidDiagnosticService(
            context = context,
            errorCodesLoader = {
                JSONObject()
                    .put(
                        "codes",
                        JSONArray().put(
                            JSONObject()
                                .put("code", "ANDROID_CARRIER_UNINSTALL_REQUEST_FAILED")
                                .put("default_summary", "Android 承载器卸载入口发起失败")
                                .put("recommended_next_action", "打开系统卸载入口后重试")
                                .put("severity", "warning")
                                .put("display_kind", "explicit_warning")
                                .put("auto_action_kind", "reinstall_request")
                                .put("entry_point_kind", "uninstall_launch"),
                        ),
                    )
                    .toString()
            },
            taskManifestLoader = {
                JSONObject()
                    .put(
                        "tasks",
                        JSONArray().put(
                            JSONObject()
                                .put("task_id", "android_request_carrier_uninstall")
                                .put("message_kind", "explicit_prompt")
                                .put("auto_action_kind", "reinstall_request")
                                .put("entry_points", JSONArray().put("uninstall_launch").put("retry")),
                        ),
                    )
                    .toString()
            },
        )

        val report = service.buildFailureReport(
            phase = AndroidWorkflowPhase.Detect,
            taskId = "android_request_carrier_uninstall",
            code = "ANDROID_CARRIER_UNINSTALL_REQUEST_FAILED",
            detail = "发起 Android 承载器卸载入口失败：测试用例。",
            artifactState = AndroidArtifactState(),
        )

        assertEquals("explicit_prompt", report.displayKind)
        assertTrue(report.entryPoints.any { it.kind == "uninstall_launch" })
        assertEquals("reinstall_request", report.findings.first().autoActionKind)
        assertEquals("explicit_prompt", report.findings.first().displayKind)
        assertEquals("uninstall_launch", report.findings.first().entryPointKind)
    }
}
