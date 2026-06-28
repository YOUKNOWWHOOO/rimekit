. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$probePy = Join-Path $PSScriptRoot "..\..\toolkit\probe_notepad_ime.py"
Log "G33_custom_phrase_mode Phase A: OFF (disabled, no custom entry)"
Warmup | Out-Null
$probeResult = Assert-BaselineProbe -Text "wm" -Tag "G33_custom_phrase_A" -Expected "我们"
Write-PhaseResult -Group "G33_custom_phrase_mode" -Phase "A_off" -Results @{custom_phrase="wm->我们"; pass=($probeResult.match)}
Log "G33_custom_phrase_mode Phase A complete"