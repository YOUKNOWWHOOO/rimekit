. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$results = @{}
$results["ue"] = Assert-BaselineProbe -Text "shenglue" -Tag "G32A_ue" -Expected "省略"
$results["fz"] = Assert-BaselineProbe -Text "zongsuozhouzhi" -Tag "G32A_fz" -Expected "众所周知"
Write-PhaseResult -Group "G32_combo_fuzzy" -Phase "A_off" -ProbeResults $results -Snapshot @{}