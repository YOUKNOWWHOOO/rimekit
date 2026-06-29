. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
Warmup | Out-Null
$results = @{}
$r = Probe -Word "zhebushinigaiguandeshiqing" -Tag "G6_A_g1" -Expected "这不是你该管的事情"
$results["g1"] = $r
$baselineFile = Join-Path $PSScriptRoot "..\..\results\G6_baseline.txt"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $baselineFile) | Out-Null
$r.o | Out-File -LiteralPath $baselineFile -Encoding UTF8 -NoNewline
Log ("  G6 baseline saved: $($r.o)")
Write-PhaseResult -Group "G6_grammar_model" -Phase "A_baseline" -ProbeResults $results -Snapshot @{}
