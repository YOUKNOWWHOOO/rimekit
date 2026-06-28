. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G75 Phase B: 高亮候选文字颜色 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_hilited_candidate_text_color","--value","0xFF000000","--config",$cfg,"--format","json") -Label "G75 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G75_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "高亮候选文字颜色_G75_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G75 Phase B complete"
