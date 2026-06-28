. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir; Destroy; Rebuild
$results = @{}
@(@{i="zongsuozhouzhi";l="f1";e="众所周知"},@{i="caofantuosu";l="f2";e="超凡脱俗"},@{i="senruqianchu";l="f3";e="深入浅出"},@{i="senlinqijing";l="f4";e="身临其境"},@{i="zishouhuajiao";l="f5";e="指手画脚"}) | % { $results[$_.l] = Assert-BaselineProbe -Text $_.i -Tag "G5_A_$($_.l)" -Expected $_.e }
Write-PhaseResult -Group "G5_fuzzy_pinyin" -Phase "A_baseline" -ProbeResults $results -Snapshot @{}
