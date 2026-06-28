. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G27 Phase B: 竖排左→右 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.vertical_text","--value","true","--config",$cfg,"--format","json") -Label "G27 prereq on" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.vertical_text_left_to_right","--value","true","--config",$cfg,"--format","json") -Label "G27 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G27_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "竖排左→右_G27_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G27 Phase B complete"
