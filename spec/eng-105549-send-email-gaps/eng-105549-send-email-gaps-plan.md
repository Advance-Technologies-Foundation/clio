# ENG-105549 Send email gaps: delivery plan

Companion of `eng-105549-send-email-gaps-spec.md`. Nothing here is started until the owner agrees the plan and answers
the open questions (section 4).

## 1. Repositories and branches

| Repo | Branch | Content |
|---|---|---|
| crt-process-builder (GHE) | `feature/ENG-105549-send-email-gaps` from `main` 1.6.6.136 | behaviours 1, 3, 4, 7, 8 (if needed), tests, stamp |
| clio | `feature/ENG-105549-send-email-gaps` from `master` | rebundle of the new cut, PROVENANCE pins, contract sentence (7e), MCP e2e, this spec folder |
| clio-knowledge | `feature/ENG-105549-send-email-gaps` from `master` 1.15.177 | `process-send-email` (5, 6, 4), `process-send-email-attachments` (1, 2, 3, 7e), `libraryVersion` + `sequence` bump |

## 2. Order of work

1. **Measurements that decide text or code** (stand `Creatio`, own `UsrSe105549*` processes only, schema writes strictly
   sequential): M6 `ignoreErrors` (spec 3.6) and M8 designer save of recipients (spec 3.8; the owner logs in to the
   Process Designer tab in Chrome). Records go to a local research folder, then summarised in a `-measurements.md` file
   here with no stand address. No package install is needed for these: they run on whatever CrtProcessBuilder the
   stand has (checked read-only first).
2. **Package, in slices**, each with targeted tests, then the full suite on `dev-nf` and `dev-n8`:
   - S1: 7d element reader (refactor first, so the new tests use it) + 7a + 7c + 7b + 7e tests.
   - S2: behaviour 1 (shared reachability helper, describe issue).
   - S3: behaviour 3 (duplicate route notice).
   - S4: behaviour 4 (performer expansion).
   - S5: behaviour 8 code, only if M8 shows the source or value changes; behaviour 5 code, only if the owner picks B.
   - Review gate: 3-lens parallel review over the whole diff; Blocker/High fixed before the stamp.
3. **Cut.** Claim the next free number (expected 1.6.6.138: highest of remote tags and remote branches'
   `descriptor.json` + 1; 1.6.6.137 is ENG-92710's) by pushing the tag at stamp time; `clio set-pkg-version`; never reuse
   a pushed number. A Jira claim comment only with the owner's yes.
4. **Stand install + run-time checks** - only with the owner's yes for each step. Before asking: the installed
   CrtProcessBuilder version is read, and the owner is told the install replaces the ENG-92710 build under test. Runs:
   TC-01..TC-05, TC-09 re-send, MCP e2e for the touched tools.
5. **clio.** Rebundle with `rebundle-process-builder.ps1 -Version <cut>`, PROVENANCE pins, contract sentence, MCP e2e
   cases (describe issue, duplicate notice, performer parameter, recipient replace if A), docs review, MCP review,
   ClioRing review (no Ring-consumed contract expected to change), targeted tests (`TestCategory=Unit&(Module=McpServer|
   Module=Common)` plus the bundled-package tests).
6. **clio-knowledge.** Guide edits within the size limit of each article (measured with the repository's own check;
   `process-send-email-attachments` is near its limit, so every added sentence is paid for by a trim), version bump,
   fact-check of every changed sentence against the measurements and the package tests, re-pin
   `curated-knowledge-names.json` in clio if the generation changes.
7. **Draft PRs**, titles exactly `ENG-105549 [Process Builder] Send email: fix the gaps found in the ENG-95985 Send email
   attachments test runs and review`. Bodies state the merge order: package (merge commit, no commit after the stamp)
   -> clio-knowledge (publish, check the published `libraryVersion`) -> clio (after the knowledge pin matches). No
   merge, no auto-merge, no reviewers unless the owner asks.

## 3. Stand rules (shared with ENG-92710)

- No `push-pkg`, install, compile, restart, unlock or delete without the owner's yes for that step.
- Nothing `UsrWsc*`, nothing in package `UsrWscEm10Proc`, nothing ENG-92710 created.
- Every object created is named `UsrSe105549*` and listed in the measurements file for cleanup.
- Schema writes through clio MCP one at a time.

## 4. Open questions for the owner (one at a time)

1. Behaviour 5: support and document `addMapping` replacement (A, recommended), refuse it (B), or a new `email` replace
   route in a separate story (C)? Spec 3.5. **Decided 2026-11-04: A** (support and document; package test + MCP e2e
   pin the in-place replacement).
2. Behaviour 7: track it as its own Sub-task (type Sub-task) under ENG-105549, or keep it in the Story? Recommendation:
   keep it in the Story - it ships in the same cut, branch and PR, and a Sub-task would only add a Jira row.
   **Decided 2026-11-04: kept in the Story.**
3. Behaviour 7d scope: this one seam only (recommended), or also the two older seams of the same shape?
   **Decided 2026-11-04: this seam only.**

## 5. Not in scope (follow-up candidates)

- `[#Name#]` expansion for a recipient `expression` (stored verbatim today, like the performer was).
- The two older static seams (`ProcessElementDependencyScanner`, `ProcessParameterService`), unless question 3 says so.
- A clio release that carries ENG-95985 (released clio 8.1.0.145 has no `email.attachments`).
