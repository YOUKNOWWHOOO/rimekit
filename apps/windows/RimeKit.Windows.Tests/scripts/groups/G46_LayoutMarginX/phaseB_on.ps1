. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G46 Phase B: 水平边距 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_margin_x","--value","30","--config",$cfg,"--format","json") -Label "G46 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G46_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "水平边距_G46_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G46 Phase B complete"
