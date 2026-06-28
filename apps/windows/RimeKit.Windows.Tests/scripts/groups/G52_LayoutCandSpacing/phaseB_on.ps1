. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G52 Phase B: 候选间距 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_candidate_spacing","--value","30","--config",$cfg,"--format","json") -Label "G52 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G52_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选间距_G52_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G52 Phase B complete"
