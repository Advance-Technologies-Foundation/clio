#Requires -Version 7.0
<#
.SYNOPSIS
    Rebundles the CrtProcessBuilder package from its own repository into this clio checkout.

.DESCRIPTION
    Performs, in one call, the eight-step procedure documented in
    docs/agent-instructions/bundled-packages.md and the package repo's docs/bundling-into-clio.md.

    Four of those steps fail SILENTLY when done by hand, which is why this script exists:

      * forgetting to delete Files/Bin ships a runtime-specific assembly inside a package that is
        supposed to be source-only. It installs, the name-based gate is satisfied, and the other
        runtime 404s. Worse, after a FAILED target-side build that stale assembly still answers the
        install command's own Ping probe, so the outcome check passes for an environment that never
        compiled the shipped sources;
      * moving PackageVersion without moving ModifiedOnUtc leaves the environment's recorded version
        behind, because Creatio decides WHETHER to rewrite the SysPackage row from the timestamp;
      * NOT moving PackageVersion at all ships the new sources to nobody: clio compares the recorded
        version against the one in the archive, so an unchanged version means every existing
        environment is already "converged" and is never asked to update;
      * refreshing the archive but not its SHA pin leaves a red test at best and a lie at worst;
      * forgetting to rebuild clio means every local verification tests the PREVIOUS archive, since an
        install resolves the bundled .gz from the BUILD OUTPUT, not from the repository.

    The three pins this run can derive - the archive SHA-256, the descriptor's ModifiedOnUtc, and the
    version - are computed FROM the archive it just produced, so "those pins are stale" stops being a
    reachable state. The SCHEMA descriptor pin is deliberately verified rather than refreshed: it guards a
    field clio's own tooling does not stamp, so a moved value stops the run instead of being accepted
    silently. Nothing is committed and nothing is pushed: the script reports what it changed and leaves
    both repositories dirty for review.

.PARAMETER PackageRepoPath
    Path to the ProcessBuilder repository checkout (the folder containing packages/CrtProcessBuilder).

.PARAMETER Version
    Four-part version to stamp. REQUIRED, and it must be higher than the version currently in the
    descriptor. There is no "internal change, leave the version alone" case any more: clio decides
    whether to update an environment by comparing its recorded version against the one in the shipped
    archive, so a rebundle under an unchanged version reaches only brand-new installs. Raising it costs
    nothing to maintain - no floor constant exists to keep in step - and is the only way for a change to
    reach environments that already have the package.

.PARAMETER Configuration
    Which clio build output to use and refresh (Debug or Release). Omit to auto-detect: with exactly one
    built output the script uses it, otherwise it lists them and asks. This matters more than it looks -
    an install resolves the bundled archive from the BUILD OUTPUT, so refreshing the wrong one leaves the
    configuration you actually run with carrying the previous archive, silently.

.PARAMETER Framework
    Target framework of that output, for example net8.0 or net10.0. Same auto-detection as -Configuration.

.PARAMETER SkipTests
    Skip the package's own build and unit tests. For a docs-only or descriptor-only rebundle.

.EXAMPLE
    ./rebundle-process-builder.ps1 -PackageRepoPath C:\Projects\workspace\ProcessBuilder -Version 1.0.1.0

.EXAMPLE
    ./rebundle-process-builder.ps1 -PackageRepoPath C:\Projects\workspace\ProcessBuilder `
        -Version 1.1.0.0 -Configuration Release -Framework net10.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $PackageRepoPath,
    [Parameter(Mandatory = $true)][string] $Version,
    [ValidateSet('Debug','Release')][string] $Configuration,
    [string] $Framework,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$clioRoot     = $PSScriptRoot
$packageDir   = Join-Path $PackageRepoPath 'packages\CrtProcessBuilder'
$descriptor   = Join-Path $packageDir 'descriptor.json'
$binDir       = Join-Path $packageDir 'Files\Bin'
$archive      = Join-Path $clioRoot 'clio\CrtProcessBuilder\CrtProcessBuilder.gz'
$pinsFile     = Join-Path $clioRoot 'clio.tests\Common\BundledProcessBuilderPackageTests.cs'

function Step([string] $text) { Write-Host "`n=== $text" -ForegroundColor Cyan }
function Ok  ([string] $text) { Write-Host "    $text" -ForegroundColor Green }
# Print the explanation to the host and throw something short: PowerShell reflows a multi-line throw
# into its own wrapped error block, which mangles exactly the guidance a refusal needs to convey.
function Die ([string] $text) {
    Write-Host ''
    foreach ($line in ($text -split "`r?`n")) { Write-Host $line -ForegroundColor Red }
    Write-Host ''
    throw 'Rebundle aborted - see the message above.'
}

foreach ($p in @($packageDir, $descriptor)) {
    if (-not (Test-Path -LiteralPath $p)) { Die "Not found: $p. Is -PackageRepoPath the ProcessBuilder checkout?" }
}
# Which build output to drive and refresh. NOT hardcoded: the repo's own build.ps1 uses Release/net10.0
# while a developer typically has Debug, and picking the wrong one is the very failure this script exists
# to prevent - an install resolves the archive from the BUILD OUTPUT, so refreshing Debug while the user
# runs Release leaves Release carrying the PREVIOUS archive with nothing on screen to say so.
$outputs = Get-ChildItem -LiteralPath (Join-Path $clioRoot 'clio\bin') -Directory -ErrorAction SilentlyContinue |
    ForEach-Object {
        $cfg = $_.Name
        Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName 'clio.dll') } |
            ForEach-Object {
                [pscustomobject]@{ Configuration = $cfg; Framework = $_.Name; Dll = Join-Path $_.FullName 'clio.dll' }
            }
    }
if (-not $outputs) { Die "No built clio found under clio\bin. Build clio first - this script uses it to stamp and pack." }

$candidates = @($outputs)
if ($Configuration) { $candidates = @($candidates | Where-Object Configuration -eq $Configuration) }
if ($Framework)     { $candidates = @($candidates | Where-Object Framework     -eq $Framework) }

if ($candidates.Count -eq 0) {
    Die ("No built clio matches the requested configuration/framework. Available:`n" +
        (($outputs | ForEach-Object { "  $($_.Configuration)/$($_.Framework)" }) -join "`n"))
}
if ($candidates.Count -gt 1) {
    Die ("Several built clio outputs exist and none was chosen. Pass -Configuration and/or -Framework:`n" +
        (($candidates | ForEach-Object { "  -Configuration $($_.Configuration) -Framework $($_.Framework)" }) -join "`n") +
        "`n`nNot a formality: whichever you pick is the one that receives the new archive. The others keep the" +
        "`nprevious one, and an install run from them ships it.")
}
$chosen  = $candidates[0]
$clioDll = $chosen.Dll
Write-Host "Using clio $($chosen.Configuration)/$($chosen.Framework)" -ForegroundColor Cyan

# ---------------------------------------------------------------- 1. sources compile, tests pass
if ($SkipTests) {
    Write-Host "`n=== 1. SKIPPED: package build and tests" -ForegroundColor Yellow
} else {
    Step '1. Validate the sources compile and their tests pass (the TARGET will have to compile them)'
    Push-Location $PackageRepoPath
    try {
        dotnet build MainSolution.slnx -c dev-nf --nologo -v q
        if ($LASTEXITCODE -ne 0) { Die 'Package build failed. Shipping sources the target cannot compile installs a package that never works.' }
        dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf --no-build --nologo -v q
        if ($LASTEXITCODE -ne 0) { Die 'Package tests failed.' }
    } finally { Pop-Location }
    Ok 'build + tests green'
}

# ---------------------------------------------------------------- 2. both descriptor fields
# Read these as TEXT, not via ConvertFrom-Json: PowerShell parses the Microsoft "/Date(ms)/" form into a
# DateTime, so the RAW string - which is what has to land in the pin and be compared against the archive
# byte for byte - would silently become "08/07/2026 03:47:12". The guard fixture caught exactly that.
function Get-DescriptorField([string] $path, [string] $field) {
    $raw = Get-Content -LiteralPath $path -Raw
    $m = [regex]::Match($raw, "`"$field`"\s*:\s*`"([^`"]*)`"")
    if (-not $m.Success) { Die "Could not read $field from $path" }
    return $m.Groups[1].Value
}

$beforeVersion = Get-DescriptorField $descriptor 'PackageVersion'
$beforeStamp   = Get-DescriptorField $descriptor 'ModifiedOnUtc'

# The version must MOVE UP against BOTH baselines, and this is the guard that carries the whole delivery
# model. Comparing only against the package repository's descriptor is not enough: from a stale ProcessBuilder
# checkout you could produce an archive LOWER than the one clio already ships, and since the run then
# overwrites the version pin unconditionally, nothing downstream would refuse. That is the very failure this
# guard exists to prevent, measured from the wrong side. So read the currently-shipped archive too.
#
# clio compares an environment's recorded version against the one in the shipped archive to decide whether to
# ask for an update, so:
#   * an unchanged version publishes to nobody - the archive installs fine, but every environment that
#     already has the package compares as converged and is never asked, silently;
#   * a LOWER version is worse than useless: SysPackage.Version is not monotonic (the platform rewrites
#     the row whenever ModifiedOnUtc DIFFERS, and an earlier stamp was measured moving a recorded version
#     downward), so it would leave environments pinned below what clio ships with no way back up.
[version] $parsedNew = $null
[version] $parsedOld = $null
if (-not [version]::TryParse($Version, [ref] $parsedNew)) {
    Die "-Version '$Version' is not a version. Use four parts, e.g. 1.0.1.0."
}
if ($parsedNew.Revision -lt 0) {
    Die "-Version '$Version' must carry all four parts: a three-part version yields Revision = -1 and sorts BELOW any four-part version an environment recorded."
}
if (-not [version]::TryParse($beforeVersion, [ref] $parsedOld)) {
    Die "The descriptor's current PackageVersion '$beforeVersion' is not a version. Fix $descriptor first."
}

# The version clio ALREADY ships, read out of the committed archive the same way clio itself reads it.
# Absent on a first bundle, in which case the package-repo descriptor is the only baseline there is.
$shippedVersion = $null
if (Test-Path -LiteralPath $archive) {
    $bytes = & {
        $fs = [IO.File]::OpenRead($archive)
        $gz = [IO.Compression.GZipStream]::new($fs, [IO.Compression.CompressionMode]::Decompress)
        $ms = [IO.MemoryStream]::new()
        $gz.CopyTo($ms); $gz.Dispose(); $fs.Dispose()
        $out = $ms.ToArray(); $ms.Dispose(); ,$out
    }
    # The descriptor is UTF-8 CONTENT inside the container, so a text scan finds it; entry NAMES are
    # UTF-16LE and would not be found this way. Only the package descriptor is needed here.
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    $m = [regex]::Match($text, '"PackageVersion":\s*"([^"]+)"')
    if ($m.Success) { $shippedVersion = $m.Groups[1].Value }
}
[version] $parsedShipped = $null
$shippedParsed = $shippedVersion -and [version]::TryParse($shippedVersion, [ref] $parsedShipped)
if ($shippedParsed -and $parsedNew -le $parsedShipped) {
    Die @"
Refusing: -Version $Version is not higher than the version clio ALREADY ships ($shippedVersion).

The descriptor in your ProcessBuilder checkout says $beforeVersion, so this run looked fine against it - but
the committed archive is already at $shippedVersion. Producing a lower one is the failure this script exists
to prevent, and it is silent: clio info would report the downgrade, every environment already on
$shippedVersion compares as AHEAD so convergence never fires, and only fresh installs would receive the older
sources.

Your ProcessBuilder checkout is probably behind. Pull it, or pass a version above $shippedVersion.
"@
}

if ($parsedNew -le $parsedOld) {
    Die @"
Refusing: -Version $Version is not higher than the version already in the descriptor ($beforeVersion).

clio decides whether to update an environment by comparing the version it recorded against the version in
the shipped archive. So this run would produce an archive that:

  * installs correctly on a machine that has never had the package, and
  * is never offered to any environment that already has it - they all compare as already converged.

Pass a higher -Version. There is no reason left to hold it back: no floor constant has to be kept in step
with it any more, so raising it costs nothing.
"@
}

Step "2. Stamp PackageVersion AND ModifiedOnUtc (version: $beforeVersion -> $Version)"
dotnet $clioDll set-pkg-version $packageDir --package-version $Version
if ($LASTEXITCODE -ne 0) { Die 'set-pkg-version refused. Nothing was written; fix the version and re-run.' }

$afterStamp = Get-DescriptorField $descriptor 'ModifiedOnUtc'
if ($afterStamp -eq $beforeStamp) {
    Die "ModifiedOnUtc did not move ($afterStamp). Creatio rewrites the SysPackage row only when it changes, so this rebundle would install and leave the recorded version behind."
}
if ($afterStamp -notmatch '^/Date\(\d+000\)/$') {
    Die "ModifiedOnUtc is '$afterStamp', which is not the /Date(<whole seconds>)/ form the guard fixture asserts. Reading it through ConvertFrom-Json does this - the raw string must be preserved."
}
Ok "ModifiedOnUtc $beforeStamp -> $afterStamp"

# ------------------------------------------------------------- 2b. schema descriptors, unstamped
Step '2b. Check schema descriptors (set-pkg-version stamps the PACKAGE descriptor only)'
# Deliberately VERIFIED, not refreshed. The schema pin exists to catch a descriptor written by something
# other than clio - the compile marker shipped for a day carrying local time in a UTC-labelled field - and
# a script that silently rewrote the pin to whatever it found would defeat exactly that. So a moved schema
# stamp stops the run and asks for a deliberate decision.
$pinnedSchemaStamp = ([regex]::Match((Get-Content -LiteralPath $pinsFile -Raw),
    'ExpectedSchemaDescriptorModifiedOnUtc = "([^"]*)"')).Groups[1].Value
$schemaStamps = @()
Get-ChildItem -LiteralPath (Join-Path $packageDir 'Schemas') -Filter descriptor.json -Recurse -ErrorAction SilentlyContinue |
    ForEach-Object {
        $raw = Get-Content -LiteralPath $_.FullName -Raw
        if ($raw -match '/Date\((\d+)\)/') {
            $ms = [int64]$Matches[1]
            $utc = [DateTimeOffset]::FromUnixTimeMilliseconds($ms).UtcDateTime
            $flag = if ($ms % 1000 -ne 0) { '  <-- NOT whole seconds: written by hand?' } else { '' }
            Ok ("{0,-34} {1:yyyy-MM-dd HH:mm:ss}Z{2}" -f $_.Directory.Name, $utc, $flag)
            $schemaStamps += "/Date($ms)/"
        }
    }
if ($pinnedSchemaStamp -and $schemaStamps -notcontains $pinnedSchemaStamp) {
    Die @"
The pinned schema descriptor stamp is no longer present in the package.

  pinned in clio : $pinnedSchemaStamp
  found in schemas: $($schemaStamps -join ', ')

This run would leave a red guard test. The pin is NOT refreshed automatically on purpose: it is the only
check on a field clio's own tooling does not stamp, and it has shipped wrong before (local time in a
UTC-labelled field). If the move is intended, set ExpectedSchemaDescriptorModifiedOnUtc in
clio.tests\Common\BundledProcessBuilderPackageTests.cs to the new value and re-run.
"@
}

# ---------------------------------------------------------------- 3. keep the archive source-only
Step '3. Remove the build output so the archive stays source-only'
if (Test-Path -LiteralPath $binDir) {
    Remove-Item -LiteralPath $binDir -Recurse -Force
    Ok 'Files/Bin removed'
} else { Ok 'Files/Bin absent already' }
$objDir = Join-Path $packageDir 'Files\obj'
if (Test-Path -LiteralPath $objDir) { Remove-Item -LiteralPath $objDir -Recurse -Force; Ok 'Files/obj removed' }

# ---------------------------------------------------------------- 4. pack into the clio checkout
Step '4. Pack straight into the clio checkout'
dotnet $clioDll compress $packageDir --skip-pdb -d $archive
if ($LASTEXITCODE -ne 0) { Die 'compress failed.' }
Ok $archive

# ---------------------------------------------------------------- 5. verify, do not trust
Step '5. Verify the archive contents (step 3 is easy to forget and its failure is silent)'
# clio's .gz is not a zip: per entry it is [int32 nameLength][UTF-16LE path][int32 contentLength][bytes].
$entries = & {
    $fs  = [IO.File]::OpenRead($archive)
    $gz  = [IO.Compression.GZipStream]::new($fs, [IO.Compression.CompressionMode]::Decompress)
    $ms  = [IO.MemoryStream]::new()
    $gz.CopyTo($ms); $gz.Dispose(); $fs.Dispose()
    $b = $ms.ToArray(); $ms.Dispose()
    $i = 0
    while ($i + 4 -le $b.Length) {
        $nameLen = [BitConverter]::ToInt32($b, $i); $i += 4
        if ($nameLen -le 0 -or $i + $nameLen * 2 -gt $b.Length) { break }
        $name = [Text.Encoding]::Unicode.GetString($b, $i, $nameLen * 2); $i += $nameLen * 2
        if ($i + 4 -gt $b.Length) { break }
        $contentLen = [BitConverter]::ToInt32($b, $i); $i += 4
        if ($contentLen -lt 0 -or $i + $contentLen -gt $b.Length) { break }
        $i += $contentLen
        [pscustomobject]@{ Name = $name; Size = $contentLen }
    }
}
if (-not $entries) { Die 'Could not read the archive container - the format may have changed.' }

$dlls = $entries | Where-Object { $_.Name -like '*.dll' }
$ownAssembly = $dlls | Where-Object { $_.Name -match '(?i)(^|[\\/])CrtProcessBuilder\.dll$' }
if ($ownAssembly) {
    Die "The archive carries the package's OWN assembly ($($ownAssembly.Name)). Step 3 did not take effect. Shipping it makes a FAILED target-side build undetectable: the stale DLL answers the install command's Ping probe."
}
$expectedLibs = @('ErrorOr.dll', 'ATF.Repository.dll')
foreach ($lib in $expectedLibs) {
    if (-not ($dlls | Where-Object { $_.Name -like "*$lib" })) {
        Die "Files/Libs/$lib is missing from the archive. It is a compile reference, so the target's configuration build would fail and the install would report 'the environment did not compile the package'."
    }
}
if ($dlls.Count -ne $expectedLibs.Count) {
    Die ("Expected exactly $($expectedLibs.Count) DLLs (the Files/Libs compile references), found $($dlls.Count): " +
         (($dlls | ForEach-Object Name) -join ', '))
}
if (-not ($entries | Where-Object { $_.Name -like '*CrtProcessBuilderCompileMarker*' })) {
    Die 'The compile-marker schema is missing. Without it the package installs and is NEVER compiled, and no database read shows the difference.'
}
Ok "$($entries.Count) entries, $($dlls.Count) DLLs (both Files/Libs), compile marker present, no own assembly"

# ---------------------------------------------------------------- 6. pins, computed from the archive
Step '6. Refresh the clio-side pins FROM the archive just produced'
$sha = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToUpperInvariant()
$stamp = $afterStamp

function Replace-InFile([string] $path, [string] $pattern, [string] $replacement, [string] $label) {
    $text = Get-Content -LiteralPath $path -Raw
    # Insist on EXACTLY one match. A pattern that hits nothing means the constant's shape changed; one that
    # hits several means it is not anchored tightly enough, and [regex]::Replace would rewrite all of them.
    # NOT $matches: that is a PowerShell automatic variable, populated by every -match in the script - step
    # 2b reads $Matches[1] straight after one - so assigning to it here would clobber a live value.
    $pinMatches = [regex]::Matches($text, $pattern)
    if ($pinMatches.Count -eq 0) { Die "Could not find $label in $path - the constant's shape changed; update this script." }
    if ($pinMatches.Count -gt 1) { Die "The pattern for $label matches $($pinMatches.Count) places in $path. Refusing to rewrite them all - tighten the pattern in this script." }
    $new = [regex]::Replace($text, $pattern, $replacement)
    if ($new -ne $text) {
        [IO.File]::WriteAllText($path, $new)
        Ok "$label -> updated"
    } else { Ok "$label -> unchanged" }
}

# Anchored on the constant's NAME, not merely on "64 hex digits on their own line": the pins file already
# carries other quoted literals, and a shape-only pattern would silently overwrite the next one added.
Replace-InFile $pinsFile '(?s)(ExpectedArchiveSha256\s*=\s*)"[0-9A-Fa-f]{64}";' "`${1}`"$sha`";" 'ExpectedArchiveSha256'
Replace-InFile $pinsFile 'ExpectedArchiveVersion = "[^"]*";' "ExpectedArchiveVersion = `"$Version`";" 'ExpectedArchiveVersion'
Replace-InFile $pinsFile 'ExpectedDescriptorModifiedOnUtc = "[^"]*";' "ExpectedDescriptorModifiedOnUtc = `"$stamp`";" 'ExpectedDescriptorModifiedOnUtc'
# There is deliberately no version constant to update. clio reads the shipped version out of this very
# archive (IBundledPackageCatalog), so nothing on the clio side has to be kept in step with it - which is
# what made raising the version cheap enough to require on every rebundle.

# ---------------------------------------------------------------- 7. rebuild, or verify nothing
Step '7. Rebuild clio (an install resolves the archive from the BUILD OUTPUT, not the repository)'
# Only the TFM this script drove clio from. Building every TFM is more thorough but routinely fails on
# Windows: a running `clio.exe mcp` holds clio.exe for its framework, and that failure has nothing to do
# with the rebundle. Other TFMs' outputs stay stale until they are built - said out loud below rather
# than left for someone to discover through an install that shipped the previous archive.
dotnet build (Join-Path $clioRoot 'clio\clio.csproj') -c $chosen.Configuration -f $chosen.Framework --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "    Rebuild FAILED. Until it succeeds, every local install still ships the PREVIOUS archive." -ForegroundColor Red
    Write-Host "    A common cause on Windows: a running 'clio.exe mcp' holds clio.exe. Stop it and re-run." -ForegroundColor Red
    Die 'Rebuild failed.'
}
Ok "clio rebuilt for $($chosen.Configuration)/$($chosen.Framework) - that output now carries this archive"

# Report EVERY other output, not just the one refreshed. An install run from any of them ships whatever
# archive that folder holds, and "I rebuilt clio" is precisely the belief that hides the mismatch.
foreach ($o in $outputs) {
    if ($o.Configuration -eq $chosen.Configuration -and $o.Framework -eq $chosen.Framework) { continue }
    $theirs = Join-Path (Split-Path $o.Dll -Parent) 'CrtProcessBuilder\CrtProcessBuilder.gz'
    $state = if (-not (Test-Path -LiteralPath $theirs)) { 'carries NO archive' }
             elseif ((Get-FileHash -LiteralPath $theirs -Algorithm SHA256).Hash.ToUpperInvariant() -ne $sha) { 'holds a DIFFERENT archive' }
             else { $null }
    if ($state) {
        Write-Host "    NOTE: clio\bin\$($o.Configuration)\$($o.Framework) $state. An install run from there ships that one." -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------- summary
Write-Host "`n=== Done. Nothing was committed." -ForegroundColor Cyan
Write-Host "    version   $Version"
Write-Host "    timestamp $stamp"
Write-Host "    sha256    $sha"
Write-Host @"

    Next, by hand, because they are judgement calls:
      * run the guard fixture:  dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Common"
      * install onto a stand and confirm the service answers
      * commit BOTH repositories, and say in the clio commit message which package-repo commit the bytes came from
"@
