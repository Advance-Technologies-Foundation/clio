#!/bin/bash

# Default parameters
AUTHOR=""
LABEL=""
STATE="open"
LIMIT=0
OUTPUT=""
WATCH=false
RELEASE_MODE=false

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --author)
            AUTHOR="$2"
            shift 2
            ;;
        --label)
            LABEL="$2"
            shift 2
            ;;
        --state)
            STATE="$2"
            shift 2
            ;;
        --limit)
            LIMIT="$2"
            shift 2
            ;;
        --output)
            OUTPUT="$2"
            shift 2
            ;;
        --watch)
            WATCH=true
            shift
            ;;
        --release-mode)
            RELEASE_MODE=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--author USER] [--label LABEL] [--state STATE] [--limit N] [--output FILE] [--watch] [--release-mode]"
            exit 1
            ;;
    esac
done

function calculate_release_score() {
    local mergeable="$1"
    local review_decision="$2"
    local ci_success="$3"
    local ci_total="$4"
    
    local score=0
    
    # Mergeable status (40 points max)
    case "$mergeable" in
        "MERGEABLE") score=$((score + 40)) ;;
        "UNKNOWN") score=$((score + 20)) ;;
        "CONFLICTING") score=$((score + 0)) ;;
    esac
    
    # Review status (30 points max)
    case "$review_decision" in
        "APPROVED") score=$((score + 30)) ;;
        "REVIEW_REQUIRED") score=$((score + 10)) ;;
        "CHANGES_REQUESTED") score=$((score + 0)) ;;
        "") score=$((score + 15)) ;;  # No review yet
    esac
    
    # CI/CD status (20 points max)
    if [[ $ci_total -gt 0 ]]; then
        local ci_score=$((ci_success * 20 / ci_total))
        score=$((score + ci_score))
    else
        score=$((score + 10))  # No CI checks, neutral score
    fi
    
    # Age bonus (10 points max) - newer PRs get slightly higher score
    score=$((score + 5))  # Default age bonus
    
    echo "$score"
}

function get_pr_category() {
    local title="$1"
    local title_lower=$(echo "$title" | tr '[:upper:]' '[:lower:]')
    
    if [[ "$title_lower" == *"fix"* || "$title_lower" == *"bug"* || "$title_lower" == *"hotfix"* ]]; then
        echo "🐛 Bug Fix"
    elif [[ "$title_lower" == *"feat"* || "$title_lower" == *"feature"* || "$title_lower" == *"add"* ]]; then
        echo "✨ Feature"
    elif [[ "$title_lower" == *"doc"* || "$title_lower" == *"readme"* ]]; then
        echo "📚 Documentation"
    elif [[ "$title_lower" == *"test"* ]]; then
        echo "🧪 Tests"
    elif [[ "$title_lower" == *"refactor"* || "$title_lower" == *"cleanup"* ]]; then
        echo "♻️ Refactoring"
    elif [[ "$title_lower" == *"bump"* || "$title_lower" == *"update"* || "$title_lower" == *"upgrade"* ]]; then
        echo "⬆️ Dependencies"
    elif [[ "$title_lower" == *"security"* ]]; then
        echo "🔒 Security"
    elif [[ "$title_lower" == *"performance"* || "$title_lower" == *"perf"* ]]; then
        echo "⚡ Performance"
    else
        echo "🔧 Other"
    fi
}

function get_impact_level() {
    local title="$1"
    local base_ref="$2"
    
    if [[ "$base_ref" == "master" || "$base_ref" == "main" ]]; then
        if [[ "$title" == *"BREAKING"* || "$title" == *"breaking"* ]]; then
            echo "🔥 HIGH (Breaking)"
        elif [[ "$title" == *"critical"* || "$title" == *"urgent"* || "$title" == *"hotfix"* ]]; then
            echo "🔴 HIGH (Critical)"
        elif [[ "$title" == *"security"* ]]; then
            echo "🟠 MEDIUM (Security)"
        else
            echo "🟡 MEDIUM"
        fi
    else
        echo "🟢 LOW"
    fi
}

function write_pr_report() {
    if [[ "$RELEASE_MODE" == true ]]; then
        echo "🎯 RELEASE READINESS REPORT"
        echo "=========================="
    else
        echo "📋 PULL REQUESTS REPORT"
        echo "====================="
    fi
    echo ""

    # Check GitHub CLI authentication
    if ! gh auth status &>/dev/null; then
        echo "❌ GitHub CLI not authenticated. Please run: gh auth login"
        return 1
    fi

    # Build query parameters
    local query_params=("--state" "$STATE" "--json" "number,title,author,createdAt,updatedAt,url,headRefName,baseRefName,mergeable,reviewDecision,statusCheckRollup")
    
    if [[ -n "$AUTHOR" ]]; then
        query_params+=("--author" "$AUTHOR")
    fi
    
    if [[ -n "$LABEL" ]]; then
        query_params+=("--label" "$LABEL")
    fi
    
    if [[ $LIMIT -gt 0 ]]; then
        query_params+=("--limit" "$LIMIT")
    fi

    # Get PR list
    echo "🔍 Fetching pull requests..."
    local prs_json
    if ! prs_json=$(gh pr list "${query_params[@]}" 2>/dev/null); then
        echo "❌ Failed to fetch pull requests"
        return 1
    fi

    if [[ "$prs_json" == "[]" || -z "$prs_json" ]]; then
        echo "✅ No pull requests found for the specified criteria"
        return 0
    fi

    if [[ "$RELEASE_MODE" == true ]]; then
        format_release_ready_report "$prs_json"
    else
        format_standard_report "$prs_json"
    fi
}

function format_release_ready_report() {
    local prs_json="$1"
    
    local ready_count=0
    local maybe_count=0
    local not_ready_count=0
    
    local ready_section=""
    local maybe_section=""
    local not_ready_section=""
    
    # Process each PR and collect data
    while IFS= read -r pr_line; do
        if [[ -z "$pr_line" ]]; then
            continue
        fi
        
        local category=$(echo "$pr_line" | cut -d'|' -f1)
        local score=$(echo "$pr_line" | cut -d'|' -f2)
        local info=$(echo "$pr_line" | cut -d'|' -f3-)
        
        case "$category" in
            "READY")
                ready_count=$((ready_count + 1))
                ready_section="$ready_section$info"
                ;;
            "MAYBE")
                maybe_count=$((maybe_count + 1))
                maybe_section="$maybe_section$info"
                ;;
            "NOT_READY")
                not_ready_count=$((not_ready_count + 1))
                not_ready_section="$not_ready_section$info"
                ;;
        esac
    done < <(
        echo "$prs_json" | jq -r '.[] | @base64' | while IFS= read -r pr_data; do
            pr=$(echo "$pr_data" | base64 --decode)
            
            local number=$(echo "$pr" | jq -r '.number')
            local title=$(echo "$pr" | jq -r '.title')
            local author_name=$(echo "$pr" | jq -r '.author.name // .author.login')
            local mergeable=$(echo "$pr" | jq -r '.mergeable')
            local review_decision=$(echo "$pr" | jq -r '.reviewDecision')
            local base_ref=$(echo "$pr" | jq -r '.baseRefName')
            
            # Count CI/CD checks
            local ci_success=$(echo "$pr" | jq -r '[.statusCheckRollup[] | select(.conclusion == "SUCCESS")] | length')
            local ci_total=$(echo "$pr" | jq -r '.statusCheckRollup | length')
            
            # Calculate release score
            local score=$(calculate_release_score "$mergeable" "$review_decision" "$ci_success" "$ci_total")
            
            local category=$(get_pr_category "$title")
            local impact=$(get_impact_level "$title" "$base_ref")
            
            # Format PR info
            local pr_info="   PR #$number (Score: $score) $category"$'\n'"   📝 $title"$'\n'"   👤 $author_name | $impact"
            
            # Status indicators
            local mergeable_icon
            case "$mergeable" in
                "MERGEABLE") mergeable_icon="✅" ;;
                "CONFLICTING") mergeable_icon="❌" ;;
                *) mergeable_icon="⚠️" ;;
            esac
            
            local review_icon
            case "$review_decision" in
                "APPROVED") review_icon="✅" ;;
                "CHANGES_REQUESTED") review_icon="❌" ;;
                *) review_icon="⏳" ;;
            esac
            
            pr_info="$pr_info"$'\n'"   🔀 $mergeable_icon Merge | 📋 $review_icon Review | 🏗️ $ci_success/$ci_total CI"
            
            # Action items
            local actions=()
            if [[ "$review_decision" == "CHANGES_REQUESTED" ]]; then
                actions+=("Address requested changes")
            fi
            if [[ "$mergeable" == "CONFLICTING" ]]; then
                actions+=("Resolve merge conflicts")
            fi
            if [[ "$review_decision" == "" || "$review_decision" == "REVIEW_REQUIRED" ]]; then
                actions+=("Wait for code review")
            fi
            if [[ $ci_success -lt $ci_total && $ci_total -gt 0 ]]; then
                actions+=("Fix failing CI/CD checks")
            fi
            
            if [[ ${#actions[@]} -gt 0 ]]; then
                pr_info="$pr_info"$'\n'"   📋 Actions: ${actions[*]}"
            fi
            
            pr_info="$pr_info"$'\n'"$'\n'
            
            # Categorize by score
            if [[ $score -ge 70 ]]; then
                echo "READY|$score|$pr_info"
            elif [[ $score -ge 40 ]]; then
                echo "MAYBE|$score|$pr_info"
            else
                echo "NOT_READY|$score|$pr_info"
            fi
        done | sort -t'|' -k2,2nr
    )
    
    # Display results
    {
        echo "🟢 READY FOR RELEASE ($ready_count PRs) - Score ≥70"
        echo "══════════════════════════════════════"
        if [[ $ready_count -gt 0 ]]; then
            echo "$ready_section"
        else
            echo "   No PRs ready for immediate release"
            echo ""
        fi
        
        echo "🟡 NEEDS ATTENTION ($maybe_count PRs) - Score 40-69"
        echo "══════════════════════════════════════"
        if [[ $maybe_count -gt 0 ]]; then
            echo "$maybe_section"
        else
            echo "   No PRs in this category"
            echo ""
        fi
        
        echo "🔴 NOT READY ($not_ready_count PRs) - Score <40"
        echo "══════════════════════════════════════"
        if [[ $not_ready_count -gt 0 ]]; then
            echo "$not_ready_section"
        else
            echo "   No PRs in this category"
            echo ""
        fi
        
        # Release recommendation
        echo "📋 RELEASE RECOMMENDATION"
        echo "═══════════════════════"
        if [[ $ready_count -gt 0 ]]; then
            echo "✅ $ready_count PR(s) ready for immediate inclusion in next release"
            echo "⚠️  $maybe_count PR(s) could be included with minor fixes"
            echo "❌ $not_ready_count PR(s) should be deferred to future releases"
            echo ""
            echo "🚀 Suggested action: Proceed with release including ready PRs"
        else
            echo "⚠️  No PRs are fully ready for release"
            echo "📝 Consider delaying release or working on highest-scoring PRs first"
        fi
    } > >(if [[ -n "$OUTPUT" ]]; then tee "$OUTPUT"; else cat; fi)
}

function format_standard_report() {
    local prs_json="$1"
    
    # Parse JSON and process each PR
    {
        echo "$prs_json" | jq -r '.[] | @base64' | while IFS= read -r pr_data; do
            pr=$(echo "$pr_data" | base64 --decode)
            
            local number=$(echo "$pr" | jq -r '.number')
            local title=$(echo "$pr" | jq -r '.title')
            local author_name=$(echo "$pr" | jq -r '.author.name // .author.login')
            local created=$(echo "$pr" | jq -r '.createdAt' | xargs -I {} date -d {} '+%Y-%m-%d %H:%M' 2>/dev/null || echo "Unknown")
            local updated=$(echo "$pr" | jq -r '.updatedAt' | xargs -I {} date -d {} '+%Y-%m-%d %H:%M' 2>/dev/null || echo "Unknown")
            local head_ref=$(echo "$pr" | jq -r '.headRefName')
            local base_ref=$(echo "$pr" | jq -r '.baseRefName')
            local mergeable=$(echo "$pr" | jq -r '.mergeable')
            local review_decision=$(echo "$pr" | jq -r '.reviewDecision')
            
            echo "🔄 PR #$number: $title"
            echo "│"
            echo "├── 👤 Author: $author_name"
            echo "├── 📅 Created: $created | Updated: $updated"
            echo "├── 🌿 Branch: $head_ref → $base_ref"
            
            # Review status
            local review_status
            case "$review_decision" in
                "APPROVED") review_status="✅ Approved" ;;
                "CHANGES_REQUESTED") review_status="❌ Changes requested" ;;
                "REVIEW_REQUIRED") review_status="⏳ Review required" ;;
                *) review_status="⏳ Pending review" ;;
            esac
            echo "├── 📝 Review: $review_status"
            
            # Mergeable status
            local mergeable_icon
            case "$mergeable" in
                "MERGEABLE") mergeable_icon="✅" ;;
                "CONFLICTING") mergeable_icon="❌" ;;
                "UNKNOWN") mergeable_icon="⚠️" ;;
                *) mergeable_icon="❓" ;;
            esac
            echo "├── 🔀 Mergeable: $mergeable_icon $mergeable"
            echo "│"
            echo "└── 🚀 CI/CD Pipelines:"
            
            # CI/CD Status
            local checks_count=$(echo "$pr" | jq -r '.statusCheckRollup | length')
            if [[ $checks_count -gt 0 ]]; then
                echo "$pr" | jq -r '.statusCheckRollup[] | @base64' | while IFS= read -r check_data; do
                    check=$(echo "$check_data" | base64 --decode)
                    
                    local check_name=$(echo "$check" | jq -r '.name')
                    local conclusion=$(echo "$check" | jq -r '.conclusion')
                    local started_at=$(echo "$check" | jq -r '.startedAt')
                    local completed_at=$(echo "$check" | jq -r '.completedAt')
                    
                    local icon
                    case "$conclusion" in
                        "SUCCESS") icon="✅" ;;
                        "FAILURE") icon="❌" ;;
                        "CANCELLED"|"SKIPPED") icon="⏸️" ;;
                        *) icon="⏳" ;;
                    esac
                    
                    local duration=""
                    if [[ "$started_at" != "null" && "$completed_at" != "null" ]]; then
                        local start_epoch=$(date -d "$started_at" +%s 2>/dev/null || echo "0")
                        local end_epoch=$(date -d "$completed_at" +%s 2>/dev/null || echo "0")
                        if [[ $start_epoch -gt 0 && $end_epoch -gt 0 ]]; then
                            local diff=$((end_epoch - start_epoch))
                            local minutes=$((diff / 60))
                            local seconds=$((diff % 60))
                            duration=" (${minutes}m ${seconds}s)"
                        fi
                    fi
                    
                    echo "    ├── 🏗️  $check_name: $icon $conclusion$duration"
                done
            else
                echo "    └── ⏳ No CI/CD checks found"
            fi
            
            # Action items
            local action_items=()
            if [[ "$review_decision" == "CHANGES_REQUESTED" ]]; then
                action_items+=("Address requested changes")
            fi
            if [[ "$mergeable" == "CONFLICTING" ]]; then
                action_items+=("Resolve merge conflicts")
            fi
            if [[ -z "$review_decision" || "$review_decision" == "REVIEW_REQUIRED" ]]; then
                action_items+=("Wait for code review")
            fi
            if echo "$pr" | jq -e '.statusCheckRollup[] | select(.conclusion == "FAILURE")' >/dev/null; then
                action_items+=("Fix failing CI/CD checks")
            fi
            
            if [[ ${#action_items[@]} -gt 0 ]]; then
                echo ""
                echo "Action Items:"
                for item in "${action_items[@]}"; do
                    echo "• $item"
                done
            fi
            
            echo ""
            echo "═══════════════════════════════════════════"
            echo ""
        done
    } > >(if [[ -n "$OUTPUT" ]]; then tee "$OUTPUT"; else cat; fi)
}

# Main execution
if [[ "$WATCH" == true ]]; then
    echo "👀 Watch mode enabled. Press Ctrl+C to stop."
    echo ""
    
    while true; do
        clear
        write_pr_report
        echo "🔄 Refreshing in 30 seconds..."
        sleep 30
    done
else
    write_pr_report
fi
