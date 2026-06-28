package com.rimekit.android.artifacts

import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class AndroidConfigRepositoryTest {
    @Test
    fun saveAndLoad_shouldPreserveExtendedFields() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val repository = AndroidConfigRepository(context)
        val model = AndroidConfigModel.createDefault().copy(
            candidatePageSize = 9,
            sharedSyncRoot = "content://sync-root",
            windowsTargetRoot = "C:/Users/demo/AppData/Rime",
            windowsFontFace = "Microsoft YaHei",
            windowsShowNotification = true,
        )

        repository.save(model)
        val restored = repository.load()

        assertEquals(9, restored.candidatePageSize)
        assertTrue(restored.sharedSyncRoot.isEmpty())
        assertEquals("C:/Users/demo/AppData/Rime", restored.windowsTargetRoot)
        assertEquals("Microsoft YaHei", restored.windowsFontFace)
        assertTrue(restored.windowsShowNotification)
    }
}
