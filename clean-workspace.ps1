[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$DryRun,
    [switch]$IncludeVsCache,
    [switch]$StopProcesses
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[clean] $Message"
}

function Remove-DirectorySafe {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$DryRunMode
    )

    if ($DryRunMode) {
        Write-Info "Would remove: $Path"
        return $true
    }

    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            Write-Info "Removed: $Path"
            return $true
        }
        catch {
            if ($attempt -lt 3) {
                Start-Sleep -Milliseconds 250
                continue
            }
        }
    }

    try {
        $children = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue)
        foreach ($child in $children) {
            Remove-Item -LiteralPath $child.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }

        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        Write-Info "Removed after retry: $Path"
        return $true
    }
    catch {
        # Attempt ACL repair for stubborn directories, then final remove.
        try {
            $principal = "$($env:USERDOMAIN)\$($env:USERNAME)"
            & takeown /f $Path /r /d y *> $null
            & icacls $Path /grant "$principal`:(OI)(CI)F" /t /c *> $null

            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            Write-Info "Removed after ACL repair: $Path"
            return $true
        }
        catch {
            Write-Warning "Failed to remove: $Path`n$($_.Exception.Message)"
            return $false
        }
    }
}

$repoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    throw "Could not resolve repository root from script location."
}

Write-Info "Repository root: $repoRoot"

if ($StopProcesses) {
    $processNames = @("SimsModDesktop")
    foreach ($name in $processNames) {
        $procs = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        foreach ($proc in $procs) {
            if ($DryRun) {
                Write-Info "Would stop process: $($proc.ProcessName) (PID $($proc.Id))"
            }
            else {
                try {
                    Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                    Write-Info "Stopped process: $($proc.ProcessName) (PID $($proc.Id))"
                }
                catch {
                    Write-Warning "Failed to stop process PID $($proc.Id): $($_.Exception.Message)"
                }
            }
        }
    }
}

$targetNames = @("bin", "obj", "build_tmp", "TestResults")
$targetDirs = @(
    Get-ChildItem -Path $repoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { $targetNames -contains $_.Name }
)

if ($IncludeVsCache) {
    $vsDir = Join-Path -Path $repoRoot -ChildPath ".vs"
    if (Test-Path -LiteralPath $vsDir) {
        $targetDirs += Get-Item -LiteralPath $vsDir
    }
}

$targetDirs = $targetDirs |
    Sort-Object -Property FullName -Unique

$prunedTargets = New-Object System.Collections.Generic.List[System.IO.DirectoryInfo]
foreach ($dir in ($targetDirs | Sort-Object { $_.FullName.Length })) {
    $isChildOfExisting = $false
    foreach ($existing in $prunedTargets) {
        $prefix = $existing.FullName.TrimEnd('\') + '\'
        if ($dir.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            $isChildOfExisting = $true
            break
        }
    }

    if (-not $isChildOfExisting) {
        $prunedTargets.Add($dir) | Out-Null
    }
}

$targetDirs = $prunedTargets

if ($targetDirs.Count -eq 0) {
    Write-Info "No cleanup targets found."
    exit 0
}

Write-Info "Found $($targetDirs.Count) directories to clean."
foreach ($dir in $targetDirs) {
    Write-Info "Target: $($dir.FullName)"
}

if (-not $Force -and -not $DryRun) {
    $answer = Read-Host "Delete all listed directories? Type 'y' to continue"
    if ($answer -ne "y") {
        Write-Info "Canceled by user."
        exit 1
    }
}

$successCount = 0
$failCount = 0
foreach ($dir in $targetDirs) {
    $ok = Remove-DirectorySafe -Path $dir.FullName -DryRunMode:$DryRun
    if ($ok) {
        $successCount++
    }
    else {
        $failCount++
    }
}

if ($DryRun) {
    Write-Info "Dry run completed. $successCount directories would be removed."
    exit 0
}

if ($failCount -gt 0) {
    Write-Warning "Cleanup finished with failures. Success: $successCount, Failed: $failCount"
    exit 2
}

Write-Info "Cleanup completed. Removed $successCount directories."
exit 0
