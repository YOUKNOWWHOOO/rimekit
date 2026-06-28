. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Log "G36_color_autocomplete Phase A: OFF baseline — auto-completion should NOT trigger"
Invoke-Cli -CliArgs @("set-config","--field","candidate_settings.show_emoji_comments","--value","true","--config",$cfg,"--format","json") -Label "show cmt on" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.mark_text","--value","","--config",$cfg,"--format","json") -Label "mark text empty" | Out-Null
Invoke-ApplyConfig
Assert-CliOk "G36A apply"
$yaml = Join-Path $env:APPDATA "Rime\weasel.custom.yaml"
if (Test-Path -LiteralPath $yaml) {
    $content = Get-Content -LiteralPath $yaml -Raw -Encoding UTF8
    if ($content -match "comment_text_color.*0x00000000") {
        Log "  WARNING: transparent color present but ShowEmojiComments is ON"
    } else {
        Log "  PASS: no transparent color when ShowEmojiComments is ON"
    }
    if ($content -match "hilited_mark_color") {
        Log "  WARNING: hilited_mark_color present but mark_text is empty"
    } else {
        Log "  PASS: no hilited_mark_color when mark_text is empty"
    }
    if ($content -match "style/color_scheme") {
        Log "  PASS: color_scheme write confirmed"
    }
}
Log "G36_color_autocomplete Phase A complete"
