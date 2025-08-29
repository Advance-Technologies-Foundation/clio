#!/bin/bash

# Default parameters
AUTHOR=""
LABEL=""
STATE="open"
LIMIT=0
OUTPUT=""
WATCH=false

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
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

function write_pr_report() {
    echo "📋 PULL REQUESTS REPORT"
    echo "====================="
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

    local report_content=()
    report_content+=("📋 PULL REQUESTS REPORT")
    report_content+=("=====================")
    report_content+=("")

    # Parse JSON and process each PR
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
    done > >(if [[ -n "$OUTPUT" ]]; then tee "$OUTPUT"; else cat; fi)
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
