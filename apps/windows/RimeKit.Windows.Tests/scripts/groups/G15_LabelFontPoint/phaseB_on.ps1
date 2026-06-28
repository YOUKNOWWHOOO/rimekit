. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G15 Phase B: 标签字号 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.label_font_point","--value","16","--config",$cfg,"--format","json") -Label "G15 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G15_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "标签字号_G15_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G15 Phase B complete"
