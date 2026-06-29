$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent $root
$root = Split-Path -Parent $root
$root = Split-Path -Parent $root

$cliDir = Join-Path $root "apps\windows\RimeKit.Windows.Cli"
$cfg = Join-Path $root "workspace\windows\state\current_config_model.json"
$activator = Join-Path $root "apps\windows\RimeKit.Windows.Activator\bin\Debug\net10.0-windows\RimeKit.Windows.Activator.exe"
$probePy = Join-Path $PSScriptRoot "toolkit\probe_notepad_ime.py"
$imeRefsSrc = Join-Path $root "ime_refs"
$env:RIMEKIT_ALLOW_REAL_INPUT = "1"
$env:RIMEKIT_WEASEL_ACTIVATOR_PATH = $activator

$workDir = Join-Path $env:TEMP "rimekit_disc_matrix"
$weaselRoot = "C:\Program Files\Rime"
$cacheDir = Join-Path $root "tmp"
$weaselInstallerCache = Join-Path $cacheDir "weasel-0.17.4.0-installer.exe"
$rimeMintZipCache = Join-Path $cacheDir "oh-my-rime.zip"
$moetypeCache = Join-Path $cacheDir "tone_moe.dict.yaml"
$sogouCache = Join-Path $cacheDir "网络流行新词.scel"
$wanxiangCache = Join-Path $cacheDir "wanxiang-lts-zh-hans.gram"
$zhwikiCache = Join-Path $cacheDir "zhwiki-20251104.dict.yaml"

$SleepPoll = 0.5
$SleepShort = 1
$SleepMedium = 3
$SleepLong = 8
$SleepExtraLong = 10

$minCfg = @'
{
  "config_version": 1,
  "profile_settings": {
    "enabled_schema_ids": ["rime_mint"],
    "windows_default_schema_id": "rime_mint",
    "android_default_schema_id": "t9"
  },
  "fuzzy_pinyin_settings": { "preset_id": "", "target_schema_ids": ["rime_mint"] },
  "personalization_settings": { "symbol_profile_id": "default", "preedit_format_mode": "upstream_default" },
  "dictionary_settings": { "enabled_dictionary_ids": [], "dictionary_order": [], "custom_entries": [] },
  "model_settings": { "enabled_model_ids": [], "active_model_id": "", "model_root": "%APPDATA%\\Rime", "model_versions": {} },
  "sync_settings": { "android_import_root": "", "windows_target_root": "%APPDATA%\\Rime", "export_root": "", "backup_root": "", "snapshot_retention_limit": 20 },
  "android_settings": { "keyboard_layout": "9_key", "candidate_text_size": 22, "candidate_view_height": 32 },
  "windows_settings": { "dpi_scale_mode": "per_monitor_v2" }
}
'@

function Init-WorkDir {
    $script:runTs = Get-Date -Format 'yyyyMMdd-HHmmss'
    $resultsClean = Join-Path $PSScriptRoot "groups\results\_latest"
    Remove-ItemSafe -Path $resultsClean -Recurse
    Remove-ItemSafe -Path $workDir -Recurse
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null
    $script:persistDir = Join-Path $PSScriptRoot "groups\results\$script:runTs"
    New-Item -ItemType Directory -Force -Path $script:persistDir | Out-Null
    $script:persistFile = Join-Path $script:persistDir "_probes.jsonl"
    $script:screenshotDir = Join-Path $root "workspace\windows\screenshots\$script:runTs"
    New-Item -ItemType Directory -Force -Path $script:screenshotDir | Out-Null
    $script:logFile = Join-Path $workDir "full_log.txt"
}

function Log {
    param([string]$Message)
    $ts = Get-Date -Format "HH:mm:ss.fff"
    $line = "[${ts}] ${Message}"
    [Console]::Error.WriteLine($line)
    if ($script:logFile) {
        try {
            Add-Content -LiteralPath $script:logFile -Value $line -Encoding UTF8
        } catch {
            [Console]::Error.WriteLine("[Log] 无法写入日志文件: $_")
        }
    }
}

function LogSection {
    param([string]$Title)
    Log ""
    Log ("=" * 60)
    Log "  ${Title}"
    Log ("=" * 60)
}

function Invoke-RestartWeaselServer {
    Log "RESTART: Starting WeaselServer..."
    $serverExe = $null
    $weaselDirs = Get-ChildItem $weaselRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
    foreach ($dir in $weaselDirs) {
        $se = Join-Path $dir.FullName "WeaselServer.exe"
        if (Test-Path $se) { $serverExe = $se; break }
    }
    if (-not $serverExe) {
        Log "  WARNING: WeaselServer.exe not found"
        return $false
    }
    Start-Process $serverExe -WindowStyle Hidden
    for ($i = 1; $i -le 10; $i++) {
        Start-Sleep $SleepShort
        if (@(Get-Process "WeaselServer" -ErrorAction SilentlyContinue).Count -gt 0) {
            Log "  WeaselServer running after ${i}s"
            return $true
        }
    }
    Log "  WARNING: WeaselServer did not start"
    return $false
}

function WaitTableBin {
    param([int]$TimeoutSec = 60)
    $tablePath = "$env:APPDATA\Rime\build\rime_mint.table.bin"
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $tablePath) {
            $sz = (Get-Item $tablePath).Length
            if ($sz -gt 0) { return $sz }
        }
        Start-Sleep $SleepPoll
    }
    return 0
}

function Warmup {
    $imeRefsDest = Join-Path $env:TEMP "ime_refs"
    if (-not (Test-Path -LiteralPath $imeRefsDest)) {
        New-Item -ItemType Directory -Force -Path $imeRefsDest | Out-Null
        if (Test-Path -Path (Join-Path $imeRefsSrc "*")) {
            Copy-ItemSafe (Join-Path $imeRefsSrc "*") -Destination $imeRefsDest
        }
    }
    for ($i = 1; $i -le 6; $i++) {
        $f = Join-Path $workDir "warmup_${i}.json"
        python $probePy --phase=vision "wu${i}" "nihao" '{SPACE}' 2>$null | Out-File -LiteralPath $f -Encoding UTF8
        if (-not $?) { Log "  warmup ${i}: python failed"; continue }
        Start-Sleep $SleepShort
        if (-not (Test-Path $f)) { Log "  warmup ${i}: no output file"; continue }
        $c = Get-Content -LiteralPath $f -Encoding UTF8 -Raw
        if (-not $c) { Log "  warmup ${i}: empty file"; continue }
        try { $j = $c | ConvertFrom-Json } catch { Log "  warmup ${i}: parse error: $_"; continue }
        $rawOut = $j.observed_output
        if ($null -eq $rawOut) { Log "  warmup ${i}: null output, status=$($j.status)"; continue }
        $out = $rawOut.ToString().Trim()
        Log ("  warmup {0}: status={1} output={2}" -f $i, $j.status, $out)
        if ($j.status -eq "completed" -and $out -eq "你好") { return $true }
    }
    return $false
}

function Write-Persist {
    param([hashtable]$Result)
    $line = ConvertTo-Json -InputObject $Result -Compress
    if ($script:persistFile) {
        try {
            Add-Content -LiteralPath $script:persistFile -Value $line -Encoding UTF8
        } catch {
            Log "  WARNING: Write-Persist 失败: $_"
        }
    }
}

function Invoke-Probe {
    param([string]$Word, [string]$Tag, [string]$Expected)
    $r = $null
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        Log ("  PROBE: {0} ({1}) expect={2} (attempt {3})" -f $Tag, $Word, $Expected, $attempt)
        $f = Join-Path $workDir "${Tag}.json"
        python $probePy --phase=vision $Tag $Word '{SPACE}' 2>$null | Out-File -LiteralPath $f -Encoding UTF8
        if (-not $?) { Log "  PROBE: python failed"; $r = @{o=""; s="python_fail"; e=$Expected; w=$Word; m=$false }; continue }
        Start-Sleep $SleepShort
        if (-not (Test-Path $f)) { $r = @{o=""; s="no_file"; e=$Expected; w=$Word; m=$false }; continue }
        $c = Get-Content -LiteralPath $f -Encoding UTF8 -Raw
        if (-not $c) { $r = @{o=""; s="empty"; e=$Expected; w=$Word; m=$false }; continue }
        try { $j = $c | ConvertFrom-Json } catch { $r = @{o=""; s="parse"; e=$Expected; w=$Word; m=$false }; continue }
        $rawOut = $j.observed_output
        if ($null -eq $rawOut -or $rawOut -is [System.DBNull]) {
            $r = @{o=$null; s=$j.status; e=$Expected; w=$Word; m=$false }
            if ($j.status -eq "blocked" -and $attempt -lt 2) { Log "    blocked, retrying..."; continue }
            break
        }
        $actual = $rawOut.ToString().Trim()
        $match = $actual -eq $Expected
        Log ("    result: status={0} actual={1} match={2}" -f $j.status, $actual, $match)
        $r = @{o=$actual; s=$j.status; e=$Expected; w=$Word; m=$match}
        if ($r.m -or $attempt -eq 2) { break }
        Log "    no match, retrying..."
    }
    Write-Persist $r
    return $r
}

function Get-AhkExe {
    $candidates = @(
        Join-Path $env:LOCALAPPDATA "Programs\AutoHotkey\v2\AutoHotkey64.exe"
        Join-Path $env:TEMP "ahk-v2-portable\AutoHotkey64.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { return $c }
    }
    return $null
}

function PreDownloadAssets {
    $assets = @{
        "Weasel installer"        = $weaselInstallerCache
        "oh-my-rime (rime_mint)" = $rimeMintZipCache
        "moetype dictionary"     = $moetypeCache
        "sogou SCEL"             = $sogouCache
        "wanxiang grammar model" = $wanxiangCache
        "zhwiki dictionary"      = $zhwikiCache
    }
    foreach ($name in $assets.Keys) {
        $path = $assets[$name]
        if (-not (Test-Path -LiteralPath $path)) {
            throw "PreDownloadAssets: missing asset '$name' at: $path"
        }
    }
}

function Stop-NotepadSafe {
    param([int]$TimeoutMs = 5000)
    Stop-Process -Name "notepad" -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $procs = Get-Process -Name "notepad" -ErrorAction SilentlyContinue
        if (-not $procs) { return $true }
        Start-Sleep -Milliseconds 200
    }
    Log "  WARNING: notepad did not exit within ${TimeoutMs}ms"
    return $false
}

function Remove-ItemSafe {
    param([string]$Path, [switch]$Recurse)
    if (-not (Test-Path -LiteralPath $Path)) { return $true }
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        if ($Recurse) { Remove-Item -LiteralPath $Path -Recurse -Force }
        else { Remove-Item -LiteralPath $Path -Force }
        if (-not (Test-Path -LiteralPath $Path)) { return $true }
        $delay = [Math]::Min(200 * [Math]::Pow(2, $attempt), 4000)
        Start-Sleep -Milliseconds $delay
    }
    Log "  WARNING: Remove-Item failed for: ${Path}"
    return (Test-Path -LiteralPath $Path) -eq $false
}

function Copy-ItemSafe {
    param([string]$Source, [string]$Destination, [switch]$Recurse)
    $hasWildcard = $Source.Contains('*') -or $Source.Contains('?')
    $sourceExists = if ($hasWildcard) { Test-Path -Path $Source } else { Test-Path -LiteralPath $Source }
    if (-not $sourceExists) { throw "Copy-ItemSafe: source not found: ${Source}" }
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        if ($hasWildcard) { Copy-Item -Path $Source -Destination $Destination -Recurse:$Recurse -Force }
        else { Copy-Item -LiteralPath $Source -Destination $Destination -Recurse:$Recurse -Force }
        $destTest = if ($hasWildcard) { Test-Path -LiteralPath $Destination } else { Test-Path -LiteralPath $Destination }
        if ($destTest) { return $true }
        $delay = [Math]::Min(200 * [Math]::Pow(2, $attempt), 4000)
        Start-Sleep -Milliseconds $delay
    }
    Log "  WARNING: Copy-Item failed for: ${Source} -> ${Destination}"
    return $false
}

function Invoke-WebRequestSafe {
    param([string]$Uri, [string]$OutFile)
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing -PassThru
            if ($response.StatusCode -eq 200 -and (Test-Path -LiteralPath $OutFile) -and (Get-Item -LiteralPath $OutFile).Length -gt 0) {
                return $true
            }
            Log "  WARNING: download returned status=$($response.StatusCode) or empty file: ${Uri}"
        } catch {
            Log "  WARNING: download attempt ${attempt}: $_"
        }
        $delay = [Math]::Min(1000 * [Math]::Pow(2, $attempt), 10000)
        Start-Sleep -Milliseconds $delay
    }
    throw "Failed to download: ${Uri}"
}
