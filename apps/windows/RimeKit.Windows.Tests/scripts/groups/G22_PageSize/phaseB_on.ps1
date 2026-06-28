. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G22 Phase B: 候选数 ON"
Invoke-Cli -CliArgs @("set-config","--field","candidate_settings.page_size","--value","6","--config",$cfg,"--format","json") -Label "G22 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G22_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选数_G22_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G22 Phase B complete"
