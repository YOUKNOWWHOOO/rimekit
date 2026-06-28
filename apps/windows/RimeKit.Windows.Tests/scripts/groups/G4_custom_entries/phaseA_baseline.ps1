. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir
Destroy
Rebuild
$results = @{}
@(
    @{i="lcbh"; l="ce1"; e="流程闭环"},
    @{i="xydm"; l="ce2"; e="虚拟动漫"},
    @{i="zctj"; l="ce3"; e="终测推进"},
    @{i="xmlh"; l="ce4"; e="星梦流花"},
    @{i="dlkc"; l="ce5"; e="多轮快测"}
) | ForEach-Object { $results[$_.l] = Assert-BaselineProbe -Text $_.i -Tag "G4_A_$($_.l)" -Expected $_.e }
Write-PhaseResult -Group "G4_custom_entries" -Phase "A_baseline" -ProbeResults $results -Snapshot @{}
