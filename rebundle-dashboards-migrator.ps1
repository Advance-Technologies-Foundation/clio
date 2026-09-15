#Requires
<#
.SYNOPSIS
    Puts a new version of the bundled CrtDashboardsMigratorApp archive into this clio checkout, from the
    package's SDLC (Jenkins) build.

.DESCRIPTION
    The dashboards migrator ships PREBUILT, like cliogate: the archive is the package .gz inside the SDLC
    build zip, carrying the package assembly for both .NET Framework (Files/Bin) and .NET
    (Files/Bin/netstandard). Nothing is built here. The script:

      1. unpacks the build zip and the package .gz inside it;
      2. reads the app version from Files/app-descriptor.json - the number clio reports and Marketplace
         shows - and refuses a build whose app version is lower than the one clio ships today;
      3. packs with `clio compress --skip-pdb` into clio/CrtDashboardsMigratorApp/;
      4. verifies the inventory: exactly the two package assemblies, no pdb, no SqlScripts, the
         DashboardsMigratorPingService schema present;
      5. rewrites the pins in clio.tests/Common/BundledDashboardsMigratorPackageTests.cs;
      6. rebuilds the chosen clio output, because an install resolves the archive from the BUILD OUTPUT.

    The producing commit is on the build's page in the SDLC app; the pin that ties the archive to it is the
    SHA-256 of the build zip.

.PARAMETER BuildZip
    The SDLC build zip, e.g. \\tscrm.com\dfs-ts\ComposableApps\CrtDashboardsMigratorApp\1.1.4\CrtDashboardsMigratorApp_1.1.4.zip

.EXAMPLE
    ./rebundle-dashboards-migrator.ps1 -BuildZip '\\tscrm.com\dfs-ts\ComposableApps\CrtDashboardsMigratorApp\1.1.4\CrtDashboardsMigratorApp_1.1.4.zip' -Configuration Debug -Framework net8.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $BuildZip,
    [ValidateSet('Debug','Release')][string] $Configuration,
    [string] $Framework
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Package   = 'CrtDashboardsMigratorApp'
$clioRoot  = $PSScriptRoot
$archive   = Join-Path $clioRoot "clio\$Package\$Package.gz"
$pinsFile  = Join-Path $clioRoot 'clio.tests\Common\BundledDashboardsMigratorPackageTests.cs'
$expectedDlls = @("Files/Bin/$Package.dll", "Files/Bin/netstandard/$Package.dll")
$allowedTopLevel = @('descriptor.json', 'Files', 'Schemas', 'Resources', 'Data')
$requiredSchema = 'DashboardsMigratorPingService'

function Step([string] $text) { Write-Host "`n=== $text" -ForegroundColor Cyan }
function Ok  ([string] $text) { Write-Host "    $text" -ForegroundColor Green }
function Die ([string] $text) { Write-Host "`n$text`n" -ForegroundColor Red; throw 'Rebundle aborted - see the message above.' }

if (-not (Test-Path -LiteralPath $BuildZip)) { Die "Build zip not found: $BuildZip" }

# Which clio build output to drive and refresh - the one an install would resolve the archive from.
$outputs = @(Get-ChildItem -LiteralPath (Join-Path $clioRoot 'clio\bin') -Directory -ErrorAction SilentlyContinue |
    ForEach-Object { $cfg = $_.Name; Get-ChildItem -LiteralPath $_.FullName -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'clio.dll') } |
        ForEach-Object { [pscustomobject]@{ Configuration = $cfg; Framework = $_.Name; Dll = Join-Path $_.FullName 'clio.dll' } } })
if ($Configuration) { $outputs = @($outputs | Where-Object Configuration -eq $Configuration) }
if ($Framework)     { $outputs = @($outputs | Where-Object Framework -eq $Framework) }
if ($outputs.Count -ne 1) {
    Die ("Exactly one built clio output must be selected; found $($outputs.Count). Build clio, or pass -Configuration/-Framework:`n" +
        (($outputs | ForEach-Object { "  -Configuration $($_.Configuration) -Framework $($_.Framework)" }) -join "`n"))
}
$clioDll = $outputs[0].Dll
Write-Host "Using clio $($outputs[0].Configuration)/$($outputs[0].Framework)" -ForegroundColor Cyan

$work = Join-Path ([IO.Path]::GetTempPath()) ("clio-rebundle-" + [Guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Step '1. Unpack the SDLC build'
    $sourceSha = (Get-FileHash -LiteralPath $BuildZip -Algorithm SHA256).Hash.ToUpperInvariant()
    Expand-Archive -LiteralPath $BuildZip -DestinationPath (Join-Path $work 'zip') -Force
    $gz = @(Get-ChildItem -LiteralPath (Join-Path $work 'zip') -Recurse -Filter "$Package.gz")
    if ($gz.Count -ne 1) { Die "Expected exactly one $Package.gz inside the build zip, found $($gz.Count)." }
    $unpacked = Join-Path $work 'pkg'
    dotnet $clioDll extract-pkg-zip $gz[0].FullName -d $unpacked | Out-Null
    if ($LASTEXITCODE -ne 0) { Die 'extract-pkg-zip failed.' }
    $packageDir = Join-Path $unpacked $Package
    if (-not (Test-Path -LiteralPath (Join-Path $packageDir 'descriptor.json'))) { Die "No $Package/descriptor.json in the build." }
    Ok "build zip sha256 $sourceSha"

    Step "2. Read the app version and check it moves forward"
    # The app names its own version, and that is the number clio reports and Marketplace shows. Nothing here
    # stamps a version into the package: the build is taken as it is.
    $appVersion = (Get-Content -LiteralPath (Join-Path $packageDir 'Filespp-descriptor.json') -Raw | ConvertFrom-Json).Version
    $shipped = ([regex]::Match((Get-Content -LiteralPath $pinsFile -Raw), 'ExpectedArchiveVersion = "([^"]*)"')).Groups[1].Value
    $parsedApp = [version] $null
    $parsedShipped = [version] $null
    if (-not [version]::TryParse($appVersion, [ref] $parsedApp)) { Die "app-descriptor.json Version '$appVersion' is not a version number." }
    if ([version]::TryParse($shipped, [ref] $parsedShipped) -and $parsedApp -lt $parsedShipped) {
        Die "The build's app version $appVersion is lower than the version clio already ships ($shipped); that is a downgrade."
    }
    $descriptorJson = Get-Content -LiteralPath (Join-Path $packageDir 'descriptor.json') -Raw
    $stamp = ([regex]::Match($descriptorJson, '"ModifiedOnUtc"\s*:\s*"([^"]*)"')).Groups[1].Value.Replace('\/', '/')
    Ok "app version $appVersion (clio ships $shipped), ModifiedOnUtc $stamp"

    Step '3. Pack into the clio checkout'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $archive) | Out-Null
    dotnet $clioDll compress $packageDir --skip-pdb -d $archive
    if ($LASTEXITCODE -ne 0) { Die 'compress failed.' }
    Ok $archive

    Step '4. Verify the archive inventory'
    $check = Join-Path $work 'check'
    dotnet $clioDll extract-pkg-zip $archive -d $check | Out-Null
    if ($LASTEXITCODE -ne 0) { Die 'extract-pkg-zip failed on the produced archive.' }
    $checkDir = Join-Path $check $Package
    $entries = @(Get-ChildItem -LiteralPath $checkDir -Recurse -File | ForEach-Object {
        $_.FullName.Substring($checkDir.Length + 1).Replace('\', '/') })
    $binaries = @($entries | Where-Object { $_ -match '\.(dll|pdb)$' } | Sort-Object)
    if (($binaries -join "`n") -ne (($expectedDlls | Sort-Object) -join "`n")) {
        Die ("Binary inventory is not the expected one.`n  expected: $($expectedDlls -join ', ')`n  found:    $($binaries -join ', ')")
    }
    $topLevel = @($entries | ForEach-Object { ($_ -split '/')[0] } | Sort-Object -Unique)
    $unexpected = @($topLevel | Where-Object { $allowedTopLevel -notcontains $_ })
    if ($unexpected.Count -gt 0) { Die "Unexpected top-level entries: $($unexpected -join ', '). Allowed: $($allowedTopLevel -join ', ')." }
    if (-not (Test-Path -LiteralPath (Join-Path $checkDir "Schemas\$requiredSchema"))) {
        Die "Schemas/$requiredSchema is missing: the install command's verdict rests on its Ping. Is this build from a commit that carries the service?"
    }
    Ok "$($entries.Count) entries, both package assemblies, no pdb, $requiredSchema present"

    Step '5. Refresh the pins'
    $sha = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToUpperInvariant()
    $text = Get-Content -LiteralPath $pinsFile -Raw
    foreach ($pin in @(
        @{ Name = 'ExpectedArchiveSha256';           Value = $sha },
        @{ Name = 'ExpectedSourceBuildSha256';       Value = $sourceSha },
        @{ Name = 'ExpectedArchiveVersion';          Value = $appVersion },
        @{ Name = 'ExpectedDescriptorModifiedOnUtc'; Value = $stamp })) {
        $pattern = '(?s)(' + $pin.Name + '\s*=\s*)"[^"]*";'
        if ([regex]::Matches($text, $pattern).Count -ne 1) { Die "Expected exactly one $($pin.Name) in $pinsFile." }
        $text = [regex]::Replace($text, $pattern, "`${1}`"$($pin.Value)`";")
        Ok "$($pin.Name) -> $($pin.Value)"
    }
    [IO.File]::WriteAllText($pinsFile, $text)

    Step '6. Rebuild clio (an install resolves the archive from the build output)'
    dotnet build (Join-Path $clioRoot 'clio\clio.csproj') -c $outputs[0].Configuration -f $outputs[0].Framework --nologo -v q
    if ($LASTEXITCODE -ne 0) { Die 'Rebuild failed; until it succeeds every local install ships the previous archive.' }
    Ok 'rebuilt'
} finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

Write-Host "`n=== Done. Nothing was committed." -ForegroundColor Cyan
Write-Host "    version   $appVersion`n    build zip $sourceSha`n    archive   $sha"
Write-Host @"

    Next, by hand:
      * dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Common"
      * install onto a stand and confirm Ping answers
      * commit the archive and the pins together; name the build zip SHA-256 in the message
"@
