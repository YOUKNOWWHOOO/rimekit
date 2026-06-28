. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Invoke-Cli -CliArgs @("set-config","--field","fuzzy_pinyin_settings.enabled","--value","true","--config",$cfg,"--format","json") -Label "enable fuzzy" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","fuzzy_pinyin_settings.additional_rules","--value","[""derive/zh/z"",""derive/ch/c"",""derive/sh/s""]","--config",$cfg,"--format","json") -Label "set additional_rules" | Out-Null
Invoke-ApplyConfig
Warmup | Out-Null
$results = @{}
@(@{i="zongsuozhouzhi";l="f1";e="众所周知"},@{i="caofantuosu";l="f2";e="超凡脱俗"},@{i="senruqianchu";l="f3";e="深入浅出"},@{i="senlinqijing";l="f4";e="身临其境"},@{i="zishouhuajiao";l="f5";e="指手画脚"}) | % { $results[$_.l] = Assert-Probe -Text $_.i -Tag "G5_B_$($_.l)" -Expected $_.e }
Write-PhaseResult -Group "G5_fuzzy_pinyin" -Phase "B_enable" -ProbeResults $results -Snapshot @{}
