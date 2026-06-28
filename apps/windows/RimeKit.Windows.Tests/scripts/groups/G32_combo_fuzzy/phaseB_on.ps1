. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$yamlPath = "$env:APPDATA\Rime\rime_mint.custom.yaml"
Invoke-Cli -CliArgs @("set-config","--field","fuzzy_pinyin_settings.enabled","--value","true","--config",$cfg,"--format","json") -Label "fz_on"
Assert-CliOk "fz_on"
Invoke-Cli -CliArgs @("set-config","--field","fuzzy_pinyin_settings.additional_rules","--value","[""derive/zh/z"",""derive/ch/c"",""derive/sh/s""]","--config",$cfg,"--format","json") -Label "fz_rules"
Assert-CliOk "fz_rules"
if (Test-Path $yamlPath) { $c = Get-Content $yamlPath -Raw -Encoding UTF8; Log "G32B YAML: size=$($c.Length) hasUe=$(($c -match 'nl.*ve.*ue')) hasFz=$(($c -match 'derive/zh'))" }
Invoke-ApplyConfig
Warmup | Out-Null
$results = @{}
$results["ue"] = Assert-BaselineProbe -Text "shenglue" -Tag "G32B_ue" -Expected "省略"
$results["fz"] = Assert-Probe -Text "zongsuozhouzhi" -Tag "G32B_fz" -Expected "众所周知"
Write-PhaseResult -Group "G32_combo_fuzzy" -Phase "B_on" -ProbeResults $results -Snapshot @{}