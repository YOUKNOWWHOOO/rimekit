. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G38 Phase C: 竖排自动反转 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.vertical_text","--value","true","--config",$cfg,"--format","json") -Label "G38 prereq on" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.vertical_auto_reverse","--value","true","--config",$cfg,"--format","json") -Label "G38 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G38_C_ON" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "竖排自动反转_G38_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.vertical_auto_reverse","--value","false","--config",$cfg,"--format","json") -Label "G38 off" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G38_C_OFF" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "竖排自动反转_G38_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G38 Phase C complete"
