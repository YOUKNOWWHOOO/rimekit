. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G40 Phase B: 预编辑类型 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.inline_preedit","--value","false","--config",$cfg,"--format","json") -Label "G40 prereq on" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.preedit_type","--value","preview","--config",$cfg,"--format","json") -Label "G40 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G40_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "预编辑类型_G40_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G40 Phase B complete"
