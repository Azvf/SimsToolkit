[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$SourcePaths,

    [Parameter()]
    [string]$TargetPath = '',

    [Parameter()]
    [switch]$SkipPruneEmptyDirs,

    [Parameter()]
    [switch]$ModFilesOnly,

    [Parameter()]
    [string[]]$ModExtensions = @(),

    [Parameter()]
    [switch]$VerifyContentOnNameConflict,

    [Parameter()]
    [ValidateRange(0, 104857600)]
    [int]$PrefixHashBytes = 0,

    [Parameter()]
    [ValidateRange(0, 64)]
    [int]$HashWorkerCount = 0
)

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# SSOT: apply defaults from SimsConfig
. (Join-Path $projectRoot 'modules\SimsConfig.ps1')
$cfg = $Script:SimsConfigDefault
if ([string]::IsNullOrEmpty($TargetPath)) { $TargetPath = $cfg.MergeTargetPath }
if ($ModExtensions.Count -eq 0) { $ModExtensions = $cfg.ModExtensions }
if ($PrefixHashBytes -eq 0) { $PrefixHashBytes = $cfg.PrefixHashBytes }
if ($HashWorkerCount -eq 0) { $HashWorkerCount = $cfg.HashWorkerCount }

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$coreModulePath = Join-Path $projectRoot 'modules\SimsFileOpsCore.psm1'
if (-not (Test-Path -LiteralPath $coreModulePath)) {
    throw "Core module not found: $coreModulePath"
}
Import-Module -Name $coreModulePath -Force

$hashCache = New-SimsHashCache

$target = $TargetPath
New-SimsDirectoryIfMissing -Path $target -ShouldProcessScript { param($targetPath, $action) $PSCmdlet.ShouldProcess($targetPath, $action) }
$target = Get-SimsResolvedLiteralPath -Path $target -AllowMissing

$sources = @()
foreach ($src in $SourcePaths) {
    $resolved = Get-SimsResolvedLiteralPath -Path $src
    if ($resolved -eq $target) { continue }
    $sources += $resolved
}

if ($sources.Count -eq 0) {
    Write-Host "No valid source paths to merge."
    return
}

$moved = 0
$replaced = 0
$droppedOlder = 0
$droppedDuplicate = 0
$nonModKept = 0
$errors = 0
$modExtSet = New-SimsModExtensionSet -Extensions $ModExtensions

$sourceFilesByRoot = @{}
$sourceConflictCandidates = New-Object System.Collections.Generic.List[System.IO.FileInfo]
$progressTotal = 0
foreach ($src in $sources) {
    $files = @(Get-ChildItem -LiteralPath $src -Recurse -File | Sort-Object FullName)
    $sourceFilesByRoot[$src] = $files

    foreach ($file in $files) {
        if ($file.DirectoryName -eq $target) { continue }
        $progressTotal++
        if ($ModFilesOnly -and (-not $modExtSet.Contains($file.Extension))) { continue }
        $sourceConflictCandidates.Add($file)
    }
}

$progressProcessed = 0
$progressStep = 200
Write-SimsProgress -Stage 'merge.file' -Current 0 -Total $progressTotal -Detail 'start'

if ($VerifyContentOnNameConflict) {
    $targetFiles = @(Get-ChildItem -LiteralPath $target -File | Sort-Object FullName)
    if ($ModFilesOnly) {
        $targetFiles = @($targetFiles | Where-Object { $modExtSet.Contains($_.Extension) })
    }

    $nameUniverse = @($targetFiles + @($sourceConflictCandidates))
    $duplicateNames = Get-SimsDuplicateFileNameSet -Files $nameUniverse
    if ($duplicateNames.Count -gt 0) {
        $seenPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        $filesToWarm = New-Object System.Collections.Generic.List[System.IO.FileInfo]
        foreach ($f in $nameUniverse) {
            if ($duplicateNames.Contains($f.Name) -and $seenPaths.Add($f.FullName)) {
                $filesToWarm.Add($f)
            }
        }
        Initialize-SimsPrefixHashCacheParallel -HashCache $hashCache -Files @($filesToWarm) -PrefixBytes $PrefixHashBytes -WorkerCount $HashWorkerCount
    }
}

foreach ($src in $sources) {
    $files = @($sourceFilesByRoot[$src])

    foreach ($file in $files) {
        if ($file.DirectoryName -eq $target) { continue }
        try {
            if ($ModFilesOnly -and (-not $modExtSet.Contains($file.Extension))) {
                $nonModKept++
                continue
            }

            $dest = Join-Path $target $file.Name
            $action = Resolve-SimsConflictAction -SourceFile $file -DestinationPath $dest -VerifyContent:$VerifyContentOnNameConflict -PrefixBytes $PrefixHashBytes -HashCache $hashCache

            switch ($action) {
                'Move' {
                    if ($PSCmdlet.ShouldProcess($file.FullName, "Move to $dest")) {
                        Move-Item -LiteralPath $file.FullName -Destination $dest
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $file.FullName
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $dest
                    }
                    $moved++
                    break
                }
                'Replace' {
                    if ($PSCmdlet.ShouldProcess($file.FullName, "Replace older file at $dest")) {
                        Move-Item -LiteralPath $file.FullName -Destination $dest -Force
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $file.FullName
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $dest
                    }
                    $replaced++
                    break
                }
                'DropOlder' {
                    if ($PSCmdlet.ShouldProcess($file.FullName, "Delete older/equal duplicate; keep $dest")) {
                        Remove-SimsItemToRecycleBin -LiteralPath $file.FullName
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $file.FullName
                    }
                    $droppedOlder++
                    break
                }
                'DropDuplicate' {
                    if ($PSCmdlet.ShouldProcess($file.FullName, "Delete same-content duplicate; keep $dest")) {
                        Remove-SimsItemToRecycleBin -LiteralPath $file.FullName
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $file.FullName
                    }
                    $droppedDuplicate++
                    break
                }
            }
        }
        catch {
            $errors++
            Write-Warning ("{0}: {1}" -f $file.FullName, $_.Exception.Message)
        }
        finally {
            $progressProcessed++
            if (($progressProcessed % $progressStep -eq 0) -or ($progressProcessed -eq $progressTotal)) {
                Write-SimsProgress -Stage 'merge.file' -Current $progressProcessed -Total $progressTotal -Detail ([System.IO.Path]::GetFileName($src))
            }
        }
    }

    if (-not $SkipPruneEmptyDirs) {
        Remove-SimsEmptyDirectories -RootPath $src -ProtectPath $target -ShouldProcessScript { param($targetPath, $action) $PSCmdlet.ShouldProcess($targetPath, $action) }
    }
}

Write-Host ("Moved: {0} | Replaced: {1} | DroppedOlder: {2} | DroppedDuplicate: {3} | NonModKept: {4} | Errors: {5}" -f $moved, $replaced, $droppedOlder, $droppedDuplicate, $nonModKept, $errors)
Write-Host ("Target: {0}" -f $target)
