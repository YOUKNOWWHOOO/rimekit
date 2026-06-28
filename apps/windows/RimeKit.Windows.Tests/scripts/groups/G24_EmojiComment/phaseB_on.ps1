. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G24 Phase B: Emoji 注释 ON"
Invoke-Cli -CliArgs @("set-config","--field","candidate_settings.show_emoji_comments","--value","true","--config",$cfg,"--format","json") -Label "G24 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G24_B" "kaixin" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "Emoji 注释_G24_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G24 Phase B complete"
