. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G47 Phase B: 垂直边距 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_margin_y","--value","30","--config",$cfg,"--format","json") -Label "G47 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G47_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "垂直边距_G47_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G47 Phase B complete"
