. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G10 Phase A: Emoji 候选 OFF"
Invoke-Cli -CliArgs @("set-config","--field","behavior_settings.emoji_suggestion_enabled","--value","false","--config",$cfg,"--format","json") -Label "G10 off" | Out-Null
Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G10_A" "haha" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "Emoji 候选_G10_A.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G10 Phase A complete"
