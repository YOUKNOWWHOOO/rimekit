package com.rimekit.android.workflow

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.view.inputmethod.InputMethodManager
import androidx.documentfile.provider.DocumentFile
import org.json.JSONObject
import java.io.File

/**
 * Android 平台服务接口。
 */
interface AndroidPlatformService {
    fun readSnapshot(): AndroidPlatformSnapshot

    fun persistGrantedUri(
        kind: AndroidPermissionKind,
        uri: Uri,
    )

    fun clearGrantedUri(kind: AndroidPermissionKind)

    fun setRuntimeConfirmation(
        kind: AndroidRuntimeConfirmationKind,
        confirmed: Boolean,
    )

    fun markDeliveryConfirmationPending()

    fun onApplicationResumed()

    fun openInputMethodSettings(): Result<Unit>

    fun showInputMethodPicker(): Result<Unit>

    fun openPackageOrUrl(
        packageName: String,
        url: String,
    ): Result<Unit>

    fun openPackageUninstallOrDetails(packageName: String): Result<Unit>

    fun openExternalUrl(url: String): Result<Unit>
}

/**
 * Android 目录授权类型。
 */
enum class AndroidPermissionKind {
    SyncRoot,
    ImportRoot,
}

enum class AndroidRuntimeConfirmationKind {
    DeliveryCompleted,
    RequiredSchemaSelected,
    KeyboardLayoutApplied,
}

/**
 * Android 平台探测与授权状态持久化服务。
 */
class AndroidPlatformServiceImpl(
    private val context: Context,
) : AndroidPlatformService {
    private val prefs = context.getSharedPreferences("android_platform_state", Context.MODE_PRIVATE)

    override fun readSnapshot(): AndroidPlatformSnapshot {
        val syncUri = prefs.getString(KEY_SYNC_ROOT_URI, null)
        val importUri = prefs.getString(KEY_IMPORT_ROOT_URI, null)
        val importManifest = readImportedApplyManifest(importUri)

        return AndroidPlatformSnapshot(
            carrierState = if (isPackageInstalled(FCITX_PACKAGE_NAME)) ProbeState.Present else ProbeState.Missing,
            rimePluginState = if (isPackageInstalled(RIME_PLUGIN_PACKAGE_NAME)) {
                ProbeState.Present
            } else {
                ProbeState.Missing
            },
            carrierVersion = readPackageVersion(FCITX_PACKAGE_NAME),
            rimePluginVersion = readPackageVersion(RIME_PLUGIN_PACKAGE_NAME),
            syncRootPermission = resolvePermissionState(syncUri),
            importRootPermission = resolvePermissionState(importUri),
            imeEnabledState = if (isImeEnabled()) ImeState.Enabled else ImeState.Missing,
            imeSelectedState = if (isImeSelected()) ImeState.Enabled else ImeState.Missing,
            requiredSchemaApplied = when (importManifest?.optString("required_schema_id")) {
                "t9" -> ProbeState.Present
                null, "" -> ProbeState.Missing
                else -> ProbeState.Missing
            },
            keyboardLayoutApplied = when (importManifest?.optString("keyboard_layout")) {
                "9_key" -> ProbeState.Present
                null, "" -> ProbeState.Missing
                else -> ProbeState.Missing
            },
            deliveryConfirmation = resolveRuntimeConfirmation(KEY_DELIVERY_COMPLETED),
            syncRootUri = syncUri,
            importRootUri = importUri,
            carrierUpdateSource = "https://f-droid.org/packages/org.fcitx.fcitx5.android/",
            rimePluginUpdateSource = "https://f-droid.org/packages/org.fcitx.fcitx5.android.plugin.rime/",
        )
    }

    override fun persistGrantedUri(
        kind: AndroidPermissionKind,
        uri: Uri,
    ) {
        val flags = IntentGrantFlags
        context.contentResolver.takePersistableUriPermission(uri, flags)
        prefs.edit()
            .putString(kind.prefKey, uri.toString())
            .apply()
    }

    override fun clearGrantedUri(kind: AndroidPermissionKind) {
        val current = prefs.getString(kind.prefKey, null)?.let(Uri::parse)
        if (current != null) {
            runCatching {
                context.contentResolver.releasePersistableUriPermission(current, IntentGrantFlags)
            }
        }

        prefs.edit()
            .remove(kind.prefKey)
            .apply()
    }

    override fun setRuntimeConfirmation(
        kind: AndroidRuntimeConfirmationKind,
        confirmed: Boolean,
    ) {
        prefs.edit()
            .putBoolean(kind.prefKey, confirmed)
            .apply()
    }

    override fun markDeliveryConfirmationPending() {
        prefs.edit()
            .putBoolean(KEY_DELIVERY_PENDING_RETURN, true)
            .putBoolean(KEY_DELIVERY_COMPLETED, false)
            .apply()
    }

    override fun onApplicationResumed() {
        if (!prefs.getBoolean(KEY_DELIVERY_PENDING_RETURN, false)) {
            return
        }

        prefs.edit()
            .remove(KEY_DELIVERY_PENDING_RETURN)
            .apply()
    }

    override fun openInputMethodSettings(): Result<Unit> {
        return runCatching {
            context.startActivity(
                Intent(Settings.ACTION_INPUT_METHOD_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            )
        }
    }

    override fun showInputMethodPicker(): Result<Unit> {
        val inputMethodManager = context.getSystemService(InputMethodManager::class.java)
            ?: return Result.failure(IllegalStateException("当前设备无法获取输入法选择器服务。"))
        return runCatching {
            inputMethodManager.showInputMethodPicker()
        }
    }

    override fun openPackageOrUrl(
        packageName: String,
        url: String,
    ): Result<Unit> {
        val packageManager = context.packageManager
        val launchIntent = packageManager.getLaunchIntentForPackage(packageName)
        val intent = when {
            launchIntent != null -> launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            isPackageInstalled(packageName) -> Intent(
                Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                Uri.parse("package:$packageName"),
            ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            else -> Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }

        return runCatching {
            context.startActivity(intent)
        }
    }

    override fun openPackageUninstallOrDetails(packageName: String): Result<Unit> {
        val uninstallIntent = Intent(Intent.ACTION_DELETE, Uri.parse("package:$packageName"))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        val detailsIntent = Intent(
            Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
            Uri.parse("package:$packageName"),
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

        return runCatching {
            try {
                context.startActivity(uninstallIntent)
            } catch (_: ActivityNotFoundException) {
                context.startActivity(detailsIntent)
            }
        }
    }

    override fun openExternalUrl(url: String): Result<Unit> {
        return runCatching {
            context.startActivity(
                Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            )
        }
    }

    private fun isPackageInstalled(packageName: String): Boolean {
        return runCatching {
            context.packageManager.getPackageInfo(packageName, 0)
        }.isSuccess
    }

    private fun readPackageVersion(packageName: String): String? {
        return runCatching {
            val packageInfo = context.packageManager.getPackageInfo(packageName, 0)
            packageInfo.versionName?.takeIf(String::isNotBlank)
                ?: packageInfo.longVersionCode.toString()
        }.getOrNull()
    }

    private fun resolvePermissionState(uriString: String?): PermissionState {
        if (uriString.isNullOrBlank()) {
            return PermissionState.Missing
        }

        localRootOrNull(uriString)?.let { localRoot ->
            return if (localRoot.exists() && localRoot.isDirectory) {
                PermissionState.Granted
            } else {
                PermissionState.Missing
            }
        }

        val persisted = context.contentResolver.persistedUriPermissions.any { permission ->
            permission.uri.toString() == uriString && permission.isReadPermission
        }
        return if (persisted) PermissionState.Granted else PermissionState.Missing
    }

    private fun resolveRuntimeConfirmation(prefKey: String): ManualConfirmationState {
        return if (prefs.contains(prefKey) && prefs.getBoolean(prefKey, false)) {
            ManualConfirmationState.Confirmed
        } else {
            ManualConfirmationState.Missing
        }
    }

    private fun readImportedApplyManifest(importRootUri: String?): JSONObject? {
        if (importRootUri.isNullOrBlank()) {
            return null
        }

        localRootOrNull(importRootUri)?.let { localRoot ->
            val manifestFile = File(localRoot, "android_apply_manifest.json")
            if (manifestFile.exists()) {
                return JSONObject(manifestFile.readText())
            }
            return null
        }

        val root = requireRoot(importRootUri)
        val manifestFile = root.findFile("android_apply_manifest.json") ?: return null
        return JSONObject(manifestFile.uri.readText())
    }

    private fun requireRoot(rootUri: String): DocumentFile {
        val uri = Uri.parse(rootUri)
        localRootOrNull(rootUri)?.let { localRoot ->
            localRoot.mkdirs()
            return DocumentFile.fromFile(localRoot)
        }
        return requireNotNull(DocumentFile.fromTreeUri(context, uri)) {
            "无法解析导入源目录：$rootUri"
        }
    }

    private fun localRootOrNull(rootUri: String): File? {
        if (rootUri.matches(Regex("^[A-Za-z]:\\\\.*"))) {
            return File(rootUri)
        }
        val uri = Uri.parse(rootUri)
        if (uri.scheme == null || uri.scheme.equals("file", ignoreCase = true)) {
            return File(requireNotNull(uri.path) { "非法 file URI：$rootUri" })
        }
        return null
    }

    private fun Uri.readText(): String {
        localRootOrNull(toString())?.let { localFile ->
            return localFile.readText()
        }
        context.contentResolver.openInputStream(this).use { input ->
            requireNotNull(input) { "无法读取导入源文件：$this" }
            return input.bufferedReader().use { reader -> reader.readText() }
        }
    }

    private fun isImeEnabled(): Boolean {
        val manager = context.getSystemService(InputMethodManager::class.java) ?: return false
        return manager.enabledInputMethodList.any { info ->
            info.packageName == FCITX_PACKAGE_NAME
        }
    }

    private fun isImeSelected(): Boolean {
        val currentId = Settings.Secure.getString(
            context.contentResolver,
            Settings.Secure.DEFAULT_INPUT_METHOD,
        ) ?: return false
        return currentId.substringBefore('/') == FCITX_PACKAGE_NAME
    }

    private val AndroidPermissionKind.prefKey: String
        get() = when (this) {
            AndroidPermissionKind.SyncRoot -> KEY_SYNC_ROOT_URI
            AndroidPermissionKind.ImportRoot -> KEY_IMPORT_ROOT_URI
        }

    private val AndroidRuntimeConfirmationKind.prefKey: String
        get() = when (this) {
            AndroidRuntimeConfirmationKind.DeliveryCompleted -> KEY_DELIVERY_COMPLETED
            AndroidRuntimeConfirmationKind.RequiredSchemaSelected -> KEY_REQUIRED_SCHEMA_SELECTED
            AndroidRuntimeConfirmationKind.KeyboardLayoutApplied -> KEY_KEYBOARD_LAYOUT_APPLIED
        }

    private companion object {
        private const val FCITX_PACKAGE_NAME = "org.fcitx.fcitx5.android"
        private const val RIME_PLUGIN_PACKAGE_NAME = "org.fcitx.fcitx5.android.plugin.rime"
        private const val KEY_SYNC_ROOT_URI = "sync_root_uri"
        private const val KEY_IMPORT_ROOT_URI = "import_root_uri"
        private const val KEY_DELIVERY_COMPLETED = "delivery_completed"
        private const val KEY_DELIVERY_PENDING_RETURN = "delivery_pending_return"
        private const val KEY_REQUIRED_SCHEMA_SELECTED = "required_schema_selected"
        private const val KEY_KEYBOARD_LAYOUT_APPLIED = "keyboard_layout_applied"
        private const val IntentGrantFlags =
            Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
    }
}
