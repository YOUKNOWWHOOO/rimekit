. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G28 Phase B: 竖排换行 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.vertical_text","--value","true","--config",$cfg,"--format","json") -Label "G28 prereq on" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.vertical_text_with_wrap","--value","true","--config",$cfg,"--format","json") -Label "G28 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G28_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "竖排换行_G28_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G28 Phase B complete"
