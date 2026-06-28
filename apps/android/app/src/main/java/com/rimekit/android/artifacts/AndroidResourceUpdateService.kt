package com.rimekit.android.artifacts

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.time.Instant

class AndroidResourceUpdateService(
    private val context: Context,
    private val manifestRepository: AndroidResourceManifestRepository,
    private val manifestLoader: (() -> AndroidResourceManifest)? = null,
) {
    private val packageManager = context.packageManager

    fun loadLastReport(): String? {
        val file = context.filesDir.resolve("state").resolve("last_resource_update_report.json")
        return file.takeIf { it.exists() }?.readText()
    }

    fun checkForUpdates(): String {
        val manifest = manifestLoader?.invoke() ?: manifestRepository.load()
        val items = JSONArray()
        (manifest.schemas + manifest.dictionaries + manifest.models).forEach { item ->
            items.put(checkRemote(item))
        }
        items.put(checkPlatformPackage("android_fcitx5_carrier", "Fcitx5 for Android", FCITX_PACKAGE_NAME, FCITX_INSTALL_URL))
        items.put(checkPlatformPackage("android_rime_plugin", "Rime 插件", RIME_PLUGIN_PACKAGE_NAME, RIME_PLUGIN_INSTALL_URL))

        val payload = JSONObject()
            .put("checked_at", Instant.now().toString())
            .put("items", items)
            .toString(2)

        val stateDir = context.filesDir.resolve("state").apply { mkdirs() }
        stateDir.resolve("last_resource_update_report.json").writeText(payload)
        return payload
    }

    private fun checkRemote(item: AndroidResourceItem): JSONObject {
        if (!item.source.startsWith("http://") && !item.source.startsWith("https://")) {
            return JSONObject()
                .put("id", item.id)
                .put("display_name", item.displayName)
                .put("source", item.source)
                .put("source_class", item.sourceClass)
                .put("reachable", false)
                .put("status_code", 0)
                .put("last_modified", "n/a")
                .put("content_length", -1)
                .put("note", "当前来源不是可直接做 HTTP 检查的远端地址。")
        }

        return runCatching {
            val connection = (URL(item.source).openConnection() as HttpURLConnection).apply {
                requestMethod = "HEAD"
                connectTimeout = 10_000
                readTimeout = 10_000
                instanceFollowRedirects = true
            }
            try {
                JSONObject()
                    .put("id", item.id)
                    .put("display_name", item.displayName)
                    .put("source", item.source)
                    .put("source_class", item.sourceClass)
                    .put("reachable", connection.responseCode in 200..299)
                    .put("status_code", connection.responseCode)
                    .put("last_modified", connection.getHeaderField("Last-Modified") ?: "n/a")
                    .put("content_length", connection.getHeaderFieldLong("Content-Length", -1))
                    .put("note", if (connection.responseCode in 200..299) "HEAD 检查完成。" else "远端返回非成功状态。")
            } finally {
                connection.disconnect()
            }
        }.getOrElse { error ->
            JSONObject()
                .put("id", item.id)
                .put("display_name", item.displayName)
                .put("source", item.source)
                .put("source_class", item.sourceClass)
                .put("reachable", false)
                .put("status_code", 0)
                .put("last_modified", "n/a")
                .put("content_length", -1)
                .put("note", "检查失败：${error.message}")
        }
    }

    private fun checkPlatformPackage(
        id: String,
        displayName: String,
        packageName: String,
        source: String,
    ): JSONObject {
        val currentVersion = runCatching {
            val packageInfo = packageManager.getPackageInfo(packageName, 0)
            packageInfo.versionName?.takeIf(String::isNotBlank)
                ?: packageInfo.longVersionCode.toString()
        }.getOrNull() ?: "not_installed"

        val remote = checkRemote(
            AndroidResourceItem(
                id = id,
                displayName = displayName,
                sourceClass = "product_fixed_decision",
                source = source,
            ),
        )
        remote.put("current_version", currentVersion)
        remote.put("package_name", packageName)
        return remote
    }

    private companion object {
        private const val FCITX_PACKAGE_NAME = "org.fcitx.fcitx5.android"
        private const val RIME_PLUGIN_PACKAGE_NAME = "org.fcitx.fcitx5.android.plugin.rime"
        private const val FCITX_INSTALL_URL = "https://f-droid.org/packages/org.fcitx.fcitx5.android/"
        private const val RIME_PLUGIN_INSTALL_URL = "https://f-droid.org/packages/org.fcitx.fcitx5.android.plugin.rime/"
    }
}
