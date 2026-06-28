. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G70 Phase B: 边框颜色 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_border_color","--value","0xFF00FF00","--config",$cfg,"--format","json") -Label "G70 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G70_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "边框颜色_G70_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G70 Phase B complete"
