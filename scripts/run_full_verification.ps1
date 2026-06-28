param()  # No parameters — always fresh clean + fresh download. Mandatory.

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
$closureScript = Join-Path $repoRoot 'apps\windows\RimeKit.Windows.Tests\scripts\run_real_windows_closure.ps1'

Write-Output "=== Full Verification Pipeline (mandatory fresh clean + fresh download) ==="
Write-Output ""

Write-Output "--- Delegating to run_real_windows_closure.ps1 (full 7-phase CLI pipeline) ---"
Write-Output ""

$result = & pwsh -NoProfile -ExecutionPolicy Bypass -File $closureScript `
    -AllowHostMutation `
    -ForceCleanState `
    -ConfigPath ".\workspace\windows\state\current_config_model.json" `
    -OutputReport ".\diagnostics\windows\requirements\current-round\real_windows_closure.md" 2>&1

Write-Output $result

Write-Output ""
Write-Output "=== Full verification pipeline complete ==="
