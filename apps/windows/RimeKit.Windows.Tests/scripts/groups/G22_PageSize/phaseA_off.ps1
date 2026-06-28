. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G22 Phase A: 候选数 OFF"
Invoke-Cli -CliArgs @("set-config","--field","candidate_settings.page_size","--value","3","--config",$cfg,"--format","json") -Label "G22 off" | Out-Null
Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G22_A" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选数_G22_A.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G22 Phase A complete"
