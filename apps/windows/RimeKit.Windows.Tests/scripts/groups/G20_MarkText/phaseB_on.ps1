. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G20 Phase B: 标记文本 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.mark_text","--value",">","--config",$cfg,"--format","json") -Label "G20 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G20_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "标记文本_G20_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G20 Phase B complete"
