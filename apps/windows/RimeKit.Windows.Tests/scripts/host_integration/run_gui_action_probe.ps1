param(
    [switch]$AllowHostMutation
)

if (-not $AllowHostMutation) {
    throw "此脚本会执行真实 CLI/GUI 操作和宿主系统交互。必须使用 -AllowHostMutation 显式确认。"
}

$ErrorActionPreference = 'Stop'

$actionsPath = $env:RIMEKIT_GUI_ACTIONS_PATH
$reportPath = $env:RIMEKIT_GUI_ACTION_REPORT_PATH

if (-not $actionsPath) { throw "RIMEKIT_GUI_ACTIONS_PATH env var not set." }
if (-not (Test-Path -LiteralPath $actionsPath)) { throw "Actions manifest not found: $actionsPath" }

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
$runner = Join-Path $repoRoot 'apps\windows\tools\GuiProbeRunner\bin\Debug\net10.0-windows\GuiProbeRunner.exe'

if (-not (Test-Path -LiteralPath $runner)) {
    throw "GuiProbeRunner not built. Run: dotnet build apps/windows/RimeKit.Windows.sln`nExpected: $runner"
}

$defaultReport = Join-Path $repoRoot 'workspace\windows\state\gui_action_probe_report.json'
$finalReport = if ($reportPath) { $reportPath } else { $defaultReport }
$reportDir = [System.IO.Path]::GetDirectoryName($finalReport)
if ($reportDir -and -not (Test-Path -LiteralPath $reportDir)) {
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
}

& $runner $actionsPath $repoRoot $finalReport
if ($LASTEXITCODE -ne 0) { throw "GuiProbeRunner failed with exit code $LASTEXITCODE" }

if (-not (Test-Path -LiteralPath $finalReport)) { throw "GuiProbeRunner did not generate report: $finalReport" }

$reportJson = Get-Content -LiteralPath $finalReport -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $reportJson) { throw "GuiProbeRunner report is empty or invalid JSON." }

$guiActions = @($reportJson | Where-Object { $_.trigger_kind -eq 'gui_click' })
if ($guiActions.Count -eq 0) { throw "GuiProbeRunner report contains no gui_click actions." }

$requiredActionsEnv = $env:RIMEKIT_GUI_REQUIRED_ACTIONS
$requiredGuiActions = if ($requiredActionsEnv) {
    $requiredActionsEnv -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
} else {
    @(
        "gui_detect_carrier_status",
        "gui_detect_scheme_status",
        "gui_detect_dictionary_status",
        "gui_detect_model_status",
        "gui_apply_display_settings",
        "gui_detect_current_settings"
    )
}

$missingRequired = @($requiredGuiActions | Where-Object {
    $actionId = $_
    $item = $reportJson | Where-Object { $_.action_id -eq $actionId } | Select-Object -First 1
    if (-not $item) { return $true }
    if ($item.status -ne 'completed') { return $true }
    if (-not $item.evidence_satisfied) { return $true }
    return $false
})

if ($missingRequired.Count -gt 0) {
    throw "Required GUI actions not fully satisfied: $($missingRequired -join ', ')"
}
