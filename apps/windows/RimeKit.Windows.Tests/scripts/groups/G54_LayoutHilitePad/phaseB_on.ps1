. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G54 Phase B: 高亮填充 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_hilite_padding","--value","16","--config",$cfg,"--format","json") -Label "G54 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G54_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "高亮填充_G54_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G54 Phase B complete"
