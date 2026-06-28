. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G62 Phase B: 浅色主题 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.color_scheme","--value","mint_dark_blue","--config",$cfg,"--format","json") -Label "G62 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G62_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "浅色主题_G62_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G62 Phase B complete"
