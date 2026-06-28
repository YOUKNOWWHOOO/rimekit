. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Log "G36_color_autocomplete Phase C: ON->OFF — verify NO auto-completion (per 7.4.97.6 / 7.4.87)"
Invoke-Cli -CliArgs @("set-config","--field","candidate_settings.show_emoji_comments","--value","false","--config",$cfg,"--format","json") -Label "show cmt off" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.mark_text","--value","|","--config",$cfg,"--format","json") -Label "mark text pipe" | Out-Null
Invoke-ApplyConfig
Assert-CliOk "G36C apply ON"
$yaml = Join-Path $env:APPDATA "Rime\weasel.custom.yaml"
if (Test-Path -LiteralPath $yaml) {
    $content = Get-Content -LiteralPath $yaml -Raw -Encoding UTF8
    if ($content -match "comment_text_color.*0x00000000") {
        Log "  FAIL: comment_text_color transparent SHOULD NOT be auto-written (ON)"
    } else {
        Log "  PASS: comment_text_color transparent correctly absent (ON)"
    }
}
Invoke-Cli -CliArgs @("set-config","--field","windows_settings.mark_text","--value","","--config",$cfg,"--format","json") -Label "mark text clear" | Out-Null
Invoke-Cli -CliArgs @("set-config","--field","candidate_settings.show_emoji_comments","--value","true","--config",$cfg,"--format","json") -Label "show cmt on" | Out-Null
Invoke-ApplyConfig
Assert-CliOk "G36C apply OFF"
if (Test-Path -LiteralPath $yaml) {
    $content = Get-Content -LiteralPath $yaml -Raw -Encoding UTF8
    if ($content -match "comment_text_color.*0x00000000") {
        Log "  WARNING: transparent still present after OFF"
    } else {
        Log "  PASS: transparent removed after OFF"
    }
    if ($content -match "hilited_mark_color") {
        Log "  WARNING: hilited_mark_color still present after OFF"
    } else {
        Log "  PASS: hilited_mark_color removed after OFF"
    }
}
Log "G36_color_autocomplete Phase C complete"
