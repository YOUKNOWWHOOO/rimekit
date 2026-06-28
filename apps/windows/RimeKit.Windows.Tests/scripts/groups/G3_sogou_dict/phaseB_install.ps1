. "$PSScriptRoot\..\..\_common_ci_test.ps1"
Init-WorkDir
Destroy
Rebuild -ExtraResourceIds @("sogou_network_popular_words")
$results = @{}
@(
    @{i="suweidetibu";      l="s1"; e="苏炜德替补"},
    @{i="atiaojietaiwenle"; l="s2"; e="阿条姐太稳了"},
    @{i="yindujingshejie";  l="s3"; e="印度敬蛇节"},
    @{i="ganfangehaowen";   l="s4"; e="干饭哥好稳"},
    @{i="jinbanshoujiang";  l="s5"; e="金扳手奖"}
) | ForEach-Object { $results[$_.l] = Assert-Probe -Text $_.i -Tag "G3_B_$($_.l)" -Expected $_.e }
Write-PhaseResult -Group "G3_sogou_dict" -Phase "B_install" -ProbeResults $results -Snapshot @{}
