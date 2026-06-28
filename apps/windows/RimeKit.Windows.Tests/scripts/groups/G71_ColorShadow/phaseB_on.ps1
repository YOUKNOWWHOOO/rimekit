. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G71 Phase B: 阴影颜色 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_shadow_color","--value","0xFFFF0000","--config",$cfg,"--format","json") -Label "G71 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G71_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "阴影颜色_G71_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G71 Phase B complete"
