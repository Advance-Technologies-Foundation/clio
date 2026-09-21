param(
    [Parameter(Mandatory = $true)][string]$BundleDirectory,
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidatePattern("^[A-Za-z0-9][A-Za-z0-9._-]*$")][string]$PackageId = "Clio10.PrimitiveBundle"
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bundlePath = (Resolve-Path -LiteralPath $BundleDirectory).Path
$manifestPath = Join-Path $bundlePath 'bundle.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'The bundle requires bundle.json and complete dependency output.' }
$numericVersion = [version]::Parse($Version)
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$declaredVersion = [version]::Parse($manifest.Version)
function Normalize-Version([version]$value) { [version]::new($value.Major, $value.Minor, [Math]::Max(0, $value.Build), [Math]::Max(0, $value.Revision)) }
if ((Normalize-Version $numericVersion) -ne (Normalize-Version $declaredVersion)) { throw 'Package and bundle versions must identify the same release.' }
$packRoot = Join-Path $projectRoot ('artifacts/bundle-pack/' + $PackageId + '/' + $numericVersion.ToString())
$outputRoot = Join-Path $projectRoot 'artifacts/runtime-packages'
New-Item -ItemType Directory -Path $packRoot, $outputRoot -Force | Out-Null
$escapedBundle = [System.Security.SecurityElement]::Escape($bundlePath.Replace('\', '/'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>$PackageId</PackageId>
    <Version>$Version</Version>
    <Authors>Clio contributors</Authors>
    <Description>Complete experimental Clio runtime payload for side-by-side loading.</Description>
    <IsPackable>true</IsPackable>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <EnableDefaultItems>false</EnableDefaultItems>
    <!-- This is a loader payload, not a compile-time library: DLLs intentionally live under bundle/. -->
    <NoWarn>NU5128;NU5100</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <None Include="$escapedBundle/**/*" Pack="true" PackagePath="bundle/%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $packRoot 'PrimitiveBundle.csproj'
[System.IO.File]::WriteAllText($projectPath, $project)
& dotnet pack $projectPath -c Release -o $outputRoot --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Bundle packaging failed.' }
Write-Output "Runtime package output: $outputRoot"
