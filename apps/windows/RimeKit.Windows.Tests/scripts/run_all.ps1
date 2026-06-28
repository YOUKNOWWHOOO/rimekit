$ErrorActionPreference = "Continue"

$scriptsRoot = Split-Path -Parent $PSScriptRoot
$scriptsRoot = Split-Path -Parent $scriptsRoot
$scriptsRoot = Split-Path -Parent $scriptsRoot
$scriptsRoot = Split-Path -Parent $scriptsRoot

$ts = Get-Date -Format 'yyyyMMdd-HHmmss'

. "$PSScriptRoot\_common.ps1"
PreDownloadAssets

$groupDir = "apps\windows\RimeKit.Windows.Tests\scripts\groups"

$allPhases = @(
    @{G="G12_cli_commands";   A="phaseA_basic.ps1";    B="phaseB_resource.ps1"; C="phaseC_carrier.ps1"}
    @{G="G1_basic_input";     A="phaseA_baseline.ps1"; B="phaseB_baseline.ps1"; C="phaseC_baseline.ps1"}
    @{G="G30_ue_compat";      A="phaseA_off.ps1";       B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G31_combo_ue";       A="phaseA_off.ps1";       B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G32_combo_fuzzy";    A="phaseA_off.ps1";       B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G34_combo_both";     A="phaseA_off.ps1";       B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G35_combo_none";     A="phaseA_off.ps1"}
    @{G="G36_color_autocomplete"; A="phaseA_off.ps1"; B="phaseB_on.ps1"; C="phaseC_off.ps1"}
    @{G="G37_zhwiki_dict";    A="phaseA_baseline.ps1"; B="phaseB_install.ps1";  C="phaseC_uninstall.ps1"}
    @{G="G2_moetype_dict";    A="phaseA_baseline.ps1"; B="phaseB_install.ps1";  C="phaseC_uninstall.ps1"}
    @{G="G3_sogou_dict";      A="phaseA_baseline.ps1"; B="phaseB_install.ps1";  C="phaseC_uninstall.ps1"}
    @{G="G4_custom_entries";  A="phaseA_baseline.ps1"; B="phaseB_install.ps1";  C="phaseC_uninstall.ps1"}
    @{G="G5_fuzzy_pinyin";    A="phaseA_baseline.ps1"; B="phaseB_enable.ps1";   C="phaseC_disable.ps1"}
    @{G="G6_grammar_model";   A="phaseA_baseline.ps1"; B="phaseB_install.ps1";  C="phaseC_uninstall.ps1"}
    @{G="G7_ascii_punct";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G8_full_shape";      A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G9_simplification";  A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G10_EmojiCandidate";  A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G11_ToneDisplay";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G12_FontFace";        A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G13_FontPoint";       A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G14_LabelFontFace";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G15_LabelFontPoint";  A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G16_CommentFontFace"; A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G17_CommentFontPoint"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G18_StatusNotify";    A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G19_LabelFormat";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G20_MarkText";        A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G21_CandidateAbbrev"; A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G22_PageSize";        A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G23_CandidateDir";    A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G24_EmojiComment";    A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G25_Fullscreen";      A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G26_VerticalText";    A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G27_VerticalLTR";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G28_VerticalWrap";    A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G38_VertAutoRev";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G39_InlinePreedit";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G40_PreeditType";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G41_TrayIcon";        A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G42_LayoutMinW";      A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G43_LayoutMinH";      A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G44_LayoutMaxW";      A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G45_LayoutMaxH";      A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G46_LayoutMarginX";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G47_LayoutMarginY";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G48_LayoutBorder";    A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G49_LayoutLinespacing"; A="phaseA_off.ps1";    B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G50_LayoutBaseline";  A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G51_LayoutSpacing";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G52_LayoutCandSpacing"; A="phaseA_off.ps1";    B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G53_LayoutHiliteSpacing"; A="phaseA_off.ps1";  B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G54_LayoutHilitePad"; A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G55_LayoutHilitePadX"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G56_LayoutHilitePadY"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G57_LayoutShadowR";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G58_LayoutShadowOffX"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G59_LayoutShadowOffY"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G60_LayoutCornerR";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G61_LayoutAlign";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G62_ThemeLight";      A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G63_ThemeDark";       A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G64_ColorText";       A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G65_ColorCandText";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G66_ColorLabel";      A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G67_ColorComment";    A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G68_ColorBack";       A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G69_ColorCandBack";   A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G70_ColorBorder";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G71_ColorShadow";     A="phaseA_off.ps1";      B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G72_ColorHiliteText"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G73_ColorHiliteBack"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G74_ColorHiliteLabel"; A="phaseA_off.ps1";    B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G75_ColorHiliteCandText"; A="phaseA_off.ps1"; B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G76_ColorHiliteCandBack"; A="phaseA_off.ps1"; B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G77_ColorHiliteCandLabel"; A="phaseA_off.ps1"; B="phaseB_on.ps1";      C="phaseC_off.ps1"}
    @{G="G78_ColorHiliteCandBorder"; A="phaseA_off.ps1"; B="phaseB_on.ps1";     C="phaseC_off.ps1"}
    @{G="G79_ColorHiliteComment"; A="phaseA_off.ps1";  B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G80_ColorHiliteMark"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G29_cli_interaction"; A="phaseA_off.ps1";     B="phaseB_on.ps1";       C="phaseC_off.ps1"}
    @{G="G33_custom_phrase_mode"; A="phaseA_off.ps1"; B="phaseB_on.ps1"; C="phaseC_off.ps1"}
    @{G="G12_cli_commands_R2"; A="phaseA_basic.ps1"; B="phaseB_resource.ps1"; C="phaseC_carrier.ps1"; Dir="G12_cli_commands"}
    @{G="G13_input_method_picker"; A="phaseA_baseline.ps1"; B="phaseB_picker.ps1"}
)

$totalPhases = 0
$allStart = Get-Date

foreach ($group in $allPhases) {
    $g = $group.G
    $phases = @()
    if ($group.A) { $phases += @{Label="A"; File=$group.A} }
    if ($group.B) { $phases += @{Label="B"; File=$group.B} }
    if ($group.C) { $phases += @{Label="C"; File=$group.C} }

    foreach ($phase in $phases) {
        $totalPhases++
        $label = "${g}_$($phase.Label)"
        $actualDir = if ($group.ContainsKey("Dir")) { $group.Dir } else { $g }
        $scriptPath = Join-Path $groupDir "$actualDir\$($phase.File)"
        Write-Host ""
        Write-Host "========================================"
        Write-Host "  [$totalPhases] RUNNING: $label"
        Write-Host "  $(Get-Date -Format 'HH:mm:ss')"
        Write-Host "========================================"

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & pwsh -NoLogo -NonInteractive -File $scriptPath
        $exitCode = $LASTEXITCODE
        $sw.Stop()
        $elapsed = $sw.Elapsed.ToString("mm\:ss")
        $status = if ($exitCode -eq 0) { "PASS" } else { "FAIL" }
        Write-Host "  $status ($elapsed) exit=$exitCode"

        if ($exitCode -ne 0) {
            Write-Host "  *** PIPELINE HALTED at $label ***"
            $allEnd = Get-Date
            $totalTime = ($allEnd - $allStart).ToString("hh\:mm\:ss")
            Write-Host "PIPELINE FAILED at $label after $totalTime"
            exit $exitCode
        }
    }
}

$allEnd = Get-Date
$totalTime = ($allEnd - $allStart).ToString("hh\:mm\:ss")
Write-Host ""
Write-Host "========================================"
Write-Host "  PIPELINE COMPLETE  ($totalTime)"
Write-Host "========================================"
Write-Host "  RAN: $totalPhases phases"
Write-Host ""
