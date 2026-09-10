#Requires -Version 7.0
<#
.SYNOPSIS
    Rebundles the CrtProcessBuilder package into this clio checkout.

.DESCRIPTION
    Thin wrapper kept for the documented command line. The procedure lives in rebundle-bundled-package.ps1,
    which serves every bundled package; this passes -Package CrtProcessBuilder and everything else through.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $PackageRepoPath,
    [Parameter(Mandatory = $true)][string] $Version,
    [ValidateSet('Debug','Release')][string] $Configuration,
    [string] $Framework,
    [switch] $SkipTests,
    [switch] $ValidateOnly
)

& (Join-Path $PSScriptRoot 'rebundle-bundled-package.ps1') -Package CrtProcessBuilder @PSBoundParameters
exit $LASTEXITCODE
