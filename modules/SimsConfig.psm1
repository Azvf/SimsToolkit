<#
.SYNOPSIS
Single Source of Truth (SSOT) for SimsToolkit defaults and configuration.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'SimsConfig.ps1')

function Get-SimsConfigDefault {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key
    )
    if ($script:SimsConfigDefault.ContainsKey($Key)) {
        return $script:SimsConfigDefault[$Key]
    }
    throw "SimsConfig: unknown key '$Key'"
}

Export-ModuleMember -Function Get-SimsConfigDefault
