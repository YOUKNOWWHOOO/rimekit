. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G25 Phase B: 全屏 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.fullscreen","--value","true","--config",$cfg,"--format","json") -Label "G25 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G25_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "全屏_G25_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G25 Phase B complete"
