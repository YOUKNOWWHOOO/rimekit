. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G65 Phase B: 候选文字颜色 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_candidate_text_color","--value","0xFF0000FF","--config",$cfg,"--format","json") -Label "G65 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G65_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选文字颜色_G65_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G65 Phase B complete"
