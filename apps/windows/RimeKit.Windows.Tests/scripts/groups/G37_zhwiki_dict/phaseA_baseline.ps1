. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir
Destroy
Rebuild
$results = @{}
@(
    @{i="huhehaote";   l="z1"; e="呼和浩特"},
    @{i="eerduosi";     l="z2"; e="鄂尔多斯"},
    @{i="xishuangbanna"; l="z3"; e="西双版纳"},
    @{i="hulunbeier";   l="z4"; e="呼伦贝尔"}
) | ForEach-Object { $results[$_.l] = Assert-BaselineProbe -Text $_.i -Tag "G37_A_$($_.l)" -Expected $_.e }
Write-PhaseResult -Group "G37_zhwiki_dict" -Phase "A_baseline" -ProbeResults $results -Snapshot @{}
