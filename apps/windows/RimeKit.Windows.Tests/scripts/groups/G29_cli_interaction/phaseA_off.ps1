. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
LogSection "G29A: CLI INTERACTION — OFF/default"
$results = @{}
function Test-Cli {
    param([string]$Name, [string[]]$CliArgs)
    Log "  CLI: $Name"
    Invoke-Cli -CliArgs $CliArgs -Label $Name
    $ec = $global:_cliExit
    $pass = ($ec -eq 0)
    Log ("    {0}: exit={1}" -f $(if($pass){'PASS'}else{'FAIL'}), $ec)
    return @{name=$Name; exit=$ec; pass=$pass}
}
$results["hover_off"]   = Test-Cli "hover_off" @("set-config","--field","windows_settings.hover_type","--value","none","--config",$cfg,"--format","json")
$results["click_off"]   = Test-Cli "click_off" @("set-config","--field","windows_settings.click_to_capture","--value","false","--config",$cfg,"--format","json")
$results["ascii_off"]   = Test-Cli "ascii_off" @("set-config","--field","windows_settings.ascii_tip_follow_cursor","--value","false","--config",$cfg,"--format","json")
$results["notify_off"]  = Test-Cli "notify_off" @("set-config","--field","windows_settings.notification_time_ms","--value","0","--config",$cfg,"--format","json")
$results["globalascii_off"] = Test-Cli "global_ascii_off" @("set-config","--field","windows_settings.global_ascii","--value","false","--config",$cfg,"--format","json")
$results["symbol_off"]        = Test-Cli "symbol_profile_id_default" @("set-config","--field","personalization_settings.symbol_profile_id","--value","default","--config",$cfg,"--format","json")
$results["preedit_off"]       = Test-Cli "preedit_format_mode_default" @("set-config","--field","personalization_settings.preedit_format_mode","--value","upstream_default","--config",$cfg,"--format","json")
$results["phrase_off"]        = Test-Cli "custom_phrase_mode_default" @("set-config","--field","personalization_settings.custom_phrase_mode","--value","simple_code_only","--config",$cfg,"--format","json")
$results["comment_off"]       = Test-Cli "comment_style_variant_default" @("set-config","--field","personalization_settings.comment_style_variant","--value","default","--config",$cfg,"--format","json")
$results["simplify_off"]      = Test-Cli "simplification_mode_default" @("set-config","--field","behavior_settings.simplification_mode","--value","simplified","--config",$cfg,"--format","json")
$results["emoji_off"]         = Test-Cli "emoji_suggestion_off" @("set-config","--field","behavior_settings.emoji_suggestion_enabled","--value","false","--config",$cfg,"--format","json")
$results["pagesize_off"]      = Test-Cli "page_size_default" @("set-config","--field","candidate_settings.page_size","--value","0","--config",$cfg,"--format","json")
$results["layout_off"]        = Test-Cli "candidate_layout_default" @("set-config","--field","candidate_settings.layout","--value","stacked","--config",$cfg,"--format","json")
$results["emojicmt_off"]      = Test-Cli "show_emoji_comments_off" @("set-config","--field","candidate_settings.show_emoji_comments","--value","false","--config",$cfg,"--format","json")
$results["fuzzy_off"]         = Test-Cli "fuzzy_enabled_off" @("set-config","--field","fuzzy_pinyin_settings.enabled","--value","false","--config",$cfg,"--format","json")
$results["enableuserdict_off"] = Test-Cli "enable_user_dict_off" @("set-config","--field","behavior_settings.enable_user_dict","--value","false","--config",$cfg,"--format","json")
Invoke-Cli -CliArgs @("apply","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply" | Out-Null
$pass = ($results.Values | Where-Object { $_.pass }).Count
$fail = ($results.Values | Where-Object { -not $_.pass }).Count
LogSection "G29A RESULT: ${pass} PASS, ${fail} FAIL"
$testDir = Join-Path $PSScriptRoot "..\results\G29_cli_interaction"
New-Item -ItemType Directory -Force -Path $testDir | Out-Null
$results | ConvertTo-Json -Depth 4 | Out-File -LiteralPath (Join-Path $testDir "A_off.json") -Encoding UTF8
