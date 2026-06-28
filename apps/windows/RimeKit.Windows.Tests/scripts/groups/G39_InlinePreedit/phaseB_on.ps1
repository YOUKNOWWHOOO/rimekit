. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G39 Phase B: 内嵌预编辑 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.inline_preedit","--value","true","--config",$cfg,"--format","json") -Label "G39 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G39_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "内嵌预编辑_G39_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G39 Phase B complete"
