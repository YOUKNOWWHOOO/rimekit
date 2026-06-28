. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G72 Phase C: 高亮文字颜色 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_hilited_text_color","--value","0xFF0000FF","--config",$cfg,"--format","json") -Label "G72 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G72_C_ON" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "高亮文字颜色_G72_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_hilited_text_color","--value","","--config",$cfg,"--format","json") -Label "G72 off" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G72_C_OFF" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "高亮文字颜色_G72_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G72 Phase C complete"
