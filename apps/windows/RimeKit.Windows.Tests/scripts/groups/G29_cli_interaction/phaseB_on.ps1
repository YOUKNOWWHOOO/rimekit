. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
LogSection "G29B: CLI INTERACTION — ON"
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
$results["symbol_on"]        = Test-Cli "symbol_profile_id_default" @("set-config","--field","personalization_settings.symbol_profile_id","--value","default","--config",$cfg,"--format","json")
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
Invoke-Cli -CliArgs @("apply","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply" | Out-Null
$pass = ($results.Values | Where-Object { $_.pass }).Count
$fail = ($results.Values | Where-Object { -not $_.pass }).Count
LogSection "G29B RESULT: ${pass} PASS, ${fail} FAIL"
$testDir = Join-Path $PSScriptRoot "..\results\G29_cli_interaction"
New-Item -ItemType Directory -Force -Path $testDir | Out-Null
$results | ConvertTo-Json -Depth 4 | Out-File -LiteralPath (Join-Path $testDir "B_on.json") -Encoding UTF8
