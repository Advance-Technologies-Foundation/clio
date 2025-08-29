#!/bin/bash

# Enhanced PR check script with release readiness analysis
# Usage: ./check-pr-release.sh [options]

AUTHOR=""
LABEL=""
STATE="open"
LIMIT=10
OUTPUT=""
WATCH=false

show_help() {
    echo "Usage: $0 [options]"
    echo "Options:"
    echo "  --author USER      Filter by author"
    echo "  --label LABEL      Filter by label" 
    echo "  --state STATE      PR state (open, closed, merged). Default: open"
    echo "  --limit N          Limit number of PRs. Default: 10"
    echo "  --output FILE      Save output to file"
    echo "  --watch            Watch mode (refresh every 30s)"
    echo "  --help             Show this help"
    echo ""
    echo "This script analyzes PRs for release readiness based on:"
    echo "  - Mergeable status (40 points)"
    echo "  - Review status (30 points)"  
    echo "  - CI/CD success (20 points)"
    echo "  - Base points (10 points)"
    echo ""
    echo "Scoring:"
    echo "  ≥70 points: Ready for release"
    echo "  40-69 points: Needs attention"
    echo "  <40 points: Not ready"
}

while [[ $# -gt 0 ]]; do
    case $1 in
        --author) AUTHOR="$2"; shift 2 ;;
        --label) LABEL="$2"; shift 2 ;;
        --state) STATE="$2"; shift 2 ;;
        --limit) LIMIT="$2"; shift 2 ;;
        --output) OUTPUT="$2"; shift 2 ;;
        --watch) WATCH=true; shift ;;
        --help) show_help; exit 0 ;;
        *) echo "Unknown option: $1. Use --help for usage info."; exit 1 ;;
    esac
done

calculate_release_score() {
    local mergeable="$1"
    local review_decision="$2" 
    local ci_success="$3"
    local ci_total="$4"
    
    local score=10  # Base points
    
    # Mergeable status (40 points max)
    case "$mergeable" in
        "MERGEABLE") score=$((score + 40)) ;;
        "UNKNOWN") score=$((score + 20)) ;;
        "CONFLICTING") score=$((score + 0)) ;;
        *) score=$((score + 10)) ;;
    esac
    
    # Review status (30 points max)
    case "$review_decision" in
        "APPROVED") score=$((score + 30)) ;;
        "REVIEW_REQUIRED") score=$((score + 10)) ;;
        "CHANGES_REQUESTED") score=$((score + 0)) ;;
        "") score=$((score + 15)) ;;  # No review yet
        *) score=$((score + 5)) ;;
    esac
    
    # CI/CD status (20 points max)
    if [[ $ci_total -gt 0 ]]; then
        if [[ $ci_success -eq $ci_total ]]; then
            score=$((score + 20))  # All checks pass
        else
            local ci_score=$((ci_success * 20 / ci_total))
            score=$((score + ci_score))
        fi
    else
        score=$((score + 10))  # No CI checks
    fi
    
    echo "$score"
}

get_pr_category() {
    local title="$1"
    local title_lower=$(echo "$title" | tr '[:upper:]' '[:lower:]')
    
    if [[ "$title_lower" == *"fix"* || "$title_lower" == *"bug"* || "$title_lower" == *"hotfix"* ]]; then
        echo "Bug Fix"
    elif [[ "$title_lower" == *"feat"* || "$title_lower" == *"feature"* ]]; then
        echo "Feature"
    elif [[ "$title_lower" == *"doc"* || "$title_lower" == *"readme"* ]]; then
        echo "Documentation"
    elif [[ "$title_lower" == *"test"* ]]; then
        echo "Tests"
    elif [[ "$title_lower" == *"refactor"* ]]; then
        echo "Refactoring"
    elif [[ "$title_lower" == *"bump"* || "$title_lower" == *"update"* || "$title_lower" == *"upgrade"* ]]; then
        echo "Dependencies"
    elif [[ "$title_lower" == *"security"* ]]; then
        echo "Security"
    else
        echo "Other"
    fi
}

get_action_items() {
    local mergeable="$1"
    local review_decision="$2"
    local ci_success="$3"
    local ci_total="$4"
    
    local actions=()
    
    [[ "$mergeable" == "CONFLICTING" ]] && actions+=("Resolve merge conflicts")
    [[ "$review_decision" == "CHANGES_REQUESTED" ]] && actions+=("Address review comments")
    [[ -z "$review_decision" || "$review_decision" == "REVIEW_REQUIRED" ]] && actions+=("Await code review")
    [[ $ci_success -lt $ci_total && $ci_total -gt 0 ]] && actions+=("Fix failing CI checks")
    
    if [[ ${#actions[@]} -eq 0 ]]; then
        actions+=("Ready to merge")
    fi
    
    echo "${actions[@]}"
}

write_pr_report() {
    echo "🎯 RELEASE READINESS REPORT"
    echo "=========================="
    echo ""

    if ! gh auth status &>/dev/null; then
        echo "❌ ERROR: GitHub CLI not authenticated. Please run: gh auth login"
        return 1
    fi

    local query_params=("--state" "$STATE" "--json" "number,title,author,mergeable,reviewDecision,statusCheckRollup,baseRefName,createdAt")
    
    [[ -n "$AUTHOR" ]] && query_params+=("--author" "$AUTHOR")
    [[ -n "$LABEL" ]] && query_params+=("--label" "$LABEL")
    [[ $LIMIT -gt 0 ]] && query_params+=("--limit" "$LIMIT")

    echo "🔍 Fetching pull requests..."
    local prs_json
    if ! prs_json=$(gh pr list "${query_params[@]}" 2>/dev/null); then
        echo "❌ ERROR: Failed to fetch pull requests"
        return 1
    fi

    if [[ "$prs_json" == "[]" || -z "$prs_json" ]]; then
        echo "✅ No pull requests found for the specified criteria"
        return 0
    fi

    echo "📊 Analyzing PRs for release readiness..."
    echo ""
    
    local ready_prs=()
    local maybe_prs=()
    local not_ready_prs=()
    
    while IFS= read -r pr_data; do
        [[ -z "$pr_data" ]] && continue
        
        pr=$(echo "$pr_data" | base64 --decode)
        
        local number=$(echo "$pr" | jq -r '.number')
        local title=$(echo "$pr" | jq -r '.title')
        local author=$(echo "$pr" | jq -r '.author.login')
        local mergeable=$(echo "$pr" | jq -r '.mergeable')
        local review_decision=$(echo "$pr" | jq -r '.reviewDecision')
        local created=$(echo "$pr" | jq -r '.createdAt' | cut -d'T' -f1)
        
        local ci_success=$(echo "$pr" | jq -r '[.statusCheckRollup[] | select(.conclusion == "SUCCESS")] | length')
        local ci_total=$(echo "$pr" | jq -r '.statusCheckRollup | length')
        
        local score=$(calculate_release_score "$mergeable" "$review_decision" "$ci_success" "$ci_total")
        local category=$(get_pr_category "$title")
        local actions=$(get_action_items "$mergeable" "$review_decision" "$ci_success" "$ci_total")
        
        local pr_info="PR #$number (Score: $score/100) [$category]
   📝 $title
   👤 $author | 📅 $created  
   🔀 Merge: $mergeable | 📋 Review: ${review_decision:-"Pending"} | 🏗️ CI: $ci_success/$ci_total
   📋 Actions: $actions
"
        
        if [[ $score -ge 70 ]]; then
            ready_prs+=("$score|$pr_info")
        elif [[ $score -ge 40 ]]; then
            maybe_prs+=("$score|$pr_info")
        else
            not_ready_prs+=("$score|$pr_info")
        fi
        
    done < <(echo "$prs_json" | jq -r '.[] | @base64')
    
    # Sort each category by score (descending)
    IFS=$'\n' ready_prs=($(sort -t'|' -k1,1nr <<<"${ready_prs[*]}"))
    IFS=$'\n' maybe_prs=($(sort -t'|' -k1,1nr <<<"${maybe_prs[*]}"))
    IFS=$'\n' not_ready_prs=($(sort -t'|' -k1,1nr <<<"${not_ready_prs[*]}"))
    
    # Generate report
    local report_content=""
    
    # Ready for release section
    report_content+="🟢 READY FOR RELEASE (${#ready_prs[@]} PRs) - Score ≥70
========================================
"
    if [[ ${#ready_prs[@]} -gt 0 ]]; then
        for pr in "${ready_prs[@]}"; do
            report_content+="$(echo "$pr" | cut -d'|' -f2-)
"
        done
    else
        report_content+="   No PRs ready for immediate release

"
    fi
    
    # Needs attention section
    report_content+="🟡 NEEDS ATTENTION (${#maybe_prs[@]} PRs) - Score 40-69
========================================
"
    if [[ ${#maybe_prs[@]} -gt 0 ]]; then
        for pr in "${maybe_prs[@]}"; do
            report_content+="$(echo "$pr" | cut -d'|' -f2-)
"
        done
    else
        report_content+="   No PRs in this category

"
    fi
    
    # Not ready section
    report_content+="🔴 NOT READY (${#not_ready_prs[@]} PRs) - Score <40
========================================
"
    if [[ ${#not_ready_prs[@]} -gt 0 ]]; then
        for pr in "${not_ready_prs[@]}"; do
            report_content+="$(echo "$pr" | cut -d'|' -f2-)
"
        done
    else
        report_content+="   No PRs in this category

"
    fi
    
    # Release recommendation
    report_content+="📋 RELEASE RECOMMENDATION
=====================
"
    if [[ ${#ready_prs[@]} -gt 0 ]]; then
        report_content+="✅ ${#ready_prs[@]} PR(s) ready for immediate inclusion in next release
⚠️ ${#maybe_prs[@]} PR(s) could be included with minor fixes  
❌ ${#not_ready_prs[@]} PR(s) should be deferred to future releases

🚀 Suggested action: Proceed with release including ready PRs"
    else
        report_content+="⚠️ No PRs are fully ready for release
📝 Consider delaying release or working on highest-scoring PRs first

Priority order for fixes:
"
        # Show top 3 highest scoring PRs that need work
        local all_prs=("${maybe_prs[@]}" "${not_ready_prs[@]}")
        IFS=$'\n' all_prs=($(sort -t'|' -k1,1nr <<<"${all_prs[*]}"))
        
        local count=0
        for pr in "${all_prs[@]}"; do
            [[ $count -ge 3 ]] && break
            local score=$(echo "$pr" | cut -d'|' -f1)
            local number=$(echo "$pr" | cut -d'|' -f2- | grep -o 'PR #[0-9]*' | head -1)
            local title=$(echo "$pr" | cut -d'|' -f2- | sed -n 's/.*📝 \(.*\)/\1/p' | head -1)
            report_content+="$((count + 1)). $number (Score: $score) - $title
"
            ((count++))
        done
    fi
    
    # Output result
    if [[ -n "$OUTPUT" ]]; then
        echo "$report_content" | tee "$OUTPUT"
    else
        echo "$report_content"
    fi
}

# Main execution
if [[ "$WATCH" == true ]]; then
    echo "👀 Watch mode enabled. Press Ctrl+C to stop."
    echo ""
    
    while true; do
        clear
        write_pr_report
        echo ""
        echo "🔄 Refreshing in 30 seconds..."
        sleep 30
    done
else
    write_pr_report
fi
