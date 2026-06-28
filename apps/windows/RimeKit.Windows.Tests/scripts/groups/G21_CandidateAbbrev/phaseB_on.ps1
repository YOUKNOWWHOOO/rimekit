. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G21 Phase B: 候选缩写长度 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.candidate_abbreviate_length","--value","2","--config",$cfg,"--format","json") -Label "G21 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G21_B" "zhongguoren" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选缩写长度_G21_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G21 Phase B complete"
