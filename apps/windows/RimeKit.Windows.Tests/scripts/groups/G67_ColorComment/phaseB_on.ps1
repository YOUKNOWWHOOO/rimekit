. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G67 Phase B: 注释文字颜色 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_comment_text_color","--value","0xFFFF0000","--config",$cfg,"--format","json") -Label "G67 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G67_B" "kaixin" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "注释文字颜色_G67_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G67 Phase B complete"
