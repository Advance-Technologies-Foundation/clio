#!/bin/bash

echo "RELEASE READINESS REPORT"
echo "========================"
echo ""

if ! gh auth status &>/dev/null; then
    echo "ERROR: GitHub CLI not authenticated"
    exit 1
fi

echo "Fetching pull requests..."
prs_json=$(gh pr list --state open --json "number,title,author,mergeable,reviewDecision,statusCheckRollup" --limit 5)

if [[ "$prs_json" == "[]" ]]; then
    echo "No PRs found"
    exit 0
fi

echo "Processing PRs..."

ready_count=0
maybe_count=0 
not_ready_count=0

ready_prs=""
maybe_prs=""
not_ready_prs=""

echo "$prs_json" | jq -r '.[] | @base64' | while IFS= read -r pr_data; do
    pr=$(echo "$pr_data" | base64 --decode)
    
    number=$(echo "$pr" | jq -r '.number')
    title=$(echo "$pr" | jq -r '.title')
    author=$(echo "$pr" | jq -r '.author.login')
    mergeable=$(echo "$pr" | jq -r '.mergeable')
    review_decision=$(echo "$pr" | jq -r '.reviewDecision')
    
    ci_success=$(echo "$pr" | jq -r '[.statusCheckRollup[] | select(.conclusion == "SUCCESS")] | length')
    ci_total=$(echo "$pr" | jq -r '.statusCheckRollup | length')
    
    # Simple scoring
    score=0
    [[ "$mergeable" == "MERGEABLE" ]] && score=$((score + 40))
    [[ "$review_decision" == "APPROVED" ]] && score=$((score + 30))
    [[ $ci_success -eq $ci_total && $ci_total -gt 0 ]] && score=$((score + 20))
    score=$((score + 10)) # base points
    
    echo "PR #$number: $title (Score: $score)"
    echo "  Author: $author"
    echo "  Mergeable: $mergeable"
    echo "  Review: $review_decision"
    echo "  CI: $ci_success/$ci_total"
    
    if [[ $score -ge 70 ]]; then
        echo "  Status: READY FOR RELEASE"
    elif [[ $score -ge 40 ]]; then
        echo "  Status: NEEDS ATTENTION"
    else
        echo "  Status: NOT READY"
    fi
    echo ""
done

echo "Report complete."
