# BMAD Status — Pipeline State Inspector

Shows the current state of all BMAD artifacts and tells you exactly what to do next.

## Usage

```
/bmad-status
/bmad-status <feature-name>
```

If `<feature-name>` is provided, filters to artifacts for that feature only.

---

## Instructions

### Step 1 — Scan artifact directories

Read the following directories and collect all files with their modification dates:
- `spec/prd/` — PRDs and SPECs
- `spec/adr/` — Architecture Decision Records
- `spec/stories/` — User stories
- `spec/test-plans/` — Test plans
- `spec/reviews/` — Adversarial review results
- `spec/sprint-status.yaml` — Sprint tracker (if exists)

### Step 2 — Read sprint-status.yaml

If `spec/sprint-status.yaml` exists, read it and extract story statuses.

### Step 3 — Build status report

Output format:

```markdown
## BMAD Pipeline Status
**As of**: {date and time}

---

### Active Features

| Feature | Phase | PRD | ADR | Stories | Test Plan | Review | Sprint Status |
|---------|-------|-----|-----|---------|-----------|--------|--------------|
| {name} | {current phase} | ✅/❌ | ✅/❌ | N files | ✅/❌ | ✅/⚠️/❌ | N ready / M done |

Legend: ✅ exists | ❌ missing | ⚠️ needs attention (review found BLOCK verdict)

---

### Story Breakdown (from sprint-status.yaml)

| Story | Feature | Status | Size |
|-------|---------|--------|------|
| story-{name}-1 | {feature} | ready-for-dev / in-progress / review / done | S/M/L |

---

### What to do next

{For each active feature, one concrete action:}

**{feature-name}**:
→ {e.g., "PRD exists, no ADR — run architect-agent with spec/prd/prd-feature.md"}
→ {e.g., "All stories ready-for-dev — pick story-feature-1 and start implementation"}
→ {e.g., "Review shows BLOCK verdict — fix L1-02 and L2-01 before proceeding"}

---

### Stale artifacts (no activity > 7 days)

{List files that haven't been modified in over a week — may be abandoned features}
```

### Step 4 — Completed features

List features where all stories are Done and test plans are Approved. Suggest archiving them:
```
✅ Completed: {feature-name} — all N stories done. Consider moving to spec/archive/.
```

### Rules

- Never modify any files — this command is read-only
- If sprint-status.yaml doesn't exist yet, note it and suggest running `/bmad` or creating it manually
- If a feature has a BLOCK review verdict, always surface it prominently
