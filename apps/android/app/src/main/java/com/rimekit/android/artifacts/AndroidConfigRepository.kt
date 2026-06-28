package com.rimekit.android.artifacts

import android.content.Context
import org.json.JSONObject

/**
 * Android 最小配置模型的本地持久化。
 */
class AndroidConfigRepository(
    context: Context,
) {
    private val prefs = context.getSharedPreferences("android_config_model", Context.MODE_PRIVATE)

    fun loadRawJson(): String {
        val raw = prefs.getString(KEY_CONFIG_JSON, null)
        if (!raw.isNullOrBlank()) {
            return JSONObject(raw).toString(2)
        }

        return AndroidConfigModel.createDefault().toFormalJson().toString(2)
    }

    fun load(): AndroidConfigModel {
        val raw = prefs.getString(KEY_CONFIG_JSON, null) ?: return AndroidConfigModel.createDefault()
        return AndroidConfigModel.fromStoredJson(JSONObject(raw))
    }

    fun save(model: AndroidConfigModel) {
        saveRawJson(model.toFormalJson().toString(2))
    }

    fun saveRawJson(rawJson: String) {
        prefs.edit()
            .putString(KEY_CONFIG_JSON, JSONObject(rawJson).toString(2))
            .apply()
    }

    private companion object {
        private const val KEY_CONFIG_JSON = "config_json"
    }
}
