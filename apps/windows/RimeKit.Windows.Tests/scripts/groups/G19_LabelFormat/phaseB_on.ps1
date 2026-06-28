. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G19 Phase B: 标签格式 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.label_format","--value","%s.","--config",$cfg,"--format","json") -Label "G19 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G19_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "标签格式_G19_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G19 Phase B complete"
