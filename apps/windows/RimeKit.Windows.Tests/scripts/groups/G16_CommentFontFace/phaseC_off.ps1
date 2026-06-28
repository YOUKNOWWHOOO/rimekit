. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G16 Phase C: 注释字体 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.comment_font_face","--value","SimSun","--config",$cfg,"--format","json") -Label "G16 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G16_C_ON" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "注释字体_G16_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.comment_font_face","--value","","--config",$cfg,"--format","json") -Label "G16 off" | Out-Null
Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G16_C_OFF" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "注释字体_G16_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G16 Phase C complete"
