. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$yamlPath = "$env:APPDATA\Rime\rime_mint.custom.yaml"
Invoke-Cli -CliArgs @("set-config","--field","pinyin_settings.ue_compat_enabled","--value","true","--config",$cfg,"--format","json") -Label "ue_on"
Assert-CliOk "ue_on"
if (Test-Path $yamlPath) { $c = Get-Content $yamlPath -Raw -Encoding UTF8; Log "G31B YAML: size=$($c.Length) hasUe=$(($c -match 'nl.*ve.*ue')) hasFz=$(($c -match 'derive/zh'))" }
Invoke-ApplyConfig
Warmup | Out-Null
$results = @{}
$results["ue"] = Assert-Probe -Text "shenglue" -Tag "G31B_ue" -Expected "省略"
$results["fz"] = Assert-BaselineProbe -Text "zongsuozhouzhi" -Tag "G31B_fz" -Expected "众所周知"
Write-PhaseResult -Group "G31_combo_ue" -Phase "B_on" -ProbeResults $results -Snapshot @{}