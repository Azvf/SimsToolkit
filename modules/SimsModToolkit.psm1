Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$script:ConfigPath = Join-Path $script:ProjectRoot 'modules\SimsConfig.psm1'
if (Test-Path -LiteralPath $script:ConfigPath) {
    Import-Module -Name $script:ConfigPath -Force
}
$script:OrganizeScript = Join-Path $script:ProjectRoot 'scripts\fileops\organize-sims-zips.ps1'
$script:FlattenScript = Join-Path $script:ProjectRoot 'scripts\fileops\flatten-mods-into-top-folder.ps1'
$script:NormalizeScript = Join-Path $script:ProjectRoot 'scripts\fileops\normalize-name-only-folders.ps1'
$script:MergeScript = Join-Path $script:ProjectRoot 'scripts\fileops\merge-into-single-folder.ps1'
$script:TrayProbeScript = Join-Path $script:ProjectRoot 'scripts\analysis\tray-mod-dependency-probe.ps1'
$script:FindDuplicatesScript = Join-Path $script:ProjectRoot 'scripts\fileops\find-md5-duplicates.ps1'
$script:InvokeUtilsModule = Join-Path $PSScriptRoot 'SimsInvokeUtils.psm1'

if (-not (Test-Path -LiteralPath $script:InvokeUtilsModule)) {
    throw "Required module not found: $script:InvokeUtilsModule"
}
Import-Module -Name $script:InvokeUtilsModule -Force

$script:OrganizeOptionalForwardNames = Get-SimsForwardParameterNames -Profile 'OrganizeOptional'
$script:FileOpsSharedForwardNames = Get-SimsForwardParameterNames -Profile 'FileOpsShared'
$script:FindDupForwardNames = Get-SimsForwardParameterNames -Profile 'FindDup'

function Test-RequiredScript {
    param([Parameter(Mandatory = $true)][string]$ScriptPath)

    if (-not (Test-Path -LiteralPath $ScriptPath)) {
        throw "Required script not found: $ScriptPath"
    }
}

function Invoke-SimsScriptWithCommon {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][hashtable]$Invocation,
        [Parameter(Mandatory = $true)][object]$CallerBoundParameters,
        [Parameter(Mandatory = $true)][bool]$CallerWhatIfPreference,
        [Parameter()][string[]]$ForwardParameterNames
    )

    Test-RequiredScript -ScriptPath $ScriptPath

    if ($null -ne $ForwardParameterNames -and $ForwardParameterNames.Count -gt 0) {
        Add-SimsBoundParameters -Invocation $Invocation -CallerBoundParameters $CallerBoundParameters -Names $ForwardParameterNames
    }

    $common = Get-SimsCommonShouldProcessParameters -CallerBoundParameters $CallerBoundParameters -CallerWhatIfPreference:$CallerWhatIfPreference
    Add-SimsHashtableParameters -Target $Invocation -Source $common

    & $ScriptPath @Invocation
}

function Invoke-SimsOrganize {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter()]
        [string]$SourceDir = (Get-Location).Path,

        [Parameter()]
        [string]$ZipNamePattern = '*',

        [Parameter()]
        [string[]]$ArchiveExtensions = @('.zip', '.rar', '.7z'),

        [Parameter()]
        [string]$ModsRoot = (Get-SimsConfigDefault -Key 'ModsRoot'),

        [Parameter()]
        [AllowEmptyString()]
        [string]$UnifiedModsFolder = '',

        [Parameter()]
        [string]$TrayRoot = (Get-SimsConfigDefault -Key 'TrayRoot'),

        [Parameter()]
        [switch]$KeepZip,

        [Parameter()]
        [bool]$RecurseSource = $true,

        [Parameter()]
        [bool]$IncludeLooseSources = $true,

        [Parameter()]
        [string[]]$ModExtensions = (Get-SimsConfigDefault -Key 'ModExtensions'),

        [Parameter()]
        [switch]$VerifyContentOnNameConflict,

        [Parameter()]
        [ValidateRange(1024, 104857600)]
        [int]$PrefixHashBytes = (Get-SimsConfigDefault -Key 'PrefixHashBytes')
    )

    $invoke = @{
        SourceDir = $SourceDir
        ZipNamePattern = $ZipNamePattern
        ArchiveExtensions = $ArchiveExtensions
        ModsRoot = $ModsRoot
        UnifiedModsFolder = $UnifiedModsFolder
        TrayRoot = $TrayRoot
    }
    Invoke-SimsScriptWithCommon -ScriptPath $script:OrganizeScript -Invocation $invoke -CallerBoundParameters $PSBoundParameters -CallerWhatIfPreference:$WhatIfPreference -ForwardParameterNames $script:OrganizeOptionalForwardNames
}

function Invoke-SimsFlatten {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter()]
        [string]$RootPath = (Get-SimsConfigDefault -Key 'FlattenRootPath'),

        [Parameter()]
        [switch]$FlattenToRoot,

        [Parameter()]
        [switch]$SkipPruneEmptyDirs,

        [Parameter()]
        [switch]$ModFilesOnly,

        [Parameter()]
        [string[]]$ModExtensions = (Get-SimsConfigDefault -Key 'ModExtensions'),
        [Parameter()]
        [switch]$VerifyContentOnNameConflict,

        [Parameter()]
        [int]$PrefixHashBytes = (Get-SimsConfigDefault -Key 'PrefixHashBytes'),

        [Parameter()]
        [ValidateRange(1, 64)]
        [int]$HashWorkerCount = (Get-SimsConfigDefault -Key 'HashWorkerCount')
    )

    $invoke = @{
        RootPath = $RootPath
        FlattenToRoot = $FlattenToRoot
    }
    Invoke-SimsScriptWithCommon -ScriptPath $script:FlattenScript -Invocation $invoke -CallerBoundParameters $PSBoundParameters -CallerWhatIfPreference:$WhatIfPreference -ForwardParameterNames $script:FileOpsSharedForwardNames
}

function Invoke-SimsNormalize {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter()]
        [string]$RootPath = (Get-SimsConfigDefault -Key 'NormalizeRootPath')
    )

    $invoke = @{
        RootPath = $RootPath
    }
    Invoke-SimsScriptWithCommon -ScriptPath $script:NormalizeScript -Invocation $invoke -CallerBoundParameters $PSBoundParameters -CallerWhatIfPreference:$WhatIfPreference
}

function Invoke-SimsMerge {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$SourcePaths,

        [Parameter()]
        [string]$TargetPath = (Get-SimsConfigDefault -Key 'MergeTargetPath'),

        [Parameter()]
        [switch]$SkipPruneEmptyDirs,

        [Parameter()]
        [switch]$ModFilesOnly,

        [Parameter()]
        [string[]]$ModExtensions = (Get-SimsConfigDefault -Key 'ModExtensions'),

        [Parameter()]
        [switch]$VerifyContentOnNameConflict,

        [Parameter()]
        [int]$PrefixHashBytes = (Get-SimsConfigDefault -Key 'PrefixHashBytes'),

        [Parameter()]
        [ValidateRange(1, 64)]
        [int]$HashWorkerCount = (Get-SimsConfigDefault -Key 'HashWorkerCount')
    )

    $invoke = @{
        SourcePaths = $SourcePaths
        TargetPath = $TargetPath
    }
    Invoke-SimsScriptWithCommon -ScriptPath $script:MergeScript -Invocation $invoke -CallerBoundParameters $PSBoundParameters -CallerWhatIfPreference:$WhatIfPreference -ForwardParameterNames $script:FileOpsSharedForwardNames
}

function Invoke-SimsFindDuplicates {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter()]
        [string]$RootPath = (Get-SimsConfigDefault -Key 'FindDupRootPath'),

        [Parameter()]
        [string]$OutputCsv = '',

        [Parameter()]
        [bool]$Recurse = $true,

        [Parameter()]
        [switch]$Cleanup,

        [Parameter()]
        [switch]$ModFilesOnly,

        [Parameter()]
        [string[]]$ModExtensions = (Get-SimsConfigDefault -Key 'ModExtensions'),

        [Parameter()]
        [int]$PrefixHashBytes = (Get-SimsConfigDefault -Key 'PrefixHashBytes'),

        [Parameter()]
        [ValidateRange(1, 64)]
        [int]$HashWorkerCount = (Get-SimsConfigDefault -Key 'HashWorkerCount')
    )

    Test-RequiredScript -ScriptPath $script:FindDuplicatesScript

    $invoke = @{
        RootPath = $RootPath
        OutputCsv = $OutputCsv
        Recurse = $Recurse
        Cleanup = $Cleanup
    }
    Add-SimsBoundParameters -Invocation $invoke -CallerBoundParameters $PSBoundParameters -Names $script:FindDupForwardNames
    $common = Get-SimsCommonShouldProcessParameters -CallerBoundParameters $PSBoundParameters -CallerWhatIfPreference:$WhatIfPreference
    Add-SimsHashtableParameters -Target $invoke -Source $common

    & $script:FindDuplicatesScript @invoke
}

function Invoke-SimsTrayProbe {
    [CmdletBinding()]
    param(
        [Parameter()]
        [hashtable]$ProbeParameters = @{}
    )

    Test-RequiredScript -ScriptPath $script:TrayProbeScript

    if ($null -eq $ProbeParameters) {
        $ProbeParameters = @{}
    }

    & $script:TrayProbeScript @ProbeParameters
}

Export-ModuleMember -Function Invoke-SimsOrganize, Invoke-SimsFlatten, Invoke-SimsNormalize, Invoke-SimsMerge, Invoke-SimsFindDuplicates, Invoke-SimsTrayProbe
