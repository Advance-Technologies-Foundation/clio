---
description: Orchestrates multiple specialized sub-agents to perform comprehensive parallel code review
allowed-tools: Task, Bash
---

# Agentic Code Review Orchestrator

Performs comprehensive code review by orchestrating multiple specialized sub-agents working in parallel.

## Instructions:

1. First, gather git context for all sub-agents:
   - Current branch: !`git branch --show-current`
   - Parent branch detection: !`git merge-base --fork-point main HEAD 2>/dev/null || git merge-base --fork-point master HEAD 2>/dev/null || git merge-base --fork-point develop HEAD 2>/dev/null || echo ""`
   - List commits: !`git log --oneline HEAD --not $(git merge-base HEAD $(git branch -r | grep -E 'origin/(main|master|develop)' | head -1 | sed 's/origin\///'))`
   - Get full diff: !`git diff $(git merge-base HEAD $(git branch -r | grep -E 'origin/(main|master|develop)' | head -1 | sed 's/origin\///'))...HEAD`
   - File statistics: !`git diff --stat $(git merge-base HEAD $(git branch -r | grep -E 'origin/(main|master|develop)' | head -1 | sed 's/origin\///'))...HEAD`

2. Launch all sub-agents in parallel with the gathered context:

```
Task(
  description="Code quality review",
  subagent_type="code-quality-reviewer",
  prompt=f"""Review code quality for the following changes:
  
  Branch: {current_branch} → {parent_branch}
  Commits: {commits_list}
  
  DIFF:
  {git_diff}
  
  FILES CHANGED:
  {file_stats}
  """@
)

Task(
  description="Security analysis",
  subagent_type="security-reviewer", 
  prompt=f"""Analyze security vulnerabilities in the following changes:
  
  Branch: {current_branch} → {parent_branch}
  Commits: {commits_list}
  
  DIFF:
  {git_diff}
  
  FILES CHANGED:
  {file_stats}
  """
)

Task(
  description="Performance review",
  subagent_type="performance-reviewer",
  prompt=f"""Review performance implications of the following changes:
  
  Branch: {current_branch} → {parent_branch}
  Commits: {commits_list}
  
  DIFF:
  {git_diff}
  
  FILES CHANGED:
  {file_stats}
  """
)

Task(
  description="Testing assessment",
  subagent_type="testing-reviewer",
  prompt=f"""Assess testing coverage and quality for the following changes:
  
  Branch: {current_branch} → {parent_branch}
  Commits: {commits_list}
  
  DIFF:
  {git_diff}
  
  FILES CHANGED:
  {file_stats}
  """
)

Task(
  description="Bug detection",
  subagent_type="bug-detector",
  prompt=f"""Detect potential bugs and edge cases in the following changes:
  
  Branch: {current_branch} → {parent_branch}
  Commits: {commits_list}
  
  DIFF:
  {git_diff}
  
  FILES CHANGED:
  {file_stats}
  """
)
```

3. Aggregate results from all sub-agents and present a unified report:

## Final Report Format:

### Executive Summary
- Branch: [current] → [parent]
- Total commits: [number]
- Files changed: [number]
- Lines: [+added/-removed]
- Overall risk: [aggregated from all agents]

### Findings by Category

#### 🎨 Code Quality (from code-quality-reviewer)
[Agent's findings]

#### 🔒 Security (from security-reviewer)
[Agent's findings]

#### ⚡ Performance (from performance-reviewer)
[Agent's findings]

#### 🧪 Testing (from testing-reviewer)
[Agent's findings]

#### 🐛 Potential Bugs (from bug-detector)
[Agent's findings]

### Consolidated Action Items
- **Critical** (must fix before merge)
- **Important** (should fix)
- **Nice to have** (can be addressed later)

### Positive Highlights
[Aggregate positive findings from all agents]

## Additional context: $ARGUMENTS