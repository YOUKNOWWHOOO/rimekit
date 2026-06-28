. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Invoke-Cli -CliArgs @("set-config","--field","behavior_settings.ascii_punct_enabled","--value","true","--config",$cfg,"--format","json") -Label "enable ascii_punct" | Out-Null
Invoke-ApplyConfig
Warmup | Out-Null
$results = @{}
$results["a1"] = Assert-Probe -Text "." -Tag "G7_B_a1" -Expected "."
Write-PhaseResult -Group "G7_ascii_punct" -Phase "B_on" -ProbeResults $results -Snapshot @{}
