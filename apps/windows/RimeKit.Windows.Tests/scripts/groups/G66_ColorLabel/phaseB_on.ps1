. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G66 Phase B: 标签颜色 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_label_color","--value","0xFF00FF00","--config",$cfg,"--format","json") -Label "G66 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G66_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "标签颜色_G66_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G66 Phase B complete"
