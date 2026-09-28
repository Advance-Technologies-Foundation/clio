# Issue routing: component → label + owner

New issues are labelled and assigned from the **Component** dropdown in the issue forms.

| Piece | File |
|---|---|
| Issue forms (Component dropdown, native type label) | `.github/ISSUE_TEMPLATE/bug_report.yml`, `feature_request.yml` |
| Component → label, owners map (**source of truth**) | `.github/component-owners.json` |
| Routing logic (pure functions + GitHub calls) | `.github/scripts/issue-routing/issue-routing.js` |
| Workflow (`issues: opened, edited`) | `.github/workflows/issue-routing.yml` |
| Tests (`make test-issue-routing`) | `.github/scripts/issue-routing/issue-routing.test.js`, run on PRs by `issue-routing-tests.yml` |

## What happens

1. The form itself adds the **type** label natively (`bug` for a bug report, `functionality` for a
   feature request). GitHub issue forms can only add fixed labels per template — they cannot add a
   label depending on the selected dropdown value, so the component label needs the workflow.
2. The workflow reads the `### Component` section of the issue body and looks the value up in
   `component-owners.json` (by `option` text, or by `id`).
3. **Resolved component** → adds the component's `label`; if the issue has **no assignee**, assigns
   the **first** login in `owners` that can be assigned in this repository.
4. **No value, unknown value, or "Other / not sure"** → adds `needs-triage` (unless a component
   label is already on the issue). A blank issue (no form) is treated the same way.
5. **An owner cannot be assigned** (not a collaborator, no access) → warning in the run log,
   the component label is still added, plus `needs-triage`. The run never fails because of routing;
   only an invalid `component-owners.json` fails it.
6. **Edit** of the body:
   - the component did **not** change (text edits, same choice) → nothing happens, so a label or
     owner a human removed is not re-applied;
   - the component changed → every other component label is removed and the new one is added;
     `needs-triage` is removed once a component resolves, or added when the choice becomes
     "Other / not sure". An owner is assigned only if the issue has no assignee.
   Title-only edits are ignored. The run re-reads the live issue before planning, because the
   event payload can be older than the previous run's changes.

Rules that protect manual work:

- An existing assignee is **never** changed or added to — not on open and not on edit. Re-routing a
  claimed issue is a human decision.
- Only one owner is assigned. The `claim-clio-issue` skill uses the assignee as the claim signal
  and stops on multiple assignees; the auto-assigned owner is exactly the person who then claims it.
- Component labels are only touched on `opened` and when an edit changes the component. After such
  a change the form choice is authoritative: the issue carries exactly the chosen component label.
  Non-component labels are never removed.
- Missing labels are created with the `color`/`description` from the map.

## Updating the map

- **Change an owner**: edit `owners` of the component. Use GitHub user logins (`kirillkrylov` or
  `@kirillkrylov`). Teams (`@org/team`) are rejected: GitHub cannot assign issues to a team. The
  owner must be a repository collaborator, or assignment is skipped with a warning.
  `owners: []` means label-only routing.
- **Add / rename a component**: add an entry to `components` **and** the same `option` text to the
  `Component` dropdown in **every** form in `.github/ISSUE_TEMPLATE/`, in the same order. The test
  `every issue form offers exactly the components…` fails otherwise.
- **Renaming an option** breaks re-routing of older issues only on edit (the old value becomes
  unknown → `needs-triage` unless the old label is still there). Prefer keeping `id` stable.
- `paths` is informational (which code the component covers); nothing reads it yet.
- Run `make test-issue-routing` (or `node --test .github/scripts/issue-routing/issue-routing.test.js`).

The initial owners were taken from recent commit history per area and should be confirmed by the
maintainers.

## Why a separate map and not CODEOWNERS

Option A — reuse `.github/CODEOWNERS` by matching the chosen component against its path patterns:

- \+ one ownership file for PRs and issues.
- − an issue has no changed files; the component would have to be translated to a representative
  path anyway, which is the same map in disguise.
- − needs a CODEOWNERS parser (gitignore-style globbing, last-match-wins) in the workflow.
- − CODEOWNERS lists teams freely, but a team cannot be an issue assignee.
- − this repository currently has no CODEOWNERS file.

Option B — `.github/component-owners.json` (chosen):

- \+ components match what a reporter can pick, not code paths; no parser; a plain JSON file that
  `github-script` reads without dependencies.
- \+ can carry the label name, colour and description, so labels are created consistently.
- − a second place to keep ownership once a CODEOWNERS file exists. The `paths` field is there so a
  later sync (generate CODEOWNERS from the map, or check they agree) is mechanical.

Out of scope: guessing the component from free text, and syncing with PR reviewers.
