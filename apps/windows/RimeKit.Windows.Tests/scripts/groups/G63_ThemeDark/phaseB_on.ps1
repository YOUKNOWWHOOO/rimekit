. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G63 Phase B: 深色主题 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.color_scheme_dark","--value","mint_light_blue","--config",$cfg,"--format","json") -Label "G63 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G63_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "深色主题_G63_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G63 Phase B complete"
