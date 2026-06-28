. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G43 Phase B: 最小高度 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_min_height","--value","200","--config",$cfg,"--format","json") -Label "G43 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G43_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "最小高度_G43_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G43 Phase B complete"
