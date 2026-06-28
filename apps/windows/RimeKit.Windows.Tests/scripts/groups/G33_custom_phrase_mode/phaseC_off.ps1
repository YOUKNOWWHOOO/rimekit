. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
Log "G33_custom_phrase_mode Phase C: ON -> OFF (revert to disabled)"
Invoke-Cli -CliArgs @("add-custom-entry","--text","我们","--code","wm","--weight","1000001","--config",$cfg,"--format","json") -Label "add_custom_entry"
Assert-CliOk "add_custom_entry"
Invoke-Cli -CliArgs @("set-config","--field","personalization_settings.custom_phrase_mode","--value","full_phrase","--config",$cfg,"--format","json") -Label "custom_phrase_mode=full_phrase"
Assert-CliOk "custom_phrase_mode=full_phrase"
Invoke-Cli -CliArgs @("apply-custom-entries","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply_custom_entries_full"
Assert-CliOk "apply_custom_entries_full"
Invoke-ApplyConfig
Warmup | Out-Null
Log "  Phase C: verified full_phrase ON"
$probeResultOn = Assert-Probe -Text "wm" -Tag "G33_custom_phrase_C_on" -Expected "我们"
Invoke-Cli -CliArgs @("delete-custom-entry","--text","我们","--code","wm","--config",$cfg,"--format","json") -Label "delete_custom_entry"
Assert-CliOk "delete_custom_entry"
Invoke-Cli -CliArgs @("set-config","--field","personalization_settings.custom_phrase_mode","--value","disabled","--config",$cfg,"--format","json") -Label "custom_phrase_mode=disabled"
Assert-CliOk "custom_phrase_mode=disabled"
Invoke-ApplyConfig
Warmup | Out-Null
Log "  Phase C: restored to disabled, entry removed"
$probeResultOff = Assert-BaselineProbe -Text "wm" -Tag "G33_custom_phrase_C_off" -Expected "我们"
Write-PhaseResult -Group "G33_custom_phrase_mode" -Phase "C_off" -Results @{custom_phrase_on_pass=($probeResultOn.match); custom_phrase_off_pass=($probeResultOff.match)}
Log "G33_custom_phrase_mode Phase C complete"