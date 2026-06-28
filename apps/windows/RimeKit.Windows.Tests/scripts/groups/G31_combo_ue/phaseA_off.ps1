. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$results = @{}
$results["ue"] = Assert-BaselineProbe -Text "shenglue" -Tag "G31A_ue" -Expected "省略"
$results["fz"] = Assert-BaselineProbe -Text "zongsuozhouzhi" -Tag "G31A_fz" -Expected "众所周知"
Write-PhaseResult -Group "G31_combo_ue" -Phase "A_off" -ProbeResults $results -Snapshot @{}