#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Automatically creates a new release by incrementing the minor version from the latest tag.

.DESCRIPTION
    This script:
    1. Gets the lat        # Create GitHub release (CLI should be ready from step 1)
        Write-Host "🚀 Step 6: Creating GitHub release..." -ForegroundColor Cyan
        
        if (Test-GitHubCLI) {       if (Test-GitHubCLI) {      if (Test-GitHubCLI) { tag from the repository
    2. Parses the current version (format X.Y.Z.W)
    3. Increments the minor version (last number) by 1
    4. Creates and pushes the new tag to the repository

.PARAMETER Force
    Skip confirmation prompt and create tag immediately

.EXAMPLE
    .\create-release.ps1
    # Interactive mode with confirmation

.EXAMPLE
    .\create-release.ps1 -Force
    # Automatic mode without confirmation
#>

param(
    [switch]$Force
)

function Get-LatestTag {
    try {
        $latestTag = git describe --tags --abbrev=0 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $null
        }
        return $latestTag
    }
    catch {
        return $null
    }
}

function Test-VersionFormat {
    param([string]$version)
    return $version -match '^\d+\.\d+\.\d+\.\d+$'
}

function Test-GitHubCLI {
    try {
        $null = Get-Command gh -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Install-GitHubCLI {
    Write-Host "🔧 GitHub CLI (gh) not found. Installing..." -ForegroundColor Yellow
    
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        Write-Host "📦 Installing GitHub CLI on Windows..." -ForegroundColor Cyan
        try {
            # Try winget first (Windows 10+)
            winget install --id GitHub.cli --silent
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ GitHub CLI installed successfully via winget" -ForegroundColor Green
                return $true
            }
        }
        catch {
            Write-Host "⚠️  winget not available, trying chocolatey..." -ForegroundColor Yellow
        }
        
        try {
            # Try chocolatey as fallback
            choco install gh -y
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ GitHub CLI installed successfully via chocolatey" -ForegroundColor Green
                return $true
            }
        }
        catch {
            Write-Host "❌ Failed to install via chocolatey" -ForegroundColor Red
        }
    }
    elseif ($IsMacOS -or $env:OSTYPE -eq "darwin") {
        Write-Host "📦 Installing GitHub CLI on macOS..." -ForegroundColor Cyan
        try {
            brew install gh
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ GitHub CLI installed successfully via homebrew" -ForegroundColor Green
                return $true
            }
        }
        catch {
            Write-Host "❌ Failed to install via homebrew. Please install manually: https://cli.github.com/" -ForegroundColor Red
        }
    }
    else {
        Write-Host "📦 Linux detected. Please install GitHub CLI manually:" -ForegroundColor Cyan
        Write-Host "🔗 https://github.com/cli/cli/blob/trunk/docs/install_linux.md" -ForegroundColor Blue
    }
    
    Write-Host "❌ Automatic installation failed" -ForegroundColor Red
    Write-Host "📝 Manual installation: https://cli.github.com/" -ForegroundColor Blue
    return $false
}

function Test-GitHubAuth {
    try {
        $authStatus = gh auth status 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ GitHub CLI is authenticated" -ForegroundColor Green
            return $true
        }
        else {
            Write-Host "⚠️  GitHub CLI is not authenticated" -ForegroundColor Yellow
            Write-Host "🔑 Please run: gh auth login" -ForegroundColor Blue
            return $false
        }
    }
    catch {
        Write-Host "❌ Failed to check GitHub CLI authentication" -ForegroundColor Red
        return $false
    }
}

function Get-NextVersion {
    param([string]$currentVersion)
    
    if (-not (Test-VersionFormat $currentVersion)) {
        throw "Invalid version format: $currentVersion. Expected format: X.Y.Z.W"
    }
    
    $parts = $currentVersion.Split('.')
    $major = [int]$parts[0]
    $minor = [int]$parts[1] 
    $patch = [int]$parts[2]
    $build = [int]$parts[3]
    
    # Increment the build number (minor version)
    $build++
    
    return "$major.$minor.$patch.$build"
}

function New-ReleaseTag {
    param(
        [string]$version,
        [bool]$force = $false
    )
    
    Write-Host "🏷️  Creating new tag: $version" -ForegroundColor Green
    
    if (-not $force) {
        $confirmation = Read-Host "Do you want to create and push tag '$version' and create GitHub release? (y/N)"
        if ($confirmation -notmatch '^[Yy]') {
            Write-Host "❌ Tag creation cancelled" -ForegroundColor Yellow
            return $false
        }
    }
    
    try {
        # Create tag
        git tag $version
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create tag"
        }
        
        # Push tag
        git push origin $version
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to push tag"
        }
        
        Write-Host "✅ Successfully created and pushed tag: $version" -ForegroundColor Green
        
        # Create GitHub release (CLI should be ready from step 1)
        Write-Host "🚀 Step 6: Creating GitHub release..." -ForegroundColor Cyan
        
        if (-not (Test-GitHubCLI)) {
            if (Install-GitHubCLI) {
                Write-Host "� Refreshing environment..." -ForegroundColor Cyan
                # Refresh PATH for current session
                $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
            }
        }
        
        if (Test-GitHubCLI) {
            if (Test-GitHubAuth) {
                try {
                    gh release create $version --title "Release $version" --notes "Automated release $version"
                    if ($LASTEXITCODE -eq 0) {
                        Write-Host "✅ Successfully created GitHub release: $version" -ForegroundColor Green
                    }
                    else {
                        Write-Host "⚠️  Could not create GitHub release (API error)" -ForegroundColor Yellow
                        Write-Host "📝 Please create release manually at: https://github.com/Advance-Technologies-Foundation/clio/releases/new?tag=$version" -ForegroundColor Blue
                    }
                }
                catch {
                    Write-Host "⚠️  Could not create GitHub release: $_" -ForegroundColor Yellow
                    Write-Host "📝 Please create release manually at: https://github.com/Advance-Technologies-Foundation/clio/releases/new?tag=$version" -ForegroundColor Blue
                }
            }
            else {
                Write-Host "📝 Please create release manually at: https://github.com/Advance-Technologies-Foundation/clio/releases/new?tag=$version" -ForegroundColor Blue
            }
        }
        else {
            Write-Host "⚠️  GitHub CLI not available (setup failed in step 1)" -ForegroundColor Yellow
            Write-Host "📝 Please create release manually at: https://github.com/Advance-Technologies-Foundation/clio/releases/new?tag=$version" -ForegroundColor Blue
        }
        
        Write-Host "🚀 Release workflow will be triggered automatically" -ForegroundColor Cyan
        return $true
    }
    catch {
        Write-Host "❌ Error creating tag: $_" -ForegroundColor Red
        return $false
    }
}

# Main execution
try {
    Write-Host "� Step 1: Checking GitHub CLI setup..." -ForegroundColor Cyan
    
    # Check and install GitHub CLI first
    if (-not (Test-GitHubCLI)) {
        if (Install-GitHubCLI) {
            Write-Host "🔄 Refreshing environment..." -ForegroundColor Cyan
            # Refresh PATH for current session
            $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
        }
        else {
            Write-Host "⚠️  GitHub CLI installation failed, but continuing with tag creation..." -ForegroundColor Yellow
        }
    }
    
    # Check authentication if CLI is available
    if (Test-GitHubCLI) {
        Test-GitHubAuth | Out-Null
    }
    
    Write-Host "�🔍 Step 2: Getting latest release tag..." -ForegroundColor Cyan
    
    $currentTag = Get-LatestTag
    
    if (-not $currentTag) {
        Write-Host "⚠️  No existing tags found. Starting with version 8.0.1.1" -ForegroundColor Yellow
        $newVersion = "8.0.1.1"
    }
    else {
        Write-Host "📍 Current latest tag: $currentTag" -ForegroundColor White
        
        # Remove 'v' prefix if present
        $cleanVersion = $currentTag -replace '^v', ''
        
        if (-not (Test-VersionFormat $cleanVersion)) {
            throw "Current tag '$currentTag' has invalid format. Expected: X.Y.Z.W"
        }
        
        $newVersion = Get-NextVersion $cleanVersion
    }
    
    Write-Host "🎯 Next version will be: $newVersion" -ForegroundColor Green
    
    $success = New-ReleaseTag -version $newVersion -force $Force
    
    if ($success) {
        Write-Host ""
        Write-Host "📋 Summary:" -ForegroundColor Cyan
        Write-Host "   ✅ Tag '$newVersion' created and pushed" -ForegroundColor White
        Write-Host "   ✅ GitHub release created (if gh CLI available)" -ForegroundColor White
        Write-Host "   🚀 NuGet package will be published automatically" -ForegroundColor White
        Write-Host ""
        Write-Host "🔗 Monitor progress at: https://github.com/Advance-Technologies-Foundation/clio/releases" -ForegroundColor Blue
    }
}
catch {
    Write-Host "❌ Error: $_" -ForegroundColor Red
    exit 1
}
