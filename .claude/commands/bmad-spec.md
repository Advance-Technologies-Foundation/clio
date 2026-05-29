# BMAD Spec — Quick Feature Distillation

Distills a feature request into a minimal 5-field SPEC kernel. Use for small features (< 5 stories) where a full PRD is overkill, or as the first step before deciding whether to run the full pipeline.

## Usage

```
/bmad-spec <feature description>
/bmad-spec --auto <feature description>
```

**Flags:**
- `--auto` — skip all checkpoint gates, produce SPEC immediately without pauses

## When to use

- Feature is < 5 stories estimated
- You need a quick sanity-check before investing in a full PRD
- You want to align on scope before starting any pipeline
- Quick-fix or small enhancement that might not need an ADR

## When NOT to use

- Feature involves multiple CLI commands
- Feature changes public API or MCP tool signatures (use full `/bmad` pipeline)
- Feature has unclear scope (run brainstorming first, then come back)

---

## Instructions

### Step 1 — Parse the request

Read `project-context.md` to ground yourself in Clio-specific rules. Then identify:
- The core problem being solved
- Who experiences this problem
- What "solved" looks like concretely

**[CHECKPOINT — skip if `--auto`]**
Present your understanding in one sentence and ask:
> Does this match your intent? [Y] Yes / [N] Correct me

### Step 2 — Elicitation (3 questions max)

If intent is clear enough, skip to Step 3.

Otherwise, ask the 3 most important clarifying questions. Do not ask more than 3. Wait for answers before proceeding.

**[CHECKPOINT — skip if `--auto`]** (inherent — waiting for user answers)

### Step 3 — Generate SPEC kernel

Apply **Spec Law** (8 rules):
1. Every capability has both intent (WHAT) and success signal (HOW WE KNOW IT WORKED)
2. Intents describe WHAT, not HOW (no implementation details in capabilities)
3. Constraints must be specific enough to bend design decisions
4. Non-goals must have ≥1 entry
5. Success signal is concrete and automatable
6. Capability IDs are stable (CAP-N format, start from CAP-01)
7. Every load-bearing claim is in the SPEC or a companion note
8. Lean prose — every sentence carries load-bearing content

Output format:
```markdown
# SPEC: {Feature Title}

**Created**: {date}  
**Size estimate**: S (1-2 stories) | M (3-4 stories) | L (5 stories — consider full /bmad)  
**Recommended next**: /bmad-spec is sufficient | Run full /bmad pipeline

---

## Why

{1-3 sentences: the problem + who has it + why it matters now}

## Capabilities

| ID | Intent (WHAT) | Success Signal (HOW WE KNOW) |
|----|--------------|------------------------------|
| CAP-01 | {verb + object, no HOW} | {concrete, automatable test} |
| CAP-02 | … | … |

## Constraints

- **C1**: {specific constraint that forces a design decision} — e.g., "Must not break existing `--package-name` flag"
- **C2**: {another constraint}

## Non-goals

- Will NOT: {explicit exclusion 1}
- Will NOT: {explicit exclusion 2}

## Success Signal

{One concrete, automatable statement that proves the feature is done.}
Example: "Running `clio export-workspace --output ./out` produces a valid .zip file at the specified path and exits 0."

---

## Companion Notes

{Any load-bearing context that didn't fit above — edge cases, open questions, dependencies}
```

### Step 4 — Size check and recommendation

If size estimate is L (5 stories): recommend running full `/bmad` pipeline instead.
If size estimate is S/M: recommend proceeding directly to story creation with `story-writer`.

**[CHECKPOINT — skip if `--auto`]**
Show the SPEC and ask:
> [A] Approve and proceed to stories
> [R] Revise SPEC
> [U] Upgrade to full /bmad pipeline

### Step 5 — Save

Save to `spec/prd/spec-{kebab-feature-name}.md`.
Report: file path + size estimate + recommended next step.
