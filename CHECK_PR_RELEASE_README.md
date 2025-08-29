# Enhanced PR Analysis for Release Planning

## Overview

The enhanced `check-pr-release.sh` script provides comprehensive analysis of Pull Requests to help you make informed decisions about which PRs to include in releases. It scores PRs based on multiple criteria and categorizes them by release readiness.

## Files

- `check-pr.sh` - Basic PR monitoring script
- `check-pr.ps1` - PowerShell version for Windows
- `check-pr-release-final.sh` - Enhanced script with release readiness scoring
- `test-pr-release.sh` - Simplified test version

## Release Readiness Scoring

The enhanced script uses a 100-point scoring system:

### Scoring Criteria

1. **Mergeable Status (40 points max)**
   - `MERGEABLE`: 40 points
   - `UNKNOWN`: 20 points  
   - `CONFLICTING`: 0 points

2. **Review Status (30 points max)**
   - `APPROVED`: 30 points
   - `REVIEW_REQUIRED`: 10 points
   - `CHANGES_REQUESTED`: 0 points
   - No review yet: 15 points

3. **CI/CD Status (20 points max)**
   - All checks pass: 20 points
   - Partial success: Proportional points
   - No CI checks: 10 points (neutral)

4. **Base Points (10 points)**
   - All PRs receive 10 base points

### Classification

- **🟢 Ready for Release (≥70 points)**: PRs that can be safely included in the next release
- **🟡 Needs Attention (40-69 points)**: PRs that could be included with minor fixes
- **🔴 Not Ready (<40 points)**: PRs that should be deferred to future releases

## Usage

### Basic Release Analysis
```bash
./check-pr-release-final.sh
```

### With Filters
```bash
# Limit to 5 PRs
./check-pr-release-final.sh --limit 5

# Filter by author
./check-pr-release-final.sh --author kirillkrylov

# Save to file
./check-pr-release-final.sh --output release-analysis.txt

# Watch mode (refreshes every 30s)
./check-pr-release-final.sh --watch
```

### Command Options

- `--author USER` - Filter PRs by author
- `--label LABEL` - Filter PRs by label
- `--state STATE` - PR state (open, closed, merged). Default: open
- `--limit N` - Limit number of PRs analyzed. Default: 10
- `--output FILE` - Save report to file
- `--watch` - Watch mode with auto-refresh
- `--help` - Show help information

## Sample Output

```
🎯 RELEASE READINESS REPORT
==========================

🔍 Fetching pull requests...
📊 Analyzing PRs for release readiness...

🟢 READY FOR RELEASE (2 PRs) - Score ≥70
========================================
PR #376 (Score: 85/100) [Documentation]
   📝 Add docs for undocumented commands
   👤 kirillkrylov | 📅 2025-07-07  
   🔀 Merge: MERGEABLE | 📋 Review: Pending | 🏗️ CI: 1/1
   📋 Actions: Await code review

PR #362 (Score: 70/100) [Other]
   📝 Add warning level support to install-application command
   👤 app/copilot-swe-agent | 📅 2025-07-20  
   🔀 Merge: MERGEABLE | 📋 Review: CHANGES_REQUESTED | 🏗️ CI: 1/1
   📋 Actions: Address review comments

🟡 NEEDS ATTENTION (1 PRs) - Score 40-69
========================================
PR #378 (Score: 65/100) [Dependencies]
   📝 Bump angular dependencies
   👤 app/dependabot | 📅 2025-08-14  
   🔀 Merge: MERGEABLE | 📋 Review: Pending | 🏗️ CI: 0/1
   📋 Actions: Fix failing CI checks

🔴 NOT READY (1 PRs) - Score <40
========================================
PR #361 (Score: 25/100) [Bug Fix]
   📝 Fix windows paths in tests
   👤 kirillkrylov | 📅 2025-07-03  
   🔀 Merge: CONFLICTING | 📋 Review: Pending | 🏗️ CI: 0/1
   📋 Actions: Resolve merge conflicts Fix failing CI checks

📋 RELEASE RECOMMENDATION
=====================
✅ 2 PR(s) ready for immediate inclusion in next release
⚠️ 1 PR(s) could be included with minor fixes  
❌ 1 PR(s) should be deferred to future releases

🚀 Suggested action: Proceed with release including ready PRs
```

## Integration with Release Process

### Step 1: Analyze Current PRs
```bash
./check-pr-release-final.sh --output current-release-analysis.txt
```

### Step 2: Review Ready PRs
Focus on PRs in the "Ready for Release" section. These have:
- No merge conflicts
- Passing CI/CD checks
- Approved or minimal review requirements

### Step 3: Consider "Needs Attention" PRs
Evaluate if minor fixes can be quickly applied:
- Address review comments
- Fix failing CI checks
- Resolve minor merge conflicts

### Step 4: Plan Future Work
Use the "Not Ready" section to prioritize future development:
- PRs with higher scores should be addressed first
- Focus on resolving merge conflicts and CI failures

## Automation with GitHub Copilot

The script integrates with the existing `/check_pr` command in GitHub Copilot:

1. Use `/check_pr` to get comprehensive PR analysis
2. Run `./check-pr-release-final.sh` for release-specific scoring
3. Make informed decisions about release contents

## Tips for Release Planning

1. **Prioritize Bug Fixes**: High-scoring bug fixes should typically be included in releases
2. **Consider Feature Impact**: Large features might be worth waiting for even if score is lower
3. **Security PRs**: Security-related PRs should be expedited regardless of score
4. **Documentation**: Documentation PRs are usually safe to include if they pass basic checks
5. **Dependencies**: Dependency updates should be carefully reviewed for breaking changes

## Troubleshooting

### GitHub CLI Not Authenticated
```bash
gh auth login
```

### No PRs Found
- Check repository context: `gh repo view`
- Verify filters: remove `--author` or `--label` restrictions
- Check PR state: try `--state all`

### Script Permissions
```bash
chmod +x check-pr-release-final.sh
```

## Contributing

To improve the scoring algorithm or add new features:

1. Test changes with `test-pr-release.sh` first
2. Update scoring criteria documentation
3. Test with various PR scenarios
4. Update help text and examples
