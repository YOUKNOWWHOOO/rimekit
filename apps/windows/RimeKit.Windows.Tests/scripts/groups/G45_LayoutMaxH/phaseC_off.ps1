. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G45 Phase C: 最大高度 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_max_height","--value","200","--config",$cfg,"--format","json") -Label "G45 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G45_C_ON" "a" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "最大高度_G45_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_max_height","--value","0","--config",$cfg,"--format","json") -Label "G45 off" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G45_C_OFF" "a" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "最大高度_G45_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G45 Phase C complete"
