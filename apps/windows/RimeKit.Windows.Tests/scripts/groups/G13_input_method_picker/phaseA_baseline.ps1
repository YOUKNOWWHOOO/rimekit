. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$screenshotDir = $script:screenshotDir
$takeScreenshotPy = Join-Path $PSScriptRoot "..\..\toolkit\take_screenshot.py"
Log "G13 Phase A: CLI open-input-method-picker baseline"
Invoke-Cli -CliArgs @("open-input-method-picker","--format","json") -Label "open-input-method-picker" | Out-Null
$ec = $global:_cliExit
Log "  exitCode=$ec"
Start-Sleep $SleepShort
python $takeScreenshotPy (Join-Path $screenshotDir "输入法选择器_OFF.png") 2>$null
Log "G13 Phase A complete"
