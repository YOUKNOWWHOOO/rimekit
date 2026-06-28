. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G14 Phase C: 标签字体 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.label_font_face","--value","SimSun","--config",$cfg,"--format","json") -Label "G14 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G14_C_ON" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "标签字体_G14_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.label_font_face","--value","","--config",$cfg,"--format","json") -Label "G14 off" | Out-Null
Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G14_C_OFF" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "标签字体_G14_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G14 Phase C complete"
