. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G59 Phase B: 阴影纵向偏移 ON"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.day_shadow_color","--value","0xFF000000","--config",$cfg,"--format","json") -Label "G59 prereq on" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_shadow_radius","--value","12","--config",$cfg,"--format","json") -Label "G59 prereq on" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.layout_shadow_offset_y","--value","15","--config",$cfg,"--format","json") -Label "G59 on" | Out-Null

Invoke-ApplyConfig
Start-Sleep $SleepLong
python $probePy --phase=screenshot "G59_B" "nihao" 2>$null
Start-Sleep $SleepMedium
python $takeScreenshotPy (Join-Path $screenshotDir "阴影纵向偏移_G59_B.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G59 Phase B complete"
