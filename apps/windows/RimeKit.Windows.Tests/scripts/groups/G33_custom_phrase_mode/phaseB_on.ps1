. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
Log "G33_custom_phrase_mode Phase B: ON (full_phrase)"
Invoke-Cli -CliArgs @("add-custom-entry","--text","我们","--code","wm","--weight","1000001","--config",$cfg,"--format","json") -Label "add_custom_entry"
Assert-CliOk "add_custom_entry"
Invoke-Cli -CliArgs @("set-config","--field","personalization_settings.custom_phrase_mode","--value","full_phrase","--config",$cfg,"--format","json") -Label "custom_phrase_mode=full_phrase"
Assert-CliOk "custom_phrase_mode=full_phrase"
Invoke-Cli -CliArgs @("apply-custom-entries","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply_custom_entries"
Assert-CliOk "apply_custom_entries"
Invoke-ApplyConfig
Warmup | Out-Null
$probeResult = Assert-Probe -Text "wm" -Tag "G33_custom_phrase_B" -Expected "我们"
Write-PhaseResult -Group "G33_custom_phrase_mode" -Phase "B_on" -Results @{custom_phrase="wm->我们"; pass=($probeResult.match)}
Log "G33_custom_phrase_mode Phase B complete"