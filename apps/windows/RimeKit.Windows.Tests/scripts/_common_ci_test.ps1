. "$PSScriptRoot\_common.ps1"

$cliExe = Join-Path $root "apps\windows\RimeKit.Windows.Cli\bin\Debug\net10.0-windows\RimeKit.Windows.Cli.exe"

$global:_cliExit = 0
$global:_cliError = ""

function Invoke-Cli {
    param([string[]]$CliArgs, [string]$Label = "")
    $tmpOut = Join-Path $env:TEMP "rimekit_cli_output.txt"
    if (Test-Path $cliExe) {
        & $cliExe @CliArgs 2>&1 | Out-File $tmpOut -Encoding UTF8
    } else {
        & dotnet run --project $cliDir -- @CliArgs 2>&1 | Out-File $tmpOut -Encoding UTF8
    }
    $global:_cliExit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) {
        $errText = [string](Get-Content $tmpOut -Raw -Encoding UTF8 -ErrorAction SilentlyContinue)
        $global:_cliError = if ($errText) { ($errText -split "`n" | Select-Object -First 6) -join "`n" } else { "" }
    } else {
        Remove-ItemSafe $tmpOut
        $global:_cliError = ""
    }
}

function Assert-CliOk {
    param([string]$Label)
    $ec = $global:_cliExit
    if ($ec -ne 0) {
        Log "  CLI $Label failed exit=$ec"
        if ($global:_cliError) {
            foreach ($l in ($global:_cliError -split "`n")) { $t = $l.Trim(); if ($t) { Log "  CLI: $t" } }
        }
        throw "$Label failed with exit $ec"
    }
}

function Invoke-PhaseDestroyCli {
    LogSection "PHASE DESTROY (product CLI)"
    Log "A. Product CLI: uninstall-weasel..."
    Invoke-Cli -CliArgs @("uninstall-weasel","--format","json") -Label "uninstall-weasel"
    $exitCode = $global:_cliExit
    Log "  exitCode=$exitCode"
    Assert-CliOk "uninstall-weasel"
    Start-Sleep $SleepMedium

    Log "B. Product CLI: uninstall-all..."
    Invoke-Cli -CliArgs @("uninstall-all","--format","json") -Label "uninstall-all"
    $exitCode = $global:_cliExit
    Log "  exitCode=$exitCode"
    Assert-CliOk "uninstall-all"
    Start-Sleep $SleepShort

    Log "C. Remove residual TEMP files..."
    Remove-ItemSafe (Join-Path $env:TEMP "rime.weasel") -Recurse
    Get-ChildItem (Join-Path $env:TEMP "rimekit*") -ErrorAction SilentlyContinue | ForEach-Object { Remove-ItemSafe $_.FullName -Recurse }

    Log "D. Recreate workDir and restore IME refs..."
    Init-WorkDir
    $imeRefsDest = Join-Path $env:TEMP "ime_refs"
    Remove-ItemSafe $imeRefsDest -Recurse
    New-Item -ItemType Directory -Force -Path $imeRefsDest | Out-Null
    if (Test-Path $imeRefsSrc) {
        Copy-ItemSafe (Join-Path $imeRefsSrc "*") -Destination $imeRefsDest
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $cfg -Parent) | Out-Null
    Log "PHASE DESTROY COMPLETE"
}

function Invoke-PhaseRebuildCli {
    param([string[]]$ExtraResourceIds = @())
    LogSection "PHASE REBUILD (product CLI)"

    Log "A. Product CLI: install-weasel (--from-file)..."
    Invoke-Cli -CliArgs @("install-weasel","--format","json","--from-file",$weaselInstallerCache) -Label "install-weasel"
    Assert-CliOk "install-weasel"
    Log "  install-weasel done"
    Start-Sleep 5

    Log "B. Start WeaselServer via CLI..."
    Invoke-Cli -CliArgs @("start-weasel-server","--format","json") -Label "start-weasel-server"
    Assert-CliOk "start-weasel-server"
    Start-Sleep 3

    Log "C. Write baseline config..."
    $minCfg | Set-Content -LiteralPath $cfg -Encoding UTF8 -NoNewline

    Log "D. Product CLI: install-resource rime_mint (--from-file)..."
    Invoke-Cli -CliArgs @("install-resource","--resource-id","rime_mint","--from-file",$rimeMintZipCache,"--config",$cfg,"--format","json","--force-stop-weasel") -Label "install-resource rime_mint"
    Assert-CliOk "install-resource rime_mint"
    Log "  install-resource rime_mint done"

    Log "E. Product CLI: apply..."
    Invoke-Cli -CliArgs @("apply","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply"
    Assert-CliOk "apply"
    Log "  apply done"
    Start-Sleep 5

    Log "F. Verify table.bin..."
    $tblSz = WaitTableBin -TimeoutSec 60
    if ($tblSz -eq 0) { throw "table.bin still 0 after apply!" }
    Log "  table.bin=${tblSz} B"

    Log "G. Install extra resources..."
    foreach ($rid in $ExtraResourceIds) {
        Log "  installing: $rid"
        $extraCache = switch ($rid) {
            "moetype" { $moetypeCache }
            "sogou_network_popular_words" { $sogouCache }
            "wanxiang_lts_zh_hans" { $wanxiangCache }
            "zhwiki" { $zhwikiCache }
            default { throw "Unknown resource id $rid. Add cache path for it in _common.ps1" }
        }
        Invoke-Cli -CliArgs @("install-resource","--resource-id",$rid,"--from-file",$extraCache,"--config",$cfg,"--format","json","--force-stop-weasel") -Label "install $rid"
        Assert-CliOk "install $rid"
    }

    if ($ExtraResourceIds.Count -gt 0) {
        Log "H. Re-apply after extra resources..."
        Invoke-Cli -CliArgs @("apply","--force-stop-weasel","--config",$cfg,"--format","json") -Label "re-apply"
        $ec = $global:_cliExit
        if ($ec -ne 0) { Log "  WARNING: re-apply exit $ec" }
        Start-Sleep 5
        $tblSz = WaitTableBin -TimeoutSec 60
        Log "  table.bin=${tblSz} B"
    }

    Start-Sleep 5

    Log "J. Warmup..."
    $wuResult = Warmup
    Log "  warmup OK=$wuResult"
    if (-not $wuResult) { Log "  WARNING: warmup failed" }

    Log "PHASE REBUILD COMPLETE"
}

function Invoke-ApplyConfig {
    Log "APPLY: applying with force-stop-weasel..."
    Invoke-Cli -CliArgs @("apply","--force-stop-weasel","--config",$cfg,"--format","json") -Label "apply-force-stop"
    Assert-CliOk "apply-force-stop"
    Start-Sleep 10
    Log "  apply + start done"
}

function Assert-Probe {
    param([string]$Text, [string]$Tag, [string]$Expected)
    $r = Probe -Word $Text -Tag $Tag -Expected $Expected
    $status = if ($r.m) { "PASS" } else { "FAIL" }
    Log ("  {0}: {1} -> '{2}' (expected='{3}')" -f $status, $Text, $r.o, $Expected)
    return $r
}

function Assert-BaselineProbe {
    param([string]$Text, [string]$Tag, [string]$Expected)
    $r = Probe -Word $Text -Tag $Tag -Expected $Expected
    if ($r.m) {
        Log ("  UNEXPECTED: {0} -> '{1}' (expected NOT '{2}' at baseline)" -f $Text, $r.o, $Expected)
    } else {
        Log ("  baseline OK: {0} -> '{1}' (not '{2}' as expected)" -f $Text, $r.o, $Expected)
    }
    return $r
}

function Write-PhaseResult {
    param([string]$Group, [string]$Phase, $ProbeResults, $Snapshot)
    $resultDir = Join-Path $script:persistDir $Group
    New-Item -ItemType Directory -Force -Path $resultDir | Out-Null
    $file = Join-Path $resultDir "${Phase}.json"
    $data = @{
        group = $Group
        phase = $Phase
        timestamp = (Get-Date -Format "yyyy-MM-ddTHH:mm:sszzz")
        snapshot = $Snapshot
        probes = @{}
    }
    foreach ($key in $ProbeResults.Keys) {
        $data.probes[$key] = @{
            input = $ProbeResults[$key].w
            expected = $ProbeResults[$key].e
            actual = $ProbeResults[$key].o
            status = $ProbeResults[$key].s
            match = $ProbeResults[$key].m
        }
    }
    $data | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding UTF8
    $matchCount = ($ProbeResults.Values | Where-Object { $_.m }).Count
    $total = $ProbeResults.Count
    Log ("GROUP={0} PHASE={1}: {2}/{3} match" -f $Group, $Phase, $matchCount, $total)
}

Set-Alias -Name Destroy -Value Invoke-PhaseDestroyCli
Set-Alias -Name Rebuild -Value Invoke-PhaseRebuildCli
