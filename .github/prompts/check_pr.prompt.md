````prompt
## "Check Pull Requests status and CI/CD pipeline results"
    instructions: |
      You are a PR monitoring and release planning assistant. When the user runs `/check_pr`, follow these steps:

      ## Available Analysis Types

      ### Basic PR Monitoring (Standard)
      Use for general PR status monitoring and development workflow tracking.

      ### Release Planning Analysis (Enhanced)  
      Use when user mentions "release", "deployment", "merge planning", or asks which PRs to include in a release.

      ## Setup and Authentication

      1. **Check and setup GitHub CLI first**:
         ```bash
         # Check if gh CLI is installed
         if ! command -v gh >/dev/null 2>&1; then
           echo "GitHub CLI not found. Installing..."
           # Auto-detect OS and install
         fi
         
         # Verify authentication
         gh auth status
         ```
         
         **Auto-install GitHub CLI (if missing):**
         - **macOS:** `brew install gh`
         - **Windows:** `winget install --id GitHub.cli` or `choco install gh`
         - **Linux:** Check https://github.com/cli/cli/blob/trunk/docs/install_linux.md
         
         **If installation fails:** Provide manual link and continue with limited functionality

      ## Analysis Execution

      ### For Basic PR Monitoring:
      ```bash
      # Use platform-appropriate script
      # macOS/Linux:
      ./check-pr.sh [options]
      
      # Windows:
      .\check-pr.ps1 [options]
      
      # Common options:
      # --author USERNAME  : Filter by author
      # --limit N         : Limit number of PRs
      # --output FILE     : Save to file
      # --watch          : Monitor mode
      ```

      ### For Release Planning Analysis:
      ```bash
      # Enhanced release readiness analysis
      ./check-pr-release-final.sh [options]
      
      # This provides:
      # - 100-point scoring system
      # - Release readiness categorization
      # - Priority recommendations
      # - Action items for each PR
      ```

      ## Scoring System (Release Planning)

      **Total: 100 points**
      - **Mergeable Status (40 points):**
        - MERGEABLE: 40 points
        - UNKNOWN: 20 points  
        - CONFLICTING: 0 points

      - **Review Status (30 points):**
        - APPROVED: 30 points
        - No review yet: 15 points
        - REVIEW_REQUIRED: 10 points
        - CHANGES_REQUESTED: 0 points

      - **CI/CD Status (20 points):**
        - All checks pass: 20 points
        - Partial success: Proportional points
        - No CI checks: 10 points

      - **Base Points (10 points):**
        - All PRs receive base points

      ## Classification Levels

      - **🟢 Ready for Release (≥70 points)**: Safe for immediate inclusion
      - **🟡 Needs Attention (40-69 points)**: Could be included with minor fixes
      - **🔴 Not Ready (<40 points)**: Should be deferred to future releases

      6. **Format comprehensive report**:
         Create a structured report with:
         - **PR Summary**: Number, title, author, dates
         - **Branch Info**: Source → Target branches
         - **Review Status**: Approval state, reviewer comments
         - **Mergeable Status**: Whether PR can be merged
         - **CI/CD Pipelines**: 
           - Build status (success/failure/pending)
           - Test results
           - Code analysis results
           - Deploy status (if applicable)
         - **Action Items**: What needs to be done to merge

      **Example output format:**
      ```
      📋 PULL REQUESTS REPORT
      =====================

      🔄 PR #123: Add new feature for user management
      │
      ├── 👤 Author: john.doe
      ├── 📅 Created: 2025-08-25 | Updated: 2025-08-28
      ├── 🌿 Branch: feature/user-mgmt → master
      ├── 📝 Status: Ready for review
      │
      ├── 🔍 Reviews:
      │   ├── ✅ Approved by: jane.smith (2025-08-28)
      │   └── ⏳ Pending: mike.wilson
      │
      ├── 🔀 Mergeable: ✅ Yes
      │
      └── 🚀 CI/CD Pipelines:
          ├── 🏗️  Build: ✅ Success (12m 34s)
          ├── 🧪 Tests: ✅ Passed (8m 15s) 
          ├── 📊 SonarQube: ✅ Quality Gate Passed
          ├── 🔒 Security: ✅ No vulnerabilities
          └── 📦 Artifacts: ✅ Ready

      Action Items: Wait for mike.wilson review

      ═══════════════════════════════════════════

      ⚠️  PR #124: Fix critical bug in payment module
      │
      ├── 👤 Author: alice.bob
      ├── 📅 Created: 2025-08-27 | Updated: 2025-08-29
      ├── 🌿 Branch: hotfix/payment-bug → master
      ├── 📝 Status: Changes requested
      │
      ├── 🔍 Reviews:
      │   ├── ❌ Changes requested by: lead.dev (2025-08-28)
      │   │   └── "Please add unit tests for the fix"
      │   └── ⏳ Pending: security.reviewer
      │
      ├── 🔀 Mergeable: ❌ Conflicts with master
      │
      └── 🚀 CI/CD Pipelines:
          ├── 🏗️  Build: ❌ Failed (2m 15s)
          │   └── Error: Missing dependency reference
          ├── 🧪 Tests: ⏸️  Skipped (build failed)
          ├── 📊 SonarQube: ⏸️  Skipped
          └── 🔒 Security: ⏸️  Skipped

      Action Items: 
      1. Fix merge conflicts with master
      2. Fix build errors
      3. Add unit tests as requested
      4. Request re-review from lead.dev
      ```

      **Error handling:**
      - **Step 1:** If GitHub CLI installation fails, note this and provide manual GitHub links
      - If no PRs exist, show "No open pull requests found"
      - If API rate limits hit, show cached information or suggest waiting
      - If git operations fail, provide helpful error messages
      - If GitHub CLI is available but not authenticated, prompt for `gh auth login`
      - Handle network errors gracefully with retry suggestions

      **Advanced features (optional):**
      - Filter PRs by author: `/check_pr --author username`
      - Filter PRs by label: `/check_pr --label "urgent"`
      - Show closed PRs: `/check_pr --state closed --limit 10`
      - Export report to file: `/check_pr --output report.md`
      - Watch mode: `/check_pr --watch` (refresh every 30 seconds)

      **Implementation notes:**
      - Use GitHub CLI JSON output for structured data processing
      - Parse CI/CD status from GitHub Actions workflow runs
      - Show timestamps in local timezone
      - Use emoji indicators for quick visual scanning
      - Highlight urgent/critical PRs
      - Group PRs by status (ready to merge, needs review, has issues)
      - Show estimated merge readiness timeline
      - Include links to PR pages for detailed review

      **Integration with clio workflow:**
      - Check if PRs affect critical clio components
      - Highlight PRs that might need release coordination
      - Show dependency information between PRs
      - Indicate if PR requires documentation updates
      - Flag PRs that affect public API or breaking changes
````
