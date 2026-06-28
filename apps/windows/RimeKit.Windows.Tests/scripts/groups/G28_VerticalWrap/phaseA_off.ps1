. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G28 Phase A: 竖排换行 OFF"
Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G28_A" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "竖排换行_G28_A.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G28 Phase A complete"
