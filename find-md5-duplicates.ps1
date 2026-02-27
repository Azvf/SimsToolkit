[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter()]
    [string]$RootPath = '',

    [Parameter()]
    [switch]$ModFilesOnly,

    [Parameter()]
    [string[]]$ModExtensions = @(),

    [Parameter()]
    [ValidateRange(0, 104857600)]
    [int]$PrefixHashBytes = 0,

    [Parameter()]
    [ValidateRange(0, 64)]
    [int]$HashWorkerCount = 0,

    [Parameter()]
    [string]$OutputCsv = '',

    [Parameter()]
    [bool]$Recurse = $true,

    [Parameter()]
    [switch]$Cleanup
)

# SSOT: apply defaults from SimsConfig
. (Join-Path $PSScriptRoot 'modules\SimsConfig.ps1')
$cfg = $Script:SimsConfigDefault
if ([string]::IsNullOrEmpty($RootPath)) { $RootPath = $cfg.FindDupRootPath }
if ($ModExtensions.Count -eq 0) { $ModExtensions = $cfg.ModExtensions }
if ($PrefixHashBytes -eq 0) { $PrefixHashBytes = $cfg.PrefixHashBytes }
if ($HashWorkerCount -eq 0) { $HashWorkerCount = $cfg.HashWorkerCount }

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$coreModulePath = Join-Path $PSScriptRoot 'modules\SimsFileOpsCore.psm1'
if (-not (Test-Path -LiteralPath $coreModulePath)) {
    throw "Core module not found: $coreModulePath"
}
Import-Module -Name $coreModulePath -Force

$root = Get-SimsResolvedLiteralPath -Path $RootPath
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Root path does not exist or is not a directory: $root"
}

$modExtSet = New-SimsModExtensionSet -Extensions $ModExtensions

$getFilesParams = @{
    LiteralPath = $root
    File       = $true
}
if ($Recurse) {
    $getFilesParams['Recurse'] = $true
}
$allFiles = @(Get-ChildItem @getFilesParams | Sort-Object FullName)
if ($ModFilesOnly) {
    $allFiles = @($allFiles | Where-Object { $modExtSet.Contains($_.Extension) })
}

$totalFiles = $allFiles.Count
Write-SimsProgress -Stage 'finddup.collect' -Current $totalFiles -Total $totalFiles -Detail 'files collected'

if ($totalFiles -eq 0) {
    Write-Host "No files found under: $root"
    return
}

$byLength = $allFiles | Group-Object -Property Length | Where-Object { $_.Count -ge 2 }

if ($byLength.Count -eq 0) {
    Write-Host "No duplicate candidates found (no same-length file pairs). Total files: $totalFiles"
    return
}

$hashCache = New-SimsHashCache
# Prefix hashes computed on-demand via Get-SimsFilePrefixMd5 (avoids Initialize-SimsPrefixHashCacheParallel parameter binding issues)

$duplicateGroups = New-Object System.Collections.Generic.List[object]
$groupIndex = 0
$lengthGroupsTotal = $byLength.Count
$lengthGroupCurrent = 0

foreach ($lengthGroup in $byLength) {
    $lengthGroupCurrent++
    Write-SimsProgress -Stage 'finddup.analyze' -Current $lengthGroupCurrent -Total $lengthGroupsTotal -Detail "length $($lengthGroup.Name)"

    $filesInGroup = @($lengthGroup.Group)
    $prefixGroups = @{}

    foreach ($f in $filesInGroup) {
        $prefixKey = Get-SimsHashCacheKey -Path $f.FullName -Length $f.Length -LastWriteTicks $f.LastWriteTimeUtc.Ticks -PrefixBytes $PrefixHashBytes
        $prefixHash = $hashCache.PrefixCache[$prefixKey]
        if ($null -eq $prefixHash) {
            $prefixHash = Get-SimsFilePrefixMd5 -HashCache $hashCache -Path $f.FullName -PrefixBytes $PrefixHashBytes -CacheKey $prefixKey
        }

        $key = "$($f.Length)|$prefixHash"
        if (-not $prefixGroups.ContainsKey($key)) {
            $prefixGroups[$key] = New-Object System.Collections.Generic.List[System.IO.FileInfo]
        }
        [void]$prefixGroups[$key].Add($f)
    }

    foreach ($kv in $prefixGroups.GetEnumerator()) {
        $list = $kv.Value
        if ($list.Count -lt 2) { continue }

        $fullHashGroups = @{}
        foreach ($f in $list) {
            $fullKey = Get-SimsHashCacheKey -Path $f.FullName -Length $f.Length -LastWriteTicks $f.LastWriteTimeUtc.Ticks
            $fullHash = Get-SimsFileMd5 -HashCache $hashCache -Path $f.FullName -CacheKey $fullKey

            if (-not $fullHashGroups.ContainsKey($fullHash)) {
                $fullHashGroups[$fullHash] = New-Object System.Collections.Generic.List[System.IO.FileInfo]
            }
            [void]$fullHashGroups[$fullHash].Add($f)
        }

        foreach ($fhkv in $fullHashGroups.GetEnumerator()) {
            $dupList = $fhkv.Value
            if ($dupList.Count -ge 2) {
                $groupIndex++
                $duplicateGroups.Add([pscustomobject]@{
                        GroupId     = $groupIndex
                        Md5Hash    = $fhkv.Key
                        FileCount  = $dupList.Count
                        FileSize   = $dupList[0].Length
                        FilePaths  = ($dupList | ForEach-Object { $_.FullName })
                    })
            }
        }
    }
}

$dupGroupCount = $duplicateGroups.Count
$dupFileCount = ($duplicateGroups | ForEach-Object { $_.FileCount } | Measure-Object -Sum).Sum
$wastedBytes = ($duplicateGroups | ForEach-Object { $_.FileSize * ($_.FileCount - 1) } | Measure-Object -Sum).Sum

Write-Host ''
Write-Host ("Root: {0}" -f $root)
Write-Host ("Total files scanned: {0}" -f $totalFiles)
Write-Host ("Duplicate groups: {0}" -f $dupGroupCount)
Write-Host ("Duplicate file instances: {1} (could remove {0} copies)" -f ($dupFileCount - $dupGroupCount), $dupFileCount)
Write-Host ("Wasted space: {0:N2} MB" -f ($wastedBytes / 1MB))
Write-Host ''

if ($dupGroupCount -eq 0) {
    Write-Host "No MD5 duplicates found."
    return
}

if ($Cleanup) {
    $removedCount = 0
    $removedBytes = 0L
    foreach ($g in $duplicateGroups) {
        $paths = @($g.FilePaths | Sort-Object)
        $keepPath = $paths[0]
        $toRemove = $paths[1..($paths.Count - 1)]
        foreach ($path in $toRemove) {
            if (-not [System.IO.File]::Exists($path)) { continue }
            $fileSize = (Get-Item -LiteralPath $path -ErrorAction SilentlyContinue).Length
            if ($PSCmdlet.ShouldProcess($path, "Remove duplicate (keep: $keepPath)")) {
                try {
                    Remove-SimsItemToRecycleBin -LiteralPath $path
                    $removedCount++
                    $removedBytes += $fileSize
                }
                catch {
                    Write-Warning ("Failed to remove: {0} - {1}" -f $path, $_.Exception.Message)
                }
            }
        }
    }
    Write-Host ''
    Write-Host ("Cleanup: removed {0} duplicate file(s), freed {1:N2} MB" -f $removedCount, ($removedBytes / 1MB))
    Write-Host ''
}

$rows = $duplicateGroups | ForEach-Object {
    $g = $_
    $g.FilePaths | ForEach-Object -Begin { $i = 0 } -Process {
        $i++
        $fi = Get-Item -LiteralPath $_ -ErrorAction SilentlyContinue
        [pscustomobject]@{
            GroupId      = $g.GroupId
            Md5Hash     = $g.Md5Hash
            FileIndex   = $i
            FileCount   = $g.FileCount
            FileSize    = $g.FileSize
            FilePath    = $_
            LastWrite   = if ($fi) { $fi.LastWriteTimeUtc.ToString('yyyy-MM-dd HH:mm:ss') } else { '(deleted)' }
        }
    }
}

$rows | Format-Table -Property GroupId, Md5Hash, FileIndex, FileCount, FileSize, FilePath -AutoSize

if (-not [string]::IsNullOrWhiteSpace($OutputCsv)) {
    $csvDir = Split-Path -Parent $OutputCsv
    if (-not [string]::IsNullOrWhiteSpace($csvDir) -and -not (Test-Path -LiteralPath $csvDir)) {
        [System.IO.Directory]::CreateDirectory($csvDir) | Out-Null
    }
    $rows | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
    Write-Host ("Exported to: {0}" -f $OutputCsv)
}
