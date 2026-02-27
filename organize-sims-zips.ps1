[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter()]
    [string]$SourceDir = (Get-Location).Path,

    [Parameter()]
    [string]$ZipNamePattern = '*',

    [Parameter()]
    [string[]]$ArchiveExtensions = @(),

    [Parameter()]
    [string]$ModsRoot = '',

    [Parameter()]
    [AllowEmptyString()]
    [string]$UnifiedModsFolder = '',

    [Parameter()]
    [string]$TrayRoot = '',

    [Parameter()]
    [switch]$KeepZip,

    [Parameter()]
    [bool]$RecurseSource = $true,

    [Parameter()]
    [bool]$IncludeLooseSources = $true,

    [Parameter()]
    [string[]]$ModExtensions = @(),

    [Parameter()]
    [switch]$VerifyContentOnNameConflict,

    [Parameter()]
    [ValidateRange(0, 104857600)]
    [int]$PrefixHashBytes = 0
)

# SSOT: apply defaults from SimsConfig
. (Join-Path $PSScriptRoot 'modules\SimsConfig.ps1')
$cfg = $Script:SimsConfigDefault
if ($ArchiveExtensions.Count -eq 0) { $ArchiveExtensions = $cfg.ArchiveExtensions }
if ([string]::IsNullOrEmpty($ModsRoot)) { $ModsRoot = $cfg.ModsRoot }
if ([string]::IsNullOrEmpty($TrayRoot)) { $TrayRoot = $cfg.TrayRoot }
if ($ModExtensions.Count -eq 0) { $ModExtensions = $cfg.ModExtensions }
if ($PrefixHashBytes -eq 0) { $PrefixHashBytes = $cfg.PrefixHashBytes }

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$coreModulePath = Join-Path $PSScriptRoot 'modules\SimsFileOpsCore.psm1'
if (-not (Test-Path -LiteralPath $coreModulePath)) {
    throw "Core module not found: $coreModulePath"
}
Import-Module -Name $coreModulePath -Force

$trayExtensions = @(
    '.trayitem',
    '.householdbinary',
    '.hhi',
    '.sgi',
    '.blueprint',
    '.bpi',
    '.bp'
)

$script:SimsReservedFileNameSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($name in @('CON', 'PRN', 'AUX', 'NUL')) {
    [void]$script:SimsReservedFileNameSet.Add($name)
}
for ($i = 1; $i -le 9; $i++) {
    [void]$script:SimsReservedFileNameSet.Add(("COM{0}" -f $i))
    [void]$script:SimsReservedFileNameSet.Add(("LPT{0}" -f $i))
}

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ($Path -replace '\\', '/').TrimStart('/')
}

function Test-SkipEntry {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $lower = $RelativePath.ToLowerInvariant()
    if ($lower.StartsWith('__macosx/')) { return $true }

    $leaf = [System.IO.Path]::GetFileName($RelativePath)
    if ([string]::IsNullOrWhiteSpace($leaf)) { return $true }
    if ($leaf.StartsWith('._')) { return $true }

    return $false
}

function Test-SafeRelativePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $normalized = ($RelativePath -replace '/', '\').Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) { return $false }
    if ($normalized.StartsWith('\')) { return $false }

    $parts = $normalized -split '\\'
    foreach ($p in $parts) {
        if ($p -eq '.' -or $p -eq '..') { return $false }
    }

    return $true
}

function ConvertTo-SimsSafePathSegment {
    param([Parameter(Mandatory = $true)][string]$Segment)

    $trimmed = $Segment.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return '_'
    }

    $invalidChars = [System.IO.Path]::GetInvalidFileNameChars()
    $builder = New-Object System.Text.StringBuilder
    foreach ($ch in $trimmed.ToCharArray()) {
        if (($invalidChars -contains $ch) -or ([int][char]$ch -lt 32)) {
            [void]$builder.Append('_')
        }
        else {
            [void]$builder.Append($ch)
        }
    }

    $safe = $builder.ToString().TrimEnd(' ', '.')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        $safe = '_'
    }

    if ($script:SimsReservedFileNameSet.Contains($safe)) {
        $safe = "_$safe"
    }

    return $safe
}

function ConvertTo-SimsSafeRelativePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $normalized = ($RelativePath -replace '/', '\').Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return [pscustomobject]@{
            SafePath = ''
            Changed = $false
        }
    }

    $parts = @($normalized -split '\\' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($parts.Count -eq 0) {
        return [pscustomobject]@{
            SafePath = ''
            Changed = $false
        }
    }

    $safeParts = New-Object 'System.Collections.Generic.List[string]'
    $changed = $false
    foreach ($part in $parts) {
        $safePart = ConvertTo-SimsSafePathSegment -Segment $part
        if ($safePart -ne $part) {
            $changed = $true
        }
        $safeParts.Add($safePart) | Out-Null
    }

    return [pscustomobject]@{
        SafePath = ($safeParts -join '\')
        Changed = $changed
    }
}

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($PSCmdlet.ShouldProcess($Path, 'Create directory')) {
            [System.IO.Directory]::CreateDirectory($Path) | Out-Null
        }
    }
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $baseFull = [System.IO.Path]::GetFullPath($BasePath)
    $targetFull = [System.IO.Path]::GetFullPath($TargetPath)

    if (-not $baseFull.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString())) {
        $baseFull = $baseFull + [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [System.Uri]$baseFull
    $targetUri = [System.Uri]$targetFull
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    $relative = [System.Uri]::UnescapeDataString($relativeUri.ToString())
    return ($relative -replace '/', '\')
}

function Get-NormalizedArchiveExtensions {
    param([Parameter(Mandatory = $true)][string[]]$Extensions)

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ext in $Extensions) {
        if ([string]::IsNullOrWhiteSpace($ext)) { continue }

        $normalized = $ext.Trim().ToLowerInvariant()
        if (-not $normalized.StartsWith('.')) {
            $normalized = ".$normalized"
        }

        $set.Add($normalized) | Out-Null
    }

    return @($set)
}

function Get-NormalizedModExtensions {
    param([Parameter(Mandatory = $true)][string[]]$Extensions)

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ext in $Extensions) {
        if ([string]::IsNullOrWhiteSpace($ext)) { continue }

        $normalized = $ext.Trim().ToLowerInvariant()
        if (-not $normalized.StartsWith('.')) {
            $normalized = ".$normalized"
        }

        $set.Add($normalized) | Out-Null
    }

    if ($set.Count -eq 0) {
        $set.Add('.package') | Out-Null
        $set.Add('.ts4script') | Out-Null
    }

    return @($set)
}

function Get-LooseSourceItems {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string[]]$ArchiveExtensions
    )

    $items = New-Object System.Collections.Generic.List[object]
    $topLevel = @(Get-ChildItem -LiteralPath $SourceDir -Force | Sort-Object Name)
    foreach ($item in $topLevel) {
        if ($item.PSIsContainer) {
            $items.Add([pscustomobject]@{
                    SourceType = 'LooseDirectory'
                    SourcePath = $item.FullName
                    SourceName = $item.Name
                })
            continue
        }

        if ($ArchiveExtensions -contains $item.Extension.ToLowerInvariant()) {
            continue
        }

        $items.Add([pscustomobject]@{
                SourceType = 'LooseFile'
                SourcePath = $item.FullName
                SourceName = $item.Name
            })
    }

    return $items.ToArray()
}

function Get-ArchiveSourceFiles {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$ZipNamePattern,
        [Parameter(Mandatory = $true)][string[]]$ArchiveExtensions,
        [Parameter(Mandatory = $true)][bool]$Recurse
    )

    $enumerateArgs = @{
        LiteralPath = $SourceDir
        File = $true
        Filter = $ZipNamePattern
        ErrorAction = 'Stop'
    }
    if ($Recurse) {
        $enumerateArgs['Recurse'] = $true
    }

    $files = @()
    try {
        $files = @(
            Get-ChildItem @enumerateArgs |
                Where-Object { $ArchiveExtensions -contains $_.Extension.ToLowerInvariant() } |
                Sort-Object FullName -Unique
        )
    }
    catch {
        throw "Failed to enumerate archive files from '$SourceDir': $($_.Exception.Message)"
    }

    return $files
}

function Get-SafeModsFolderName {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Fallback
    )

    $fallbackSafe = ConvertTo-SimsSafePathSegment -Segment $Fallback
    if ([string]::IsNullOrWhiteSpace($Name)) {
        return $fallbackSafe
    }

    $candidate = ConvertTo-SimsSafePathSegment -Segment $Name
    if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate -eq '_') {
        return $fallbackSafe
    }

    return $candidate
}

function Get-ArchiveFolderName {
    param([Parameter(Mandatory = $true)][string]$ArchiveFileName)

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($ArchiveFileName).Trim()
    if ([string]::IsNullOrWhiteSpace($baseName)) {
        return 'archive'
    }

    return $baseName.TrimEnd([char]'.')
}

function Get-TopFolderToStrip {
    param([Parameter(Mandatory = $true)][object[]]$FileRecords)

    if (@($FileRecords).Count -eq 0) { return $null }

    $first = $null
    foreach ($r in $FileRecords) {
        $parts = $r.RelativePath -split '/'
        if (@($parts).Count -lt 2) { return $null }

        if ($null -eq $first) {
            $first = $parts[0]
            continue
        }

        if ($parts[0] -ne $first) { return $null }
    }

    return $first
}

function Get-EntryRecords {
    param([Parameter(Mandatory = $true)][object]$Archive)

    $records = New-Object System.Collections.Generic.List[object]
    try {
        foreach ($entry in $Archive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }

            $relativePath = Get-NormalizedPath -Path $entry.FullName
            if (Test-SkipEntry -RelativePath $relativePath) { continue }

            $ext = [System.IO.Path]::GetExtension($entry.Name).ToLowerInvariant()
            $parts = $relativePath -split '/'
            $modsIndex = -1
            for ($i = 0; $i -lt $parts.Count; $i++) {
                if ($parts[$i].ToLowerInvariant() -eq 'mods') {
                    $modsIndex = $i
                    break
                }
            }

            $records.Add([pscustomobject]@{
                Entry = $entry
                RelativePath = $relativePath
                Extension = $ext
                IsTray = ($trayExtensions -contains $ext)
                ModsIndex = $modsIndex
            })
        }
    }
    catch {
        throw "Get-EntryRecords failed: $($_.Exception.Message) (line $($_.InvocationInfo.ScriptLineNumber))"
    }

    return $records
}

function Get-ModRecords {
    param(
        [Parameter(Mandatory = $true)][object[]]$Records,
        [Parameter(Mandatory = $true)][string]$ZipName,
        [Parameter(Mandatory = $true)][string[]]$LikelyModExtensions
    )

    $modsRecords = New-Object System.Collections.Generic.List[object]
    foreach ($r in $Records) {
        $splitPath = $r.RelativePath -split '/'
        if ($r.ModsIndex -lt 0 -or $splitPath.Count -le ($r.ModsIndex + 1)) { continue }

        $tail = $splitPath[(($r.ModsIndex + 1))..($splitPath.Count - 1)] -join '/'
        if ([string]::IsNullOrWhiteSpace($tail)) { continue }

        $modsRecords.Add([pscustomobject]@{
            Entry = $r.Entry
            RelativePath = $tail
        })
    }

    if ($modsRecords.Count -gt 0) {
        return $modsRecords
    }

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($ZipName)
    $isNamedModsZip = $baseName -match '_Mods$'
    $hasLikelyModFile = @($Records | Where-Object { $LikelyModExtensions -contains $_.Extension }).Count -gt 0

    if (-not ($isNamedModsZip -or $hasLikelyModFile)) {
        return @()
    }

    $fallback = @($Records | Where-Object { -not $_.IsTray -and ($LikelyModExtensions -contains $_.Extension) })
    if ($fallback.Count -eq 0) { return @() }

    $strip = Get-TopFolderToStrip -FileRecords $fallback
    foreach ($f in $fallback) {
        $rel = $f.RelativePath
        if ($strip -and $rel.StartsWith("$strip/")) {
            $rel = $rel.Substring($strip.Length + 1)
        }
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }

        $modsRecords.Add([pscustomobject]@{
            Entry = $f.Entry
            RelativePath = $rel
        })
    }

    return $modsRecords
}

function Get-TrayRecords {
    param([Parameter(Mandatory = $true)][object[]]$Records)
    return @($Records | Where-Object { $_.IsTray } | ForEach-Object {
            [pscustomobject]@{
                Entry = $_.Entry
                RelativePath = [System.IO.Path]::GetFileName($_.RelativePath)
            }
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_.RelativePath) })
}

function Get-EntryTimestamp {
    param([Parameter(Mandatory = $true)][object]$Entry)
    return [DateTime]$Entry.LastWriteTime.LocalDateTime
}

function Resolve-DateBasedAction {
    param(
        [Parameter(Mandatory = $true)][object]$Entry,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $existing = Get-Item -LiteralPath $DestinationPath -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        return [pscustomobject]@{
            Action = 'Write'
            Reason = 'NewFile'
        }
    }

    $srcTime = Get-EntryTimestamp -Entry $Entry
    $dstTime = $existing.LastWriteTime
    if ($srcTime -gt $dstTime) {
        return [pscustomobject]@{
            Action = 'Replace'
            Reason = 'SourceNewer'
        }
    }

    return [pscustomobject]@{
        Action = 'Skip'
        Reason = 'TargetNewerOrEqual'
    }
}

function Write-ZipEntryToFile {
    param(
        [Parameter(Mandatory = $true)][object]$Entry,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $parent = [System.IO.Path]::GetDirectoryName($DestinationPath)
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($Entry, $DestinationPath, $true)
    [System.IO.File]::SetLastWriteTime($DestinationPath, (Get-EntryTimestamp -Entry $Entry))
}

function Get-7ZipExecutable {
    $candidates = @(
        'C:\Program Files\7-Zip\7z.exe',
        'C:\Program Files\7-Zip\7zz.exe',
        '7z.exe',
        '7z',
        '7za.exe',
        'C:\Program Files (x86)\7-Zip\7z.exe'
    )

    foreach ($candidate in $candidates) {
        try {
            $command = Get-Command -Name $candidate -ErrorAction Stop
            return $command.Source
        }
        catch {
            if ([System.IO.File]::Exists($candidate)) {
                return $candidate
            }
        }
    }

    throw "7-Zip executable not found. Install 7-Zip and ensure '7z.exe' is available."
}

function Expand-ArchiveWith7Zip {
    param(
        [Parameter(Mandatory = $true)][string]$SevenZipExecutable,
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    [System.IO.Directory]::CreateDirectory($DestinationDir) | Out-Null

    & $SevenZipExecutable 'x' '-y' ("-o{0}" -f $DestinationDir) $ArchivePath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed to extract '$ArchivePath' with exit code $LASTEXITCODE."
    }
}

function Get-FileRecordsFromExtractRoot {
    param(
        [Parameter(Mandatory = $true)][string]$ExtractRoot,
        [Parameter()][string[]]$IgnoredExtensions = @()
    )

    $ignoredExtSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ext in $IgnoredExtensions) {
        if ([string]::IsNullOrWhiteSpace($ext)) { continue }
        $normalized = $ext.Trim().ToLowerInvariant()
        if (-not $normalized.StartsWith('.')) {
            $normalized = ".$normalized"
        }
        [void]$ignoredExtSet.Add($normalized)
    }

    $records = New-Object System.Collections.Generic.List[object]
    $files = @()
    try {
        $files = @(Get-ChildItem -LiteralPath $ExtractRoot -Recurse -File -ErrorAction Stop | Sort-Object FullName)
    }
    catch {
        throw "Failed to enumerate files from '$ExtractRoot': $($_.Exception.Message)"
    }

    foreach ($file in $files) {
        $relativePath = Get-NormalizedPath -Path (Get-RelativePathCompat -BasePath $ExtractRoot -TargetPath $file.FullName)
        if (Test-SkipEntry -RelativePath $relativePath) { continue }
        if (-not (Test-SafeRelativePath -RelativePath $relativePath)) { continue }

        $ext = $file.Extension.ToLowerInvariant()
        if ($ignoredExtSet.Contains($ext)) { continue }
        $parts = $relativePath -split '/'
        $modsIndex = -1
        for ($i = 0; $i -lt $parts.Count; $i++) {
            if ($parts[$i].ToLowerInvariant() -eq 'mods') {
                $modsIndex = $i
                break
            }
        }

        $records.Add([pscustomobject]@{
            SourcePath = $file.FullName
            RelativePath = $relativePath
            Extension = $ext
            IsTray = ($trayExtensions -contains $ext)
            ModsIndex = $modsIndex
        })
    }

    return $records
}

function Get-ModSourceRecords {
    param(
        [Parameter(Mandatory = $true)][object[]]$Records,
        [Parameter(Mandatory = $true)][string]$ArchiveName,
        [Parameter(Mandatory = $true)][string[]]$LikelyModExtensions
    )

    $modsRecords = New-Object System.Collections.Generic.List[object]
    foreach ($r in $Records) {
        $splitPath = $r.RelativePath -split '/'
        if ($r.ModsIndex -lt 0 -or $splitPath.Count -le ($r.ModsIndex + 1)) { continue }

        $tail = $splitPath[(($r.ModsIndex + 1))..($splitPath.Count - 1)] -join '/'
        if ([string]::IsNullOrWhiteSpace($tail)) { continue }

        $modsRecords.Add([pscustomobject]@{
            SourcePath = $r.SourcePath
            RelativePath = $tail
        })
    }

    if ($modsRecords.Count -gt 0) {
        return $modsRecords
    }

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($ArchiveName)
    $isNamedModsArchive = $baseName -match '_Mods$'
    $hasLikelyModFile = @($Records | Where-Object { $LikelyModExtensions -contains $_.Extension }).Count -gt 0
    if (-not ($isNamedModsArchive -or $hasLikelyModFile)) {
        return @()
    }

    $fallback = @($Records | Where-Object { -not $_.IsTray -and ($LikelyModExtensions -contains $_.Extension) })
    if ($fallback.Count -eq 0) { return @() }

    $strip = Get-TopFolderToStrip -FileRecords $fallback
    foreach ($f in $fallback) {
        $rel = $f.RelativePath
        if ($strip -and $rel.StartsWith("$strip/")) {
            $rel = $rel.Substring($strip.Length + 1)
        }
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }

        $modsRecords.Add([pscustomobject]@{
            SourcePath = $f.SourcePath
            RelativePath = $rel
        })
    }

    return $modsRecords
}

function Get-TraySourceRecords {
    param([Parameter(Mandatory = $true)][object[]]$Records)
    return @($Records | Where-Object { $_.IsTray } | ForEach-Object {
            [pscustomobject]@{
                SourcePath = $_.SourcePath
                RelativePath = [System.IO.Path]::GetFileName($_.RelativePath)
            }
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_.RelativePath) })
}

function Resolve-DateBasedActionForFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter()][bool]$VerifyContent = $false,
        [Parameter()][int]$PrefixBytes = 102400,
        [Parameter()][object]$HashCache = $null
    )

    $source = Get-Item -LiteralPath $SourcePath -ErrorAction Stop
    $existing = Get-Item -LiteralPath $DestinationPath -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        return [pscustomobject]@{
            Action = 'Write'
            Reason = 'NewFile'
        }
    }

    if ($VerifyContent -and $null -ne $HashCache) {
        try {
            if (Test-SimsFastContentEqual -HashCache $HashCache -PathA $SourcePath -PathB $DestinationPath -PrefixBytes $PrefixBytes) {
                return [pscustomobject]@{
                    Action = 'Skip'
                    Reason = 'SameContent'
                }
            }
        }
        catch {
            # Fallback to timestamp-based resolution when hashing is unavailable.
        }
    }

    if ($source.LastWriteTime -gt $existing.LastWriteTime) {
        return [pscustomobject]@{
            Action = 'Replace'
            Reason = 'SourceNewer'
        }
    }

    return [pscustomobject]@{
        Action = 'Skip'
        Reason = 'TargetNewerOrEqual'
    }
}

function Copy-SourceFileToDestination {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $parent = [System.IO.Path]::GetDirectoryName($DestinationPath)
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
    $src = Get-Item -LiteralPath $SourcePath -ErrorAction Stop
    [System.IO.File]::SetLastWriteTime($DestinationPath, $src.LastWriteTime)
}

function Add-OrganizeReportRow {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Report,
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$SourceType,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][int]$ModsWritten,
        [Parameter(Mandatory = $true)][int]$ModsReplaced,
        [Parameter(Mandatory = $true)][int]$ModsSkippedByDate,
        [Parameter(Mandatory = $true)][int]$TrayWritten,
        [Parameter(Mandatory = $true)][int]$TrayReplaced,
        [Parameter(Mandatory = $true)][int]$TraySkippedByDate,
        [Parameter(Mandatory = $true)][bool]$SourceDeleted,
        [Parameter()][string]$Notes = ''
    )

    $Report.Add([pscustomobject]@{
            Source = $Source
            SourceType = $SourceType
            Status = $Status
            ModsWritten = $ModsWritten
            ModsReplaced = $ModsReplaced
            ModsSkippedByDate = $ModsSkippedByDate
            TrayWritten = $TrayWritten
            TrayReplaced = $TrayReplaced
            TraySkippedByDate = $TraySkippedByDate
            SourceDeleted = $SourceDeleted
            Notes = $Notes
        })
}

function Add-SimsSanitizedPathRecord {
    param(
        [Parameter(Mandatory = $true)][string]$SourceLabel,
        [Parameter(Mandatory = $true)][string]$OriginalPath,
        [Parameter(Mandatory = $true)][string]$SafePath
    )

    $script:sanitizedPathCount++
    if ($script:sanitizedPathSamples.Count -lt 30) {
        $script:sanitizedPathSamples.Add(("{0}::{1} -> {2}" -f $SourceLabel, $OriginalPath, $SafePath)) | Out-Null
    }
}

$useUnifiedModsFolder = -not [string]::IsNullOrWhiteSpace($UnifiedModsFolder)
$modsTargetRoot = $ModsRoot
$modsTargetUnified = if ($useUnifiedModsFolder) { Join-Path $ModsRoot $UnifiedModsFolder } else { $null }

$normalizedArchiveExtensions = @(Get-NormalizedArchiveExtensions -Extensions $ArchiveExtensions)
if ($normalizedArchiveExtensions.Count -eq 0) {
    throw "ArchiveExtensions cannot be empty."
}
$normalizedModExtensions = @(Get-NormalizedModExtensions -Extensions $ModExtensions)
if ($normalizedModExtensions.Count -eq 0) {
    throw "ModExtensions cannot be empty."
}

$archiveFiles = @(Get-ArchiveSourceFiles -SourceDir $SourceDir -ZipNamePattern $ZipNamePattern -ArchiveExtensions $normalizedArchiveExtensions -Recurse:$RecurseSource)

$sourceItems = New-Object System.Collections.Generic.List[object]
foreach ($archiveFile in $archiveFiles) {
    $sourceItems.Add([pscustomobject]@{
            SourceType = 'Archive'
            SourcePath = $archiveFile.FullName
            SourceName = $archiveFile.Name
        })
}

if ($IncludeLooseSources) {
    $looseItems = @(Get-LooseSourceItems -SourceDir $SourceDir -ArchiveExtensions $normalizedArchiveExtensions)
    foreach ($loose in $looseItems) {
        $sourceItems.Add($loose)
    }
}

if ($sourceItems.Count -eq 0) {
    Write-Host ("No source items found under: {0} (pattern: {1}, archiveExtensions: {2}, includeLoose: {3})" -f $SourceDir, $ZipNamePattern, ($normalizedArchiveExtensions -join ', '), $IncludeLooseSources)
    return
}

if ($useUnifiedModsFolder) {
    Ensure-Directory -Path $modsTargetUnified
}
else {
    Ensure-Directory -Path $modsTargetRoot
}
Ensure-Directory -Path $TrayRoot

$report = New-Object System.Collections.Generic.List[object]
$ts4scriptDetectedCount = 0
$ts4scriptDetectedSamples = New-Object 'System.Collections.Generic.List[string]'
$script:sanitizedPathCount = 0
$script:sanitizedPathSamples = New-Object 'System.Collections.Generic.List[string]'
$sourceTotal = $sourceItems.Count
$sourceProcessed = 0
Write-SimsProgress -Stage 'organize.source' -Current 0 -Total $sourceTotal -Detail 'start'

$needsSevenZip = @($archiveFiles | Where-Object { $_.Extension.ToLowerInvariant() -in @('.rar', '.7z') }).Count -gt 0
$sevenZipExecutable = $null
if ($needsSevenZip) {
    $sevenZipExecutable = Get-7ZipExecutable
}

if ($VerifyContentOnNameConflict) {
    Write-Verbose ("Content verification enabled for file-based sources. PrefixHashBytes={0}" -f $PrefixHashBytes)
}
$hashCache = New-SimsHashCache

foreach ($sourceItem in $sourceItems) {
    $modsWritten = 0
    $modsReplaced = 0
    $modsSkippedByDate = 0
    $trayWritten = 0
    $trayReplaced = 0
    $traySkippedByDate = 0
    $sourceDeleted = $false
    $status = 'Skipped'
    $notes = ''
    $deleteSourceAfter = $false
    $sourceType = [string]$sourceItem.SourceType
    $sourcePath = [string]$sourceItem.SourcePath
    $sourceName = [string]$sourceItem.SourceName
    $sourceLabel = ("{0}:{1}" -f $sourceType, $sourceName)
    $ext = [System.IO.Path]::GetExtension($sourcePath).ToLowerInvariant()
    $tempExtractRoot = $null
    $archive = $null
    $modsTargetForSource = if ($useUnifiedModsFolder) {
        $modsTargetUnified
    }
    else {
        switch ($sourceType) {
            'Archive' {
                Join-Path $modsTargetRoot (Get-ArchiveFolderName -ArchiveFileName $sourceName)
                break
            }
            'LooseDirectory' {
                Join-Path $modsTargetRoot (Get-SafeModsFolderName -Name $sourceName -Fallback 'LooseDirectory')
                break
            }
            'LooseFile' {
                $looseFolder = Get-SafeModsFolderName -Name ([System.IO.Path]::GetFileNameWithoutExtension($sourceName)) -Fallback 'LooseFile'
                Join-Path $modsTargetRoot $looseFolder
                break
            }
            default {
                Join-Path $modsTargetRoot (Get-SafeModsFolderName -Name $sourceName -Fallback 'source')
            }
        }
    }

    try {
        $records = @()
        $modsRecords = @()
        $trayRecords = @()

        if ($sourceType -eq 'Archive') {
            if ($ext -eq '.zip') {
                $archive = [System.IO.Compression.ZipFile]::OpenRead($sourcePath)
                $records = @(Get-EntryRecords -Archive $archive)
                if ($records.Count -eq 0) {
                    $notes = 'No usable file entries in archive'
                    Add-OrganizeReportRow -Report $report -Source $sourceName -SourceType $sourceType -Status $status -ModsWritten $modsWritten -ModsReplaced $modsReplaced -ModsSkippedByDate $modsSkippedByDate -TrayWritten $trayWritten -TrayReplaced $trayReplaced -TraySkippedByDate $traySkippedByDate -SourceDeleted $sourceDeleted -Notes $notes
                    continue
                }

                $modsRecords = @(Get-ModRecords -Records $records -ZipName $sourceName -LikelyModExtensions $normalizedModExtensions)
                $trayRecords = @(Get-TrayRecords -Records $records)

                if ($modsRecords.Count -eq 0 -and $trayRecords.Count -eq 0) {
                    $notes = 'No Mods/Tray content detected'
                    Add-OrganizeReportRow -Report $report -Source $sourceName -SourceType $sourceType -Status $status -ModsWritten $modsWritten -ModsReplaced $modsReplaced -ModsSkippedByDate $modsSkippedByDate -TrayWritten $trayWritten -TrayReplaced $trayReplaced -TraySkippedByDate $traySkippedByDate -SourceDeleted $sourceDeleted -Notes $notes
                    continue
                }

                if ($modsRecords.Count -gt 0) {
                    Ensure-Directory -Path $modsTargetForSource
                }

                foreach ($m in $modsRecords) {
                    if (-not (Test-SafeRelativePath -RelativePath $m.RelativePath)) { continue }
                    if ([System.IO.Path]::GetExtension([string]$m.RelativePath).ToLowerInvariant() -eq '.ts4script') {
                        $ts4scriptDetectedCount++
                        if ($ts4scriptDetectedSamples.Count -lt 20) {
                            $ts4scriptDetectedSamples.Add(("{0}::{1}" -f $sourceLabel, [string]$m.RelativePath)) | Out-Null
                        }
                    }

                    $safeRelInfo = ConvertTo-SimsSafeRelativePath -RelativePath ([string]$m.RelativePath)
                    if ([string]::IsNullOrWhiteSpace($safeRelInfo.SafePath)) { continue }
                    if ($safeRelInfo.Changed) {
                        Add-SimsSanitizedPathRecord -SourceLabel $sourceLabel -OriginalPath ([string]$m.RelativePath) -SafePath $safeRelInfo.SafePath
                    }

                    $dest = Join-Path $modsTargetForSource $safeRelInfo.SafePath
                    $decision = Resolve-DateBasedAction -Entry $m.Entry -DestinationPath $dest
                    if ($decision.Action -eq 'Skip') {
                        $modsSkippedByDate++
                        continue
                    }

                    $op = if ($decision.Action -eq 'Replace') {
                        'Replace mod file (source newer)'
                    }
                    else {
                        'Write mod file'
                    }

                    if ($PSCmdlet.ShouldProcess($dest, $op)) {
                        Write-ZipEntryToFile -Entry $m.Entry -DestinationPath $dest
                        if ($decision.Action -eq 'Replace') {
                            $modsReplaced++
                        }
                        else {
                            $modsWritten++
                        }
                    }
                }

                foreach ($t in $trayRecords) {
                    $leaf = [System.IO.Path]::GetFileName([string]$t.RelativePath)
                    if ([string]::IsNullOrWhiteSpace($leaf)) { continue }

                    $safeLeaf = ConvertTo-SimsSafePathSegment -Segment $leaf
                    if ($safeLeaf -ne $leaf) {
                        Add-SimsSanitizedPathRecord -SourceLabel $sourceLabel -OriginalPath $leaf -SafePath $safeLeaf
                    }

                    $dest = Join-Path $TrayRoot $safeLeaf
                    $decision = Resolve-DateBasedAction -Entry $t.Entry -DestinationPath $dest
                    if ($decision.Action -eq 'Skip') {
                        $traySkippedByDate++
                        continue
                    }

                    $op = if ($decision.Action -eq 'Replace') {
                        'Replace tray file (source newer)'
                    }
                    else {
                        'Write tray file'
                    }

                    if ($PSCmdlet.ShouldProcess($dest, $op)) {
                        Write-ZipEntryToFile -Entry $t.Entry -DestinationPath $dest
                        if ($decision.Action -eq 'Replace') {
                            $trayReplaced++
                        }
                        else {
                            $trayWritten++
                        }
                    }
                }
            }
            else {
                $tempExtractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("simsmod_extract_{0}" -f ([Guid]::NewGuid().ToString('N')))
                Expand-ArchiveWith7Zip -SevenZipExecutable $sevenZipExecutable -ArchivePath $sourcePath -DestinationDir $tempExtractRoot

                $records = @(Get-FileRecordsFromExtractRoot -ExtractRoot $tempExtractRoot -IgnoredExtensions $normalizedArchiveExtensions)
                if ($records.Count -eq 0) {
                    $notes = 'No usable file entries in archive'
                    Add-OrganizeReportRow -Report $report -Source $sourceName -SourceType $sourceType -Status $status -ModsWritten $modsWritten -ModsReplaced $modsReplaced -ModsSkippedByDate $modsSkippedByDate -TrayWritten $trayWritten -TrayReplaced $trayReplaced -TraySkippedByDate $traySkippedByDate -SourceDeleted $sourceDeleted -Notes $notes
                    continue
                }

                $modsRecords = @(Get-ModSourceRecords -Records $records -ArchiveName $sourceName -LikelyModExtensions $normalizedModExtensions)
                $trayRecords = @(Get-TraySourceRecords -Records $records)
                if ($modsRecords.Count -eq 0 -and $trayRecords.Count -eq 0) {
                    $notes = 'No Mods/Tray content detected'
                    Add-OrganizeReportRow -Report $report -Source $sourceName -SourceType $sourceType -Status $status -ModsWritten $modsWritten -ModsReplaced $modsReplaced -ModsSkippedByDate $modsSkippedByDate -TrayWritten $trayWritten -TrayReplaced $trayReplaced -TraySkippedByDate $traySkippedByDate -SourceDeleted $sourceDeleted -Notes $notes
                    continue
                }

                if ($modsRecords.Count -gt 0) {
                    Ensure-Directory -Path $modsTargetForSource
                }

                foreach ($m in $modsRecords) {
                    if (-not (Test-SafeRelativePath -RelativePath $m.RelativePath)) { continue }
                    if ([System.IO.Path]::GetExtension([string]$m.RelativePath).ToLowerInvariant() -eq '.ts4script') {
                        $ts4scriptDetectedCount++
                        if ($ts4scriptDetectedSamples.Count -lt 20) {
                            $ts4scriptDetectedSamples.Add(("{0}::{1}" -f $sourceLabel, [string]$m.RelativePath)) | Out-Null
                        }
                    }

                    $safeRelInfo = ConvertTo-SimsSafeRelativePath -RelativePath ([string]$m.RelativePath)
                    if ([string]::IsNullOrWhiteSpace($safeRelInfo.SafePath)) { continue }
                    if ($safeRelInfo.Changed) {
                        Add-SimsSanitizedPathRecord -SourceLabel $sourceLabel -OriginalPath ([string]$m.RelativePath) -SafePath $safeRelInfo.SafePath
                    }

                    $dest = Join-Path $modsTargetForSource $safeRelInfo.SafePath
                    $decision = Resolve-DateBasedActionForFile -SourcePath $m.SourcePath -DestinationPath $dest -VerifyContent:$VerifyContentOnNameConflict -PrefixBytes $PrefixHashBytes -HashCache $hashCache
                    if ($decision.Action -eq 'Skip') {
                        $modsSkippedByDate++
                        continue
                    }

                    $op = if ($decision.Action -eq 'Replace') {
                        'Replace mod file (source newer)'
                    }
                    else {
                        'Write mod file'
                    }

                    if ($PSCmdlet.ShouldProcess($dest, $op)) {
                        Copy-SourceFileToDestination -SourcePath $m.SourcePath -DestinationPath $dest
                        if ($decision.Action -eq 'Replace') {
                            $modsReplaced++
                        }
                        else {
                            $modsWritten++
                        }
                    }
                }

                foreach ($t in $trayRecords) {
                    $leaf = [System.IO.Path]::GetFileName([string]$t.RelativePath)
                    if ([string]::IsNullOrWhiteSpace($leaf)) { continue }

                    $safeLeaf = ConvertTo-SimsSafePathSegment -Segment $leaf
                    if ($safeLeaf -ne $leaf) {
                        Add-SimsSanitizedPathRecord -SourceLabel $sourceLabel -OriginalPath $leaf -SafePath $safeLeaf
                    }

                    $dest = Join-Path $TrayRoot $safeLeaf
                    $decision = Resolve-DateBasedActionForFile -SourcePath $t.SourcePath -DestinationPath $dest -VerifyContent:$VerifyContentOnNameConflict -PrefixBytes $PrefixHashBytes -HashCache $hashCache
                    if ($decision.Action -eq 'Skip') {
                        $traySkippedByDate++
                        continue
                    }

                    $op = if ($decision.Action -eq 'Replace') {
                        'Replace tray file (source newer)'
                    }
                    else {
                        'Write tray file'
                    }

                    if ($PSCmdlet.ShouldProcess($dest, $op)) {
                        Copy-SourceFileToDestination -SourcePath $t.SourcePath -DestinationPath $dest
                        if ($decision.Action -eq 'Replace') {
                            $trayReplaced++
                        }
                        else {
                            $trayWritten++
                        }
                    }
                }
            }

            $status = 'Processed'
            if (-not $KeepZip) {
                $deleteSourceAfter = $true
            }
            else {
                $notes = 'Source archive kept by -KeepZip'
            }
        }
        else {
            if ($sourceType -eq 'LooseDirectory') {
                $records = @(Get-FileRecordsFromExtractRoot -ExtractRoot $sourcePath -IgnoredExtensions $normalizedArchiveExtensions)
            }
            elseif ($sourceType -eq 'LooseFile') {
                $sourceFile = Get-Item -LiteralPath $sourcePath -ErrorAction Stop
                $extLower = $sourceFile.Extension.ToLowerInvariant()
                if ($normalizedArchiveExtensions -contains $extLower) {
                    $records = @()
                }
                else {
                    $records = @(
                        [pscustomobject]@{
                            SourcePath = $sourceFile.FullName
                            RelativePath = $sourceFile.Name
                            Extension = $extLower
                            IsTray = ($trayExtensions -contains $extLower)
                            ModsIndex = -1
                        }
                    )
                }
            }

            if ($records.Count -eq 0) {
                $notes = 'No usable file entries in source'
                Add-OrganizeReportRow -Report $report -Source $sourceName -SourceType $sourceType -Status $status -ModsWritten $modsWritten -ModsReplaced $modsReplaced -ModsSkippedByDate $modsSkippedByDate -TrayWritten $trayWritten -TrayReplaced $trayReplaced -TraySkippedByDate $traySkippedByDate -SourceDeleted $sourceDeleted -Notes $notes
                continue
            }

            $modsRecords = @(Get-ModSourceRecords -Records $records -ArchiveName $sourceName -LikelyModExtensions $normalizedModExtensions)
            $trayRecords = @(Get-TraySourceRecords -Records $records)
            if ($modsRecords.Count -eq 0 -and $trayRecords.Count -eq 0) {
                $notes = 'No Mods/Tray content detected'
                Add-OrganizeReportRow -Report $report -Source $sourceName -SourceType $sourceType -Status $status -ModsWritten $modsWritten -ModsReplaced $modsReplaced -ModsSkippedByDate $modsSkippedByDate -TrayWritten $trayWritten -TrayReplaced $trayReplaced -TraySkippedByDate $traySkippedByDate -SourceDeleted $sourceDeleted -Notes $notes
                continue
            }

            if ($modsRecords.Count -gt 0) {
                Ensure-Directory -Path $modsTargetForSource
            }

            foreach ($m in $modsRecords) {
                if (-not (Test-SafeRelativePath -RelativePath $m.RelativePath)) { continue }
                if ([System.IO.Path]::GetExtension([string]$m.RelativePath).ToLowerInvariant() -eq '.ts4script') {
                    $ts4scriptDetectedCount++
                    if ($ts4scriptDetectedSamples.Count -lt 20) {
                        $ts4scriptDetectedSamples.Add(("{0}::{1}" -f $sourceLabel, [string]$m.RelativePath)) | Out-Null
                    }
                }

                $safeRelInfo = ConvertTo-SimsSafeRelativePath -RelativePath ([string]$m.RelativePath)
                if ([string]::IsNullOrWhiteSpace($safeRelInfo.SafePath)) { continue }
                if ($safeRelInfo.Changed) {
                    Add-SimsSanitizedPathRecord -SourceLabel $sourceLabel -OriginalPath ([string]$m.RelativePath) -SafePath $safeRelInfo.SafePath
                }

                $dest = Join-Path $modsTargetForSource $safeRelInfo.SafePath
                $decision = Resolve-DateBasedActionForFile -SourcePath $m.SourcePath -DestinationPath $dest -VerifyContent:$VerifyContentOnNameConflict -PrefixBytes $PrefixHashBytes -HashCache $hashCache
                if ($decision.Action -eq 'Skip') {
                    $modsSkippedByDate++
                    continue
                }

                $op = if ($decision.Action -eq 'Replace') {
                    'Replace mod file (source newer)'
                }
                else {
                    'Write mod file'
                }

                if ($PSCmdlet.ShouldProcess($dest, $op)) {
                    Copy-SourceFileToDestination -SourcePath $m.SourcePath -DestinationPath $dest
                    if ($decision.Action -eq 'Replace') {
                        $modsReplaced++
                    }
                    else {
                        $modsWritten++
                    }
                }
            }

            foreach ($t in $trayRecords) {
                $leaf = [System.IO.Path]::GetFileName([string]$t.RelativePath)
                if ([string]::IsNullOrWhiteSpace($leaf)) { continue }

                $safeLeaf = ConvertTo-SimsSafePathSegment -Segment $leaf
                if ($safeLeaf -ne $leaf) {
                    Add-SimsSanitizedPathRecord -SourceLabel $sourceLabel -OriginalPath $leaf -SafePath $safeLeaf
                }

                $dest = Join-Path $TrayRoot $safeLeaf
                $decision = Resolve-DateBasedActionForFile -SourcePath $t.SourcePath -DestinationPath $dest -VerifyContent:$VerifyContentOnNameConflict -PrefixBytes $PrefixHashBytes -HashCache $hashCache
                if ($decision.Action -eq 'Skip') {
                    $traySkippedByDate++
                    continue
                }

                $op = if ($decision.Action -eq 'Replace') {
                    'Replace tray file (source newer)'
                }
                else {
                    'Write tray file'
                }

                if ($PSCmdlet.ShouldProcess($dest, $op)) {
                    Copy-SourceFileToDestination -SourcePath $t.SourcePath -DestinationPath $dest
                    if ($decision.Action -eq 'Replace') {
                        $trayReplaced++
                    }
                    else {
                        $trayWritten++
                    }
                }
            }

            $status = 'Processed'
        }
    }
    catch {
        $status = 'Error'
        $notes = "$($_.Exception.Message) (line $($_.InvocationInfo.ScriptLineNumber))"
        Write-Warning ("{0}: {1}" -f $sourceLabel, $notes)
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
        if (-not [string]::IsNullOrWhiteSpace($tempExtractRoot) -and (Test-Path -LiteralPath $tempExtractRoot)) {
            Remove-Item -LiteralPath $tempExtractRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        $sourceProcessed++
        Write-SimsProgress -Stage 'organize.source' -Current $sourceProcessed -Total $sourceTotal -Detail $sourceLabel
    }

    if ($status -eq 'Processed' -and $deleteSourceAfter) {
        try {
            if ($PSCmdlet.ShouldProcess($sourcePath, 'Delete source archive')) {
                Remove-SimsItemToRecycleBin -LiteralPath $sourcePath
                $sourceDeleted = $true
            }
        }
        catch {
            $status = 'Error'
            if ([string]::IsNullOrWhiteSpace($notes)) {
                $notes = "Delete failed: $($_.Exception.Message)"
            }
            else {
                $notes = "$notes | Delete failed: $($_.Exception.Message)"
            }
        }
    }

    Add-OrganizeReportRow -Report $report -Source $sourceName -SourceType $sourceType -Status $status -ModsWritten $modsWritten -ModsReplaced $modsReplaced -ModsSkippedByDate $modsSkippedByDate -TrayWritten $trayWritten -TrayReplaced $trayReplaced -TraySkippedByDate $traySkippedByDate -SourceDeleted $sourceDeleted -Notes $notes
}

$report | Sort-Object Status, SourceType, Source | Format-Table Source, SourceType, Status, ModsWritten, ModsReplaced, ModsSkippedByDate, TrayWritten, TrayReplaced, TraySkippedByDate, SourceDeleted, Notes -AutoSize -Wrap

$processed = @($report | Where-Object { $_.Status -eq 'Processed' }).Count
$errors = @($report | Where-Object { $_.Status -eq 'Error' }).Count
$skipped = @($report | Where-Object { $_.Status -eq 'Skipped' }).Count

Write-Host ''
Write-Host ("Processed: {0} | Skipped: {1} | Errors: {2}" -f $processed, $skipped, $errors)
if ($useUnifiedModsFolder) {
    Write-Host ("Mods target (unified): {0}" -f $modsTargetUnified)
}
else {
    Write-Host ("Mods target (per-source root): {0}" -f $modsTargetRoot)
}
Write-Host ("Tray target: {0}" -f $TrayRoot)

if ($ts4scriptDetectedCount -gt 0) {
    Write-Warning ("Detected {0} .ts4script file(s). Review script-mod compatibility and game patch matching before launch." -f $ts4scriptDetectedCount)
    foreach ($sample in @($ts4scriptDetectedSamples)) {
        Write-Warning ("ts4script: {0}" -f $sample)
    }
}

if ($script:sanitizedPathCount -gt 0) {
    Write-Warning ("Sanitized invalid path characters for {0} file path(s)." -f $script:sanitizedPathCount)
    foreach ($sample in @($script:sanitizedPathSamples)) {
        Write-Warning ("sanitized-path: {0}" -f $sample)
    }
}
