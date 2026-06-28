. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G49 Phase B: 行距 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_linespacing","--value","500","--config",$cfg,"--format","json") -Label "G49 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G49_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "行距_G49_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G49 Phase B complete"
