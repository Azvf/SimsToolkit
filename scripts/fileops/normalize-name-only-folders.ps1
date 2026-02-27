[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter()]
    [string]$RootPath = ''
)

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$coreModulePath = Join-Path $projectRoot 'modules\SimsFileOpsCore.psm1'
if (Test-Path -LiteralPath $coreModulePath) {
    Import-Module -Name $coreModulePath -Force
}

# SSOT: apply defaults from SimsConfig
. (Join-Path $projectRoot 'modules\SimsConfig.ps1')
if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = $Script:SimsConfigDefault.NormalizeRootPath
}

$overrides = @{
    '♡ LOLA ♡'                    = 'LOLA'
    'Harry-Whitelighter_Charmed'  = 'Harry Whitelighter'
    'rapunzel_collection'         = 'Rapunzel'
    'ScoobyDoo-MysteryInc'        = 'Scooby Doo'
    'WizardofOz-emberfalls'       = 'Wizard of Oz'
}

$sourceTags = @(
    'victorious',
    'emberfalls',
    'animalcrossing',
    'charmed',
    'mysteryinc',
    'collection',
    'mods',
    'tray'
)

function Convert-CamelCaseToWords {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value -notmatch '[A-Za-z]') {
        return $Value
    }

    $v = [regex]::Replace($Value, '(?<=[a-z])(?=[A-Z])', ' ')
    $v = [regex]::Replace($v, '(?<=[A-Z])(?=[A-Z][a-z])', ' ')
    return $v
}

function Get-NameOnly {
    param([Parameter(Mandatory = $true)][string]$OriginalName)

    if ($overrides.ContainsKey($OriginalName)) {
        return $overrides[$OriginalName]
    }

    $name = $OriginalName.Trim()

    # Remove "(...)" version notes and "by xxx" author suffixes.
    $name = [regex]::Replace($name, '\s*\([^)]*\)', '')
    $name = [regex]::Replace($name, '\s+by\s+.+$', '', 'IgnoreCase')

    # Strip creator prefix like "7cupsbobatae_".
    if ($name -match '^(?<prefix>[a-z0-9]{5,})_(?<rest>.+)$') {
        $rest = $Matches.rest
        if ($rest -match '[A-Z]' -or $rest -match '\s' -or $rest -match '[\u4e00-\u9fff]') {
            $name = $rest
        }
    }

    # Strip known source tags at the tail.
    $tagPattern = ($sourceTags -join '|')
    if ($name -match "^(?<base>.+?)[_-](?<tag>$tagPattern)$") {
        $name = $Matches.base
    }

    $name = $name.Replace('_', ' ')
    $name = Convert-CamelCaseToWords -Value $name

    # Hyphen between letters is usually a separator in current dataset.
    $name = [regex]::Replace($name, '(?<=\p{L})-(?=\p{L})', ' ')

    # Remove leading/trailing decoration symbols.
    $name = [regex]::Replace($name, '^[^\p{L}\p{Nd}]+', '')
    $name = [regex]::Replace($name, '[^\p{L}\p{Nd}\s&\.\-·]+$', '')

    $name = [regex]::Replace($name, '\s+', ' ').Trim()

    # Remove path-invalid characters.
    foreach ($ch in [System.IO.Path]::GetInvalidFileNameChars()) {
        $name = $name.Replace([string]$ch, '')
    }

    if ([string]::IsNullOrWhiteSpace($name)) {
        return $OriginalName
    }

    return $name
}

function Resolve-ConflictDestination {
    param(
        [Parameter(Mandatory = $true)][string]$DestinationFile,
        [Parameter(Mandatory = $true)][long]$SourceLength
    )

    $existing = Get-Item -LiteralPath $DestinationFile -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        return [pscustomobject]@{ Path = $DestinationFile; Reused = $false }
    }

    if ($existing -is [System.IO.FileInfo] -and $existing.Length -eq $SourceLength) {
        return [pscustomobject]@{ Path = $DestinationFile; Reused = $true }
    }

    $dir = [System.IO.Path]::GetDirectoryName($DestinationFile)
    $base = [System.IO.Path]::GetFileNameWithoutExtension($DestinationFile)
    $ext = [System.IO.Path]::GetExtension($DestinationFile)

    $i = 2
    do {
        $candidate = Join-Path $dir ("{0}_dup{1}{2}" -f $base, $i, $ext)
        $i++
    } while (Test-Path -LiteralPath $candidate)

    return [pscustomobject]@{ Path = $candidate; Reused = $false }
}

function Merge-DirectoryIntoTarget {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$TargetDir
    )

    $moved = 0
    $reused = 0
    $renamed = 0

    $files = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -File)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($SourceDir.Length).TrimStart('\')
        $dest = Join-Path $TargetDir $relative
        $parent = [System.IO.Path]::GetDirectoryName($dest)
        [System.IO.Directory]::CreateDirectory($parent) | Out-Null

        $resolved = Resolve-ConflictDestination -DestinationFile $dest -SourceLength $file.Length
        if ($resolved.Reused) {
            if ($PSCmdlet.ShouldProcess($file.FullName, 'Delete duplicate source file')) {
                Remove-SimsItemToRecycleBin -LiteralPath $file.FullName
            }
            $reused++
            continue
        }

        if ($PSCmdlet.ShouldProcess($file.FullName, "Move file to $($resolved.Path)")) {
            Move-Item -LiteralPath $file.FullName -Destination $resolved.Path -Force
        }

        if ($resolved.Path -ne $dest) {
            $renamed++
        }
        else {
            $moved++
        }
    }

    $dirsDesc = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -Directory | Sort-Object FullName -Descending)
    foreach ($d in $dirsDesc) {
        if (@(Get-ChildItem -LiteralPath $d.FullName -Force).Count -eq 0) {
            if ($PSCmdlet.ShouldProcess($d.FullName, 'Remove empty directory')) {
                Remove-SimsItemToRecycleBin -LiteralPath $d.FullName
            }
        }
    }

    if (@(Get-ChildItem -LiteralPath $SourceDir -Force).Count -eq 0) {
        if ($PSCmdlet.ShouldProcess($SourceDir, 'Remove merged source root')) {
            Remove-SimsItemToRecycleBin -LiteralPath $SourceDir
        }
    }

    return [pscustomobject]@{
        Moved   = $moved
        Reused  = $reused
        Renamed = $renamed
    }
}

if (-not (Test-Path -LiteralPath $RootPath)) {
    throw "Root path not found: $RootPath"
}

$dirs = @(Get-ChildItem -LiteralPath $RootPath -Directory | Sort-Object Name)
$report = New-Object System.Collections.Generic.List[object]
$folderTotal = $dirs.Count
$folderProcessed = 0
Write-SimsProgress -Stage 'normalize.folder' -Current 0 -Total $folderTotal -Detail 'start'

foreach ($dir in $dirs) {
    $oldName = $dir.Name
    $newName = Get-NameOnly -OriginalName $oldName
    $oldPath = $dir.FullName
    $newPath = Join-Path $RootPath $newName

    $action = 'Unchanged'
    $notes = ''
    $moved = 0
    $reused = 0
    $renamed = 0

    try {
        if ($oldName -eq $newName) {
            $action = 'Unchanged'
        }
        elseif (-not (Test-Path -LiteralPath $newPath)) {
            if ($PSCmdlet.ShouldProcess($oldPath, "Rename to $newName")) {
                Move-Item -LiteralPath $oldPath -Destination $newPath
            }
            $action = 'Renamed'
        }
        else {
            $merge = Merge-DirectoryIntoTarget -SourceDir $oldPath -TargetDir $newPath
            $moved = $merge.Moved
            $reused = $merge.Reused
            $renamed = $merge.Renamed
            $action = 'Merged'
            $notes = "target exists: $newName"
        }
    }
    catch {
        $action = 'Error'
        $notes = $_.Exception.Message
    }

    $report.Add([pscustomobject]@{
        OldName  = $oldName
        NewName  = $newName
        Action   = $action
        Moved    = $moved
        Reused   = $reused
        Renamed  = $renamed
        Notes    = $notes
    })

    $folderProcessed++
    Write-SimsProgress -Stage 'normalize.folder' -Current $folderProcessed -Total $folderTotal -Detail $dir.Name
}

$report | Format-Table -AutoSize -Wrap

$renamedCount = @($report | Where-Object { $_.Action -eq 'Renamed' }).Count
$mergedCount = @($report | Where-Object { $_.Action -eq 'Merged' }).Count
$errorCount = @($report | Where-Object { $_.Action -eq 'Error' }).Count

Write-Host ''
Write-Host ("Renamed: {0} | Merged: {1} | Errors: {2}" -f $renamedCount, $mergedCount, $errorCount)
Write-Host ("Root: {0}" -f $RootPath)
