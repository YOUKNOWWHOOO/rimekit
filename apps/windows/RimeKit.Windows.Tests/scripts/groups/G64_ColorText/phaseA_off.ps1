. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G64 Phase A: 文字颜色 OFF"
Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G64_A" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "文字颜色_G64_A.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G64 Phase A complete"
