. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Invoke-Cli -CliArgs @("set-config","--field","behavior_settings.simplification_mode","--value","traditional","--config",$cfg,"--format","json") -Label "enable trad" | Out-Null
Invoke-ApplyConfig
Warmup | Out-Null
$r=@{};
$r["t1"]=Assert-Probe -Text "toufa" -Tag "G9_B_t1" -Expected "頭髮"
Write-PhaseResult -Group "G9_simplification" -Phase "B_on" -ProbeResults $r -Snapshot @{}
