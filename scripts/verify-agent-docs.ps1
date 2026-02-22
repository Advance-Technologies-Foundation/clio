<#
.SYNOPSIS
Validates that agent-specific wrapper instruction files are synchronized with a canonical instruction file.

.DESCRIPTION
Reads the canonical instruction file, extracts its `Canonical-Version` marker, and then checks each wrapper file for:
- `Canonical-Source` matching the canonical path (normalized to forward slashes)
- `Canonical-Version` matching the canonical version

Returns a non-zero exit code when any wrapper is missing or out of sync, so the script can be used in CI.

.PARAMETER CanonicalPath
Path to the canonical instruction file.

.PARAMETER WrapperPaths
List of wrapper files that must reference the same canonical source and version.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\verify-agent-docs.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\verify-agent-docs.ps1 `
  -CanonicalPath "docs/agent-instructions/document-command.md" `
  -WrapperPaths @(".codex/skills/document-command/SKILL.md", ".github/prompts/doc-command.prompt.md")
#>
param(
    [string]$CanonicalPath = "docs/agent-instructions/document-command.md",
    [string[]]$WrapperPaths = @(
        ".codex/skills/document-command/SKILL.md",
        ".github/prompts/doc-command.prompt.md"
    )
)

$ErrorActionPreference = "Stop"

# Validate canonical source exists.
if (-not (Test-Path -LiteralPath $CanonicalPath)) {
    Write-Error "Canonical file not found: $CanonicalPath"
}

# Read canonical version marker used for sync checks.
$canonicalContent = Get-Content -LiteralPath $CanonicalPath -Raw
$versionMatch = [regex]::Match($canonicalContent, "(?m)^Canonical-Version:\s*(.+)$")
if (-not $versionMatch.Success) {
    Write-Error "Canonical version marker missing in $CanonicalPath"
}

$expectedVersion = $versionMatch.Groups[1].Value.Trim()
$expectedSource = $CanonicalPath.Replace("\", "/")

$hasFailure = $false

foreach ($wrapperPath in $WrapperPaths) {
    $wrapperHasFailure = $false

    # Validate wrapper file exists.
    if (-not (Test-Path -LiteralPath $wrapperPath)) {
        Write-Host "[FAIL] Missing wrapper: $wrapperPath"
        $hasFailure = $true
        $wrapperHasFailure = $true
        continue
    }

    # Extract wrapper markers that must match canonical values.
    $wrapperContent = Get-Content -LiteralPath $wrapperPath -Raw
    $sourceMatch = [regex]::Match($wrapperContent, "(?m)^Canonical-Source:\s*(.+)$")
    $versionMatch = [regex]::Match($wrapperContent, "(?m)^Canonical-Version:\s*(.+)$")

    if (-not $sourceMatch.Success) {
        Write-Host "[FAIL] Canonical-Source missing: $wrapperPath"
        $hasFailure = $true
        $wrapperHasFailure = $true
    }
    elseif ($sourceMatch.Groups[1].Value.Trim() -ne $expectedSource) {
        Write-Host "[FAIL] Canonical-Source mismatch in $wrapperPath"
        Write-Host "       expected: $expectedSource"
        Write-Host "       actual:   $($sourceMatch.Groups[1].Value.Trim())"
        $hasFailure = $true
        $wrapperHasFailure = $true
    }

    if (-not $versionMatch.Success) {
        Write-Host "[FAIL] Canonical-Version missing: $wrapperPath"
        $hasFailure = $true
        $wrapperHasFailure = $true
    }
    elseif ($versionMatch.Groups[1].Value.Trim() -ne $expectedVersion) {
        Write-Host "[FAIL] Canonical-Version mismatch in $wrapperPath"
        Write-Host "       expected: $expectedVersion"
        Write-Host "       actual:   $($versionMatch.Groups[1].Value.Trim())"
        $hasFailure = $true
        $wrapperHasFailure = $true
    }

    if (-not $wrapperHasFailure) {
        Write-Host "[OK] $wrapperPath"
    }
}

# Fail CI/build if any wrapper is missing or out of sync.
if ($hasFailure) {
    exit 1
}

Write-Host "All wrapper files are in sync with $CanonicalPath (version $expectedVersion)."
