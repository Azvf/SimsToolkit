[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter()]
    [string]$RootPath = '',

    [Parameter()]
    [switch]$FlattenToRoot,

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
if ([string]::IsNullOrEmpty($RootPath)) { $RootPath = $cfg.FlattenRootPath }
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

$root = Get-SimsResolvedLiteralPath -Path $RootPath
$topLevelDirs = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name)

$report = New-Object System.Collections.Generic.List[object]
$modExtSet = New-SimsModExtensionSet -Extensions $ModExtensions
$workItems = New-Object System.Collections.Generic.List[object]
if ($FlattenToRoot) {
    $workItems.Add([pscustomobject]@{
            ScopePath = $root
            DestinationPath = $root
            FolderName = '[root]'
        })
}
else {
    if ($topLevelDirs.Count -eq 0) {
        Write-Host "No subfolders found under: $root"
        return
    }

    foreach ($top in $topLevelDirs) {
        $workItems.Add([pscustomobject]@{
                ScopePath = $top.FullName
                DestinationPath = $top.FullName
                FolderName = $top.Name
            })
    }
}

$folderTotal = $workItems.Count
$folderProcessed = 0
Write-SimsProgress -Stage 'flatten.folder' -Current 0 -Total $folderTotal -Detail 'start'

foreach ($work in $workItems) {
    $moved = 0
    $replaced = 0
    $droppedDuplicate = 0
    $droppedOlder = 0
    $alreadyFlat = 0
    $nonModKept = 0
    $errors = 0

    $scopePath = $work.ScopePath
    $destinationPath = $work.DestinationPath
    $files = @(Get-ChildItem -LiteralPath $scopePath -Recurse -File | Sort-Object FullName)
    if ($VerifyContentOnNameConflict) {
        $candidateFiles = @($files | Where-Object {
                $_.DirectoryName -ne $destinationPath -and ((-not $ModFilesOnly) -or $modExtSet.Contains($_.Extension))
            })
        $topFilesForConflict = @($files | Where-Object {
                $_.DirectoryName -eq $destinationPath -and ((-not $ModFilesOnly) -or $modExtSet.Contains($_.Extension))
            })

        $nameUniverse = @($candidateFiles + $topFilesForConflict)
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

    foreach ($file in $files) {
        try {
            if ($ModFilesOnly -and (-not $modExtSet.Contains($file.Extension))) {
                $nonModKept++
                continue
            }

            if ($file.DirectoryName -eq $destinationPath) {
                $alreadyFlat++
                continue
            }

            $destination = Join-Path $destinationPath $file.Name
            $action = Resolve-SimsConflictAction -SourceFile $file -DestinationPath $destination -VerifyContent:$VerifyContentOnNameConflict -PrefixBytes $PrefixHashBytes -HashCache $hashCache

            switch ($action) {
                'Move' {
                    if ($PSCmdlet.ShouldProcess($file.FullName, "Move to $destination")) {
                        Move-Item -LiteralPath $file.FullName -Destination $destination
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $file.FullName
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $destination
                    }
                    $moved++
                    break
                }
                'Replace' {
                    if ($PSCmdlet.ShouldProcess($file.FullName, "Replace older file at $destination")) {
                        Move-Item -LiteralPath $file.FullName -Destination $destination -Force
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $file.FullName
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $destination
                    }
                    $replaced++
                    break
                }
                'DropOlder' {
                    if ($PSCmdlet.ShouldProcess($file.FullName, "Delete older/equal duplicate; keep $destination")) {
                        Remove-SimsItemToRecycleBin -LiteralPath $file.FullName
                        Remove-SimsHashCacheByPath -HashCache $hashCache -Path $file.FullName
                    }
                    $droppedOlder++
                    break
                }
                'DropDuplicate' {
                    if ($PSCmdlet.ShouldProcess($file.FullName, "Delete same-content duplicate; keep $destination")) {
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
    }

    if (-not $SkipPruneEmptyDirs) {
        Remove-SimsEmptyDirectories -RootPath $scopePath -ShouldProcessScript { param($target, $action) $PSCmdlet.ShouldProcess($target, $action) }
    }

    $report.Add([pscustomobject]@{
        Folder = $work.FolderName
        Moved = $moved
        Replaced = $replaced
        DroppedOlder = $droppedOlder
        DroppedDuplicate = $droppedDuplicate
        AlreadyFlat = $alreadyFlat
        NonModKept = $nonModKept
        Errors = $errors
    })

    $folderProcessed++
    Write-SimsProgress -Stage 'flatten.folder' -Current $folderProcessed -Total $folderTotal -Detail $work.FolderName
}

$report | Sort-Object Folder | Format-Table Folder, Moved, Replaced, DroppedOlder, DroppedDuplicate, AlreadyFlat, NonModKept, Errors -AutoSize

$totalMoved = @($report | Measure-Object -Property Moved -Sum).Sum
$totalReplaced = @($report | Measure-Object -Property Replaced -Sum).Sum
$totalDropped = @($report | Measure-Object -Property DroppedOlder -Sum).Sum
$totalDroppedDuplicate = @($report | Measure-Object -Property DroppedDuplicate -Sum).Sum
$totalFlat = @($report | Measure-Object -Property AlreadyFlat -Sum).Sum
$totalNonModKept = @($report | Measure-Object -Property NonModKept -Sum).Sum
$totalErrors = @($report | Measure-Object -Property Errors -Sum).Sum

Write-Host ''
Write-Host ("Moved: {0} | Replaced: {1} | DroppedOlder: {2} | DroppedDuplicate: {3} | AlreadyFlat: {4} | NonModKept: {5} | Errors: {6}" -f $totalMoved, $totalReplaced, $totalDropped, $totalDroppedDuplicate, $totalFlat, $totalNonModKept, $totalErrors)
Write-Host ("Root: {0}" -f $root)
