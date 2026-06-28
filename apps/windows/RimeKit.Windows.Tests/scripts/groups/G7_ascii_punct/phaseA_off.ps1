. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$results = @{}
$results["a1"] = Assert-Probe -Text "." -Tag "G7_A_a1" -Expected "。"
Write-PhaseResult -Group "G7_ascii_punct" -Phase "A_off" -ProbeResults $results -Snapshot @{}
