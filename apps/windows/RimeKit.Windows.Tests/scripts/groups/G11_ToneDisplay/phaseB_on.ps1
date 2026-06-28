. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G11 Phase B: 声调显示 ON"
Invoke-Cli -CliArgs @("set-config","--field","behavior_settings.tone_display_enabled","--value","true","--config",$cfg,"--format","json") -Label "G11 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G11_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "声调显示_G11_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G11 Phase B complete"
