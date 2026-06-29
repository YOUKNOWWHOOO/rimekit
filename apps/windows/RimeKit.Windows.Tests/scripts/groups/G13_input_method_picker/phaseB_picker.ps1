. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$screenshotDir = $script:screenshotDir
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
$ahkExe = Get-AhkExe
$pickerAhk = Join-Path $env:TEMP "rimekit_picker_hold.ahk"

Log "G13 Phase B: open input method picker via Win+Space hold"
$ahkScript = @'
#SingleInstance Force
SetKeyDelay(10, 0)
Send("{LWin down}")
Sleep(500)
Send("{Space}")
Sleep(3000)
Send("{LWin up}")
ExitApp(0)
'@
$ahkScript | Out-File -LiteralPath $pickerAhk -Encoding UTF8

Invoke-Cli -CliArgs @("open-input-method-picker","--format","json") -Label "open-input-method-picker" | Out-Null
Log "  exitCode=$($global:_cliExit)"
Start-Sleep $SleepShort
if ($ahkExe) {
    Start-Process $ahkExe -ArgumentList $pickerAhk -WindowStyle Hidden
} else {
    Log "  WARNING: AutoHotkey64.exe not found, picker screenshot may be incomplete"
}
Start-Sleep $SleepShort
python $takeScreenshotPy (Join-Path $screenshotDir "输入法选择器_ON.png") 2>$null
Start-Sleep $SleepMedium
Log "G13 Phase B complete"
