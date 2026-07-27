# ENG-93879 — OOTB requests: Upload file — Phase 1: Review and Narrowed Scope

Status: APPROVED (user gate 2026-07-24) — open blocker 1 resolved: creatio-ui gets JSDoc
AND in-repo recipe .md files with `@see` links (user chose the fuller pattern over the draft
default). The In/Out-of-scope sections below are amended accordingly.
Ticket: https://creatio.atlassian.net/browse/ENG-93879
Repos in scope: `W:\Repos\static-files-mcp` (primary), `W:\Repos\creatio-ui` (JSDoc source of truth)
Consumer (read-only reference, no changes): `W:\Repos\clio`

## Scope summary

Add `crt.UploadFileRequest` to the OOTB button-action request catalog served to AI agents via
clio `get-request-info`: a web entry in `latest/RequestRegistry.json` + authoring recipe
`latest/request-docs/upload-file.request.md`, a mobile entry in `latest/MobileRequestRegistry.json`
+ `latest/mobile-request-docs/mobile-upload-file.request.md` (all in `static-files-mcp`), and
JSDoc on the request classes in `creatio-ui` as the producer source of truth. No clio code changes.

## Confirmed findings

All statements below are grounded in current code on the checked-out branches
(`creatio-ui` @ master, `static-files-mcp` @ main, `clio` @ master).

1. **The catalog is missing Upload file.** The published web registry
   (`static-files-mcp/latest/RequestRegistry.json`) contains exactly 5 request types:
   `crt.CancelRecordChangesRequest`, `crt.ClosePageRequest`, `crt.CopilotActionRequest`
   (added 2026-07-24 by ENG-93880), `crt.PrintablesRequest`, `crt.RunBusinessProcessRequest`.
   The mobile registry (`latest/MobileRequestRegistry.json`) contains exactly 3:
   `crt.CancelRecordChangesRequest`, `crt.ClosePageRequest`, `crt.RunBusinessProcessRequest`
   (no Printables, no CopilotAction). Neither contains `crt.UploadFileRequest`. Both
   `requests` arrays are alphabetically ordered. (Note: clio's pinned test fixture
   `MobileRequestRegistry.live-snapshot.json` shows 4 mobile entries incl. Printables — it is
   stale relative to the live repo; the live files above are authoritative for this change.)

2. **Delivery pattern (most recent sibling, ENG-93880 — CopilotActionRequest,
   commits `3082ab8` + `5c05982` in static-files-mcp):** one new object in the
   `requests` array of `latest/RequestRegistry.json` (fields: `requestType`, `parameters`
   with per-parameter `type` / `required` / `description`, top-level `description`,
   `references.docs[]`), plus one authoring-recipe markdown under `latest/request-docs/`.
   Only `latest/` was touched — no per-version dirs (`8.3.x/`, `10.0.0/`).
   ENG-93880 did NOT touch creatio-ui.

3. **JSDoc pattern (earlier sibling, ENG-93187, creatio-ui commit `46ff964d109`, merged
   to master via PR #2593):** class-level JSDoc summary + per-property JSDoc on the request
   class in `libs/studio-enterprise/util/model/src/lib/requests/` (see
   `printables-request.ts:3-48`). The follow-up commits that added recipe `.md` files
   *inside creatio-ui* + `@see {@link ./*.md}` links (`980e02084bd`, `51d276ce010`) live on
   the **unmerged** remote branch `feature/ENG-93187-request-md-docs`; master has JSDoc only.
   The operative published docs are the copies in `static-files-mcp/latest/request-docs/`.

4. **Request classes.** `creatio-ui/libs/studio-enterprise/util/model/src/lib/requests/upload-file-request.ts`
   declares `crt.UploadFileRequest` (`@CrtRequest`, properties panel
   `crt.UploadFileRequestPropertiesPanel`) with an empty body and **no JSDoc**; all 12
   parameters live undocumented on `BaseUploadFileRequest`
   (`base-upload-file-request.ts:5-18`): `viewElementName`, `itemsAttributeName`,
   `maximumAllowedFileSize?`, `allowedFileTypes?`, `fileGroup?`, `tag?`,
   `fileEntitySchemaName`, `recordEntitySchemaName`, `recordColumnName`, `recordId`,
   `files?`, `params?`. The mobile designer registers the **same request type** for mobile
   pages via `MobileUploadFileRequest extends BaseUploadFileRequest`
   (`mobile-page-interface-designer/src/lib/services/requests/mobile-upload-file.request.ts:4-10`).

5. **Authorable vs preprocessor-injected parameters (the core of the parameter map).**
   The Designer authors only `viewElementName` (+ optional `allowedFileTypes`,
   `maximumAllowedFileSize`; mobile additionally `params.selectFileOptions`) — see
   `UploadFileRequestParams` (`interface-designer-properties-panel/.../models/upload-file-request-params.model.ts`)
   and the panels. At schema load time `UploadFileRequestMetaDataWorker`
   (`components-preprocessors/src/lib/file-upload/workers/upload-file-request-worker.ts:98-144`)
   derives and injects the rest from the `crt.FileList` named by `viewElementName`
   (or page defaults when absent): `fileGroup`, `tag`, `files: '@event.files'`, `recordId`,
   `itemsAttributeName`, `fileEntitySchemaName` (default `SysFile`),
   `recordEntitySchemaName`, `recordColumnName` (default `RecordId`).
   `setRequestParam` (line 92-96) only fills **absent** keys — hand-authored values win.

6. **Runtime-required parameters.** `UploadFileHandler.validateRequest`
   (`schema-view/src/lib/handlers/upload-file/upload-file-handler.ts:211-217`) hard-fails
   without `recordId`, `recordColumnName`, `fileEntitySchemaName`; `checkTargetSchema`
   (lines 187-198) rejects upload to an unsaved record unless `fileEntitySchemaName === 'SysFile'`.
   These are the preprocessor-injected ones — an agent authoring a standard page never
   supplies them by hand.

7. **Units.** `maximumAllowedFileSize` is in **megabytes**; when unset, the `MaxFileSize`
   sys setting applies (`util/common/src/lib/services/file-api/base-file-upload-processing.service.ts:158-165`;
   properties panel placeholder reads the same setting).

8. **Mobile-only knob.** `params.selectFileOptions` (`allowCamera` / `allowGallery` /
   `allowFiles`, each defaulting to `true`) is authored only by the mobile panel
   (`mobile-upload-file-request-properties-panel.component.ts:8-32`); the web panel has no
   such controls.

9. **Wiring surfaces.** Besides button/menu-item `clicked`, the `crt.FileList` component's
   `fileDropped` and `uploadClicked` outputs wire the same request (preprocessor worker
   lines 30-39; also confirmed by the published component catalog —
   `clio.tests/.../ComponentRegistry.live-snapshot.json:6405,9523`).

10. **Consumer needs no changes.** clio's `RequestInfoTool` already deserialises exactly the
    fields the new entries will use (`requestType`, `parameters`, `description`,
    `references.docs[]`); mobile flavor already ships (ENG-93783, clio commit `07bc9195`).
    Doc paths are validated against `^(request-docs|mobile-request-docs)/...\.md$`
    (`clio/Command/McpServer/Tools/ComponentRegistryDocsPath.cs`). Local verification is
    possible offline via `CLIO_REQUEST_REGISTRY_LOCAL_FILE` /
    `CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE` pointing at the edited files.

11a. **Mobile runtime evidence (added at the phase-2 gate; source repo
    `W:\Repos\mobile-app`, read-only reference — no changes there).** The Flutter runtime
    accepts the same parameter surface minus `files`
    (`flutter_creatio/lib/modules/file_list/domain/upload_file_request.dart:29-41` — no
    `files` key) and mirrors the web preprocessor with fill-only-absent semantics
    (`.../domain/upload_file_request_preprocessor.dart:77-84`, comment "matches web
    implementation"), injecting `itemsAttributeName`, `fileGroup`, `tag`, `recordId`,
    `recordColumnName`, `fileEntitySchemaName` (FileList `entityName`, default `SysFile`)
    from the `crt.FileList` named by `viewElementName`. Differences from web, all grounded:
    the mobile preprocessor ABORTS with an error when no matching `crt.FileList` is found —
    there is no page-defaults fallback (`upload_file_request_preprocessor.dart:28-35`);
    `recordEntitySchemaName` is optional at runtime (falls back to the primary model's
    schema — `.../app/upload_file_request_handler.dart:58`); `fileEntitySchemaName` and
    `recordColumnName` are runtime-required and an unresolved `recordId` yields "Cannot
    upload files for unsaved records" (`upload_file_request_handler.dart:88-108`);
    `params.selectFileOptions` supports FOUR booleans — `allowCamera`, `allowGallery`,
    `allowFiles`, `allowMultiple` — each defaulting to `true`
    (`upload_file_request_handler.dart:151-179`; the designer panel exposes only the first
    three); `maximumAllowedFileSize` uses the same min-with-`MaxFileSize` rule as web
    (`.../attachment/domain/attachment_validator.dart` `validate`); `allowedFileTypes` is a
    comma-separated EXTENSION list intersected with the `FileExtensionsAllowList` sys
    setting acting as an allow-list (`attachment_validator.dart`
    `resolveAllowedExtensions`) — unlike web's deny-list + mime-pattern support.

11. **No environment probe is needed.** Unlike `crt.PrintablesRequest.templateId`
    (probe `list-printables`) or `crt.RunBusinessProcessRequest.processName`
    (probe `get-process-signature`), every Upload file parameter is resolvable from the
    page schema itself (`viewElementName` names a `crt.FileList` on the same page), so no
    `valueSource` annotation and no new probe tool.

## Concrete implementation unit

- **Start seam:** the `requests` array in `static-files-mcp/latest/RequestRegistry.json` —
  insert one `crt.UploadFileRequest` entry as the **last** array element (after
  `crt.RunBusinessProcessRequest`), preserving the array's alphabetical order. This is the
  first seam where the AI-facing parameter map, required/optional flags, and value formats
  are all published together (AC #1-#2 of the ticket).
- **Allowed adjacent seams:**
  - `static-files-mcp/latest/request-docs/upload-file.request.md` (new recipe doc, web);
  - `static-files-mcp/latest/MobileRequestRegistry.json` — one `crt.UploadFileRequest`
    entry, likewise inserted as the **last** array element (after
    `crt.RunBusinessProcessRequest`) — plus
    `latest/mobile-request-docs/mobile-upload-file.request.md` (new recipe doc, mobile);
  - `creatio-ui/libs/studio-enterprise/util/model/src/lib/requests/upload-file-request.ts`
    and `base-upload-file-request.ts` — JSDoc only, per the merged ENG-93187 convention
    (class summary + per-property docs); no signature/behavior changes.
- **Receiving end boundary:** clio `get-request-info` (web + `schema-type=mobile`) returning
  the new entry with docs — verified locally through the `CLIO_*_LOCAL_FILE` overrides.
  clio source is read-only reference; no clio commits in this unit.

## In scope

- One web registry entry + one mobile registry entry for `crt.UploadFileRequest`
  (parameter map distinguishing designer-authorable vs preprocessor-injected vs
  runtime-only parameters, with required flags, formats, units, defaults).
- Two authoring-recipe markdowns (web `request-docs/`, mobile `mobile-request-docs/`),
  following the structure of the existing sibling docs (summary, canonical wiring,
  parameter table, pitfalls, checklist).
- JSDoc on `UploadFileRequest` (class summary) and `BaseUploadFileRequest`
  (per-property) in creatio-ui, mirroring `printables-request.ts` style.
- In-repo recipe `.md` files in creatio-ui next to the request classes, linked via
  `@see {@link ./<file>.md}` class JSDoc (user-approved at the phase-1 gate; pattern from
  the `feature/ENG-93187-request-md-docs` branch, commit `980e02084bd`): web recipe next to
  `upload-file-request.ts`, mobile recipe next to `mobile-upload-file.request.ts`; content
  kept identical to the published static-files-mcp copies.
- Local consumer verification via clio local-override env vars.

## Out of scope

- Any clio code, test, fixture, or guidance change — including refreshing
  `RequestRegistry.live-snapshot.json` / `MobileRequestRegistry.live-snapshot.json`
  (those are pinned from the live CDN **after** publication, a separate post-merge step).
- Per-version registry files (`8.3.0/`…`10.0.0/`) in static-files-mcp — sibling tickets
  touch `latest/` only.
- Behavior changes to `UploadFileHandler`, `UploadFileRequestMetaDataWorker`, properties
  panels, or any other creatio-ui runtime/designer code.
- Other OOTB action types (each has its own sibling ticket, e.g. ENG-93880).
- Reviving or merging the stale `feature/ENG-93187-request-md-docs` branch (its pattern is
  reused for the NEW upload-file files only; the four existing requests' in-repo docs stay
  untouched on that branch).
- New MCP probe tools or `valueSource` annotations (not needed — finding 11).
- `crt.SelectFileRequest` (internal chained request used by the handler; not a
  button-action request and not cataloged).

## Anti-drift rules

- Do **not** touch any existing entry in either registry JSON; the diff must be one added
  object per file (plus nothing else — no reformatting, no key reordering).
- Do **not** invent JSON fields beyond those the sibling entries use today:
  entry-level `requestType` / `parameters` / `description` / `references.docs[]`
  (these are the fields clio's `RequestInfoTool` maps and surfaces), and inside each
  parameter blob only `type` / `required` / `description` / `values` (blobs are free-form
  `JsonElement` data on the consumer side, but clio's list-mode search inspects only these
  well-known keys, so an unconventional key is silently invisible to the agent).
- Do **not** merge or restate `baseParameters` (`$context`, `scopes`, `type`) inside the
  entry's `parameters` — they are platform-injected (clio consumer rule).
- Do **not** document `params.selectFileOptions` on the **web** entry (mobile-only knob),
  and do not document web-only behavior in the mobile doc.
- Do **not** present preprocessor-injected parameters (`recordId`, `fileEntitySchemaName`,
  `recordEntitySchemaName`, `recordColumnName`, `itemsAttributeName`, `fileGroup`, `tag`,
  `files`) as values the agent should author on standard pages — the recipe must steer to
  `viewElementName` + a `crt.FileList` instead.
- Do **not** change TypeScript signatures, types, or decorators in creatio-ui — JSDoc
  comments only.
- Do **not** edit files under `static-files-mcp/8.3.*` or `10.0.0/`.

## Accepted assumptions

- Scope is **web + mobile** (user-confirmed at intake).
- Publishing the registry JSON in `W:\Repos\static-files-mcp` is **in scope**
  (user-confirmed at intake, checkout path provided).
- Phase artifacts live under `W:\Repos\clio\spec\ootb-requests-upload-file\`
  (user chose "clio/spec"; folder/file naming follows the repo's feature-doc convention).

## Open blockers

1. ~~creatio-ui JSDoc — confirm inclusion.~~ **RESOLVED at the phase-1 gate:** user chose
   JSDoc + in-repo recipe `.md` with `@see` links (see amended In scope).
2. ~~Mobile runtime semantics are designer-side-inferred.~~ **RESOLVED at the phase-2
   gate:** the user provided the mobile runtime repo (`W:\Repos\mobile-app`, read-only
   evidence source); mobile semantics are now grounded in finding 11a. Mobile statements in
   the entry/docs must cite that evidence and go no further.

## Current-state risks

- **Two schema generations risk:** the registry `parameters` blobs are producer-owned
  free-form JSON; a typo in field names (`required` vs `Required`) silently degrades the
  AI contract — mitigated by verifying through clio `get-request-info` locally before merge.
- **Doc-name collision risk is nil but naming matters:** doc paths must match clio's
  validator regex and the sibling naming scheme (`upload-file.request.md`,
  `mobile-upload-file.request.md`); a wrong prefix (e.g. `docs/`) would be rejected by the
  consumer's path validator.
- **Stale-branch confusion:** anyone following ENG-93187's unmerged branch pattern would
  add `.md` files to creatio-ui that nothing publishes; this scope deliberately avoids that.
- **Insert-position stability:** both `requests` arrays are alphabetically ordered today and
  `crt.UploadFileRequest` sorts last; if an intervening producer commit adds another entry
  before this lands, the rule stays "preserve alphabetical order" (which currently means
  append last in both files).
