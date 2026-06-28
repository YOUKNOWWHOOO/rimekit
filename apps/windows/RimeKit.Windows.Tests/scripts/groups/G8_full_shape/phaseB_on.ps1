. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Invoke-Cli -CliArgs @("set-config","--field","behavior_settings.full_shape_enabled","--value","true","--config",$cfg,"--format","json") -Label "enable full_shape" | Out-Null
Invoke-ApplyConfig
Warmup | Out-Null
$r=@{};
$r["f1"]=Assert-Probe -Text "123" -Tag "G8_B_f1" -Expected "１２３"
Write-PhaseResult -Group "G8_full_shape" -Phase "B_on" -ProbeResults $r -Snapshot @{}
