. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy
Rebuild -ExtraResourceIds @("wanxiang_lts_zh_hans")
Invoke-Cli -CliArgs @("uninstall-resource","--resource-id","wanxiang_lts_zh_hans","--config",$cfg,"--format","json","--force-stop-weasel") -Label "uninstall wanxiang_lts_zh_hans" | Out-Null
Invoke-ApplyConfig
Warmup | Out-Null
$results = @{}
$r = Probe -Word "zhebushinigaiguandeshiqing" -Tag "G6_C_g1" -Expected "这不是你该管的事情"
    $baselineFile = Join-Path $PSScriptRoot "..\..\results\G6_baseline.txt"
$baselineText = ""
if (Test-Path $baselineFile) { $baselineText = (Get-Content $baselineFile -Raw -Encoding UTF8).Trim() }
$match = $false
if ($baselineText -and $r.o -and $r.o.Trim() -eq $baselineText) { $match = $true }
$r.m = $match
$results["g1"] = $r
$status = if ($match) { "PASS" } else { if ($baselineText) { "FAIL(diff)" } else { "FAIL(no baseline)" } }
Log ("  G6_C delta: baseline='$baselineText' actual='$($r.o)' match=$match ($status)")
Write-PhaseResult -Group "G6_grammar_model" -Phase "C_uninstall" -ProbeResults $results -Snapshot @{}
