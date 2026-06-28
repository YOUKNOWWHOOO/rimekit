. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir
Destroy
Rebuild
Log "C+. adding custom entries..."
@(
    @("流程闭环","lcbh"), @("虚拟动漫","xydm"), @("终测推进","zctj"),
    @("星梦流花","xmlh"), @("多轮快测","dlkc")
) | ForEach-Object {
    Invoke-Cli -CliArgs @("add-custom-entry","--text",$_[0],"--code",$_[1],"--weight","1000001","--config",$cfg,"--format","json") -Label "add-entry" | Out-Null
}
Invoke-Cli -CliArgs @("apply-custom-entries","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply-custom" | Out-Null
Log "C+. deleting custom entries..."
@(@("流程闭环","lcbh"), @("虚拟动漫","xydm"), @("终测推进","zctj"), @("星梦流花","xmlh"), @("多轮快测","dlkc")) | ForEach-Object {
    Invoke-Cli -CliArgs @("delete-custom-entry","--text",$_[0],"--code",$_[1],"--config",$cfg,"--format","json") -Label "delete-entry" | Out-Null
}
Invoke-Cli -CliArgs @("apply-custom-entries","--force-stop-weasel","--config",$cfg,"--format","json") -Label "re-apply-custom" | Out-Null
Invoke-ApplyConfig
$results = @{}
@(
    @{i="lcbh"; l="ce1"; e="流程闭环"},
    @{i="xydm"; l="ce2"; e="虚拟动漫"},
    @{i="zctj"; l="ce3"; e="终测推进"},
    @{i="xmlh"; l="ce4"; e="星梦流花"},
    @{i="dlkc"; l="ce5"; e="多轮快测"}
) | ForEach-Object { $results[$_.l] = Assert-BaselineProbe -Text $_.i -Tag "G4_C_$($_.l)" -Expected $_.e }
Write-PhaseResult -Group "G4_custom_entries" -Phase "C_uninstall" -ProbeResults $results -Snapshot @{}
