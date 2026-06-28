. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$yamlPath = "$env:APPDATA\Rime\rime_mint.custom.yaml"
Invoke-Cli -CliArgs @("set-config","--field","pinyin_settings.ue_compat_enabled","--value","true","--config",$cfg,"--format","json") -Label "ue_on"
Assert-CliOk "ue_on"
Invoke-ApplyConfig
Warmup | Out-Null
$results = @{}
$results["ue_on"] = Assert-Probe -Text "shenglue" -Tag "G30C_ue_on" -Expected "省略"
Invoke-Cli -CliArgs @("set-config","--field","pinyin_settings.ue_compat_enabled","--value","false","--config",$cfg,"--format","json") -Label "ue_off"
Assert-CliOk "ue_off"
if (Test-Path $yamlPath) { $c = Get-Content $yamlPath -Raw -Encoding UTF8; $hasUe = $c -match 'nl.*ve.*ue'; Log "G30C YAML after ue_off: size=$($c.Length) hasUe=$hasUe" }
Invoke-ApplyConfig
Warmup | Out-Null
$results["ue_off"] = Assert-BaselineProbe -Text "shenglue" -Tag "G30C_ue_off" -Expected "省略"
Write-PhaseResult -Group "G30_ue_compat" -Phase "C_off" -ProbeResults $results -Snapshot @{}