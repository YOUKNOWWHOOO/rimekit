. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Invoke-Cli -CliArgs @("set-config","--field","fuzzy_pinyin_settings.enabled","--value","true","--config",$cfg,"--format","json") -Label "fz_on"
Assert-CliOk "fz_on"
Invoke-Cli -CliArgs @("set-config","--field","fuzzy_pinyin_settings.additional_rules","--value","[""derive/zh/z"",""derive/ch/c"",""derive/sh/s""]","--config",$cfg,"--format","json") -Label "fz_rules"
Assert-CliOk "fz_rules"
Invoke-ApplyConfig
Warmup | Out-Null
$results = @{}
$results["fz_on"] = Assert-Probe -Text "zongsuozhouzhi" -Tag "G32C_fz_on" -Expected "众所周知"
Invoke-Cli -CliArgs @("set-config","--field","fuzzy_pinyin_settings.enabled","--value","false","--config",$cfg,"--format","json") -Label "fz_off"
Assert-CliOk "fz_off"
Invoke-ApplyConfig
Warmup | Out-Null
$results["fz_off"] = Assert-BaselineProbe -Text "zongsuozhouzhi" -Tag "G32C_fz_off" -Expected "众所周知"
Write-PhaseResult -Group "G32_combo_fuzzy" -Phase "C_off" -ProbeResults $results -Snapshot @{}