Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Add-SimsBoundParameters {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Invocation,

        [Parameter(Mandatory = $true)]
        [object]$CallerBoundParameters,

        [Parameter(Mandatory = $true)]
        [string[]]$Names
    )

    foreach ($name in $Names) {
        if ($CallerBoundParameters.ContainsKey($name)) {
            $Invocation[$name] = $CallerBoundParameters[$name]
        }
    }
}

function Get-SimsCommonShouldProcessParameters {
    param(
        [Parameter(Mandatory = $true)]
        [object]$CallerBoundParameters,

        [Parameter(Mandatory = $true)]
        [bool]$CallerWhatIfPreference
    )

    $common = @{}
    if ($CallerBoundParameters.ContainsKey('WhatIf')) {
        $common['WhatIf'] = $CallerBoundParameters['WhatIf']
    }
    elseif ($CallerWhatIfPreference) {
        $common['WhatIf'] = $true
    }

    if ($CallerBoundParameters.ContainsKey('Confirm')) {
        $common['Confirm'] = $CallerBoundParameters['Confirm']
    }

    return $common
}

function Add-SimsHashtableParameters {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Target,

        [Parameter(Mandatory = $true)]
        [hashtable]$Source
    )

    foreach ($key in $Source.Keys) {
        $Target[$key] = $Source[$key]
    }
}

function Get-SimsForwardParameterNames {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('OrganizeOptional', 'FileOpsShared', 'FindDup', 'TrayProbe')]
        [string]$Profile
    )

    switch ($Profile) {
        'OrganizeOptional' {
            return @(
                'KeepZip',
                'ArchiveExtensions',
                'RecurseSource',
                'IncludeLooseSources',
                'ModExtensions',
                'VerifyContentOnNameConflict',
                'PrefixHashBytes'
            )
        }
        'FileOpsShared' {
            return @('SkipPruneEmptyDirs', 'ModFilesOnly', 'ModExtensions', 'VerifyContentOnNameConflict', 'PrefixHashBytes', 'HashWorkerCount')
        }
        'FindDup' {
            return @('ModFilesOnly', 'ModExtensions', 'PrefixHashBytes', 'HashWorkerCount')
        }
        'TrayProbe' {
            return @(
                'TrayPath',
                'ModsPath',
                'CandidateMinFrequency',
                'TrayItemKey',
                'ListTrayItems',
                'ListTopN',
                'MinMatchCount',
                'RawScanStep',
                'TopN',
                'MaxPackageCount',
                'ProbeWorkerCount',
                'PreviewTrayItems',
                'PreviewTopN',
                'PreviewFilesPerItem',
                'PreviewOutputCsv',
                'PreviewOutputHtml',
                'OutputCsv',
                'ExportMatchedPackages',
                'ExportTargetPath',
                'ExportUnusedPackages',
                'UnusedOutputCsv',
                'ExportMinConfidence',
                'AnalysisMode',
                'S4tiPath'
            )
        }
    }
}

Export-ModuleMember -Function Add-SimsBoundParameters, Get-SimsCommonShouldProcessParameters, Add-SimsHashtableParameters, Get-SimsForwardParameterNames
