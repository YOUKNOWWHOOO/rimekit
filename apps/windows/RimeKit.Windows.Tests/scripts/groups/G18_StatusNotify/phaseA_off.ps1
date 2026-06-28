. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$screenshotDir = (Join-Path $root "workspace\windows\screenshots")
New-Item -ItemType Directory -Force -Path $screenshotDir | Out-Null
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
$toggleAhk = Join-Path $PSScriptRoot "..\..\toolkit\probe_ime_toggle.ahk"
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
$ahkExe = Get-AhkExe

Log "G18 Phase A: 状态变化通知 OFF"
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.show_notification","--value","false","--config",$cfg,"--format","json") -Label "G18 off" | Out-Null
Invoke-ApplyConfig
Start-Sleep $SleepLong

python $probePy --phase=screenshot "G18_A" "a" 2>$null
Start-Sleep $SleepShort
if ($ahkExe) { & $ahkExe $toggleAhk 2>$null }
Start-Sleep 0
python $takeScreenshotPy (Join-Path $screenshotDir "状态变化通知_G18_A.png") 2>$null
Stop-NotepadSafe
Start-Sleep $SleepShort
Log "G18 Phase A complete"
