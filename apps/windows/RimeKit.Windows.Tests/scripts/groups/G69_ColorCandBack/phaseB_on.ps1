. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G69 Phase B: 候选背景颜色 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_candidate_back_color","--value","0xFF00FFFF","--config",$cfg,"--format","json") -Label "G69 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G69_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选背景颜色_G69_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G69 Phase B complete"
