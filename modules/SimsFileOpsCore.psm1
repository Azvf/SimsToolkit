Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName Microsoft.VisualBasic

function Remove-SimsItemToRecycleBin {
    <#
    .SYNOPSIS
    Moves a file or directory to the Recycle Bin instead of permanently deleting.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    if (-not [System.IO.File]::Exists($LiteralPath) -and -not [System.IO.Directory]::Exists($LiteralPath)) {
        return
    }

    try {
        $item = Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
        if ($item.PSIsContainer) {
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($LiteralPath, [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs, [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin)
        }
        else {
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($LiteralPath, [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs, [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin)
        }
    }
    catch {
        throw
    }
}

function New-SimsHashCache {
    $prefixCache = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $fullCache = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::OrdinalIgnoreCase)

    return [pscustomobject]@{
        PrefixCache = $prefixCache
        FullCache = $fullCache
    }
}

function New-SimsModExtensionSet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Extensions
    )

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ext in $Extensions) {
        if ([string]::IsNullOrWhiteSpace($ext)) { continue }
        $normalized = if ($ext.StartsWith('.')) { $ext } else { ".$ext" }
        [void]$set.Add($normalized)
    }

    return ,$set
}

function Get-SimsResolvedLiteralPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter()]
        [switch]$AllowMissing
    )

    if (Test-Path -LiteralPath $Path) {
        return (Resolve-Path -LiteralPath $Path -ErrorAction Stop).ProviderPath
    }

    if ($AllowMissing) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    throw "Path not found: $Path"
}

function New-SimsDirectoryIfMissing {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter()]
        [scriptblock]$ShouldProcessScript
    )

    if (Test-Path -LiteralPath $Path) { return }

    $shouldCreate = $true
    if ($null -ne $ShouldProcessScript) {
        $shouldCreate = [bool](& $ShouldProcessScript $Path 'Create directory')
    }

    if ($shouldCreate) {
        [System.IO.Directory]::CreateDirectory($Path) | Out-Null
    }
}

function Remove-SimsEmptyDirectories {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter()]
        [string]$ProtectPath,

        [Parameter()]
        [scriptblock]$ShouldProcessScript
    )

    $dirs = @(Get-ChildItem -LiteralPath $RootPath -Recurse -Directory | Sort-Object FullName -Descending)
    foreach ($dir in $dirs) {
        if (-not [string]::IsNullOrEmpty($ProtectPath) -and $dir.FullName -eq $ProtectPath) { continue }
        if (@(Get-ChildItem -LiteralPath $dir.FullName -Force).Count -ne 0) { continue }

        $shouldRemove = $true
        if ($null -ne $ShouldProcessScript) {
            $shouldRemove = [bool](& $ShouldProcessScript $dir.FullName 'Remove empty directory')
        }

        if ($shouldRemove) {
            Remove-SimsItemToRecycleBin -LiteralPath $dir.FullName
        }
    }
}

function Write-SimsProgress {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][int]$Current,
        [Parameter(Mandatory = $true)][int]$Total,
        [Parameter()][string]$Detail = ''
    )
    $safeStage = $Stage.Replace('|', '/')
    $safeDetail = if ([string]::IsNullOrWhiteSpace($Detail)) { '' } else { $Detail.Replace('|', '/') }
    $percent = if ($Total -gt 0) { [int][Math]::Floor(([double]$Current / [double]$Total) * 100.0) } else { -1 }
    if ($percent -gt 100) { $percent = 100 }
    if ($percent -lt -1) { $percent = -1 }
    Write-Output ("##SIMS_PROGRESS##|{0}|{1}|{2}|{3}|{4}" -f $safeStage, $Current, $Total, $percent, $safeDetail)
}

function Get-SimsHashCacheKey {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][long]$Length,
        [Parameter(Mandatory = $true)][long]$LastWriteTicks,
        [Parameter()][int]$PrefixBytes = 0
    )

    if ($PrefixBytes -gt 0) {
        return "{0}|{1}|{2}|{3}" -f $Path, $PrefixBytes, $Length, $LastWriteTicks
    }

    return "{0}|{1}|{2}" -f $Path, $Length, $LastWriteTicks
}

function Remove-SimsHashCacheByPath {
    param(
        [Parameter(Mandatory = $true)]
        [object]$HashCache,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $prefix = "$Path|"
    foreach ($k in @($HashCache.PrefixCache.Keys)) {
        if ($k.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            [void]$HashCache.PrefixCache.Remove($k)
        }
    }

    foreach ($k in @($HashCache.FullCache.Keys)) {
        if ($k.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            [void]$HashCache.FullCache.Remove($k)
        }
    }
}

function Initialize-SimsPrefixHashCacheParallel {
    param(
        [Parameter(Mandatory = $true)]
        [object]$HashCache,

        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo[]]$Files,

        [Parameter(Mandatory = $true)]
        [int]$PrefixBytes,

        [Parameter(Mandatory = $true)]
        [int]$WorkerCount
    )

    if (@($Files).Count -eq 0) { return }

    $todo = New-Object System.Collections.Generic.List[object]
    foreach ($f in $Files) {
        $prefixKey = Get-SimsHashCacheKey -Path $f.FullName -Length $f.Length -LastWriteTicks $f.LastWriteTimeUtc.Ticks -PrefixBytes $PrefixBytes
        if ($HashCache.PrefixCache.ContainsKey($prefixKey)) { continue }

        $todo.Add([pscustomobject]@{
                Path = $f.FullName
                PrefixKey = $prefixKey
            })
    }

    if ($todo.Count -eq 0) { return }

    $results = $todo | ForEach-Object -Parallel {
        $item = $_
        if (-not [System.IO.File]::Exists($item.Path)) { return $null }

        $stream = [System.IO.File]::OpenRead($item.Path)
        try {
            $take = [int][Math]::Min([long]$using:PrefixBytes, $stream.Length)
            $buffer = New-Object byte[] $take
            $offset = 0
            while ($offset -lt $take) {
                $read = $stream.Read($buffer, $offset, ($take - $offset))
                if ($read -le 0) { break }
                $offset += $read
            }

            $md5 = [System.Security.Cryptography.MD5]::Create()
            try {
                $prefixHash = ($md5.ComputeHash($buffer, 0, $offset) | ForEach-Object { $_.ToString('x2') }) -join ''
            }
            finally {
                $md5.Dispose()
            }

            return [pscustomobject]@{
                PrefixKey = $item.PrefixKey
                PrefixMd5 = $prefixHash
            }
        }
        finally {
            $stream.Dispose()
        }
    } -ThrottleLimit $WorkerCount

    foreach ($r in $results) {
        if ($null -eq $r) { continue }
        $HashCache.PrefixCache[$r.PrefixKey] = $r.PrefixMd5
    }
}

function Get-SimsFilePrefixMd5 {
    param(
        [Parameter(Mandatory = $true)]
        [object]$HashCache,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [int]$PrefixBytes,

        [Parameter(Mandatory = $true)]
        [string]$CacheKey
    )

    if ($HashCache.PrefixCache.ContainsKey($CacheKey)) {
        return $HashCache.PrefixCache[$CacheKey]
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $take = [Math]::Min($PrefixBytes, [int]$stream.Length)
        $buffer = New-Object byte[] $take
        $offset = 0
        while ($offset -lt $take) {
            $read = $stream.Read($buffer, $offset, ($take - $offset))
            if ($read -le 0) { break }
            $offset += $read
        }

        $md5 = [System.Security.Cryptography.MD5]::Create()
        try {
            $hash = $md5.ComputeHash($buffer, 0, $offset)
            $result = ([System.BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
            $HashCache.PrefixCache[$CacheKey] = $result
            return $result
        }
        finally {
            $md5.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-SimsFileMd5 {
    param(
        [Parameter(Mandatory = $true)]
        [object]$HashCache,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$CacheKey
    )

    if ($HashCache.FullCache.ContainsKey($CacheKey)) {
        return $HashCache.FullCache[$CacheKey]
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $md5 = [System.Security.Cryptography.MD5]::Create()
        try {
            $hash = $md5.ComputeHash($stream)
            $result = ([System.BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
            $HashCache.FullCache[$CacheKey] = $result
            return $result
        }
        finally {
            $md5.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-SimsFastContentEqual {
    param(
        [Parameter(Mandatory = $true)][object]$HashCache,
        [Parameter(Mandatory = $true)][string]$PathA,
        [Parameter(Mandatory = $true)][string]$PathB,
        [Parameter(Mandatory = $true)][int]$PrefixBytes
    )

    $a = Get-Item -LiteralPath $PathA -ErrorAction Stop
    $b = Get-Item -LiteralPath $PathB -ErrorAction Stop
    if ($a.Length -ne $b.Length) { return $false }

    $aPrefixKey = Get-SimsHashCacheKey -Path $PathA -Length $a.Length -LastWriteTicks $a.LastWriteTimeUtc.Ticks -PrefixBytes $PrefixBytes
    $bPrefixKey = Get-SimsHashCacheKey -Path $PathB -Length $b.Length -LastWriteTicks $b.LastWriteTimeUtc.Ticks -PrefixBytes $PrefixBytes
    $aPrefix = Get-SimsFilePrefixMd5 -HashCache $HashCache -Path $PathA -PrefixBytes $PrefixBytes -CacheKey $aPrefixKey
    $bPrefix = Get-SimsFilePrefixMd5 -HashCache $HashCache -Path $PathB -PrefixBytes $PrefixBytes -CacheKey $bPrefixKey
    if ($aPrefix -ne $bPrefix) { return $false }

    $aFullKey = Get-SimsHashCacheKey -Path $PathA -Length $a.Length -LastWriteTicks $a.LastWriteTimeUtc.Ticks
    $bFullKey = Get-SimsHashCacheKey -Path $PathB -Length $b.Length -LastWriteTicks $b.LastWriteTimeUtc.Ticks
    $aFull = Get-SimsFileMd5 -HashCache $HashCache -Path $PathA -CacheKey $aFullKey
    $bFull = Get-SimsFileMd5 -HashCache $HashCache -Path $PathB -CacheKey $bFullKey
    return ($aFull -eq $bFull)
}

function Resolve-SimsConflictAction {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$SourceFile,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][bool]$VerifyContent,
        [Parameter(Mandatory = $true)][int]$PrefixBytes,
        [Parameter(Mandatory = $true)][object]$HashCache
    )

    $existing = Get-Item -LiteralPath $DestinationPath -ErrorAction SilentlyContinue
    if ($null -eq $existing) { return 'Move' }

    if ($VerifyContent -and (Test-SimsFastContentEqual -HashCache $HashCache -PathA $SourceFile.FullName -PathB $existing.FullName -PrefixBytes $PrefixBytes)) {
        return 'DropDuplicate'
    }

    if ($SourceFile.LastWriteTime -gt $existing.LastWriteTime) {
        return 'Replace'
    }

    return 'DropOlder'
}

function Get-SimsDuplicateFileNameSet {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo[]]$Files
    )

    $counts = New-Object 'System.Collections.Generic.Dictionary[string,int]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Files) {
        if ($null -eq $file) { continue }
        if ($counts.ContainsKey($file.Name)) {
            $counts[$file.Name] = $counts[$file.Name] + 1
        }
        else {
            $counts[$file.Name] = 1
        }
    }

    $duplicates = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($kv in $counts.GetEnumerator()) {
        if ($kv.Value -gt 1) {
            [void]$duplicates.Add($kv.Key)
        }
    }

    return ,$duplicates
}

Export-ModuleMember -Function New-SimsHashCache, New-SimsModExtensionSet, Get-SimsResolvedLiteralPath, New-SimsDirectoryIfMissing, Remove-SimsEmptyDirectories, Remove-SimsItemToRecycleBin, Write-SimsProgress, Get-SimsHashCacheKey, Remove-SimsHashCacheByPath, Initialize-SimsPrefixHashCacheParallel, Get-SimsFilePrefixMd5, Get-SimsFileMd5, Test-SimsFastContentEqual, Resolve-SimsConflictAction, Get-SimsDuplicateFileNameSet
