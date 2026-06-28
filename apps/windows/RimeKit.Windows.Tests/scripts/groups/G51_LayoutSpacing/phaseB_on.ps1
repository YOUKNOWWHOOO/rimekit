. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G51 Phase B: 候选项间距 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.inline_preedit","--value","false","--config",$cfg,"--format","json") -Label "G51 prereq on" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_spacing","--value","30","--config",$cfg,"--format","json") -Label "G51 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G51_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "候选项间距_G51_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G51 Phase B complete"
