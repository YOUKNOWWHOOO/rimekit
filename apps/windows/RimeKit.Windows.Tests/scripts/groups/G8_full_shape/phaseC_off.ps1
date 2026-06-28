. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Invoke-Cli -CliArgs @("set-config","--field","behavior_settings.full_shape_enabled","--value","true","--config",$cfg,"--format","json") -Label "enable full_shape" | Out-Null
Invoke-ApplyConfig
Invoke-Cli -CliArgs @("set-config","--field","behavior_settings.full_shape_enabled","--value","false","--config",$cfg,"--format","json") -Label "disable full_shape" | Out-Null
Invoke-ApplyConfig
Warmup | Out-Null
$r=@{};
$r["f1"]=Assert-Probe -Text "123" -Tag "G8_C_f1" -Expected "123"
Write-PhaseResult -Group "G8_full_shape" -Phase "C_off" -ProbeResults $r -Snapshot @{}
