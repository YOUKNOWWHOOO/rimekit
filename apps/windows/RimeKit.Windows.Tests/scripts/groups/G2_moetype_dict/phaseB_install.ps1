. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir
Destroy
Rebuild -ExtraResourceIds @("moetype")
$results = @{}
@(
    @{i="zuozuomunainaiai";   l="m1"; e="佐佐木乃乃爱"},
    @{i="abaiguai";           l="m2"; e="阿柏怪"},
    @{i="abaishe";            l="m3"; e="阿柏蛇"},
    @{i="abosuolu";           l="m4"; e="阿勃梭鲁"},
    @{i="aboniya";            l="m5"; e="阿波尼亚"},
    @{i="xukongzhiheyulingzhimaliya"; l="m6"; e="虚空之盒与零之麻理亚"},
    @{i="jujilong";           l="m7"; e="巨戟龙"}
) | ForEach-Object { $results[$_.l] = Assert-Probe -Text $_.i -Tag "G2_B_$($_.l)" -Expected $_.e }
Write-PhaseResult -Group "G2_moetype_dict" -Phase "B_install" -ProbeResults $results -Snapshot @{}
