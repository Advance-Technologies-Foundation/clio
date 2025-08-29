#!/usr/bin/env pwsh

param(
    [string]$Author = "",
    [string]$Label = "",
    [string]$State = "open",
    [int]$Limit = 0,
    [string]$Output = "",
    [switch]$Watch = $false
)

function Write-PRReport {
    Write-Host "📋 PULL REQUESTS REPORT" -ForegroundColor Cyan
    Write-Host "=====================" -ForegroundColor Cyan
    Write-Host ""

    # Check GitHub CLI authentication
    try {
        gh auth status | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ GitHub CLI not authenticated. Please run: gh auth login" -ForegroundColor Red
            return
        }
    }
    catch {
        Write-Host "❌ GitHub CLI not found. Please install it first." -ForegroundColor Red
        return
    }

    # Build query parameters
    $queryParams = @("--state", $State, "--json", "number,title,author,createdAt,updatedAt,url,headRefName,baseRefName,mergeable,reviewDecision,statusCheckRollup")
    
    if ($Author) {
        $queryParams += @("--author", $Author)
    }
    
    if ($Label) {
        $queryParams += @("--label", $Label)
    }
    
    if ($Limit -gt 0) {
        $queryParams += @("--limit", $Limit.ToString())
    }

    # Get PR list
    Write-Host "🔍 Fetching pull requests..." -ForegroundColor Yellow
    $prsJson = & gh pr list @queryParams
    
    if ($LASTEXITCODE -ne 0 -or !$prsJson) {
        Write-Host "❌ Failed to fetch pull requests" -ForegroundColor Red
        return
    }

    $prs = $prsJson | ConvertFrom-Json
    
    if ($prs.Count -eq 0) {
        Write-Host "✅ No pull requests found for the specified criteria" -ForegroundColor Green
        return
    }

    $reportContent = @()
    $reportContent += "📋 PULL REQUESTS REPORT"
    $reportContent += "====================="
    $reportContent += ""

    foreach ($pr in $prs) {
        Write-Host "🔄 PR #$($pr.number): $($pr.title)" -ForegroundColor Blue
        $reportContent += "🔄 PR #$($pr.number): $($pr.title)"
        
        Write-Host "│" -ForegroundColor Gray
        $reportContent += "│"
        
        # Author and dates
        $authorName = if ($pr.author.name) { $pr.author.name } else { $pr.author.login }
        $created = [datetime]::Parse($pr.createdAt).ToString("yyyy-MM-dd HH:mm")
        $updated = [datetime]::Parse($pr.updatedAt).ToString("yyyy-MM-dd HH:mm")
        
        Write-Host "├── 👤 Author: $authorName" -ForegroundColor Gray
        Write-Host "├── 📅 Created: $created | Updated: $updated" -ForegroundColor Gray
        Write-Host "├── 🌿 Branch: $($pr.headRefName) → $($pr.baseRefName)" -ForegroundColor Gray
        
        $reportContent += "├── 👤 Author: $authorName"
        $reportContent += "├── 📅 Created: $created | Updated: $updated"
        $reportContent += "├── 🌿 Branch: $($pr.headRefName) → $($pr.baseRefName)"
        
        # Review status
        $reviewStatus = switch ($pr.reviewDecision) {
            "APPROVED" { "✅ Approved" }
            "CHANGES_REQUESTED" { "❌ Changes requested" }
            "REVIEW_REQUIRED" { "⏳ Review required" }
            default { "⏳ Pending review" }
        }
        Write-Host "├── 📝 Review: $reviewStatus" -ForegroundColor Gray
        $reportContent += "├── 📝 Review: $reviewStatus"
        
        # Mergeable status
        $mergeableIcon = switch ($pr.mergeable) {
            "MERGEABLE" { "✅" }
            "CONFLICTING" { "❌" }
            "UNKNOWN" { "⚠️" }
            default { "❓" }
        }
        Write-Host "├── 🔀 Mergeable: $mergeableIcon $($pr.mergeable)" -ForegroundColor Gray
        $reportContent += "├── 🔀 Mergeable: $mergeableIcon $($pr.mergeable)"
        
        Write-Host "│" -ForegroundColor Gray
        $reportContent += "│"
        
        # CI/CD Status
        Write-Host "└── 🚀 CI/CD Pipelines:" -ForegroundColor Gray
        $reportContent += "└── 🚀 CI/CD Pipelines:"
        
        if ($pr.statusCheckRollup -and $pr.statusCheckRollup.Count -gt 0) {
            foreach ($check in $pr.statusCheckRollup) {
                $icon = switch ($check.conclusion) {
                    "SUCCESS" { "✅" }
                    "FAILURE" { "❌" }
                    "CANCELLED" { "⏸️" }
                    "SKIPPED" { "⏸️" }
                    default { "⏳" }
                }
                
                $duration = ""
                if ($check.startedAt -and $check.completedAt) {
                    $start = [datetime]::Parse($check.startedAt)
                    $end = [datetime]::Parse($check.completedAt)
                    $span = $end - $start
                    $duration = " ($($span.Minutes)m $($span.Seconds)s)"
                }
                
                Write-Host "    ├── 🏗️  $($check.name): $icon $($check.conclusion)$duration" -ForegroundColor Gray
                $reportContent += "    ├── 🏗️  $($check.name): $icon $($check.conclusion)$duration"
            }
        } else {
            Write-Host "    └── ⏳ No CI/CD checks found" -ForegroundColor Gray
            $reportContent += "    └── ⏳ No CI/CD checks found"
        }
        
        # Action items
        $actionItems = @()
        if ($pr.reviewDecision -eq "CHANGES_REQUESTED") {
            $actionItems += "Address requested changes"
        }
        if ($pr.mergeable -eq "CONFLICTING") {
            $actionItems += "Resolve merge conflicts"
        }
        if ($pr.reviewDecision -eq "" -or $pr.reviewDecision -eq "REVIEW_REQUIRED") {
            $actionItems += "Wait for code review"
        }
        if ($pr.statusCheckRollup | Where-Object { $_.conclusion -eq "FAILURE" }) {
            $actionItems += "Fix failing CI/CD checks"
        }
        
        if ($actionItems.Count -gt 0) {
            Write-Host ""
            Write-Host "Action Items:" -ForegroundColor Yellow
            $reportContent += ""
            $reportContent += "Action Items:"
            foreach ($item in $actionItems) {
                Write-Host "• $item" -ForegroundColor Yellow
                $reportContent += "• $item"
            }
        }
        
        Write-Host ""
        Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host ""
        
        $reportContent += ""
        $reportContent += "═══════════════════════════════════════════"
        $reportContent += ""
    }

    # Save to file if requested
    if ($Output) {
        $reportContent | Out-File -FilePath $Output -Encoding UTF8
        Write-Host "📄 Report saved to: $Output" -ForegroundColor Green
    }
}

# Main execution
if ($Watch) {
    Write-Host "👀 Watch mode enabled. Press Ctrl+C to stop." -ForegroundColor Yellow
    Write-Host ""
    
    while ($true) {
        Clear-Host
        Write-PRReport
        Write-Host "🔄 Refreshing in 30 seconds..." -ForegroundColor Yellow
        Start-Sleep -Seconds 30
    }
} else {
    Write-PRReport
}
