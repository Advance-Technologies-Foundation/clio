# BMAD Workflow

Runs the full BMAD pipeline for a feature request: PM → Architect → Stories → QA Plan.

## Usage

```
/bmad <feature description>
/bmad --auto <feature description>
```

**Flags:**
- (default) **Facilitator mode**: each phase pauses at checkpoint gates for user approval before proceeding to the next phase
- `--auto` **Autonomous mode**: all phases run without pauses; each agent skips its internal checkpoints and runs to completion; full summary is printed at the end

> Autonomous mode is opt-in and explicit. It does not persist across sessions.

---

## Pipeline

```
Feature request
      │
      ▼
[pm-agent]         → spec/prd/prd-{name}.md
      │              ↳ adversarial review optional
      ▼
[architect-agent]  → spec/adr/adr-{name}.md
      │              ↳ adversarial review optional
      ▼
[story-writer]     → spec/stories/story-{name}-*.md
      │              ↳ spec/sprint-status.yaml updated
      ▼
[qa-planner]       → spec/test-plans/tp-{name}.md
```

---

## Instructions

Detect the `--auto` flag from the user's invocation. Strip it from the feature description before passing to agents.

### Phase 1 — PM Agent

Run `pm-agent` with the feature description (pass `--auto` if set).

**Facilitator mode**: after pm-agent completes and saves the PRD, ask:
> Phase 1 complete: `{prd-path}`
> [C] Continue to Architect
> [R] Request adversarial review first (`bmad-reviewer {prd-path}`)
> [S] Stop here

**Autonomous mode**: proceed immediately.

---

### Phase 2 — Architect Agent

Run `architect-agent` with the PRD path (pass `--auto` if set).

**Facilitator mode**: after architect-agent saves the ADR, ask:
> Phase 2 complete: `{adr-path}`
> [C] Continue to Stories
> [R] Request adversarial review first (`bmad-reviewer {adr-path}`)
> [S] Stop here

**Autonomous mode**: proceed immediately.

---

### Phase 3 — Story Writer

Run `story-writer` with the PRD and ADR paths (pass `--auto` if set).

**Facilitator mode**: after stories are saved, ask:
> Phase 3 complete: {N} stories in `spec/stories/`, sprint-status.yaml updated
> [C] Continue to QA Planner
> [S] Stop here

**Autonomous mode**: proceed immediately.

---

### Phase 4 — QA Planner

Run `qa-planner` with the feature name (pass `--auto` if set).

After completion, always print the final summary (both modes).

---

## Final Summary (always printed)

```markdown
## BMAD Pipeline Complete

**Feature**: {feature name}
**Mode**: Facilitator | Autonomous
**Date**: {date}

### Artifacts created

| Phase | Artifact | Path |
|-------|---------|------|
| 1 — PRD | prd-{name}.md | spec/prd/ |
| 2 — ADR | adr-{name}.md | spec/adr/ |
| 3 — Stories | {N} stories | spec/stories/ |
| 3 — Sprint | sprint-status.yaml | spec/ |
| 4 — Test plan | tp-{name}.md | spec/test-plans/ |

### Stories ready for development

| Story | Size | Status |
|-------|------|--------|
| story-{name}-1 | M | ready-for-dev |
| story-{name}-2 | S | ready-for-dev |

### Next steps

1. Pick story-{name}-1 from `spec/sprint-status.yaml`
2. Implement following the ADR implementation plan
3. Run `dotnet test --filter "Category=Unit"` locally before PR
4. Update story Status to `done` when PR merges
5. Run `/bmad-status` to see overall pipeline health
```

---

## Artifact locations

| Type | Path | Naming |
|------|------|--------|
| PRD | `spec/prd/` | `prd-{kebab-feature-name}.md` |
| ADR | `spec/adr/` | `adr-{kebab-feature-name}.md` |
| Stories | `spec/stories/` | `story-{name}-{N}.md` |
| Sprint tracker | `spec/` | `sprint-status.yaml` |
| Test plans | `spec/test-plans/` | `tp-{kebab-feature-name}.md` |
| Reviews | `spec/reviews/` | `review-{artifact}-{date}.md` |

---

## Related commands

| Command | When to use |
|---------|------------|
| `/bmad-spec` | Small feature (< 5 stories) — quick distillation before full pipeline |
| `/bmad-status` | Check where all features are in the pipeline |
| Use `bmad-reviewer` agent | Adversarially review any artifact before proceeding |
