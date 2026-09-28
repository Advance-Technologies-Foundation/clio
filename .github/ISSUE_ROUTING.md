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
3. **Resolved component** → the issue ends with exactly that component's `component:*` label,
   `needs-triage` is removed, and the owners are notified according to `ownerNotification`:
   - `assign` → if the issue has **no assignee**, the **first** login in `owners` that can be
     assigned in this repository becomes the assignee;
   - `mention` → nobody is assigned; one routing comment mentions **every** owner (a later
     re-route updates that comment instead of adding another). A component with `owners: []` gets its label **and**
   `needs-triage`, because nobody is routed to pick it up.
4. **No value, unknown value, or "Other / not sure"** → adds `needs-triage` (unless a component
   label is already on the issue). A blank issue (no form) is treated the same way.
5. **An owner cannot be assigned** (not a collaborator, no access) → warning in the run log,
   the component label is still added, plus `needs-triage`. The run never fails because of routing;
   only an invalid `component-owners.json` fails it.
6. **Edit** of the body. The run re-reads the live issue and compares its labels with the current
   form choice (not with the previous body), so runs GitHub cancelled or collapsed in the
   concurrency queue cannot leave a stale label:
   - live labels already match the choice (text edits) → nothing happens, so an owner a human
     unassigned is not re-assigned;
   - they do not match → the other `component:*` labels are removed and the chosen one is added;
     an owner is assigned only if the issue has no assignee;
   - the author switched the dropdown to "Other / not sure" → `component:*` labels are removed and
     `needs-triage` is added. A component label a triager put on a "not sure" or blank issue is
     kept on later text edits.
   Title-only edits are ignored.

Rules that protect manual work:

- An existing assignee is **never** changed or added to — not on open and not on edit. Re-routing a
  claimed issue is a human decision.
- Only one owner is assigned. The `claim-clio-issue` skill uses the assignee as the claim signal
  and stops on multiple assignees; in `assign` mode the auto-assigned owner is exactly the person
  who then claims it. Where the owner should only be told and anyone may take the issue (for
  example an owner of many components), use `mention`: the assignee stays free for the claim.
- Labels with the `componentLabelPrefix` (`component:`) belong to routing: on a routed issue,
  change the Component dropdown rather than the label. Every other label — including the topic
  labels `MCP`, `Guidance`, `process-builder`, `ring` — is never added or removed by routing. The
  map validation rejects a component label outside the prefix for that reason.
- Missing labels are created with the `color`/`description` from the map.
- The run summary lists only the label changes GitHub accepted.

## Updating the map

- **Change an owner**: edit `owners` of the component. Use GitHub user logins (`kirillkrylov` or
  `@kirillkrylov`). Teams (`@org/team`) are rejected: GitHub cannot assign issues to a team. The
  owner must be a repository collaborator, or assignment is skipped with a warning.
  `owners: []` means the label is routed and the issue stays in `needs-triage`.
- **Add / rename a component**: add an entry to `components` **and** the same `option` text to the
  `Component` dropdown in **every** form in `.github/ISSUE_TEMPLATE/`, in the same order. The test
  `every issue form offers exactly the components…` fails otherwise.
- **Renaming an option** breaks re-routing of older issues only on edit (the old value becomes
  unknown → `needs-triage` unless the old label is still there). Prefer keeping `id` stable.
- **Choose how owners are notified**: top-level `ownerNotification` (`assign` or `mention`) is
  the default; a component's own `ownerNotification` overrides it.
- **Labels** must start with `componentLabelPrefix`. Renaming one leaves the old label on older
  issues; relabel them by hand.
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
