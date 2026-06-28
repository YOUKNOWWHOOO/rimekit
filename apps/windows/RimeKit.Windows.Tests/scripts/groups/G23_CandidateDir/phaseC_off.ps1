. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G23 Phase C: 候选方向 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","candidate_settings.layout","--value","linear","--config",$cfg,"--format","json") -Label "G23 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G23_C_ON" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选方向_G23_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","candidate_settings.layout","--value","","--config",$cfg,"--format","json") -Label "G23 off" | Out-Null
Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G23_C_OFF" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选方向_G23_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G23 Phase C complete"
