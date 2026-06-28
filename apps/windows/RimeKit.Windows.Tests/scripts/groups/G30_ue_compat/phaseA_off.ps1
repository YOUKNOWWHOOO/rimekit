. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$yamlPath = "$env:APPDATA\Rime\rime_mint.custom.yaml"
if (Test-Path $yamlPath) {
    $c = Get-Content $yamlPath -Raw -Encoding UTF8
    $hasUe = $c -match 'nl.*ve.*ue'
    Log "G30A YAML: size=$($c.Length) hasUe=$hasUe"
}
$results = @{}
$results["ue"] = Assert-BaselineProbe -Text "shenglue" -Tag "G30A_ue" -Expected "省略"
$results["n1"] = Assert-Probe -Text "nihao" -Tag "G30A_n1" -Expected "你好"
Write-PhaseResult -Group "G30_ue_compat" -Phase "A_off" -ProbeResults $results -Snapshot @{}