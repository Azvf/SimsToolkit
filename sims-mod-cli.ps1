[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('organize', 'flatten', 'normalize', 'merge', 'finddup', 'trayprobe')]
    [string]$Action,

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
    [string]$FlattenRootPath = '',

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
    [int]$HashWorkerCount = 0,

    [Parameter()]
    [string]$NormalizeRootPath = '',

    [Parameter()]
    [string[]]$MergeSourcePaths,

    [Parameter()]
    [string]$MergeTargetPath = '',

    [Parameter()]
    [string]$FindDupRootPath = '',

    [Parameter()]
    [string]$FindDupOutputCsv = '',

    [Parameter()]
    [bool]$FindDupRecurse = $true,

    [Parameter()]
    [switch]$FindDupCleanup,

    [Parameter()]
    [string]$TrayPath = '',

    [Parameter()]
    [string]$ModsPath = '',

    [Parameter()]
    [ValidateRange(1, 100)]
    [int]$CandidateMinFrequency = 1,

    [Parameter()]
    [string]$TrayItemKey = '',

    [Parameter()]
    [switch]$ListTrayItems,

    [Parameter()]
    [ValidateRange(1, 5000)]
    [int]$ListTopN = 200,

    [Parameter()]
    [ValidateRange(1, 1000)]
    [int]$MinMatchCount = 1,

    [Parameter()]
    [ValidateRange(0, 16)]
    [int]$RawScanStep = 0,

    [Parameter()]
    [ValidateRange(1, 10000)]
    [int]$TopN = 200,

    [Parameter()]
    [int]$MaxPackageCount = 0,

    [Parameter()]
    [ValidateRange(1, 64)]
    [int]$ProbeWorkerCount = 8,

    [Parameter()]
    [switch]$PreviewTrayItems,

    [Parameter()]
    [ValidateRange(1, 5000)]
    [int]$PreviewTopN = 300,

    [Parameter()]
    [ValidateRange(1, 200)]
    [int]$PreviewFilesPerItem = 12,

    [Parameter()]
    [string]$PreviewOutputCsv = '',

    [Parameter()]
    [string]$PreviewOutputHtml = '',

    [Parameter()]
    [string]$OutputCsv = '',

    [Parameter()]
    [switch]$ExportMatchedPackages,

    [Parameter()]
    [string]$ExportTargetPath = '',

    [Parameter()]
    [switch]$ExportUnusedPackages,

    [Parameter()]
    [string]$UnusedOutputCsv = '',

    [Parameter()]
    [ValidateSet('Low', 'Medium', 'High')]
    [string]$ExportMinConfidence = 'Low',

    [Parameter()]
    [ValidateSet('Legacy', 'StrictS4TI')]
    [string]$AnalysisMode = 'StrictS4TI',

    [Parameter()]
    [string]$S4tiPath = 'D:\SIM4\Sims 4 Tray Importer (S4TI)'
)

# SSOT: apply defaults from SimsConfig (param block must be first, so we load config here)
. (Join-Path $PSScriptRoot 'modules\SimsConfig.ps1')
$cfg = $Script:SimsConfigDefault
if ($ArchiveExtensions.Count -eq 0) { $ArchiveExtensions = $cfg.ArchiveExtensions }
if ([string]::IsNullOrEmpty($ModsRoot)) { $ModsRoot = $cfg.ModsRoot }
if ([string]::IsNullOrEmpty($TrayRoot)) { $TrayRoot = $cfg.TrayRoot }
if ([string]::IsNullOrEmpty($FlattenRootPath)) { $FlattenRootPath = $cfg.FlattenRootPath }
if ($ModExtensions.Count -eq 0) { $ModExtensions = $cfg.ModExtensions }
if ($PrefixHashBytes -eq 0) { $PrefixHashBytes = $cfg.PrefixHashBytes }
if ($HashWorkerCount -eq 0) { $HashWorkerCount = $cfg.HashWorkerCount }
if ([string]::IsNullOrEmpty($NormalizeRootPath)) { $NormalizeRootPath = $cfg.NormalizeRootPath }
if ([string]::IsNullOrEmpty($MergeTargetPath)) { $MergeTargetPath = $cfg.MergeTargetPath }
if ([string]::IsNullOrEmpty($FindDupRootPath)) { $FindDupRootPath = $cfg.FindDupRootPath }
if ([string]::IsNullOrEmpty($TrayPath)) { $TrayPath = $cfg.TrayRoot }
if ([string]::IsNullOrEmpty($ModsPath)) { $ModsPath = $cfg.ModsPath }

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot 'modules\SimsModToolkit.psm1'
if (-not (Test-Path -LiteralPath $modulePath)) {
    throw "Module not found: $modulePath"
}
Import-Module -Name $modulePath -Force
$invokeUtilsPath = Join-Path $PSScriptRoot 'modules\SimsInvokeUtils.psm1'
if (-not (Test-Path -LiteralPath $invokeUtilsPath)) {
    throw "Module not found: $invokeUtilsPath"
}
Import-Module -Name $invokeUtilsPath -Force

$common = Get-SimsCommonShouldProcessParameters -CallerBoundParameters $PSBoundParameters -CallerWhatIfPreference:$WhatIfPreference
$callerBoundParameters = $PSBoundParameters
$trayProbeForwardNames = Get-SimsForwardParameterNames -Profile 'TrayProbe'

$actionSpecs = @{
    organize = @{
        Command = 'Invoke-SimsOrganize'
        ForwardProfile = 'OrganizeOptional'
        BuildInvocation = {
            @{
                SourceDir = $SourceDir
                ZipNamePattern = $ZipNamePattern
                ArchiveExtensions = $ArchiveExtensions
                ModsRoot = $ModsRoot
                UnifiedModsFolder = $UnifiedModsFolder
                TrayRoot = $TrayRoot
            }
        }
    }
    flatten = @{
        Command = 'Invoke-SimsFlatten'
        ForwardProfile = 'FileOpsShared'
        BuildInvocation = {
            @{
                RootPath = $FlattenRootPath
                FlattenToRoot = $FlattenToRoot
            }
        }
    }
    normalize = @{
        Command = 'Invoke-SimsNormalize'
        ForwardProfile = ''
        BuildInvocation = {
            @{
                RootPath = $NormalizeRootPath
            }
        }
    }
    merge = @{
        Command = 'Invoke-SimsMerge'
        ForwardProfile = 'FileOpsShared'
        SupportsCommonShouldProcess = $true
        BuildInvocation = {
            if (-not $MergeSourcePaths -or $MergeSourcePaths.Count -eq 0) {
                throw "Action 'merge' requires -MergeSourcePaths."
            }
            @{
                SourcePaths = $MergeSourcePaths
                TargetPath = $MergeTargetPath
            }
        }
    }
    finddup = @{
        Command = 'Invoke-SimsFindDuplicates'
        ForwardProfile = 'FindDup'
        SupportsCommonShouldProcess = $true
        BuildInvocation = {
            @{
                RootPath = $FindDupRootPath
                OutputCsv = $FindDupOutputCsv
                Recurse = $FindDupRecurse
                Cleanup = $FindDupCleanup.IsPresent
            }
        }
    }
    trayprobe = @{
        Command = 'Invoke-SimsTrayProbe'
        ForwardProfile = ''
        SupportsCommonShouldProcess = $false
        BuildInvocation = {
            $probeParameters = @{}
            Add-SimsBoundParameters -Invocation $probeParameters -CallerBoundParameters $callerBoundParameters -Names $trayProbeForwardNames
            @{
                ProbeParameters = $probeParameters
            }
        }
    }
}

$spec = $actionSpecs[$Action]
$invoke = & $spec.BuildInvocation
$forwardProfile = $spec.ForwardProfile
if (-not [string]::IsNullOrWhiteSpace($forwardProfile)) {
    $forwardNames = Get-SimsForwardParameterNames -Profile $forwardProfile
    Add-SimsBoundParameters -Invocation $invoke -CallerBoundParameters $PSBoundParameters -Names $forwardNames
}
$supportsCommon = $true
if ($spec.ContainsKey('SupportsCommonShouldProcess')) {
    $supportsCommon = [bool]$spec.SupportsCommonShouldProcess
}
if ($supportsCommon) {
    Add-SimsHashtableParameters -Target $invoke -Source $common
}

& $spec.Command @invoke
