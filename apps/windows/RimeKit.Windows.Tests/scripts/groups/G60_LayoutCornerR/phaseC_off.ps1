. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G60 Phase C: 圆角半径 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_corner_radius","--value","16","--config",$cfg,"--format","json") -Label "G60 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G60_C_ON" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "圆角半径_G60_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_corner_radius","--value","0","--config",$cfg,"--format","json") -Label "G60 off" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G60_C_OFF" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "圆角半径_G60_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G60 Phase C complete"
