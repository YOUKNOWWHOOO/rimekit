. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G61 Phase C: 对齐方式 ON -> OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_align_type","--value","top","--config",$cfg,"--format","json") -Label "G61 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G61_C_ON" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "对齐方式_G61_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_align_type","--value","bottom","--config",$cfg,"--format","json") -Label "G61 off" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G61_C_OFF" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "对齐方式_G61_C.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G61 Phase C complete"
