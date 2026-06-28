. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G76 Phase B: 高亮候选背景颜色 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_hilited_candidate_back_color","--value","0xFF00FFFF","--config",$cfg,"--format","json") -Label "G76 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G76_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "高亮候选背景颜色_G76_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G76 Phase B complete"
