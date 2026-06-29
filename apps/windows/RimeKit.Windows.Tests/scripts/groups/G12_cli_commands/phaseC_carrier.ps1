. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
LogSection "G12C: CLI COMMANDS — CARRIER (install/uninstall) + UNINSTALL-ALL"

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

$results["install-weasel"]   = Test-Cli "install-weasel" @("install-weasel","--from-file",$weaselInstallerCache,"--format","json")
$results["uninstall-weasel"] = Test-Cli "uninstall-weasel" @("uninstall-weasel","--format","json")
$results["uninstall-all"]    = Test-Cli "uninstall-all" @("uninstall-all","--format","json")

$pass = ($results.Values | Where-Object { $_.pass }).Count
$fail = ($results.Values | Where-Object { -not $_.pass }).Count
LogSection "G12C RESULT: ${pass} PASS, ${fail} FAIL"
$results | ConvertTo-Json -Depth 4 | Out-File -LiteralPath (Join-Path $testDir "C_carrier.json") -Encoding UTF8
