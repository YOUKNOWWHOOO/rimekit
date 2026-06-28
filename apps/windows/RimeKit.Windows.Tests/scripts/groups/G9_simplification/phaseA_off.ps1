. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$r=@{};
$r["t1"]=Assert-Probe -Text "toufa" -Tag "G9_A_t1" -Expected "头发"
Write-PhaseResult -Group "G9_simplification" -Phase "A_off" -ProbeResults $r -Snapshot @{}
