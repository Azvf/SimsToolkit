param(
    [string]$OutputPath = "artifacts/perf/round2-baseline.json",
    [string]$RunCoreTests = "true"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Ensure-ParentDirectory {
    param([string]$Path)
    $parent = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($parent)) {
        return
    }

    if (-not (Test-Path $parent)) {
        New-Item -Path $parent -ItemType Directory | Out-Null
    }
}

function Measure-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    $started = Get-Date
    & $Action
    $elapsed = (Get-Date) - $started
    return [ordered]@{
        name = $Name
        elapsedMs = [int][Math]::Round($elapsed.TotalMilliseconds)
    }
}

Write-Host "Collecting Round2 baseline..."

$steps = New-Object System.Collections.Generic.List[object]
$runTests = -not ($RunCoreTests -match '^(0|false|no)$')

if ($runTests) {
    $steps.Add((Measure-Step -Name "tests.packagecore" -Action {
        dotnet test src/SimsModDesktop.PackageCore.Tests/SimsModDesktop.PackageCore.Tests.csproj --configuration Release --no-restore | Out-Null
    }))
    $steps.Add((Measure-Step -Name "tests.trayengine" -Action {
        dotnet test src/SimsModDesktop.TrayDependencyEngine.Tests/SimsModDesktop.TrayDependencyEngine.Tests.csproj --configuration Release --no-restore | Out-Null
    }))
    $steps.Add((Measure-Step -Name "tests.main" -Action {
        dotnet test src/SimsModDesktop.Tests/SimsModDesktop.Tests.csproj --configuration Release --no-restore | Out-Null
    }))
}

$process = Get-Process -Id $PID
$result = [ordered]@{
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    machine = [ordered]@{
        os = [System.Environment]::OSVersion.ToString()
        processorCount = [System.Environment]::ProcessorCount
        processWorkingSetBytes = $process.WorkingSet64
        processPeakWorkingSetBytes = $process.PeakWorkingSet64
    }
    notes = @(
        "This baseline script captures repeatable command timings.",
        "Warmup/export/analysis/organize/savepreview in-app timings should be collected from app logs and merged into this JSON."
    )
    targets = [ordered]@{
        modsWarmupColdP50ImprovePct = 35
        trayWarmupColdP50ImprovePct = 50
        trayWarmupWarmP50ImprovePct = 70
        organizeThroughputImprovePct = 40
        savePreviewThroughputImprovePct = 35
        peakMemoryMaxIncreasePct = 20
    }
    appScenarios = [ordered]@{
        modsWarmupColdMs = $null
        modsWarmupWarmMs = $null
        trayWarmupColdMs = $null
        trayWarmupWarmMs = $null
        trayExportMs = $null
        trayAnalysisMs = $null
        organizeMs = $null
        savePreviewMs = $null
        appPeakWorkingSetBytes = $null
    }
    commandSteps = $steps
}

Ensure-ParentDirectory -Path $OutputPath
$json = $result | ConvertTo-Json -Depth 8
Set-Content -Path $OutputPath -Value $json -Encoding UTF8

Write-Host "Round2 baseline written to $OutputPath"
