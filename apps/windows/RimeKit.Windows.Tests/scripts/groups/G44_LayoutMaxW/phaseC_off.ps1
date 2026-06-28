. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G44 Phase C: 最大宽度 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_max_width","--value","400","--config",$cfg,"--format","json") -Label "G44 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G44_C_ON" "a" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "最大宽度_G44_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_max_width","--value","0","--config",$cfg,"--format","json") -Label "G44 off" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G44_C_OFF" "a" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "最大宽度_G44_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G44 Phase C complete"
