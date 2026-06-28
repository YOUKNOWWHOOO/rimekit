. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir

Destroy
Rebuild -ExtraResourceIds @()

Warmup | Out-Null
$results = @{}
$results["n1"] = Assert-Probe -Text "nihao" -Tag "G1_A_n1" -Expected "你好"
Write-PhaseResult -Group "G1_basic_input" -Phase "A_baseline" -ProbeResults $results -Snapshot @{}
