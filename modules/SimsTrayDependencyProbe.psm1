Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-SimsPotentialInstanceId {
    param(
        [Parameter(Mandatory = $true)]
        [UInt64]$Value
    )

    $minValue = [Convert]::ToUInt64('0000000100000000', 16)
    $maskHigh = [Convert]::ToUInt64('FFFFFFFF00000000', 16)
    $maskLow = [Convert]::ToUInt64('00000000FFFFFFFF', 16)

    if ($Value -lt $minValue) { return $false }
    if (($Value -band $maskHigh) -eq [UInt64]0) { return $false }
    if (($Value -band $maskLow) -eq [UInt64]0) { return $false }
    return $true
}

function ConvertTo-SimsNormalizedInstanceHex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $trimmed = $Value.Trim()
    if ($trimmed -match '^0x([0-9a-fA-F]{1,16})$') {
        $num = [Convert]::ToUInt64($Matches[1], 16)
        return ('0x{0:x16}' -f $num)
    }
    return ''
}

function ConvertTo-SimsTgiHex {
    param(
        [Parameter(Mandatory = $true)]
        [UInt32]$Type,

        [Parameter(Mandatory = $true)]
        [UInt32]$Group,

        [Parameter(Mandatory = $true)]
        [UInt64]$Instance
    )

    return ('0x{0:x8}!0x{1:x8}!0x{2:x16}' -f [UInt32]$Type, [UInt32]$Group, [UInt64]$Instance)
}

function Get-SimsTrayFileIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File
    )

    $match = [regex]::Match($File.BaseName, '^0x([0-9a-fA-F]{1,8})!0x([0-9a-fA-F]{1,16})$')
    if (-not $match.Success) {
        return [pscustomobject]@{
            ParseSuccess = $false
            TypeHex = ''
            InstanceHex = ''
            BaseName = $File.BaseName
        }
    }

    $typeValue = [Convert]::ToUInt32($match.Groups[1].Value, 16)
    $instanceValue = [Convert]::ToUInt64($match.Groups[2].Value, 16)
    return [pscustomobject]@{
        ParseSuccess = $true
        TypeHex = ('0x{0:x8}' -f $typeValue)
        InstanceHex = ('0x{0:x16}' -f $instanceValue)
        BaseName = $File.BaseName
    }
}

function New-SimsTrayExtensionSet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$TrayExtensions
    )

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ext in $TrayExtensions) {
        if ([string]::IsNullOrWhiteSpace($ext)) { continue }
        $normalized = if ($ext.StartsWith('.')) { $ext } else { ".$ext" }
        [void]$set.Add($normalized)
    }
    return ,$set
}

function Resolve-SimsTraySelection {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.IO.FileInfo[]]$Files,

        [Parameter(Mandatory = $false)]
        [string]$TrayItemKey
    )

    if ([string]::IsNullOrWhiteSpace($TrayItemKey)) {
        return [pscustomobject]@{
            SelectedFiles = @($Files)
            ResolvedTrayItemKey = ''
        }
    }

    $trimmed = $TrayItemKey.Trim()
    $instanceKey = ''
    $fullMatch = [regex]::Match($trimmed, '^0x[0-9a-fA-F]{1,8}!0x([0-9a-fA-F]{1,16})$')
    if ($fullMatch.Success) {
        $instanceKey = ConvertTo-SimsNormalizedInstanceHex -Value ("0x{0}" -f $fullMatch.Groups[1].Value)
    }
    elseif ($trimmed -match '^0x[0-9a-fA-F]{1,16}$') {
        $instanceKey = ConvertTo-SimsNormalizedInstanceHex -Value $trimmed
    }

    if (-not [string]::IsNullOrWhiteSpace($instanceKey)) {
        $selected = @(
            $Files | Where-Object {
                $identity = Get-SimsTrayFileIdentity -File $_
                $identity.ParseSuccess -and $identity.InstanceHex -ieq $instanceKey
            }
        )

        # Backward compatibility: also allow exact basename match.
        $selectedByBase = @($Files | Where-Object { $_.BaseName -ieq $trimmed })
        if ($selectedByBase.Count -gt 0) {
            $selected = @($selected + $selectedByBase | Sort-Object FullName -Unique)
        }

        return [pscustomobject]@{
            SelectedFiles = $selected
            ResolvedTrayItemKey = $instanceKey
        }
    }

    return [pscustomobject]@{
        SelectedFiles = @($Files | Where-Object { $_.BaseName -ieq $trimmed })
        ResolvedTrayItemKey = $trimmed
    }
}

function Read-SimsVarUInt64 {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes,

        [Parameter(Mandatory = $true)]
        [int]$StartIndex
    )

    [UInt64]$value = 0
    $shift = 0
    for ($i = 0; $i -lt 10; $i++) {
        $index = $StartIndex + $i
        if ($index -ge $Bytes.Length) {
            return [pscustomobject]@{
                Success = $false
                Value = [UInt64]0
                NextIndex = $StartIndex
            }
        }

        $byte = $Bytes[$index]
        $value = $value -bor ([UInt64]($byte -band 0x7F) -shl $shift)
        if (($byte -band 0x80) -eq 0) {
            return [pscustomobject]@{
                Success = $true
                Value = $value
                NextIndex = ($index + 1)
            }
        }

        $shift += 7
    }

    return [pscustomobject]@{
        Success = $false
        Value = [UInt64]0
        NextIndex = $StartIndex
    }
}

function Get-SimsProtobufInstanceCandidates {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes,

        [Parameter()]
        [ValidateRange(0, 16)]
        [int]$MaxDepth = 4
    )

    $candidates = New-Object 'System.Collections.Generic.HashSet[UInt64]'
    if ($Bytes.Length -eq 0) { return ,$candidates }

    $segments = New-Object 'System.Collections.Generic.Stack[object]'
    $segments.Push([pscustomobject]@{
            Start = 0
            End = $Bytes.Length
            Depth = 0
        })

    while ($segments.Count -gt 0) {
        $segment = $segments.Pop()
        $position = [int]$segment.Start
        $end = [int]$segment.End
        $depth = [int]$segment.Depth

        while ($position -lt $end) {
            $keyRead = Read-SimsVarUInt64 -Bytes $Bytes -StartIndex $position
            if (-not $keyRead.Success) { break }

            $wireType = [int]($keyRead.Value -band 0x7)
            $position = [int]$keyRead.NextIndex

            switch ($wireType) {
                0 {
                    $valueRead = Read-SimsVarUInt64 -Bytes $Bytes -StartIndex $position
                    if (-not $valueRead.Success) {
                        $position = $end
                        break
                    }

                    $position = [int]$valueRead.NextIndex
                    $value = [UInt64]$valueRead.Value
                    if (Test-SimsPotentialInstanceId -Value $value) {
                        [void]$candidates.Add($value)
                    }
                }
                1 {
                    if (($position + 8) -gt $end) {
                        $position = $end
                        break
                    }

                    $value = [BitConverter]::ToUInt64($Bytes, $position)
                    $position += 8
                    if (Test-SimsPotentialInstanceId -Value $value) {
                        [void]$candidates.Add($value)
                    }
                }
                2 {
                    $lenRead = Read-SimsVarUInt64 -Bytes $Bytes -StartIndex $position
                    if (-not $lenRead.Success) {
                        $position = $end
                        break
                    }

                    $payloadLength64 = [UInt64]$lenRead.Value
                    $position = [int]$lenRead.NextIndex
                    if ($payloadLength64 -gt [UInt64]([int]::MaxValue)) {
                        $position = $end
                        break
                    }

                    $payloadLength = [int]$payloadLength64
                    $payloadEnd = $position + $payloadLength
                    if ($payloadLength -lt 0 -or $payloadEnd -gt $end) {
                        $position = $end
                        break
                    }

                    if ($depth -lt $MaxDepth -and $payloadLength -ge 2) {
                        $segments.Push([pscustomobject]@{
                                Start = $position
                                End = $payloadEnd
                                Depth = ($depth + 1)
                            })
                    }

                    $position = $payloadEnd
                }
                5 {
                    if (($position + 4) -gt $end) {
                        $position = $end
                        break
                    }
                    $position += 4
                }
                default {
                    $position = $end
                }
            }
        }
    }

    return ,$candidates
}

function Get-SimsPackageInstanceSet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath
    )

    $stream = $null
    $reader = $null
    try {
        $stream = [System.IO.File]::OpenRead($PackagePath)
        $reader = [System.IO.BinaryReader]::new($stream)

        if ($stream.Length -lt 96) {
            return [pscustomobject]@{
                PackagePath = $PackagePath
                Instances = (New-Object 'System.Collections.Generic.HashSet[UInt64]')
                EntryCount = 0
                ParseError = 'HeaderTooShort'
            }
        }

        $magic = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
        if ($magic -ne 'DBPF') {
            return [pscustomobject]@{
                PackagePath = $PackagePath
                Instances = (New-Object 'System.Collections.Generic.HashSet[UInt64]')
                EntryCount = 0
                ParseError = 'NotDBPF'
            }
        }

        $reader.BaseStream.Position = 36
        $indexEntryCount = [UInt32]$reader.ReadUInt32()
        $indexOffsetShort = [UInt32]$reader.ReadUInt32()
        $indexSize = [UInt32]$reader.ReadUInt32()

        $reader.BaseStream.Position = 64
        $indexOffsetLong = [UInt64]$reader.ReadUInt64()
        $indexOffset = if ($indexOffsetShort -ne 0) { [UInt64]$indexOffsetShort } else { $indexOffsetLong }

        $instances = New-Object 'System.Collections.Generic.HashSet[UInt64]'
        if ($indexEntryCount -eq 0 -or $indexOffset -eq 0) {
            return [pscustomobject]@{
                PackagePath = $PackagePath
                Instances = $instances
                EntryCount = 0
                ParseError = $null
            }
        }

        if ($indexOffset -ge [UInt64]$stream.Length) {
            return [pscustomobject]@{
                PackagePath = $PackagePath
                Instances = $instances
                EntryCount = 0
                ParseError = 'IndexOffsetOutOfRange'
            }
        }

        $indexEnd = $indexOffset + [UInt64]$indexSize
        if ($indexEnd -gt [UInt64]$stream.Length) {
            $indexEnd = [UInt64]$stream.Length
        }

        $reader.BaseStream.Position = [Int64]$indexOffset
        $indexFlags = [UInt32]$reader.ReadUInt32()

        $hasConstType = (($indexFlags -band 0x1) -ne 0)
        $hasConstGroup = (($indexFlags -band 0x2) -ne 0)
        $hasConstInstanceHigh = (($indexFlags -band 0x4) -ne 0)

        if ($hasConstType) { [void]$reader.ReadUInt32() }
        if ($hasConstGroup) { [void]$reader.ReadUInt32() }
        $constInstanceHigh = [UInt32]0
        if ($hasConstInstanceHigh) { $constInstanceHigh = [UInt32]$reader.ReadUInt32() }

        $entryRead = 0
        for ($i = 0; $i -lt $indexEntryCount; $i++) {
            if ([UInt64]$reader.BaseStream.Position -ge $indexEnd) { break }

            if (-not $hasConstType) {
                if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
                [void]$reader.ReadUInt32()
            }

            if (-not $hasConstGroup) {
                if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
                [void]$reader.ReadUInt32()
            }

            $instanceHigh = if ($hasConstInstanceHigh) { $constInstanceHigh } else {
                if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
                [UInt32]$reader.ReadUInt32()
            }

            if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
            $instanceLow = [UInt32]$reader.ReadUInt32()
            $instanceId = ([UInt64]$instanceHigh -shl 32) -bor [UInt64]$instanceLow
            [void]$instances.Add($instanceId)

            if (([UInt64]$reader.BaseStream.Position + 12) -gt $indexEnd) { break }
            [void]$reader.ReadUInt32()
            $packedSizeWithFlag = [UInt32]$reader.ReadUInt32()
            [void]$reader.ReadUInt32()

            $isExtended = (($packedSizeWithFlag -band 0x80000000) -ne 0)
            if ($isExtended) {
                if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
                [void]$reader.ReadUInt16()
                [void]$reader.ReadUInt16()
            }

            $entryRead++
        }

        return [pscustomobject]@{
            PackagePath = $PackagePath
            Instances = $instances
            EntryCount = $entryRead
            ParseError = $null
        }
    }
    catch {
        return [pscustomobject]@{
            PackagePath = $PackagePath
            Instances = (New-Object 'System.Collections.Generic.HashSet[UInt64]')
            EntryCount = 0
            ParseError = $_.Exception.Message
        }
    }
    finally {
        if ($reader) { $reader.Dispose() }
        elseif ($stream) { $stream.Dispose() }
    }
}

function Get-SimsStrictTrayReferenceSet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TrayPath,

        [Parameter()]
        [string]$TrayItemKey = '',

        [Parameter(Mandatory = $true)]
        [string]$S4tiPath
    )

    $trayRoot = (Resolve-Path -LiteralPath $TrayPath -ErrorAction Stop).ProviderPath
    $s4tiRoot = (Resolve-Path -LiteralPath $S4tiPath -ErrorAction Stop).ProviderPath
    $powershellExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $powershellExe)) {
        throw "StrictS4TI mode requires Windows PowerShell executable: $powershellExe"
    }

    $tempScript = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), '.ps1')
    $tempJson = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), '.json')

    $extractorScript = @'
param(
    [Parameter(Mandatory = $true)]
    [string]$TrayPath,

    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$TrayItemKey,

    [Parameter(Mandatory = $true)]
    [string]$S4tiPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputJsonPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($TrayItemKey -eq '__S4TI_ALL__') {
    $TrayItemKey = ''
}

function Convert-ToNormalizedInstanceHex {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value -match '^0x([0-9a-fA-F]{1,16})$') {
        $num = [Convert]::ToUInt64($Matches[1], 16)
        return ('0x{0:x16}' -f $num)
    }
    return ''
}

function Convert-ToTgiHex {
    param(
        [Parameter(Mandatory = $true)][UInt32]$Type,
        [Parameter(Mandatory = $true)][UInt32]$Group,
        [Parameter(Mandatory = $true)][UInt64]$Instance
    )

    return ('0x{0:x8}!0x{1:x8}!0x{2:x16}' -f [UInt32]$Type, [UInt32]$Group, [UInt64]$Instance)
}

function Resolve-TrayItemSelection {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo[]]$Files,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$TrayItemKey
    )

    if ([string]::IsNullOrWhiteSpace($TrayItemKey)) {
        return [pscustomobject]@{
            ResolvedTrayItemKey = ''
            SelectedFiles = @($Files)
        }
    }

    $trimmed = $TrayItemKey.Trim()
    $instanceKey = ''
    $fullMatch = [regex]::Match($trimmed, '^0x[0-9a-fA-F]{1,8}!0x([0-9a-fA-F]{1,16})$')
    if ($fullMatch.Success) {
        $instanceKey = Convert-ToNormalizedInstanceHex -Value ("0x{0}" -f $fullMatch.Groups[1].Value)
    }
    elseif ($trimmed -match '^0x[0-9a-fA-F]{1,16}$') {
        $instanceKey = Convert-ToNormalizedInstanceHex -Value $trimmed
    }

    if (-not [string]::IsNullOrWhiteSpace($instanceKey)) {
        $selected = @(
            $Files | Where-Object {
                $m = [regex]::Match($_.BaseName, '^0x[0-9a-fA-F]{1,8}!0x([0-9a-fA-F]{1,16})$')
                if (-not $m.Success) { return $false }
                $instanceFromName = ('0x{0:x16}' -f [Convert]::ToUInt64($m.Groups[1].Value, 16))
                return $instanceFromName -ieq $instanceKey
            }
        )
        return [pscustomobject]@{
            ResolvedTrayItemKey = $instanceKey
            SelectedFiles = $selected
        }
    }

    return [pscustomobject]@{
        ResolvedTrayItemKey = $trimmed
        SelectedFiles = @($Files | Where-Object { $_.BaseName -ieq $trimmed })
    }
}

$resolvedTrayPath = (Resolve-Path -LiteralPath $TrayPath -ErrorAction Stop).ProviderPath
$resolvedS4tiPath = (Resolve-Path -LiteralPath $S4tiPath -ErrorAction Stop).ProviderPath
$trayItemFiles = @(Get-ChildItem -LiteralPath $resolvedTrayPath -File -Filter '*.trayitem' | Sort-Object Name)
$selection = Resolve-TrayItemSelection -Files $trayItemFiles -TrayItemKey $TrayItemKey
$selectedTrayItems = @($selection.SelectedFiles)

$objectIdSet = New-Object 'System.Collections.Generic.HashSet[UInt64]'
$resourceKeySet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$resourceKeyInstanceSet = New-Object 'System.Collections.Generic.HashSet[UInt64]'
$lotTraitSet = New-Object 'System.Collections.Generic.HashSet[UInt64]'
$parseErrors = New-Object 'System.Collections.Generic.List[object]'

Push-Location $resolvedS4tiPath
try {
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $resolvedS4tiPath 'Sims4.UserData.dll'))

    foreach ($trayFile in $selectedTrayItems) {
        try {
            $trayItem = [Sims4.UserData.TrayItem]::Open($trayFile.FullName)
            $content = [Sims4.UserData.TrayContentFileAll]::FromTrayItem($trayItem)

            if ($content.BpContent -ne $null) {
                $bpMetadata = $content.BpContent.BpMetadata
                if ($bpMetadata -ne $null) {
                    if ($bpMetadata.LotObjectList -ne $null -and $bpMetadata.LotObjectList.LotObjects -ne $null) {
                        foreach ($lotObject in $bpMetadata.LotObjectList.LotObjects) {
                            if ($lotObject -eq $null) { continue }
                            $id = [UInt64]$lotObject.ObjectDefGuid64
                            if ($id -ne [UInt64]0) {
                                [void]$objectIdSet.Add($id)
                            }
                        }
                    }

                    try {
                        $bpArchitecture = New-Object Sims4.UserData.BlueprintArchitecture
                        $bpArchitecture.Parse($bpMetadata.Architecture, 0, $bpMetadata.Version)

                        $archLotObjectList = $bpArchitecture.LDNB.Tables.Table20.LotObjectList
                        if ($archLotObjectList -ne $null -and $archLotObjectList.LotObjects -ne $null) {
                            foreach ($lotObject in $archLotObjectList.LotObjects) {
                                if ($lotObject -eq $null) { continue }
                                $id = [UInt64]$lotObject.ObjectDefGuid64
                                if ($id -ne [UInt64]0) {
                                    [void]$objectIdSet.Add($id)
                                }
                            }
                        }

                        $table00Keys = @($bpArchitecture.LDNB.Tables.Table00.ResourceKeys)
                        foreach ($rk in $table00Keys) {
                            if ($rk -eq $null) { continue }
                            $type = [UInt32]$rk.Type
                            if ($type -eq [UInt32]0 -or $type -eq [UInt32]2673671952 -or $type -eq [UInt32]2438063804) { continue }
                            $group = [UInt32]$rk.Group
                            $instance = [UInt64]$rk.Instance
                            $key = Convert-ToTgiHex -Type $type -Group $group -Instance $instance
                            [void]$resourceKeySet.Add($key)
                            [void]$resourceKeyInstanceSet.Add($instance)
                        }

                        $table05Keys1 = @($bpArchitecture.LDNB.Tables.Table05.ResourceKeys1)
                        foreach ($usage in $table05Keys1) {
                            if ($usage -eq $null) { continue }
                            $rk = $usage.Key
                            if ($rk -eq $null) { continue }
                            $type = [UInt32]$rk.Type
                            if ($type -eq [UInt32]0 -or $type -eq [UInt32]2673671952 -or $type -eq [UInt32]2438063804) { continue }
                            $group = [UInt32]$rk.Group
                            $instance = [UInt64]$rk.Instance
                            $key = Convert-ToTgiHex -Type $type -Group $group -Instance $instance
                            [void]$resourceKeySet.Add($key)
                            [void]$resourceKeyInstanceSet.Add($instance)
                        }

                        $table05Keys2 = @($bpArchitecture.LDNB.Tables.Table05.ResourceKeys2)
                        foreach ($usage in $table05Keys2) {
                            if ($usage -eq $null) { continue }
                            $rk = $usage.Key
                            if ($rk -eq $null) { continue }
                            $type = [UInt32]$rk.Type
                            if ($type -eq [UInt32]0 -or $type -eq [UInt32]2673671952 -or $type -eq [UInt32]2438063804) { continue }
                            $group = [UInt32]$rk.Group
                            $instance = [UInt64]$rk.Instance
                            $key = Convert-ToTgiHex -Type $type -Group $group -Instance $instance
                            [void]$resourceKeySet.Add($key)
                            [void]$resourceKeyInstanceSet.Add($instance)
                        }
                    }
                    catch {
                        $parseErrors.Add([pscustomobject]@{
                                TrayItemPath = $trayFile.FullName
                                Error = ("BlueprintArchitecture: {0}" -f $_.Exception.Message)
                            }) | Out-Null
                    }
                }

                try {
                    $lotTraits = $trayItem.Metadata.Metadata.BpMetadata.LotTraits
                    if ($lotTraits -ne $null) {
                        foreach ($traitId in $lotTraits) {
                            $id = [UInt64]$traitId
                            if ($id -ne [UInt64]0) {
                                [void]$lotTraitSet.Add($id)
                            }
                        }
                    }
                }
                catch {
                }
            }

            if ($content.RoContent -ne $null) {
                $roMetadata = $content.RoContent.RoMetadata
                if ($roMetadata -ne $null) {
                    if ($roMetadata.LotObjectList -ne $null -and $roMetadata.LotObjectList.LotObjects -ne $null) {
                        foreach ($lotObject in $roMetadata.LotObjectList.LotObjects) {
                            if ($lotObject -eq $null) { continue }
                            $id = [UInt64]$lotObject.ObjectDefGuid64
                            if ($id -ne [UInt64]0) {
                                [void]$objectIdSet.Add($id)
                            }
                        }
                    }

                    if ($roMetadata.BuildBuyUnlockList -ne $null -and $roMetadata.BuildBuyUnlockList.ResourceKeys -ne $null) {
                        foreach ($rk in $roMetadata.BuildBuyUnlockList.ResourceKeys) {
                            if ($rk -eq $null) { continue }
                            $type = [UInt32]$rk.Type
                            $group = [UInt32]$rk.Group
                            $instance = [UInt64]$rk.Instance
                            if ($type -eq [UInt32]0 -and $group -eq [UInt32]0 -and $instance -eq [UInt64]0) { continue }
                            $key = Convert-ToTgiHex -Type $type -Group $group -Instance $instance
                            [void]$resourceKeySet.Add($key)
                            [void]$resourceKeyInstanceSet.Add($instance)
                        }
                    }

                    try {
                        $roomArchitecture = New-Object Sims4.UserData.RoomArchitecture
                        $roomArchitecture.Parse($roMetadata.Architecture)

                        foreach ($section in @($roomArchitecture.section1)) {
                            if ($section -eq $null -or $section.resourceKeys -eq $null -or $section.resourceKeys.values -eq $null) { continue }
                            foreach ($rk in $section.resourceKeys.values) {
                                if ($rk -eq $null -or $rk.IsKeyEmpty) { continue }
                                $type = [UInt32]$rk.type
                                $group = [UInt32]$rk.group
                                $instance = [UInt64]$rk.instance
                                $key = Convert-ToTgiHex -Type $type -Group $group -Instance $instance
                                [void]$resourceKeySet.Add($key)
                                [void]$resourceKeyInstanceSet.Add($instance)
                            }
                        }

                        foreach ($section in @($roomArchitecture.section4)) {
                            if ($section -eq $null -or $section.resourceKeys -eq $null -or $section.resourceKeys.values -eq $null) { continue }
                            foreach ($rk in $section.resourceKeys.values) {
                                if ($rk -eq $null -or $rk.IsKeyEmpty) { continue }
                                $type = [UInt32]$rk.type
                                $group = [UInt32]$rk.group
                                $instance = [UInt64]$rk.instance
                                $key = Convert-ToTgiHex -Type $type -Group $group -Instance $instance
                                [void]$resourceKeySet.Add($key)
                                [void]$resourceKeyInstanceSet.Add($instance)
                            }
                        }

                        if ($roomArchitecture.objectIDList -ne $null -and $roomArchitecture.objectIDList.Indices -ne $null) {
                            foreach ($objIndex in $roomArchitecture.objectIDList.Indices) {
                                $id = [UInt64]$objIndex.Id
                                if ($id -eq [UInt64]0) { continue }
                                [void]$objectIdSet.Add($id)
                                $objKey = Convert-ToTgiHex -Type ([UInt32]832458525) -Group ([UInt32]0) -Instance $id
                                [void]$resourceKeySet.Add($objKey)
                                [void]$resourceKeyInstanceSet.Add($id)
                            }
                        }
                    }
                    catch {
                        $parseErrors.Add([pscustomobject]@{
                                TrayItemPath = $trayFile.FullName
                                Error = ("RoomArchitecture: {0}" -f $_.Exception.Message)
                            }) | Out-Null
                    }
                }
            }
        }
        catch {
            $parseErrors.Add([pscustomobject]@{
                    TrayItemPath = $trayFile.FullName
                    Error = $_.Exception.Message
                }) | Out-Null
        }
    }
}
finally {
    Pop-Location
}

foreach ($instance in $resourceKeyInstanceSet) {
    if ($objectIdSet.Contains([UInt64]$instance)) {
        [void]$objectIdSet.Remove([UInt64]$instance)
    }
}

$objectIdHex = New-Object 'System.Collections.Generic.List[string]'
foreach ($id in $objectIdSet) {
    $objectIdHex.Add(('0x{0:x16}' -f [UInt64]$id)) | Out-Null
}

$resourceKeyHex = New-Object 'System.Collections.Generic.List[string]'
foreach ($key in $resourceKeySet) {
    $resourceKeyHex.Add([string]$key) | Out-Null
}

$lotTraitHex = New-Object 'System.Collections.Generic.List[string]'
foreach ($id in $lotTraitSet) {
    $lotTraitHex.Add(('0x{0:x16}' -f [UInt64]$id)) | Out-Null
}

$objectIdArray = @($objectIdHex)
[Array]::Sort($objectIdArray)
$resourceKeyArray = @($resourceKeyHex)
[Array]::Sort($resourceKeyArray)
$lotTraitArray = @($lotTraitHex)
[Array]::Sort($lotTraitArray)
$parseErrorArray = if ($parseErrors.Count -gt 0) { $parseErrors.ToArray() } else { @() }

$result = [pscustomobject]@{
    TrayPath = $resolvedTrayPath
    TrayItemKey = $selection.ResolvedTrayItemKey
    TrayFileCount = $selectedTrayItems.Count
    ObjectIds = $objectIdArray
    ResourceKeys = $resourceKeyArray
    LotTraitIds = $lotTraitArray
    ParseErrorCount = $parseErrors.Count
    ParseErrors = $parseErrorArray
}

$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
'@

    try {
        Set-Content -LiteralPath $tempScript -Value $extractorScript -Encoding UTF8
        $trayItemKeyArg = if ([string]::IsNullOrWhiteSpace($TrayItemKey)) { '__S4TI_ALL__' } else { $TrayItemKey }
        $extractorOutput = & $powershellExe -NoProfile -ExecutionPolicy Bypass -File $tempScript -TrayPath $trayRoot -TrayItemKey $trayItemKeyArg -S4tiPath $s4tiRoot -OutputJsonPath $tempJson 2>&1
        if ($LASTEXITCODE -ne 0) {
            $errText = if ($extractorOutput) { ($extractorOutput -join [Environment]::NewLine) } else { 'Unknown error from strict extractor.' }
            throw "StrictS4TI tray parser failed.`n$errText"
        }
        if (-not (Test-Path -LiteralPath $tempJson)) {
            throw "StrictS4TI tray parser did not generate output JSON: $tempJson"
        }

        $parsed = Get-Content -LiteralPath $tempJson -Raw | ConvertFrom-Json
        $objectSet = New-Object 'System.Collections.Generic.HashSet[UInt64]'
        foreach ($hex in @($parsed.ObjectIds)) {
            $normalized = ConvertTo-SimsNormalizedInstanceHex -Value ([string]$hex)
            if ([string]::IsNullOrWhiteSpace($normalized)) { continue }
            [void]$objectSet.Add([Convert]::ToUInt64($normalized.Substring(2), 16))
        }

        $lotTraitSet = New-Object 'System.Collections.Generic.HashSet[UInt64]'
        foreach ($hex in @($parsed.LotTraitIds)) {
            $normalized = ConvertTo-SimsNormalizedInstanceHex -Value ([string]$hex)
            if ([string]::IsNullOrWhiteSpace($normalized)) { continue }
            [void]$lotTraitSet.Add([Convert]::ToUInt64($normalized.Substring(2), 16))
        }

        $resourceKeySet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($rk in @($parsed.ResourceKeys)) {
            $keyText = [string]$rk
            $match = [regex]::Match($keyText, '^0x([0-9a-fA-F]{1,8})!0x([0-9a-fA-F]{1,8})!0x([0-9a-fA-F]{1,16})$')
            if (-not $match.Success) { continue }
            $typeValue = [Convert]::ToUInt32($match.Groups[1].Value, 16)
            $groupValue = [Convert]::ToUInt32($match.Groups[2].Value, 16)
            $instanceValue = [Convert]::ToUInt64($match.Groups[3].Value, 16)
            [void]$resourceKeySet.Add((ConvertTo-SimsTgiHex -Type $typeValue -Group $groupValue -Instance $instanceValue))
        }

        $normalizedParseErrors = New-Object 'System.Collections.Generic.List[object]'
        foreach ($err in @($parsed.ParseErrors)) {
            if ($null -eq $err) { continue }
            $pathText = ''
            $errorText = ''
            if ($err.PSObject.Properties.Match('TrayItemPath').Count -gt 0) {
                $pathText = [string]$err.TrayItemPath
            }
            if ($err.PSObject.Properties.Match('Error').Count -gt 0) {
                $errorText = [string]$err.Error
            }
            elseif ($err -is [string]) {
                $errorText = [string]$err
            }

            if ([string]::IsNullOrWhiteSpace($pathText) -and [string]::IsNullOrWhiteSpace($errorText)) {
                continue
            }

            $normalizedParseErrors.Add([pscustomobject]@{
                    TrayItemPath = $pathText
                    Error = $errorText
                }) | Out-Null
        }
        $parseErrorCount = [int]$normalizedParseErrors.Count
        $parseErrorArray = if ($parseErrorCount -gt 0) { @($normalizedParseErrors.ToArray()) } else { @() }

        $resolvedTrayItemKey = [string]$parsed.TrayItemKey
        if ($resolvedTrayItemKey -eq '__S4TI_ALL__') {
            $resolvedTrayItemKey = ''
        }

        return [pscustomobject]@{
            TrayPath = [string]$parsed.TrayPath
            TrayItemKey = $resolvedTrayItemKey
            TrayFileCount = [int]$parsed.TrayFileCount
            ObjectIds = $objectSet
            ResourceKeys = $resourceKeySet
            LotTraitIds = $lotTraitSet
            ObjectCount = $objectSet.Count
            ResourceKeyCount = $resourceKeySet.Count
            LotTraitCount = $lotTraitSet.Count
            ParseErrorCount = $parseErrorCount
            ParseErrors = $parseErrorArray
            S4tiPath = $s4tiRoot
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempScript) {
            Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $tempJson) {
            Remove-Item -LiteralPath $tempJson -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-SimsPackageStrictMatchProfile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[UInt64]]$ObjectIds,

        [Parameter()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$ResourceKeys,

        [Parameter()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[UInt64]]$LotTraitIds,

        [Parameter()]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[UInt64]]$CandidateInstanceIds
    )

    $objCatalogType = [UInt32]832458525
    $columnType = [UInt32]493744591
    $dataType = [UInt32]1415235194
    $lotTraitGroup = [UInt32]1935269

    if ($null -eq $ObjectIds) {
        $ObjectIds = New-Object 'System.Collections.Generic.HashSet[UInt64]'
    }
    if ($null -eq $ResourceKeys) {
        $ResourceKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    }
    if ($null -eq $LotTraitIds) {
        $LotTraitIds = New-Object 'System.Collections.Generic.HashSet[UInt64]'
    }
    if ($null -eq $CandidateInstanceIds) {
        $CandidateInstanceIds = New-Object 'System.Collections.Generic.HashSet[UInt64]'
    }

    $stream = $null
    $reader = $null
    try {
        $stream = [System.IO.File]::OpenRead($PackagePath)
        $reader = [System.IO.BinaryReader]::new($stream)

        if ($stream.Length -lt 96) {
            return [pscustomobject]@{
                PackagePath = $PackagePath
                EntryCount = 0
                MatchCount = 0
                MatchObjectCount = 0
                MatchResourceKeyCount = 0
                MatchLotTraitCount = 0
                MatchCandidateInstanceCount = 0
                ParseError = 'HeaderTooShort'
            }
        }

        $magic = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
        if ($magic -ne 'DBPF') {
            return [pscustomobject]@{
                PackagePath = $PackagePath
                EntryCount = 0
                MatchCount = 0
                MatchObjectCount = 0
                MatchResourceKeyCount = 0
                MatchLotTraitCount = 0
                MatchCandidateInstanceCount = 0
                ParseError = 'NotDBPF'
            }
        }

        $reader.BaseStream.Position = 36
        $indexEntryCount = [UInt32]$reader.ReadUInt32()
        $indexOffsetShort = [UInt32]$reader.ReadUInt32()
        $indexSize = [UInt32]$reader.ReadUInt32()

        $reader.BaseStream.Position = 64
        $indexOffsetLong = [UInt64]$reader.ReadUInt64()
        $indexOffset = if ($indexOffsetShort -ne 0) { [UInt64]$indexOffsetShort } else { $indexOffsetLong }

        if ($indexEntryCount -eq 0 -or $indexOffset -eq 0) {
            return [pscustomobject]@{
                PackagePath = $PackagePath
                EntryCount = 0
                MatchCount = 0
                MatchObjectCount = 0
                MatchResourceKeyCount = 0
                MatchLotTraitCount = 0
                MatchCandidateInstanceCount = 0
                ParseError = $null
            }
        }

        if ($indexOffset -ge [UInt64]$stream.Length) {
            return [pscustomobject]@{
                PackagePath = $PackagePath
                EntryCount = 0
                MatchCount = 0
                MatchObjectCount = 0
                MatchResourceKeyCount = 0
                MatchLotTraitCount = 0
                MatchCandidateInstanceCount = 0
                ParseError = 'IndexOffsetOutOfRange'
            }
        }

        $indexEnd = $indexOffset + [UInt64]$indexSize
        if ($indexEnd -gt [UInt64]$stream.Length) {
            $indexEnd = [UInt64]$stream.Length
        }

        $reader.BaseStream.Position = [Int64]$indexOffset
        $indexFlags = [UInt32]$reader.ReadUInt32()

        $hasConstType = (($indexFlags -band 0x1) -ne 0)
        $hasConstGroup = (($indexFlags -band 0x2) -ne 0)
        $hasConstInstanceHigh = (($indexFlags -band 0x4) -ne 0)

        $constType = [UInt32]0
        if ($hasConstType) {
            $constType = [UInt32]$reader.ReadUInt32()
        }
        $constGroup = [UInt32]0
        if ($hasConstGroup) {
            $constGroup = [UInt32]$reader.ReadUInt32()
        }
        $constInstanceHigh = [UInt32]0
        if ($hasConstInstanceHigh) {
            $constInstanceHigh = [UInt32]$reader.ReadUInt32()
        }

        $matchObjects = New-Object 'System.Collections.Generic.HashSet[UInt64]'
        $matchResourceKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        $matchLotTraits = New-Object 'System.Collections.Generic.HashSet[UInt64]'
        $matchCandidateInstances = New-Object 'System.Collections.Generic.HashSet[UInt64]'
        $entryRead = 0

        for ($i = 0; $i -lt $indexEntryCount; $i++) {
            if ([UInt64]$reader.BaseStream.Position -ge $indexEnd) { break }

            $type = if ($hasConstType) {
                $constType
            }
            else {
                if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
                [UInt32]$reader.ReadUInt32()
            }

            $group = if ($hasConstGroup) {
                $constGroup
            }
            else {
                if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
                [UInt32]$reader.ReadUInt32()
            }

            $instanceHigh = if ($hasConstInstanceHigh) {
                $constInstanceHigh
            }
            else {
                if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
                [UInt32]$reader.ReadUInt32()
            }

            if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
            $instanceLow = [UInt32]$reader.ReadUInt32()
            $instanceId = ([UInt64]$instanceHigh -shl 32) -bor [UInt64]$instanceLow
            $entryRead++

            if (($type -eq $objCatalogType -or $type -eq $columnType) -and $ObjectIds.Contains([UInt64]$instanceId)) {
                [void]$matchObjects.Add([UInt64]$instanceId)
            }

            if ($ResourceKeys.Count -gt 0) {
                $tgiHex = ConvertTo-SimsTgiHex -Type $type -Group $group -Instance $instanceId
                if ($ResourceKeys.Contains($tgiHex)) {
                    [void]$matchResourceKeys.Add($tgiHex)
                }
            }

            if ($LotTraitIds.Count -gt 0 -and $type -eq $dataType -and $group -eq $lotTraitGroup -and $LotTraitIds.Contains([UInt64]$instanceId)) {
                [void]$matchLotTraits.Add([UInt64]$instanceId)
            }

            if ($CandidateInstanceIds.Count -gt 0 -and $CandidateInstanceIds.Contains([UInt64]$instanceId)) {
                [void]$matchCandidateInstances.Add([UInt64]$instanceId)
            }

            if (([UInt64]$reader.BaseStream.Position + 12) -gt $indexEnd) { break }
            [void]$reader.ReadUInt32()
            $packedSizeWithFlag = [UInt32]$reader.ReadUInt32()
            [void]$reader.ReadUInt32()

            $isExtended = (($packedSizeWithFlag -band 0x80000000) -ne 0)
            if ($isExtended) {
                if (([UInt64]$reader.BaseStream.Position + 4) -gt $indexEnd) { break }
                [void]$reader.ReadUInt16()
                [void]$reader.ReadUInt16()
            }
        }

        $matchCount = $matchObjects.Count + $matchResourceKeys.Count + $matchLotTraits.Count + $matchCandidateInstances.Count
        return [pscustomobject]@{
            PackagePath = $PackagePath
            EntryCount = $entryRead
            MatchCount = $matchCount
            MatchObjectCount = $matchObjects.Count
            MatchResourceKeyCount = $matchResourceKeys.Count
            MatchLotTraitCount = $matchLotTraits.Count
            MatchCandidateInstanceCount = $matchCandidateInstances.Count
            ParseError = $null
        }
    }
    catch {
        return [pscustomobject]@{
            PackagePath = $PackagePath
            EntryCount = 0
            MatchCount = 0
            MatchObjectCount = 0
            MatchResourceKeyCount = 0
            MatchLotTraitCount = 0
            MatchCandidateInstanceCount = 0
            ParseError = $_.Exception.Message
        }
    }
    finally {
        if ($reader) { $reader.Dispose() }
        elseif ($stream) { $stream.Dispose() }
    }
}

function Get-SimsTrayItemSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TrayPath,

        [Parameter()]
        [string[]]$TrayExtensions = @('.trayitem', '.blueprint', '.bpi', '.room', '.rmi', '.householdbinary', '.hhi', '.sgi')
    )

    $trayRoot = (Resolve-Path -LiteralPath $TrayPath -ErrorAction Stop).ProviderPath
    $extSet = New-SimsTrayExtensionSet -TrayExtensions $TrayExtensions
    $files = @(Get-ChildItem -LiteralPath $trayRoot -File | Where-Object { $extSet.Contains($_.Extension) } | Sort-Object Name)

    $groups = @{}
    foreach ($file in $files) {
        $identity = Get-SimsTrayFileIdentity -File $file
        $key = if ($identity.ParseSuccess) { $identity.InstanceHex } else { $file.BaseName }
        if (-not $groups.ContainsKey($key)) {
            $groups[$key] = [pscustomobject]@{
                TrayItemKey = $key
                FileCount = 0
                TotalBytes = [Int64]0
                LatestWriteTimeUtc = [DateTime]::MinValue
                ResourceTypes = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
                Extensions = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
            }
        }

        $group = $groups[$key]
        $group.FileCount++
        $group.TotalBytes += [Int64]$file.Length
        if ($file.LastWriteTimeUtc -gt $group.LatestWriteTimeUtc) {
            $group.LatestWriteTimeUtc = $file.LastWriteTimeUtc
        }

        if ($identity.ParseSuccess -and -not [string]::IsNullOrWhiteSpace($identity.TypeHex)) {
            [void]$group.ResourceTypes.Add($identity.TypeHex)
        }
        [void]$group.Extensions.Add($file.Extension)
    }

    $rows = foreach ($kv in $groups.GetEnumerator()) {
        [pscustomobject]@{
            TrayItemKey = $kv.Value.TrayItemKey
            FileCount = $kv.Value.FileCount
            TotalBytes = $kv.Value.TotalBytes
            TotalMB = [Math]::Round(($kv.Value.TotalBytes / 1MB), 4)
            LatestWriteTimeUtc = $kv.Value.LatestWriteTimeUtc
            ResourceTypes = (@($kv.Value.ResourceTypes | Sort-Object) -join ',')
            Extensions = (@($kv.Value.Extensions | Sort-Object) -join ',')
        }
    }

    return @(
        $rows | Sort-Object `
            @{ Expression = 'LatestWriteTimeUtc'; Descending = $true }, `
            @{ Expression = 'FileCount'; Descending = $true }, `
            @{ Expression = 'TotalBytes'; Descending = $true }, `
            @{ Expression = 'TrayItemKey'; Descending = $false }
    )
}

function Get-SimsTrayPresetType {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Extensions
    )

    $extSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($ext in $Extensions) {
        if ([string]::IsNullOrWhiteSpace($ext)) { continue }
        [void]$extSet.Add($ext)
    }

    $hasLot = $extSet.Contains('.blueprint') -or $extSet.Contains('.bpi')
    $hasRoom = $extSet.Contains('.room') -or $extSet.Contains('.rmi')
    $hasHousehold = $extSet.Contains('.householdbinary') -or $extSet.Contains('.hhi') -or $extSet.Contains('.sgi')

    $bucketCount = 0
    if ($hasLot) { $bucketCount++ }
    if ($hasRoom) { $bucketCount++ }
    if ($hasHousehold) { $bucketCount++ }

    if ($bucketCount -gt 1) { return 'Mixed' }
    if ($hasLot) { return 'Lot' }
    if ($hasRoom) { return 'Room' }
    if ($hasHousehold) { return 'Household' }
    if ($extSet.Contains('.trayitem')) { return 'GenericTray' }
    return 'Unknown'
}

function Get-SimsTrayPreviewItems {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TrayPath,

        [Parameter()]
        [string]$TrayItemKey = '',

        [Parameter()]
        [ValidateRange(1, 50000)]
        [int]$TopN = 500,

        [Parameter()]
        [switch]$IncludeFiles,

        [Parameter()]
        [ValidateRange(1, 200)]
        [int]$MaxFilesPerItem = 12,

        [Parameter()]
        [string[]]$TrayExtensions = @('.trayitem', '.blueprint', '.bpi', '.room', '.rmi', '.householdbinary', '.hhi', '.sgi')
    )

    $trayRoot = (Resolve-Path -LiteralPath $TrayPath -ErrorAction Stop).ProviderPath
    $extSet = New-SimsTrayExtensionSet -TrayExtensions $TrayExtensions
    $allTrayFiles = @(Get-ChildItem -LiteralPath $trayRoot -File | Where-Object { $extSet.Contains($_.Extension) } | Sort-Object Name)
    $selection = Resolve-SimsTraySelection -Files $allTrayFiles -TrayItemKey $TrayItemKey
    $files = @($selection.SelectedFiles)

    $groups = @{}
    foreach ($file in $files) {
        $identity = Get-SimsTrayFileIdentity -File $file
        $key = if ($identity.ParseSuccess) { $identity.InstanceHex } else { $file.BaseName }

        if (-not $groups.ContainsKey($key)) {
            $groups[$key] = [pscustomobject]@{
                TrayItemKey = $key
                FileCount = 0
                TotalBytes = [Int64]0
                LatestWriteTimeUtc = [DateTime]::MinValue
                ResourceTypes = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
                Extensions = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
                FileNames = New-Object 'System.Collections.Generic.List[string]'
            }
        }

        $group = $groups[$key]
        $group.FileCount++
        $group.TotalBytes += [Int64]$file.Length
        if ($file.LastWriteTimeUtc -gt $group.LatestWriteTimeUtc) {
            $group.LatestWriteTimeUtc = $file.LastWriteTimeUtc
        }

        if ($identity.ParseSuccess -and -not [string]::IsNullOrWhiteSpace($identity.TypeHex)) {
            [void]$group.ResourceTypes.Add($identity.TypeHex)
        }
        [void]$group.Extensions.Add($file.Extension)

        if ($IncludeFiles -and $group.FileNames.Count -lt $MaxFilesPerItem) {
            $group.FileNames.Add($file.Name)
        }
    }

    $rows = foreach ($kv in $groups.GetEnumerator()) {
        $sortedExt = @($kv.Value.Extensions | Sort-Object)
        $latestLocal = if ($kv.Value.LatestWriteTimeUtc -gt [DateTime]::MinValue) {
            $kv.Value.LatestWriteTimeUtc.ToLocalTime()
        }
        else {
            [DateTime]::MinValue
        }

        [pscustomobject]@{
            TrayItemKey = $kv.Value.TrayItemKey
            PresetType = Get-SimsTrayPresetType -Extensions $sortedExt
            FileCount = $kv.Value.FileCount
            TotalBytes = $kv.Value.TotalBytes
            TotalMB = [Math]::Round(($kv.Value.TotalBytes / 1MB), 4)
            LatestWriteTimeUtc = $kv.Value.LatestWriteTimeUtc
            LatestWriteTimeLocal = $latestLocal
            ResourceTypes = (@($kv.Value.ResourceTypes | Sort-Object) -join ',')
            Extensions = ($sortedExt -join ',')
            FileListPreview = if ($IncludeFiles) { (@($kv.Value.FileNames) -join '|') } else { '' }
            TrayPath = $trayRoot
        }
    }

    return @(
        $rows | Sort-Object `
            @{ Expression = 'LatestWriteTimeUtc'; Descending = $true }, `
            @{ Expression = 'FileCount'; Descending = $true }, `
            @{ Expression = 'TotalBytes'; Descending = $true }, `
            @{ Expression = 'TrayItemKey'; Descending = $false } | Select-Object -First $TopN
    )
}

function Get-SimsTrayCandidateInstanceMap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TrayPath,

        [Parameter()]
        [string[]]$TrayExtensions = @('.trayitem', '.blueprint', '.bpi', '.room', '.rmi', '.householdbinary', '.hhi', '.sgi'),

        [Parameter()]
        [ValidateRange(1, 100)]
        [int]$CandidateMinFrequency = 1,

        [Parameter()]
        [string]$TrayItemKey = '',

        [Parameter()]
        [ValidateRange(0, 16)]
        [int]$RawScanStep = 0
    )

    $trayRoot = (Resolve-Path -LiteralPath $TrayPath -ErrorAction Stop).ProviderPath
    $extSet = New-SimsTrayExtensionSet -TrayExtensions $TrayExtensions
    $allTrayFiles = @(Get-ChildItem -LiteralPath $trayRoot -File | Where-Object { $extSet.Contains($_.Extension) } | Sort-Object Name)
    $selection = Resolve-SimsTraySelection -Files $allTrayFiles -TrayItemKey $TrayItemKey
    $trayFiles = @($selection.SelectedFiles)

    $candidateFreq = New-Object 'System.Collections.Generic.Dictionary[UInt64,int]'
    foreach ($file in $trayFiles) {
        $identity = Get-SimsTrayFileIdentity -File $file
        if ($identity.ParseSuccess) {
            $instanceFromName = [Convert]::ToUInt64($identity.InstanceHex.Substring(2), 16)
            if ($candidateFreq.ContainsKey($instanceFromName)) {
                $candidateFreq[$instanceFromName] = $candidateFreq[$instanceFromName] + 1
            }
            else {
                $candidateFreq[$instanceFromName] = 1
            }
        }

        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        $protobufCandidates = Get-SimsProtobufInstanceCandidates -Bytes $bytes
        foreach ($id in $protobufCandidates) {
            if ($candidateFreq.ContainsKey([UInt64]$id)) {
                $candidateFreq[[UInt64]$id] = $candidateFreq[[UInt64]$id] + 1
            }
            else {
                $candidateFreq[[UInt64]$id] = 1
            }
        }

        if ($RawScanStep -gt 0) {
            for ($i = 0; $i -le ($bytes.Length - 8); $i += $RawScanStep) {
                $id = [BitConverter]::ToUInt64($bytes, $i)
                if (-not (Test-SimsPotentialInstanceId -Value $id)) { continue }

                if ($candidateFreq.ContainsKey($id)) {
                    $candidateFreq[$id] = $candidateFreq[$id] + 1
                }
                else {
                    $candidateFreq[$id] = 1
                }
            }
        }
    }

    $filtered = New-Object 'System.Collections.Generic.Dictionary[UInt64,int]'
    foreach ($kv in $candidateFreq.GetEnumerator()) {
        if ($kv.Value -ge $CandidateMinFrequency) {
            $filtered[$kv.Key] = $kv.Value
        }
    }

    return [pscustomobject]@{
        TrayPath = $trayRoot
        TrayItemKey = $selection.ResolvedTrayItemKey
        TrayFileCount = $trayFiles.Count
        CandidateCountRaw = $candidateFreq.Count
        CandidateCountFiltered = $filtered.Count
        CandidateMap = $filtered
    }
}

function Find-SimsTrayModDependencies {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TrayPath,

        [Parameter(Mandatory = $true)]
        [string]$ModsPath,

        [Parameter()]
        [ValidateRange(1, 100)]
        [int]$CandidateMinFrequency = 1,

        [Parameter()]
        [string]$TrayItemKey = '',

        [Parameter()]
        [ValidateRange(1, 1000)]
        [int]$MinMatchCount = 1,

        [Parameter()]
        [ValidateRange(1, 10000)]
        [int]$TopN = 200,

        [Parameter()]
        [int]$MaxPackageCount = 0,

        [Parameter()]
        [ValidateRange(0, 16)]
        [int]$RawScanStep = 0,

        [Parameter()]
        [switch]$IncludeUnusedPackages,

        [Parameter()]
        [ValidateSet('Legacy', 'StrictS4TI')]
        [string]$AnalysisMode = 'StrictS4TI',

        [Parameter()]
        [ValidateRange(1, 64)]
        [int]$ProbeWorkerCount = 8,

        [Parameter()]
        [string]$S4tiPath = 'D:\SIM4\Sims 4 Tray Importer (S4TI)'
    )

    $parallelAvailable = ($PSVersionTable.PSVersion.Major -ge 7)
    $modsRoot = (Resolve-Path -LiteralPath $ModsPath -ErrorAction Stop).ProviderPath
    $packages = @(Get-ChildItem -LiteralPath $modsRoot -Recurse -File -Filter '*.package' | Sort-Object FullName)
    if ($MaxPackageCount -gt 0) {
        $packages = @($packages | Select-Object -First $MaxPackageCount)
    }
    $packageItems = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $packages.Count; $i++) {
        $pkg = $packages[$i]
        $packageItems.Add([pscustomobject]@{
                PackageIndex = [int]$i
                PackagePath = [string]$pkg.FullName
                PackageLength = [Int64]$pkg.Length
            }) | Out-Null
    }

    $effectiveProbeWorkerCount = 1
    if ($parallelAvailable -and $ProbeWorkerCount -gt 1 -and $packageItems.Count -gt 1) {
        $effectiveProbeWorkerCount = [Math]::Min($ProbeWorkerCount, $packageItems.Count)
    }

    $results = New-Object System.Collections.Generic.List[object]
    $unusedResults = New-Object System.Collections.Generic.List[object]
    $parseErrors = New-Object System.Collections.Generic.List[object]
    $processed = 0
    [Int64]$unusedTotalBytes = 0

    if ($AnalysisMode -eq 'StrictS4TI') {
        $strictProbe = Get-SimsStrictTrayReferenceSet -TrayPath $TrayPath -TrayItemKey $TrayItemKey -S4tiPath $S4tiPath
        $householdProbe = Get-SimsTrayCandidateInstanceMap -TrayPath $TrayPath -TrayExtensions @('.householdbinary', '.hhi', '.sgi') -CandidateMinFrequency 1 -TrayItemKey $TrayItemKey -RawScanStep 0
        $strictCandidateInstances = New-Object 'System.Collections.Generic.HashSet[UInt64]'
        foreach ($kv in $householdProbe.CandidateMap.GetEnumerator()) {
            [void]$strictCandidateInstances.Add([UInt64]$kv.Key)
        }

        foreach ($trayErr in @($strictProbe.ParseErrors)) {
            if ($null -eq $trayErr) { continue }
            $trayItemPath = ''
            $trayErrorText = ''
            if ($trayErr.PSObject.Properties.Match('TrayItemPath').Count -gt 0) {
                $trayItemPath = [string]$trayErr.TrayItemPath
            }
            elseif ($trayErr.PSObject.Properties.Match('PackagePath').Count -gt 0) {
                $trayItemPath = [string]$trayErr.PackagePath
            }
            else {
                $trayItemPath = ''
            }

            if ($trayErr.PSObject.Properties.Match('Error').Count -gt 0) {
                $trayErrorText = [string]$trayErr.Error
            }
            else {
                $trayErrorText = [string]$trayErr
            }

            $parseErrors.Add([pscustomobject]@{
                    PackagePath = $trayItemPath
                    Error = ("TrayParse: {0}" -f $trayErrorText)
                })
        }

        $strictProfiles = @()
        if ($effectiveProbeWorkerCount -gt 1) {
            $strictObjectIds = @($strictProbe.ObjectIds)
            $strictResourceKeys = @($strictProbe.ResourceKeys)
            $strictLotTraitIds = @($strictProbe.LotTraitIds)
            $strictCandidateIds = @($strictCandidateInstances)
            $modulePath = $PSCommandPath

            $strictChunks = New-Object System.Collections.Generic.List[object]
            $strictChunkBuckets = @()
            for ($i = 0; $i -lt $effectiveProbeWorkerCount; $i++) {
                $strictChunkBuckets += , (New-Object System.Collections.Generic.List[object])
            }
            foreach ($item in $packageItems) {
                $bucketIndex = [int]$item.PackageIndex % $effectiveProbeWorkerCount
                $strictChunkBuckets[$bucketIndex].Add($item) | Out-Null
            }
            for ($i = 0; $i -lt $strictChunkBuckets.Count; $i++) {
                if ($strictChunkBuckets[$i].Count -eq 0) { continue }
                $strictChunks.Add([pscustomobject]@{
                        ChunkIndex = [int]$i
                        Items = @($strictChunkBuckets[$i].ToArray())
                    }) | Out-Null
            }

            $strictProfiles = @(
                $strictChunks | ForEach-Object -Parallel {
                    Import-Module -Name $using:modulePath -Force

                    $objectSet = New-Object 'System.Collections.Generic.HashSet[UInt64]'
                    foreach ($value in $using:strictObjectIds) {
                        [void]$objectSet.Add([UInt64]$value)
                    }
                    $resourceKeySet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
                    foreach ($value in $using:strictResourceKeys) {
                        [void]$resourceKeySet.Add([string]$value)
                    }
                    $lotTraitSet = New-Object 'System.Collections.Generic.HashSet[UInt64]'
                    foreach ($value in $using:strictLotTraitIds) {
                        [void]$lotTraitSet.Add([UInt64]$value)
                    }
                    $candidateSet = New-Object 'System.Collections.Generic.HashSet[UInt64]'
                    foreach ($value in $using:strictCandidateIds) {
                        [void]$candidateSet.Add([UInt64]$value)
                    }

                    $rows = New-Object System.Collections.Generic.List[object]
                    foreach ($item in @($_.Items)) {
                        try {
                            $profile = Get-SimsPackageStrictMatchProfile -PackagePath ([string]$item.PackagePath) -ObjectIds $objectSet -ResourceKeys $resourceKeySet -LotTraitIds $lotTraitSet -CandidateInstanceIds $candidateSet
                            $rows.Add([pscustomobject]@{
                                    PackageIndex = [int]$item.PackageIndex
                                    PackagePath = [string]$item.PackagePath
                                    PackageLength = [Int64]$item.PackageLength
                                    EntryCount = [int]$profile.EntryCount
                                    MatchCount = [int]$profile.MatchCount
                                    MatchObjectCount = [int]$profile.MatchObjectCount
                                    MatchResourceKeyCount = [int]$profile.MatchResourceKeyCount
                                    MatchLotTraitCount = [int]$profile.MatchLotTraitCount
                                    MatchCandidateInstanceCount = [int]$profile.MatchCandidateInstanceCount
                                    ParseError = if ([string]::IsNullOrWhiteSpace([string]$profile.ParseError)) { '' } else { [string]$profile.ParseError }
                                }) | Out-Null
                        }
                        catch {
                            $rows.Add([pscustomobject]@{
                                    PackageIndex = [int]$item.PackageIndex
                                    PackagePath = [string]$item.PackagePath
                                    PackageLength = [Int64]$item.PackageLength
                                    EntryCount = 0
                                    MatchCount = 0
                                    MatchObjectCount = 0
                                    MatchResourceKeyCount = 0
                                    MatchLotTraitCount = 0
                                    MatchCandidateInstanceCount = 0
                                    ParseError = $_.Exception.Message
                                }) | Out-Null
                        }
                    }

                    return @($rows.ToArray())
                } -ThrottleLimit $effectiveProbeWorkerCount
            )
        }
        else {
            $strictRows = New-Object System.Collections.Generic.List[object]
            foreach ($item in $packageItems) {
                try {
                    $profile = Get-SimsPackageStrictMatchProfile -PackagePath ([string]$item.PackagePath) -ObjectIds $strictProbe.ObjectIds -ResourceKeys $strictProbe.ResourceKeys -LotTraitIds $strictProbe.LotTraitIds -CandidateInstanceIds $strictCandidateInstances
                    $strictRows.Add([pscustomobject]@{
                            PackageIndex = [int]$item.PackageIndex
                            PackagePath = [string]$item.PackagePath
                            PackageLength = [Int64]$item.PackageLength
                            EntryCount = [int]$profile.EntryCount
                            MatchCount = [int]$profile.MatchCount
                            MatchObjectCount = [int]$profile.MatchObjectCount
                            MatchResourceKeyCount = [int]$profile.MatchResourceKeyCount
                            MatchLotTraitCount = [int]$profile.MatchLotTraitCount
                            MatchCandidateInstanceCount = [int]$profile.MatchCandidateInstanceCount
                            ParseError = if ([string]::IsNullOrWhiteSpace([string]$profile.ParseError)) { '' } else { [string]$profile.ParseError }
                        }) | Out-Null
                }
                catch {
                    $strictRows.Add([pscustomobject]@{
                            PackageIndex = [int]$item.PackageIndex
                            PackagePath = [string]$item.PackagePath
                            PackageLength = [Int64]$item.PackageLength
                            EntryCount = 0
                            MatchCount = 0
                            MatchObjectCount = 0
                            MatchResourceKeyCount = 0
                            MatchLotTraitCount = 0
                            MatchCandidateInstanceCount = 0
                            ParseError = $_.Exception.Message
                        }) | Out-Null
                }
            }
            $strictProfiles = @($strictRows.ToArray())
        }

        $strictProfiles = @($strictProfiles | Sort-Object PackageIndex)
        $processed = $strictProfiles.Count

        foreach ($profile in $strictProfiles) {
            if (-not [string]::IsNullOrWhiteSpace([string]$profile.ParseError)) {
                $parseErrors.Add([pscustomobject]@{
                        PackagePath = [string]$profile.PackagePath
                        Error = [string]$profile.ParseError
                    })
                continue
            }

            $matchCount = [int]$profile.MatchCount
            $weightedScore = $matchCount
            $confidence = if ($matchCount -ge 8) {
                'High'
            }
            elseif ($matchCount -ge 3) {
                'Medium'
            }
            elseif ($matchCount -gt 0) {
                'Low'
            }
            else {
                'None'
            }

            if ($matchCount -eq 0) {
                $unusedTotalBytes += [Int64]$profile.PackageLength
            }

            $row = [pscustomobject]@{
                PackagePath = [string]$profile.PackagePath
                MatchInstanceCount = $matchCount
                MatchObjectCount = [int]$profile.MatchObjectCount
                MatchResourceKeyCount = [int]$profile.MatchResourceKeyCount
                MatchLotTraitCount = [int]$profile.MatchLotTraitCount
                MatchCandidateInstanceCount = [int]$profile.MatchCandidateInstanceCount
                WeightedScore = $weightedScore
                Confidence = $confidence
                PackageInstanceCount = [int]$profile.EntryCount
                MatchRatePct = if ([int]$profile.EntryCount -gt 0) {
                    [Math]::Round((100.0 * $matchCount / [double]$profile.EntryCount), 4)
                }
                else {
                    0.0
                }
                AnalysisMode = $AnalysisMode
            }

            if ($matchCount -ge $MinMatchCount) {
                $results.Add($row)
            }

            if ($IncludeUnusedPackages -and $matchCount -eq 0) {
                $unusedResults.Add($row)
            }
        }

        $sorted = @($results | Sort-Object WeightedScore, MatchInstanceCount -Descending | Select-Object -First $TopN)
        $parseErrorsArray = if ($parseErrors.Count -gt 0) { $parseErrors.ToArray() } else { @() }
        $sortedArray = if ($sorted) { @($sorted) } else { @() }
        $unusedArray = if ($IncludeUnusedPackages -and $unusedResults.Count -gt 0) { $unusedResults.ToArray() } else { @() }
        $candidateTotal = $strictProbe.ObjectCount + $strictProbe.ResourceKeyCount + $strictProbe.LotTraitCount + $strictCandidateInstances.Count

        return [pscustomobject]@{
            AnalysisMode = $AnalysisMode
            S4tiPath = $strictProbe.S4tiPath
            TrayPath = $strictProbe.TrayPath
            TrayItemKey = $strictProbe.TrayItemKey
            ModsPath = $modsRoot
            CandidateMinFrequency = $CandidateMinFrequency
            RawScanStep = $RawScanStep
            TrayFileCount = $strictProbe.TrayFileCount
            CandidateCountRaw = $candidateTotal
            CandidateCountFiltered = $candidateTotal
            SearchObjectCount = $strictProbe.ObjectCount
            SearchResourceKeyCount = $strictProbe.ResourceKeyCount
            SearchLotTraitCount = $strictProbe.LotTraitCount
            SearchCandidateInstanceCount = $strictCandidateInstances.Count
            PackageCount = $packages.Count
            PackageProcessed = $processed
            ProbeWorkerCount = $effectiveProbeWorkerCount
            ParseErrorCount = $parseErrors.Count
            ParseErrors = $parseErrorsArray
            Results = $sortedArray
            UnusedPackageCount = $unusedResults.Count
            UnusedResults = $unusedArray
            UnusedPackageTotalBytes = $unusedTotalBytes
            UnusedPackageTotalGB = [Math]::Round(($unusedTotalBytes / 1GB), 4)
        }
    }

    $trayProbe = Get-SimsTrayCandidateInstanceMap -TrayPath $TrayPath -CandidateMinFrequency $CandidateMinFrequency -TrayItemKey $TrayItemKey -RawScanStep $RawScanStep
    $candidateMap = $trayProbe.CandidateMap

    $legacyProfiles = @()
    if ($effectiveProbeWorkerCount -gt 1) {
        $candidatePairs = @(
            $candidateMap.GetEnumerator() | ForEach-Object {
                [pscustomobject]@{
                    Key = [UInt64]$_.Key
                    Frequency = [int]$_.Value
                }
            }
        )
        $modulePath = $PSCommandPath

        $legacyChunks = New-Object System.Collections.Generic.List[object]
        $legacyChunkBuckets = @()
        for ($i = 0; $i -lt $effectiveProbeWorkerCount; $i++) {
            $legacyChunkBuckets += , (New-Object System.Collections.Generic.List[object])
        }
        foreach ($item in $packageItems) {
            $bucketIndex = [int]$item.PackageIndex % $effectiveProbeWorkerCount
            $legacyChunkBuckets[$bucketIndex].Add($item) | Out-Null
        }
        for ($i = 0; $i -lt $legacyChunkBuckets.Count; $i++) {
            if ($legacyChunkBuckets[$i].Count -eq 0) { continue }
            $legacyChunks.Add([pscustomobject]@{
                    ChunkIndex = [int]$i
                    Items = @($legacyChunkBuckets[$i].ToArray())
                }) | Out-Null
        }

        $legacyProfiles = @(
            $legacyChunks | ForEach-Object -Parallel {
                Import-Module -Name $using:modulePath -Force

                $candidateDict = New-Object 'System.Collections.Generic.Dictionary[UInt64,int]'
                foreach ($pair in $using:candidatePairs) {
                    $key = [UInt64]$pair.Key
                    if (-not $candidateDict.ContainsKey($key)) {
                        $candidateDict[$key] = [int]$pair.Frequency
                    }
                }

                $rows = New-Object System.Collections.Generic.List[object]
                foreach ($item in @($_.Items)) {
                    try {
                        $parsed = Get-SimsPackageInstanceSet -PackagePath ([string]$item.PackagePath)
                        if (-not [string]::IsNullOrWhiteSpace([string]$parsed.ParseError)) {
                            $rows.Add([pscustomobject]@{
                                    PackageIndex = [int]$item.PackageIndex
                                    PackagePath = [string]$item.PackagePath
                                    PackageLength = [Int64]$item.PackageLength
                                    MatchCount = 0
                                    WeightedScore = 0
                                    PackageInstanceCount = 0
                                    ParseError = [string]$parsed.ParseError
                                }) | Out-Null
                            continue
                        }

                        $matchCount = 0
                        $weightedScore = 0
                        foreach ($id in @($parsed.Instances)) {
                            $freq = 0
                            if ($candidateDict.TryGetValue([UInt64]$id, [ref]$freq)) {
                                $matchCount++
                                $weightedScore += $freq
                            }
                        }

                        $rows.Add([pscustomobject]@{
                                PackageIndex = [int]$item.PackageIndex
                                PackagePath = [string]$item.PackagePath
                                PackageLength = [Int64]$item.PackageLength
                                MatchCount = [int]$matchCount
                                WeightedScore = [int]$weightedScore
                                PackageInstanceCount = [int]$parsed.Instances.Count
                                ParseError = ''
                            }) | Out-Null
                    }
                    catch {
                        $rows.Add([pscustomobject]@{
                                PackageIndex = [int]$item.PackageIndex
                                PackagePath = [string]$item.PackagePath
                                PackageLength = [Int64]$item.PackageLength
                                MatchCount = 0
                                WeightedScore = 0
                                PackageInstanceCount = 0
                                ParseError = $_.Exception.Message
                            }) | Out-Null
                    }
                }

                return @($rows.ToArray())
            } -ThrottleLimit $effectiveProbeWorkerCount
        )
    }
    else {
        $legacyRows = New-Object System.Collections.Generic.List[object]
        foreach ($item in $packageItems) {
            try {
                $parsed = Get-SimsPackageInstanceSet -PackagePath ([string]$item.PackagePath)
                if (-not [string]::IsNullOrWhiteSpace([string]$parsed.ParseError)) {
                    $legacyRows.Add([pscustomobject]@{
                            PackageIndex = [int]$item.PackageIndex
                            PackagePath = [string]$item.PackagePath
                            PackageLength = [Int64]$item.PackageLength
                            MatchCount = 0
                            WeightedScore = 0
                            PackageInstanceCount = 0
                            ParseError = [string]$parsed.ParseError
                        }) | Out-Null
                    continue
                }

                $matchCount = 0
                $weightedScore = 0
                foreach ($id in @($parsed.Instances)) {
                    $freq = 0
                    if ($candidateMap.TryGetValue([UInt64]$id, [ref]$freq)) {
                        $matchCount++
                        $weightedScore += $freq
                    }
                }

                $legacyRows.Add([pscustomobject]@{
                        PackageIndex = [int]$item.PackageIndex
                        PackagePath = [string]$item.PackagePath
                        PackageLength = [Int64]$item.PackageLength
                        MatchCount = [int]$matchCount
                        WeightedScore = [int]$weightedScore
                        PackageInstanceCount = [int]$parsed.Instances.Count
                        ParseError = ''
                    }) | Out-Null
            }
            catch {
                $legacyRows.Add([pscustomobject]@{
                        PackageIndex = [int]$item.PackageIndex
                        PackagePath = [string]$item.PackagePath
                        PackageLength = [Int64]$item.PackageLength
                        MatchCount = 0
                        WeightedScore = 0
                        PackageInstanceCount = 0
                        ParseError = $_.Exception.Message
                    }) | Out-Null
            }
        }
        $legacyProfiles = @($legacyRows.ToArray())
    }

    $legacyProfiles = @($legacyProfiles | Sort-Object PackageIndex)
    $processed = $legacyProfiles.Count

    foreach ($profile in $legacyProfiles) {
        if (-not [string]::IsNullOrWhiteSpace([string]$profile.ParseError)) {
            $parseErrors.Add([pscustomobject]@{
                    PackagePath = [string]$profile.PackagePath
                    Error = [string]$profile.ParseError
                })
            continue
        }

        $matchCount = [int]$profile.MatchCount
        $weightedScore = [int]$profile.WeightedScore
        $confidence = if ($matchCount -ge 8) {
            'High'
        }
        elseif ($matchCount -ge 3) {
            'Medium'
        }
        elseif ($matchCount -gt 0) {
            'Low'
        }
        else {
            'None'
        }

        if ($matchCount -eq 0) {
            $unusedTotalBytes += [Int64]$profile.PackageLength
        }

        $row = [pscustomobject]@{
            PackagePath = [string]$profile.PackagePath
            MatchInstanceCount = $matchCount
            MatchObjectCount = 0
            MatchResourceKeyCount = 0
            MatchLotTraitCount = 0
            MatchCandidateInstanceCount = 0
            WeightedScore = $weightedScore
            Confidence = $confidence
            PackageInstanceCount = [int]$profile.PackageInstanceCount
            MatchRatePct = if ([int]$profile.PackageInstanceCount -gt 0) {
                [Math]::Round((100.0 * $matchCount / [int]$profile.PackageInstanceCount), 4)
            }
            else {
                0.0
            }
            AnalysisMode = $AnalysisMode
        }

        if ($matchCount -ge $MinMatchCount) {
            $results.Add($row)
        }

        if ($IncludeUnusedPackages -and $matchCount -eq 0) {
            $unusedResults.Add($row)
        }
    }

    $sorted = @($results | Sort-Object WeightedScore, MatchInstanceCount -Descending | Select-Object -First $TopN)
    $parseErrorsArray = if ($parseErrors.Count -gt 0) { $parseErrors.ToArray() } else { @() }
    $sortedArray = if ($sorted) { @($sorted) } else { @() }
    $unusedArray = if ($IncludeUnusedPackages -and $unusedResults.Count -gt 0) { $unusedResults.ToArray() } else { @() }

    return [pscustomobject]@{
        AnalysisMode = $AnalysisMode
        S4tiPath = ''
        TrayPath = $trayProbe.TrayPath
        TrayItemKey = $trayProbe.TrayItemKey
        ModsPath = $modsRoot
        CandidateMinFrequency = $CandidateMinFrequency
        RawScanStep = $RawScanStep
        TrayFileCount = $trayProbe.TrayFileCount
        CandidateCountRaw = $trayProbe.CandidateCountRaw
        CandidateCountFiltered = $trayProbe.CandidateCountFiltered
        SearchObjectCount = 0
        SearchResourceKeyCount = 0
        SearchLotTraitCount = 0
        SearchCandidateInstanceCount = 0
        PackageCount = $packages.Count
        PackageProcessed = $processed
        ProbeWorkerCount = $effectiveProbeWorkerCount
        ParseErrorCount = $parseErrors.Count
        ParseErrors = $parseErrorsArray
        Results = $sortedArray
        UnusedPackageCount = $unusedResults.Count
        UnusedResults = $unusedArray
        UnusedPackageTotalBytes = $unusedTotalBytes
        UnusedPackageTotalGB = [Math]::Round(($unusedTotalBytes / 1GB), 4)
    }
}

Export-ModuleMember -Function Get-SimsPackageInstanceSet, Get-SimsPackageStrictMatchProfile, Get-SimsTrayItemSummary, Get-SimsTrayPreviewItems, Get-SimsTrayCandidateInstanceMap, Find-SimsTrayModDependencies
