. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$r=@{};
$r["f1"]=Assert-Probe -Text "123" -Tag "G8_A_f1" -Expected "123"
Write-PhaseResult -Group "G8_full_shape" -Phase "A_off" -ProbeResults $r -Snapshot @{}
