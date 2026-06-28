. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G56 Phase A: 高亮纵向填充 OFF"
Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G56_A" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "高亮纵向填充_G56_A.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G56 Phase A complete"
