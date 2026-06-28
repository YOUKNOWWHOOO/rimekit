. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Invoke-Cli -CliArgs @("set-config","--field","pinyin_settings.ue_compat_enabled","--value","true","--config",$cfg,"--format","json") -Label "ue_on"
Assert-CliOk "ue_on"
Invoke-ApplyConfig
Warmup | Out-Null
$yamlPath = "$env:APPDATA\Rime\rime_mint.custom.yaml"
if (Test-Path $yamlPath) { $c = Get-Content $yamlPath -Raw -Encoding UTF8; $hasUe = $c -match 'nl.*ve.*ue'; Log "G30B YAML: size=$($c.Length) hasUe=$hasUe" }
$results = @{}
$results["ue"] = Assert-Probe -Text "shenglue" -Tag "G30B_ue" -Expected "省略"
Write-PhaseResult -Group "G30_ue_compat" -Phase "B_on" -ProbeResults $results -Snapshot @{}