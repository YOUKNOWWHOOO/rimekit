. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G53 Phase B: 高亮间距 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_hilite_spacing","--value","20","--config",$cfg,"--format","json") -Label "G53 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G53_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "高亮间距_G53_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G53 Phase B complete"
