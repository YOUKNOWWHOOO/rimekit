. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G26 Phase B: 竖排 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.vertical_text","--value","true","--config",$cfg,"--format","json") -Label "G26 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G26_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "竖排_G26_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G26 Phase B complete"
