. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
LogSection "G29C: CLI INTERACTION — ON->OFF"
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
$results["hover_on"]   = Test-Cli "hover_on" @("set-config","--field","windows_settings.hover_type","--value","hilite","--config",$cfg,"--format","json")
$results["click_on"]   = Test-Cli "click_on" @("set-config","--field","windows_settings.click_to_capture","--value","true","--config",$cfg,"--format","json")
$results["ascii_on"]   = Test-Cli "ascii_on" @("set-config","--field","windows_settings.ascii_tip_follow_cursor","--value","true","--config",$cfg,"--format","json")
$results["notify_on"]  = Test-Cli "notify_on" @("set-config","--field","windows_settings.notification_time_ms","--value","2000","--config",$cfg,"--format","json")
$results["globalascii_on"] = Test-Cli "global_ascii_on" @("set-config","--field","windows_settings.global_ascii","--value","true","--config",$cfg,"--format","json")
$results["preedit_on"]       = Test-Cli "preedit_format_mode_raw" @("set-config","--field","personalization_settings.preedit_format_mode","--value","raw_code","--config",$cfg,"--format","json")
$results["phrase_on"]        = Test-Cli "custom_phrase_mode_full" @("set-config","--field","personalization_settings.custom_phrase_mode","--value","full_phrase","--config",$cfg,"--format","json")
$results["comment_on"]       = Test-Cli "comment_style_variant_none" @("set-config","--field","personalization_settings.comment_style_variant","--value","none","--config",$cfg,"--format","json")
$results["simplify_on"]      = Test-Cli "simplification_mode_traditional" @("set-config","--field","behavior_settings.simplification_mode","--value","traditional","--config",$cfg,"--format","json")
$results["emoji_on"]         = Test-Cli "emoji_suggestion_on" @("set-config","--field","behavior_settings.emoji_suggestion_enabled","--value","true","--config",$cfg,"--format","json")
$results["pagesize_on"]      = Test-Cli "page_size_7" @("set-config","--field","candidate_settings.page_size","--value","7","--config",$cfg,"--format","json")
$results["layout_on"]        = Test-Cli "candidate_layout_linear" @("set-config","--field","candidate_settings.layout","--value","linear","--config",$cfg,"--format","json")
$results["emojicmt_on"]      = Test-Cli "show_emoji_comments_on" @("set-config","--field","candidate_settings.show_emoji_comments","--value","true","--config",$cfg,"--format","json")
$results["fuzzy_on"]         = Test-Cli "fuzzy_enabled_on" @("set-config","--field","fuzzy_pinyin_settings.enabled","--value","true","--config",$cfg,"--format","json")
$results["enableuserdict_on"] = Test-Cli "enable_user_dict_on" @("set-config","--field","behavior_settings.enable_user_dict","--value","true","--config",$cfg,"--format","json")
Invoke-Cli -CliArgs @("apply","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply_on" | Out-Null
$results["hover_off"]   = Test-Cli "hover_off" @("set-config","--field","windows_settings.hover_type","--value","none","--config",$cfg,"--format","json")
$results["click_off"]   = Test-Cli "click_off" @("set-config","--field","windows_settings.click_to_capture","--value","false","--config",$cfg,"--format","json")
$results["ascii_off"]   = Test-Cli "ascii_off" @("set-config","--field","windows_settings.ascii_tip_follow_cursor","--value","false","--config",$cfg,"--format","json")
$results["notify_off"]  = Test-Cli "notify_off" @("set-config","--field","windows_settings.notification_time_ms","--value","0","--config",$cfg,"--format","json")
$results["globalascii_off"] = Test-Cli "global_ascii_off" @("set-config","--field","windows_settings.global_ascii","--value","false","--config",$cfg,"--format","json")
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
Invoke-Cli -CliArgs @("apply","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply_off" | Out-Null
$pass = ($results.Values | Where-Object { $_.pass }).Count
$fail = ($results.Values | Where-Object { -not $_.pass }).Count
LogSection "G29C RESULT: ${pass} PASS, ${fail} FAIL"
$testDir = Join-Path $PSScriptRoot "..\results\G29_cli_interaction"
New-Item -ItemType Directory -Force -Path $testDir | Out-Null
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $testDir "C_off.json") -Encoding UTF8
