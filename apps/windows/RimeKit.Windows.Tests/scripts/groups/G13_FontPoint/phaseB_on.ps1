. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G13 Phase B: 候选词字号 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.font_point","--value","24","--config",$cfg,"--format","json") -Label "G13 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G13_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选词字号_G13_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G13 Phase B complete"
