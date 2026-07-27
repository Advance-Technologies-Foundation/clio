# ENG-93879 — OOTB requests: Upload file — Phase 2: Final Implementation Package

Status: APPROVED (user gate 2026-07-24) — scope gate closed; implementation belongs to a
separate session with this package as its binding input.
AMENDED 2026-07-27 (post-implementation review, user-approved) — see "Amendments" at the end.
Two substantive changes: (a) descriptions must be **mechanism-free** (contract rules 5-7, 10),
and (b) the clio snapshot-fixture refresh is now **in scope** (Verification targets). Where the
amendment and the original rule text disagree, the amendment wins.
Ticket: https://creatio.atlassian.net/browse/ENG-93879
Prerequisite: approved Phase 1 — `ootb-requests-upload-file-review-phase-1.md` (same folder).
This package constrains ONE bounded unit: publishing `crt.UploadFileRequest` to the OOTB
request catalog. It is not a redesign, not a task split, and not the implementation itself.

## Scope boundary

- **Start:** append one `crt.UploadFileRequest` entry as the **last** element of the
  `requests` array in `static-files-mcp/latest/RequestRegistry.json` (after
  `crt.RunBusinessProcessRequest`; keeps alphabetical order).
- **End (receiving boundary):** clio `get-request-info` (web and `schema-type=mobile`)
  returns the new entries when pointed at the edited files via
  `CLIO_REQUEST_REGISTRY_LOCAL_FILE` / `CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE`.
  clio source is read-only reference — no clio source change. (AMENDED 2026-07-27: the two
  snapshot fixtures, targets 10-11, are refreshed in this unit; see Verification targets.)
- **Stays out:** all items in Phase 1 "Out of scope" (clio changes incl. fixture refresh,
  per-version dirs, runtime/designer behavior changes, other action types, probe tools,
  `crt.SelectFileRequest`).

## File-level targets

### Repo `W:\Repos\static-files-mcp` (branch off `main`; sibling convention `feature/ENG-93879-...`)

| # | File | Allowed change |
|---|---|---|
| 1 | `latest/RequestRegistry.json` | Append ONE entry object, last in `requests`. No other byte changes (no reformat, no key reorder, no `references.*` edits). |
| 2 | `latest/request-docs/upload-file.request.md` | New file. Web authoring recipe. |
| 3 | `latest/MobileRequestRegistry.json` | Append ONE entry object, last in `requests`. No other byte changes. |
| 4 | `latest/mobile-request-docs/mobile-upload-file.request.md` | New file. Mobile authoring recipe. |

### Repo `W:\Repos\creatio-ui` (branch off `master`; convention `feature/ENG-93879-...`)

| # | File | Allowed change |
|---|---|---|
| 5 | `libs/studio-enterprise/util/model/src/lib/requests/upload-file-request.ts` | Add class-level JSDoc (summary + `@see {@link ./upload-file-request.md}`). No code changes. |
| 6 | `libs/studio-enterprise/util/model/src/lib/requests/base-upload-file-request.ts` | Add per-property JSDoc on all 12 properties. No signature/type/decorator changes. |
| 7 | `libs/studio-enterprise/util/model/src/lib/requests/upload-file-request.md` | New file. Content identical to target #2. |
| 8 | `libs/studio-enterprise/feature/mobile-page-interface-designer/src/lib/services/requests/mobile-upload-file.request.ts` | Add class-level JSDoc (summary + `@see {@link ./mobile-upload-file.request.md}`). No code changes. |
| 9 | `libs/studio-enterprise/feature/mobile-page-interface-designer/src/lib/services/requests/mobile-upload-file.request.md` | New file. Content identical to target #4. |

### Verification targets (no new test files)

- **Repo `W:\Repos\clio` — snapshot fixtures (AMENDMENT 2026-07-27, user-approved; supersedes
  the original "no clio changes" boundary for these two files ONLY):**

  | # | File | Allowed change |
  |---|---|---|
  | 10 | `clio.tests/Command/McpServer/Fixtures/RequestRegistry.live-snapshot.json` | Replace with the edited `static-files-mcp/latest/RequestRegistry.json` bytes. |
  | 11 | `clio.tests/Command/McpServer/Fixtures/MobileRequestRegistry.live-snapshot.json` | Replace with the edited `static-files-mcp/latest/MobileRequestRegistry.json` bytes. |

  Sanctioned by the guard test's own contract: "Until the producer publishes the file to the
  academy CDN, the fixture pins the authored payload from the `static-files-mcp` repository
  … — the same bytes the CDN will serve" (`RequestRegistrySnapshotTests.cs:28-30`). Still NO
  clio source/tool/POCO change. `RequestRegistrySnapshotTests` (5 tests) must stay green; note
  the refresh also corrects pre-existing drift (the mobile fixture was a stale copy of the web
  payload). Re-pin both from the CDN after publication.
- JSON validity + single-append diff checks on targets 1 and 3.
- clio `get-request-info` local-override run (see Minimum verification expectations).
- creatio-ui lint on the two touched projects (`util/model`,
  `feature/mobile-page-interface-designer`) — JSDoc/markdown additions must not introduce
  lint findings.

## Contract rules

1. **Entry envelope.** Each new entry carries exactly these top-level fields:
   `requestType`, `parameters`, `description`, `references` (with only `docs: [<one path>]`).
   Current code proves: these are the fields clio maps and surfaces
   (`clio/Command/McpServer/Tools/RequestInfoTool.cs` — `RequestInfoResponse` mapping)
   and the fields every sibling entry uses (`latest/RequestRegistry.json`).
2. **Parameter blob keys.** Inside each parameter: only `type`, `required` (only when
   `true`), `description`. No `values` needed here (no enum-valued parameter), and **no
   `valueSource`** — current code proves every parameter is page-resolvable, not
   environment-resolvable (Phase 1 finding 11; the `crt.FileList` referenced by
   `viewElementName` lives in the same page schema).
3. **Doc paths.** Exactly `request-docs/upload-file.request.md` (web) and
   `mobile-request-docs/mobile-upload-file.request.md` (mobile). Current code proves both
   match clio's validator regex
   (`clio/Command/McpServer/Tools/ComponentRegistryDocsPath.cs`) and the sibling naming
   scheme (`printables.request.md`, `mobile-close-page.request.md`).
4. **Web parameter set (exact, closed list).** The 11 `BaseUploadFileRequest` properties
   except `params`: `viewElementName`, `itemsAttributeName`, `maximumAllowedFileSize`,
   `allowedFileTypes`, `fileGroup`, `tag`, `fileEntitySchemaName`,
   `recordEntitySchemaName`, `recordColumnName`, `recordId`, `files`. `params` is omitted
   on web: its only documented shape (`selectFileOptions`) is authored solely by the
   mobile properties panel (Phase 1 finding 8), and inventing web semantics for it is
   forbidden.
5. **Required flags, web.** `required: true` ONLY on `viewElementName`. The
   runtime-validated trio (`recordId`, `recordColumnName`, `fileEntitySchemaName` —
   `upload-file-handler.ts:211-217`) is platform-derived
   (`upload-file-request-worker.ts:98-144`) and must NOT be flagged required, or agents
   will hand-author platform-derived values. Their descriptions must state: runtime-required,
   derived by the platform from the `crt.FileList` named by `viewElementName` (or page
   defaults — `SysFile` / `RecordId` / primary data source's Id), hand-author only in advanced
   scenarios without a file list; authored values win over derived ones
   (`setRequestParam`, worker lines 92-96).
   **Mechanism-free wording (amendment).** Source-code citations in this package identify the
   evidence; they are NOT vocabulary for the published text. Descriptions state the observable
   contract — behavior, ownership, precedence, timing — and never name an internal mechanism.
   Say "the platform derives … at page load", never "the preprocessor injects"; never name
   `UploadFileRequestMetaDataWorker` or the internal `crt.SelectFileRequest` chain. Rationale:
   preprocessors are not part of Creatio's supported extensibility surface, and the merged
   ENG-93187 precedent this package tells you to mirror (`printables-request.ts:33-48`) states
   ownership and timing only. Applies to every published surface: both registry entries, both
   recipe docs, and all creatio-ui JSDoc.
   **Web fallback, stated precisely (amendment).** The no-`viewElementName` fallback is
   `getDefaultRequestParams` (`upload-file-request-worker.ts:73-76, 106-114`): defaults derived
   from the page's PRIMARY DATA SOURCE (`SysFile` / `RecordId` / primary record Id). There is
   no "default file list" lookup, and the same fallback fires when `viewElementName` matches no
   `crt.FileList` on the page — so a misnamed value degrades SILENTLY to the page defaults
   rather than failing loudly. The web recipe's pitfall must describe that silent-wrong-storage
   outcome, not an empty-parameter failure.
6. **Provenance facts the descriptions must carry (each proven in current code):**
   - `maximumAllowedFileSize`: megabytes; effective limit is the MINIMUM of the `MaxFileSize`
     sys setting and this value (`base-file-upload-processing.service.ts:158-165`,
     `file-api/util/file.utils.ts:10`); designer validates min 1.
   - `allowedFileTypes`: comma-separated, case-insensitive items; each item is `.ext`,
     `ext`, `mime/type`, or `mime/*`; empty/absent = all types allowed except the
     `FileExtensionsDenyList` sys setting (`base-file-upload-processing.service.ts:77-129`).
   - `fileGroup`: `type: "string"`; document the OOTB Attachments group GUID
     `efbf3a0d-d780-465a-8e4b-8c0765197cfb` inline
     (`util/model/src/lib/models/file-group.type.ts`); do NOT add a `FileGroup` type
     definition to the registry's global `references`.
   - `recordId`: `type: "string | LookupValue"` (`base-upload-file-request.ts:15`); handler
     unwraps a LookupValue to its `value` (`upload-file-handler.ts:200-208`).
   - `files`: authored only as the `@event.files` event binding on `crt.FileList`'s
     `fileDropped`/`uploadClicked` wiring (worker lines 98-104); when absent the platform
     opens the file picker (`upload-file-handler.ts:148-161, 227-231`) — describe the
     behavior, do NOT name the internal `crt.SelectFileRequest` chain (rule 5 amendment).
     Note the worker injects `files: '@event.files'` into BUTTON requests too (worker lines
     98-104); a click simply carries no files, so the picker opens. State that reason — not
     "a button has no `files`", which is mechanically false.
   - Unsaved-record rule: upload to a non-`SysFile` storage on a record in Create mode is
     rejected with a notification (`upload-file-handler.ts:187-198`).
7. **Mobile parameter set (exact, closed list — grounded in the mobile runtime,
   Phase 1 finding 11a; evidence repo `W:\Repos\mobile-app`, read-only).** The web list
   minus `files` plus `params` — 11 parameters: `viewElementName` (required — the mobile
   preprocessor aborts with an error when no matching `crt.FileList` exists; no
   page-defaults fallback, `upload_file_request_preprocessor.dart:28-35`),
   `itemsAttributeName`, `maximumAllowedFileSize`, `allowedFileTypes`, `fileGroup`, `tag`,
   `fileEntitySchemaName`, `recordEntitySchemaName`, `recordColumnName`, `recordId`,
   `params` (`type: "JsonObject"`, description documenting the `selectFileOptions`
   nesting: `allowCamera` / `allowGallery` / `allowFiles` / `allowMultiple`, booleans,
   each defaulting to `true` — `upload_file_request_handler.dart:151-179`; note the
   designer panel exposes only the first three,
   `mobile-upload-file-request-properties-panel.component.ts:8-32`). `files` must NOT
   appear on the mobile entry (the mobile request has no such field —
   `upload_file_request.dart:29-41`; the picker flow is always used).
   Mobile-specific provenance facts the descriptions must carry:
   - **Wiring surface is BUTTON-ONLY on mobile (amendment).** The mobile runtime collects
     upload requests exclusively from `crt.Button` / `crt.MenuItem` `clicked` bindings
     (`upload_file_request_preprocessor.dart:44-55`); `fileDropped` / `uploadClicked` do not
     exist anywhere in `flutter_creatio/lib/`. Phase 1 finding 9's file-list-output wiring is
     WEB-ONLY (`upload-file-request-worker.ts:30-39`). The mobile entry and recipe must say
     button/menu-item `clicked` only, and must NOT offer the file-list-output wiring — a
     request bound there receives no storage parameters and the upload fails.
   - derived-vs-authored split mirrors web (fill-only-absent,
     `upload_file_request_preprocessor.dart:62-84`): `required: true` ONLY on
     `viewElementName`; the platform-derived storage parameters follow contract rule 5's
     description discipline, including its mechanism-free wording rule;
   - `recordEntitySchemaName` is optional at runtime — falls back to the page's primary
     model schema (`upload_file_request_handler.dart:58`);
   - `fileEntitySchemaName` / `recordColumnName` runtime-required; unresolved `recordId`
     → "Cannot upload files for unsaved records" (`upload_file_request_handler.dart:88-108`);
   - `maximumAllowedFileSize`: MB, min-with-`MaxFileSize` (same as web,
     `attachment_validator.dart` `validate`);
   - `allowedFileTypes` on MOBILE: comma-separated EXTENSIONS only (no mime patterns),
     intersected with the `FileExtensionsAllowList` sys setting acting as an allow-list
     (`attachment_validator.dart` `resolveAllowedExtensions`) — the mobile description
     must NOT copy the web mime-pattern/deny-list semantics. **Case-sensitivity differs from
     web (amendment):** mobile normalises the authored list with `.trim().replaceAll('.','')`
     only — no lowercasing (`attachment_validator.dart:86-89`) — and matches it against the
     file's LOWERCASED extension (`file_utils.dart:75`) via a case-sensitive `contains`
     (`attachment_validator.dart:103`). So the mobile description must NOT say
     "case-insensitive" (web does lowercase both sides,
     `base-file-upload-processing.service.ts:67,73`); it must say matching is case-sensitive
     against the lowercased extension and instruct authors to write lowercase entries.
8. **`baseParameters` (`$context`, `scopes`, `type`, `$initialEvent`) are never restated**
   inside `parameters`. Current code proves: clio surfaces them separately and instructs
   agents never to author them (`RequestInfoTool.cs` — `BaseParameters` doc comments).
9. **Registry description fields** (entry-level `description`, web + mobile): one to two
   sentences, pattern of the siblings — what it does + what it's bound to. The class JSDoc
   summary (target #5) and the entry description must tell the same story.
10. **Recipe doc structure** (targets 2/4/7/9): follow `printables.request.md`'s skeleton —
    title ("How to Wire …"), blockquote audience note, `## Metadata` block, mental-model
    section, concrete recipe section(s) with full `viewConfigDiff` wiring JSON, pitfalls,
    quick checklist. Web recipe must cover: FileList-connected button (canonical),
    FileList `fileDropped`/`uploadClicked` wiring, the preprocessor derivation story, and
    the advanced no-FileList case. Mobile recipe covers the mobile designer contract only.
11. **In-repo copies** (targets 7/9) are byte-identical to their published twins
    (targets 2/4) at commit time; the `@see` links use the relative-file form
    `@see {@link ./upload-file-request.md}` (pattern proof: unmerged branch commit
    `980e02084bd`, `printables-request.ts`).

## DTO and model deltas

- **None anywhere.** No TypeScript signature, type, decorator, or export changes in
  creatio-ui (JSDoc comments and new `.md` files only). No clio POCO/tool/test/fixture
  changes — the new entries use only already-mapped fields.
- **Must NOT be mirrored/duplicated:** no `FileGroup` (or any) addition to the registries'
  global `references.typeDefinitions` or `baseParameters`; no copy of registry content
  into clio guidance resources or snapshot fixtures; no duplication of the parameter map
  between the entry `description` and `parameters` blobs.

## Exact behavior to implement

### static-files-mcp

1. Append the web entry (contract rules 1-6, 9) as the last element of `requests` in
   `latest/RequestRegistry.json`.
2. Create `latest/request-docs/upload-file.request.md` (contract rule 10).
3. Append the mobile entry (contract rules 1-3, 7-9) as the last element of `requests` in
   `latest/MobileRequestRegistry.json`.
4. Create `latest/mobile-request-docs/mobile-upload-file.request.md` (contract rule 10,
   mobile scope).

### creatio-ui

5. Add class JSDoc to `UploadFileRequest`: summary sentence(s) matching the web entry
   description + `@see {@link ./upload-file-request.md}`.
6. Add per-property JSDoc to all 12 `BaseUploadFileRequest` properties carrying the same
   provenance facts as contract rules 5-6 (trimmed to the design-time contract, per the
   ENG-93187 JSDoc style in `printables-request.ts`). `params` gets a property JSDoc noting
   the mobile-only `selectFileOptions` use.
7. Create `upload-file-request.md` next to the class — identical content to step 2.
8. Add class JSDoc to `MobileUploadFileRequest`: mobile summary +
   `@see {@link ./mobile-upload-file.request.md}`.
9. Create `mobile-upload-file.request.md` next to it — identical content to step 4.

### Verification

10. Run the checks in "Minimum verification expectations".

## Ownership mapping

| Layer | Responsibility (already owned — do not duplicate in callers) |
|---|---|
| `static-files-mcp/latest/*.json` + docs | The published AI-facing contract; sole source agents read (via CDN) |
| creatio-ui request classes (JSDoc) | Design-time source of truth for future producer updates |
| `UploadFileRequestMetaDataWorker` (creatio-ui) | Deriving storage parameters from the page's `crt.FileList` — the docs must direct agents to rely on that derivation, never re-implement it in authored params. Refer to it in published text only as "the platform" (rule 5 amendment); this row names the type for implementers, not for the catalog |
| `UploadFileHandler` (creatio-ui) | Runtime validation and upload execution — unchanged |
| clio `RequestInfoTool` + registry transport | Catalog delivery, version resolution, doc fetch — unchanged |

## Deletion guidance

- **Nothing is deleted in this unit.** Explicitly do NOT: remove or edit existing sibling
  entries/docs; touch the unmerged `feature/ENG-93187-request-md-docs` branch or its files.
  ~~delete or refresh clio's stale `MobileRequestRegistry.live-snapshot.json` fixture
  (post-publish concern, out of scope)~~ — **AMENDED 2026-07-27:** refreshing both snapshot
  fixtures is now in scope (targets 10-11); re-pin from the CDN after publication.
- No temporary bypasses, obsolete helpers, or conflicting paths exist for this request —
  there is no legacy upload-file catalog entry anywhere to supersede.

## Minimum verification expectations

| Check | Expected proof |
|---|---|
| JSON validity | `python -c "import json; json.load(open(...))"` passes on both edited registries |
| Diff discipline | `git diff` in static-files-mcp shows exactly one appended object per JSON + 2 new doc files; nothing else |
| Web consumer round-trip | With `CLIO_REQUEST_REGISTRY_LOCAL_FILE` pointing at edited `RequestRegistry.json`: `get-request-info request-type=crt.UploadFileRequest` → `success: true`, `mode: detail`, the closed parameter list of contract rule 4, `required` only on `viewElementName` |
| Mobile consumer round-trip | Same with `CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE` + `schema-type=mobile` → contract rule 7 list |
| Docs pre-publish caveat | `documentation` may be absent and `documentationUnavailable: true` in local runs — docs fetch goes to the CDN, which does not have the files until merge/mirror; this is expected, not a failure |
| Doc-path validity | Both `references.docs[0]` values match `^(request-docs\|mobile-request-docs)/[A-Za-z0-9._-]+\.md$` |
| creatio-ui lint | `nx lint` (or repo-standard lint target) clean on `util/model` and `feature/mobile-page-interface-designer` |
| Content parity | Targets 7/9 byte-identical to targets 2/4 |

## Explicit remaining blockers

None.

## Strict no-invention rule

The implementation chat must NOT:

- add any request other than `crt.UploadFileRequest`, or a `crt.SelectFileRequest` entry;
- add `valueSource` annotations, new MCP probe tools, or any clio code/tool/POCO/test-logic
  change (the two snapshot FIXTURE files, targets 10-11, are the sole amended exception);
- add parameters, JSON fields, or type definitions beyond the closed lists in contract
  rules 1-2, 4, 7 (specifically: no `params`/`selectFileOptions` on the web entry, no
  `files` on the mobile entry, no global `references` edits);
- state mobile behavior beyond what `W:\Repos\mobile-app` proves (Phase 1 finding 11a and
  contract rule 7) — that repo is a read-only evidence source; no changes, commits, or
  PRs there;
- state upload size/type limits other than the verified, flavor-specific semantics: MB
  units and min-with-`MaxFileSize` on both flavors; on WEB a comma-separated type list
  (`.ext`/`ext`/`mime/type`/`mime/*`) with the `FileExtensionsDenyList` fallback (contract
  rule 6); on MOBILE a comma-separated extension-only list intersected with the
  `FileExtensionsAllowList` allow-list (contract rule 7) — never swap the two flavors'
  wording;
- touch `static-files-mcp/8.3.*/` or `10.0.0/`, or reformat/reorder anything in the two JSONs;
- change TypeScript code (signatures, types, decorators, imports) in creatio-ui;
- switch delivery to a generator/pipeline — the sibling-proven manual edit is the flow;
- open PRs/commits beyond the repos named in File-level targets and Verification targets
  (static-files-mcp, creatio-ui, and — per the 2026-07-27 amendment — the two clio snapshot
  fixtures);
- name an internal platform mechanism in any published description (rule 5 amendment).

If evidence during implementation contradicts this package, stop and surface it instead of
adapting silently.

## What the implementation agent may rely on without re-asking

- Everything in the approved Phase 1 "Confirmed findings" (1-11, as amended post-review).
- The calibration facts recorded here: sibling entry shape (`crt.PrintablesRequest` /
  `crt.CopilotActionRequest` in `latest/RequestRegistry.json`), `printables.request.md`
  doc skeleton, `allowedFileTypes` parsing, MB units and min() precedence, LookupValue
  unwrap, unsaved-record rule, preprocessor injection list and fill-only-absent semantics,
  mobile `selectFileOptions` defaults.
- User decisions: web + mobile scope; static-files-mcp publication in scope
  (`W:\Repos\static-files-mcp`); creatio-ui JSDoc **and** in-repo recipe `.md` + `@see`
  links; artifacts under `clio/spec/ootb-requests-upload-file/`; `W:\Repos\mobile-app`
  available as a read-only mobile-runtime evidence source.
- The mobile-runtime facts in Phase 1 finding 11a (preprocessor abort without FileList,
  four `selectFileOptions` booleans, allow-list extension filtering, min() size rule,
  `recordEntitySchemaName` fallback).
- Both registries' `requests` arrays are alphabetically ordered and `crt.UploadFileRequest`
  currently sorts last in both.

## Amendments

### 2026-07-27 — post-implementation review (user-approved)

The unit was implemented, then reviewed adversarially against the creatio-ui and
`W:\Repos\mobile-app` sources. Five findings were confirmed and fixed; this package was
amended so its wording matches what ships. Where an amendment and the original rule text
disagree, the amendment wins.

| # | Finding | Where the original package was wrong / silent | Amended in |
|---|---|---|---|
| 1 | Mobile wires upload from `crt.Button` / `crt.MenuItem` `clicked` ONLY; `fileDropped` / `uploadClicked` do not exist in the mobile runtime (`upload_file_request_preprocessor.dart:44-55`). Phase 1 finding 9's output wiring is WEB-only. | Silent — the package never scoped finding 9 to web, so the implementation offered mobile output wiring that cannot work. | Contract rule 7 |
| 2 | Mobile `allowedFileTypes` matching is CASE-SENSITIVE against the file's lowercased extension (`attachment_validator.dart:86-89, 103`; `file_utils.dart:75`); only web lowercases both sides. | Silent — "case-insensitive" was carried over from the web rule. | Contract rule 7 |
| 3 | The web no-/unmatched-`viewElementName` fallback is primary-data-source defaults, with no file-list lookup, and degrades SILENTLY (`upload-file-request-worker.ts:73-76, 106-114`). | Rule 5 was correct; the implementation embellished it. Amendment adds the silent-degradation consequence the recipe pitfall must state. | Contract rule 5 |
| 4 | The worker injects `files: '@event.files'` into button requests too (worker lines 98-104); the picker opens because a click carries no files — not because "a button has no `files`". | Silent. | Contract rule 6 |
| 5 | Published descriptions must be mechanism-free: no "preprocessor", no `UploadFileRequestMetaDataWorker`, no internal `crt.SelectFileRequest`. | **Contradicted** — rules 5-6 mandated "preprocessor-injected" provenance wording, which conflicts with the ENG-93187 precedent this package tells the implementer to mirror (`printables-request.ts:33-48` names no mechanism). Every provenance FACT is retained; only the mechanism naming is removed. | Contract rules 5-7, 10 |

Scope amendment (separate from the review): refreshing clio's two request-registry snapshot
fixtures is now IN scope (Verification targets 10-11), enabling local verification before
publication. No clio source, tool, POCO, or test-logic change. Re-pin both from the academy
CDN once the producer publishes.

Rule 5's mechanism-free discipline is general: it applies to any future request added to this
catalog, not just `crt.UploadFileRequest`.
