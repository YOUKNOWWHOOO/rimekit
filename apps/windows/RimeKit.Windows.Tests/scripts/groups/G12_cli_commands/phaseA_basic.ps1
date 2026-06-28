. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
LogSection "G12A: CLI COMMANDS — READ/MUTATE (rime_mint only)"

$results = @{}
$testDir = Join-Path $PSScriptRoot "..\results\G12_cli_commands"
New-Item -ItemType Directory -Force -Path $testDir | Out-Null

function Test-Cli {
    param([string]$Name, [string[]]$CliArgs)
    Log "  CLI: $Name"
    Invoke-Cli -CliArgs $CliArgs -Label $Name
    $ec = $global:_cliExit
    $pass = ($ec -eq 0)
    Log ("    {0}: exit={1}" -f $(if($pass){'PASS'}else{'FAIL'}), $ec)
    return @{name=$Name; exit=$ec; pass=$pass}
}

$results["doctor"]          = Test-Cli "doctor" @("doctor","--config",$cfg,"--format","json")
$results["activate"]        = Test-Cli "activate-weasel-profile" @("activate-weasel-profile","--format","json")
$results["apply"]           = Test-Cli "apply" @("apply","--force-stop-weasel","--config",$cfg,"--format","json")
$results["rollback"]        = Test-Cli "rollback" @("rollback","--force-stop-weasel","--format","json")
$results["export"]          = Test-Cli "export" @("export","--kind","user-config-toml","--output",(Join-Path $testDir "export.toml"),"--config",$cfg,"--format","json")
$results["print-config"]    = Test-Cli "print-config" @("print-config","--config",$cfg,"--format","json")
$results["resource-status"] = Test-Cli "resource-status" @("resource-status","--format","json")
$results["set-config"]      = Test-Cli "set-config" @("set-config","--field","candidate_settings.page_size","--value","5","--config",$cfg,"--format","json")
$results["reset-config"]    = Test-Cli "reset-config" @("reset-config","--config",$cfg,"--format","json")

$setConfigFields = @(
    @{n="sc-label-ff";      f="windows_settings.label_font_face";      v="霞鹜文楷 GB 屏幕阅读版"}
    @{n="sc-label-fp";      f="windows_settings.label_font_point";     v="14"}
    @{n="sc-cmnt-ff";       f="windows_settings.comment_font_face";    v="霞鹜文楷 GB 屏幕阅读版"}
    @{n="sc-cmnt-fp";       f="windows_settings.comment_font_point";   v="12"}
    @{n="sc-inline-pre";    f="windows_settings.inline_preedit";       v="true"}
    @{n="sc-preedit-type";  f="windows_settings.preedit_type";         v="preview"}
    @{n="sc-fullscreen";    f="windows_settings.fullscreen";           v="true"}
    @{n="sc-vertical-text"; f="windows_settings.vertical_text";        v="true"}
    @{n="sc-global-ascii";  f="windows_settings.global_ascii";         v="true"}
    @{n="sc-notify-ms";     f="windows_settings.notification_time_ms"; v="2000"}
    @{n="sc-corner";        f="windows_settings.layout_corner_radius"; v="8"}
    @{n="sc-shadow-r";      f="windows_settings.layout_shadow_radius"; v="4"}
    @{n="sc-margin-x";      f="windows_settings.layout_margin_x";      v="10"}
    @{n="sc-spacing";       f="windows_settings.layout_spacing";       v="12"}
    @{n="sc-border-w";      f="windows_settings.layout_border_width";  v="3"}
    @{n="sc-hover";         f="windows_settings.hover_type";           v="hilite"}
    @{n="sc-antialias";     f="windows_settings.antialias_mode";       v="cleartype"}
    @{n="sc-label-fmt";     f="windows_settings.label_format";         v="%s."}
    @{n="sc-ue-compat";     f="pinyin_settings.ue_compat_enabled";     v="false"}
    @{n="sc-candidate-len"; f="windows_settings.candidate_abbreviate_length"; v="20"}
    @{n="sc-mark-text";     f="windows_settings.mark_text";            v=">"}
    @{n="sc-hilite-sp";     f="windows_settings.layout_hilite_spacing"; v="10"}
    @{n="sc-hilite-pad";    f="windows_settings.layout_hilite_padding"; v="12"}
    @{n="sc-align-type";    f="windows_settings.layout_align_type";    v="top"}
    @{n="sc-baseline";      f="windows_settings.layout_baseline";      v="3"}
    @{n="sc-min-h";         f="windows_settings.layout_min_height";    v="100"}
    @{n="sc-min-w";         f="windows_settings.layout_min_width";     v="200"}
    @{n="sc-shadow-ox";     f="windows_settings.layout_shadow_offset_x"; v="3"}
    @{n="sc-shadow-oy";     f="windows_settings.layout_shadow_offset_y"; v="4"}
    @{n="sc-enhanced-pos";  f="windows_settings.enhanced_position";    v="true"}
    @{n="sc-tray-icon";     f="windows_settings.display_tray_icon";    v="true"}
    @{n="sc-click-capture"; f="windows_settings.click_to_capture";     v="true"}
    @{n="sc-ascii-tip";     f="windows_settings.ascii_tip_follow_cursor"; v="true"}
    @{n="sc-vert-ltr";      f="windows_settings.vertical_text_left_to_right"; v="true"}
    @{n="sc-vert-wrap";     f="windows_settings.vertical_text_with_wrap"; v="true"}
    @{n="sc-vert-rev";      f="windows_settings.vertical_auto_reverse"; v="true"}
    @{n="sc-max-h";         f="windows_settings.layout_max_height";    v="800"}
    @{n="sc-max-w";         f="windows_settings.layout_max_width";     v="900"}
    @{n="sc-hpad-x";        f="windows_settings.layout_hilite_padding_x"; v="4"}
    @{n="sc-hpad-y";        f="windows_settings.layout_hilite_padding_y"; v="5"}
    @{n="sc-linespacing";   f="windows_settings.layout_linespacing";   v="3"}
    @{n="sc-cand-sp";       f="windows_settings.layout_candidate_spacing"; v="24"}
)
foreach ($t in $setConfigFields) {
    $results[$t.n] = Test-Cli $t.n @("set-config","--field",$t.f,"--value",$t.v,"--config",$cfg,"--format","json")
}

$pass = ($results.Values | Where-Object { $_.pass }).Count
$fail = ($results.Values | Where-Object { -not $_.pass }).Count
LogSection "G12A RESULT: ${pass} PASS, ${fail} FAIL"
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $testDir "A_basic.json") -Encoding UTF8
