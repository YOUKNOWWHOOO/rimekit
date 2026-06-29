. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy
Rebuild -ExtraResourceIds @("moetype")
Warmup | Out-Null
LogSection "G12B: CLI COMMANDS — RESOURCE/ENTRIES (rime_mint+moetype)"

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

$results["install-resource"]  = Test-Cli "install-resource(moetype)" @("install-resource","--resource-id","moetype","--from-file",$moetypeCache,"--config",$cfg,"--format","json","--force-stop-weasel")
$results["uninstall-resource"]= Test-Cli "uninstall-resource(moetype)" @("uninstall-resource","--resource-id","moetype","--config",$cfg,"--format","json","--force-stop-weasel")
$results["add-entry"]         = Test-Cli "add-custom-entry" @("add-custom-entry","--text","CLI-test","--code","clitc","--weight","1000001","--config",$cfg,"--format","json")
$results["delete-entry"]      = Test-Cli "delete-custom-entry" @("delete-custom-entry","--text","CLI-test","--code","clitc","--config",$cfg,"--format","json")
$results["list-custom"]       = Test-Cli "list-custom-entries" @("list-custom-entries","--config",$cfg,"--format","json")
$results["apply-custom"]      = Test-Cli "apply-custom-entries" @("apply-custom-entries","--force-stop-weasel","--config",$cfg,"--format","json")

$pass = ($results.Values | Where-Object { $_.pass }).Count
$fail = ($results.Values | Where-Object { -not $_.pass }).Count
LogSection "G12B RESULT: ${pass} PASS, ${fail} FAIL"
$results | ConvertTo-Json -Depth 4 | Out-File -LiteralPath (Join-Path $testDir "B_resource.json") -Encoding UTF8
