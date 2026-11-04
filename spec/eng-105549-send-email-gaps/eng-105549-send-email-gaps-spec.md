# ENG-105549 Send email gaps: analysis and fix design

Jira: ENG-105549 "[Process Builder] Send email: fix the gaps found in the ENG-95985 Send email attachments test runs
and review" (Story, parent ENG-92704 "Create BP via AI Toolkit"). Follow-up of ENG-95985 "Send email attachments"
(shipped in CrtProcessBuilder 1.6.6.131, clio-knowledge 1.15.176).

Sources: the Jira description (behaviours 1-8, TC-01..TC-09), the two ENG-95985 test-run comments (manual run on
CrtProcessBuilder 1.6.6.131; mail-delivery run on 1.6.6.136), and the accepted review notes on the package pull
request of ENG-95985. Basis: CrtProcessBuilder `main` at 1.6.6.136 (ENG-95244 "descriptor schema" already merged
there), clio `master` bundling 1.6.6.136, clio-knowledge `master` at library 1.15.177. Baseline package suite:
5272/5272 green on both `dev-nf` (net472) and `dev-n8` (net8.0).

Citation rule (as in `spec/eng-95985-send-email-attachments/`): package and platform sources are cited by type name
only; stand addresses are not written here.

## 1. Short answer

| # | Behaviour | Where | Fix | Stand measurement |
|---|---|---|---|---|
| 1 | Describe hides a source that does not run before the email | `SendEmailAttachmentsDescriber` | Reuse the write-time reachability test; same sentence as the warning | No (logic is the write path's, already measured in ENG-95985 EM10) |
| 2 | Removal notice level in the guide | guide `process-send-email-attachments` | State that each removed row is a Warning, `[]` included | No (observed in the ENG-95985 manual run) |
| 3 | Duplicate files through two routes | `SendEmailAttachmentNotices`, `ElementConfigurationLedger` | Extend the same-files notice to a parameter whose only stored writer is the same element output | No (duplicate delivery already observed: 6 files = 3 x 2) |
| 4 | `performer.contact` = `[#ParameterName#]` refused | `UserTaskPerformerApplier` | Expand names through `ProcessExpressionNames.Resolve`, the formula-body and condition resolver | Yes, after install: TC-05 run |
| 5 | Recipient replacement through `addMapping` | guide `process-send-email` (+ package if refused) | **Owner's decision** (section 3.5) | Already measured (works in place) |
| 6 | `ignoreErrors` with no sending mailbox | guide `process-send-email` | State what it covers, from the platform source and a stand run | Yes: two runs (section 3.6) |
| 7a | Nested read failure + nested email passes | `EmailAttachmentSources`, `FileProcessingFilterLedger` | Fail closed on any file source or slot-bearing element | No (unit seam) |
| 7b | Forward-reference check uses the top level only | `SendEmailApplier.Attachments` | Declared set from the all-depth list | No |
| 7c | `Mapping` value that decodes to nothing | `EmailAttachmentSources.Walk` | Count it as undecodable | No |
| 7d | Static test seam `BaseElementSource` | `ProcessSchemaAllFlowElements` + callers | Pass the element reader per request | No |
| 7e | Replace-rule order not pinned | package tests + contract doc | Test + one contract sentence | No |
| 8 | Recipient type after a designer save | `SendEmailApplier.HasRecipient`, `SendEmailElementHandler.DescribeRecipients` | Measure first; matching already ignores the type | Yes: TC-09 designer save |

## 2. Findings that change the ticket's assumptions

- **ENG-95244 is no longer pending.** It merged into the package `main` as 1.6.6.136, and clio `master` already bundles
  1.6.6.136. No combined rebundle with it is needed. ENG-92710 "Call web service element" has claimed 1.6.6.137 (stamped
  locally, not pushed). This task's cut is therefore the next free number at stamp time: 1.6.6.138 was cut and burned, the shipped cut is 1.6.6.139.
- **Behaviour 7b is unreachable through the build contract today.** The build path's declaration index is built from
  `elements[]` placed at the TOP LEVEL (`ProcessBuildHandler.DeclarationIndex`), and an element descriptor has no field
  that places an element inside an event or expanded sub-process. A nested element is never "declared" by a build
  request, so the forward-reference rule cannot fire for it whichever list `declared` comes from. The change is still
  worth making for consistency with the slot-read check (one line), and it is pinned by a unit test with a hand-built
  declaration index; the acceptance criterion is met vacuously and the PR says so.
- **Behaviour 6 has a source explanation.** CrtProcessDesigner `AutoEmailUserTaskSender.Execute`: when the element's
  sender (`From`) is EMPTY, the email goes through `SendEmailWithDefaultSender`, and on the current email integration
  (features `OldEmailIntegration` and `OldEmailSend` off) that path calls `ActivityEmailSender.Send` with NO
  `IgnoreErrors` handling - only the legacy SMTP branch honours it. With a sender set, the send goes through
  `TrySendEmailWithRetry`, which swallows the error when `IgnoreErrors` is on. An empty recipient list is also
  ignored. So "Mailbox does not exist" with `ignoreErrors = true` is expected when no sender is set. This is a source
  trace (CrtProcessDesigner 7.8.0 source), so it is measured before the guide states it (section 3.6).
- **Behaviour 8: matching is already type-independent.** `HasRecipient` compares only the stored source and value
  (ordinal), never the data type. Describe projects each recipient through `ToDescribeParameter`, which reports the
  type it reads. Whether a designer save changes the SOURCE or the VALUE (not only the type) is what decides whether a
  fix is needed; that is measured (section 3.8).
- **Behaviour 4: the element's other formula fields do not resolve names either.** A recipient `expression` and the
  performer contact are both stored verbatim; only a Formula element body and a flow condition expand `[#Name#]`
  (`ProcessExpressionNames`). The fix routes the performer contact through the same resolver. `UserTaskPerformerApplier`
  is shared by every user task with a performer (Perform task, Send email manual mode, approval-like tasks), so the
  fix reaches all of them. The recipient `expression` is left as it is (out of scope; noted as a possible follow-up).

## 3. Per behaviour

### 3.1 Describe flags a source that does not run before the email (behaviour 1, TC-01..TC-03)

- **Today.** `ElementConfigurationLedger.DescribeAttachments` warns at the end of a request: for each entry with a
  source ELEMENT, when both the source and the email are top-level and the email is not in
  `ProcessGraphBuilder.ReachableFrom(source, OutgoingFlows(schema))`, it adds
  `SendEmailAttachmentNotices.DescribeUnreachable(email, member, source, kind)`. Describe
  (`SendEmailAttachmentsDescriber.DescribeSlots`) has no such issue, so an agent reading the process later gets no
  signal (second test run, TC-11 note).
- **Fix.** One shared helper (for example `SendEmailAttachmentReachability`) owns "does this source lie on a path
  before this email", with the outgoing-flow map and per-source reachable sets cached per call; the ledger and the
  describer both call it. Describe adds the issue to a row when the row's decoded source is an ELEMENT (the same rows the
  write path judges; a process-parameter row is not judged on either side), both elements are top-level, and the email
  is not reachable from the source. The sentence is the warning's own text with the row kind's consequence: a row read
  as a collection (both levels, or the outer level) -> "goes out without these files and without an error"; an
  item-only row reading one file or a formula -> "the element fails, as the file is empty". The member is named by the
  slot (`'Attachments2'`) on the read side instead of `email.attachments[1]`.
- **Edge cases.** A source on a gateway branch that merges before the email reaches it (TC-02): no issue, as
  `ReachableFrom` follows every flow. A nested source or email: no issue (same top-level-only rule as the warning).
- **Tests (package).** TC-01 (Object attachments after the email -> collection wording), TC-02 (branch + merge -> no
  issue), TC-03 (Sub-process File output after the email -> single-file wording), process-parameter row -> no issue,
  nested email -> no issue, and a parity test that the describe issue and the write-time warning for the same schema
  share the sentence.
- **clio.** No contract change (`issues` already exists). MCP e2e: describe of a mis-ordered process carries the issue.
- **Guide.** One clause in `process-send-email-attachments` where it describes the reachability warning: describe
  reports the same fact on the row.

### 3.2 Removal notice level (behaviour 2)

- **Today.** The guide says a removal by a sent `[]` is reported as Info. The manual run (TC-07 step 2) received the
  per-row removal notice (`SendEmailAttachmentNotices.DescribeRemoval`) as a Warning.
- **Fix.** Guide only: every removed row gets its own warning naming what it read and the entry that restores it,
  `[]` included. Before writing it, check in clio whether its own before/after read-back adds an Info line for `[]`
  beside the package's warnings, and state exactly what the agent receives.
- **Tests.** Package: an existing removal test asserts the level stays Warning for `[]` (add if missing). Guide fact-check.

### 3.3 Duplicate files through two routes (behaviour 3, TC-04)

- **Today.** `SendEmailAttachmentNotices.DescribeEntries` groups entries by write key and reports "read the same files,
  so each file is attached twice". A row reading process parameter `Documents` whose stored value is
  `ReadAccountFiles.ObjectFiles`, beside a row reading `ReadAccountFiles.ObjectFiles` directly, has two different keys:
  no notice, and the run attached every file twice.
- **Fix.** For a row that reads a top-level process parameter, compute the element output it takes its rows from: the
  parameter's outer level decodes (as describe decodes it) to exactly one element output, no Formula element targets
  the parameter or its items, and its direction is not `In` (a caller could pass other files). Such a row joins the
  group of the element output's key. The notice names the route:
  `email.attachments[0] reads 'Documents', which takes its files from 'ReadAccountFiles.ObjectFiles', and [1] reads
  'ReadAccountFiles.ObjectFiles' too, so each file is attached twice.` A notice, never a refusal (AC).
- **Not covered (stated in the guide):** a parameter filled by a Script task, or by a sub-process it is passed to; two
  different outputs of the same element (`ObjectFiles` vs the copies) are different files and are not grouped.
- **Tests.** TC-04, plus: formula writes the parameter too -> no notice; `In` parameter -> no notice; parameter reading
  a different output of the same element -> no notice; the existing same-key notice unchanged.
- **Guide.** Replace "the same files reached by two different routes are not detected" with the new rule and its
  limits (net: no growth).

### 3.4 Manual-mode performer from a process parameter (behaviour 4, TC-05)

- **Today.** `UserTaskPerformerApplier.ResolveContactFormula` stores any non-Guid text verbatim; the platform pre-save
  validator then refuses `[#ContractOwnerParameter#]` with "Formula value error: Expression expected (at index 0)".
- **Fix.** Before storing, expand the text with `ProcessExpressionNames.Resolve(schema, "<element label>: performer
  'contact'", text, columns)` - the resolver the Formula body and the flow condition share - bounded before and after
  as `ResolveCondition` bounds a condition. Then: `[#ParameterName#]` becomes `[#[Parameter:{uid}]#]`;
  `[#Element.Output#]` and `[#Element.Output.Column#]` work too; `[#SysVariable.CurrentUserContact#]` and
  `[#Lookup.…#]` pass through unchanged (the resolver leaves those families alone); an unknown name is refused naming
  the parameter (the resolver's own refusal); a bare Guid keeps today's id route. If the text still fails the platform
  validator after expansion, the refusal lists the accepted forms (id, `[#ParameterName#]`, `[#Element.Output#]`,
  `[#SysVariable.CurrentUserContact#]`). Applies on build and modify.
- **Tests.** Parameter name -> UId meta path; unknown name -> refusal naming it; SysVariable unchanged; Lookup macro
  unchanged; element output; bare id unchanged; another user task (Perform task) gets the same expansion.
- **Stand.** After install: TC-05 (build, run, the prepared email activity is assigned to the parameter's contact).
- **clio / guide.** The create contract already calls `contact` a formula; if a sentence is needed it goes into the
  guide (`process-send-email` performer line, and the shared performer guide if one documents `contact`), not the
  modify contract (3 bytes of headroom). Review MCP e2e for a performer-from-parameter case.

### 3.5 Recipient replacement through `addMapping` (behaviour 5, TC-06) - decided: A (owner, 2026-11-04)

- **Measured (ENG-95985 manual run, TC-09).** `addMapping` onto an existing `Recipient1` with a contact id replaced the
  recipient in place: its source moved from Script (parameter) to ConstValue, no second recipient, attachments
  untouched. The guide says a recipient "cannot be replaced or removed through `modify`".
- **Options.**
  - **A. Support and document (recommended).** No package change. `process-send-email` names the route: to replace a
    recipient, `addMapping` onto the existing `Recipient<N>` / `CopyRecipient<N>` / `BlindCopyRecipient<N>` with the new
    value (a contact id for a contact recipient, a formula, or a process parameter); `email.to` still only appends;
    removal stays a designer step. A package test pins the in-place replacement; an MCP e2e case covers it.
  - **B. Refuse.** The mapping service refuses a target named like a recipient slot, naming the `email` block route;
    the guide keeps "no replace". Agents then have no replace path at all (the `email` block cannot replace).
  - **C. A replace route in the `email` block.** New contract (for example a recipient list that replaces, as
    `attachments` does). Largest change, contract headroom is 3 bytes; a separate story.
- **Why A.** It is the only replace path agents have, it already works, and refusing it removes a capability users
  asked for (TC-09 "change the recipient"). The open risk - a value of the wrong kind for the recipient's type - is the
  same as for any mapping and is checked by the platform's pre-save validator.

### 3.6 `ignoreErrors` with no sending mailbox (behaviour 6, TC-07)

- **Hypothesis (source, section 2).** With `ignoreErrors = true`: no sender set -> a send failure (no mailbox) still
  fails the element; a sender set -> the send failure is ignored and the process continues; no recipients -> ignored.
- **Measurement (stand `Creatio`, processes `UsrSe105549*`, read-only otherwise).** Two automatic Send email elements
  with `ignoreErrors = true` and one recipient: (a) no sender; (b) a sender address no mailbox on the stand serves.
  Run each, read `SysProcessLog` / `SysProcessElementLog` and the run's error. (a)'s "no mailbox at all" case cannot be
  reproduced without removing the stand's mail settings, which would break the other session; the manual run's TC-05
  (no mailbox, run failed with "Mailbox does not exist") is that evidence. If (a) sends through the stand's mailbox,
  the record says so.
- **Guide.** One or two sentences in `process-send-email`: what `ignoreErrors` does (a send failure through the
  element's sender, an empty recipient list) and what it does not (no sender set: the default-sender path fails the
  element; errors while the message is built, for example an empty single-File attachment). The exact wording follows
  the measurement.

### 3.7 Package review follow-ups (behaviour 7, TC-08)

- **7a nested read failure.** `EmailAttachmentSources.ReadersBySource` lists `notRead` only for emails it walked, and a
  failed nested read leaves only the top level, so a nested Send email is never walked; `FileProcessingFilterLedger`
  refuses only when `readFailure.Emails.Count > 0`. Fix: when the nested read fails, refuse if the schema has any
  Process file source or any element with an attachment slot - at the top level, or in a container's own
  `FlowElements` read without `GetBaseElements` (the walk `FindRegionOf` already uses); if even that fallback throws,
  refuse (fail closed). A stand where nested elements read normally refuses nothing new. The describe issue for a
  failed read is aligned with the new rule. Tests: TC-08 (nested email + nested unscoped source + throwing seam ->
  refused with the read-failure reason), top-level source only -> refused, no source and no slot -> saved, seam not
  throwing -> unchanged.
- **7b forward reference.** `SendEmailApplier.Attachments.EnsureFormulaReadsNothingDeclaredLater` takes `declared`
  from `ProcessSchemaAllFlowElements` (unreachable through the build contract today, section 2). Unit test with a
  hand-built declaration index.
- **7c undecodable `Mapping`.** In the walk, a value with source `Mapping` from which no element, element parameter or
  process parameter can be read sets `Undecodable` (fail closed; the sweep then counts the email as reading every
  source). A designer-stored slot is always Script, so no stored process changes verdict. Tests: a bare map path
  `guid.guid` on a slot -> undecodable; a Mapping value with a segment -> decoded as before.
- **7d static test seam.** Replace `ProcessSchemaAllFlowElements.BaseElementSource` with an element reader passed per
  request: an `IProcessSchemaElementReader` (production: `GetBaseElements`) registered per request and handed to the
  instance callers (ledgers, describer, applier), and passed as an argument to the static walks (`EmailAttachmentSources`,
  `EmailAttachmentSlot`). Tests inject a throwing reader instead of setting a static, and the fixtures pinned
  non-parallel because of the seam can run in parallel again. Scope: this seam only; the two older seams of the same
  shape in `ProcessElementDependencyScanner` and `ProcessParameterService` stay (noted as a follow-up).
- **7e replace order.** A package test pins: stored `[A, B]` + request `[B, A]` -> nothing written, describe `[A, B]`;
  request `[X, A, B]` -> `[A, B, X]`. One sentence in the clio contract doc (`create-business-process` attachments
  text; the modify contract only if 3 bytes allow, otherwise the guide, which already says it).
- **Split.** Behaviour 7 can be tracked as its own Sub-task; see the plan.

### 3.8 Recipient type after a designer save (behaviour 8, TC-09)

- **Measurement first.** Build `UsrSe105549RecipientType` with: a contact-id recipient (expression + `referenceSchema`
  Contact), a process-parameter recipient and a constant address. Read the raw element (source, value, data type,
  reference) before and after a designer save (Process Designer in Chrome; the owner logs in). Then describe, and send
  the same three recipients again through `setElement`.
- **Decision rule.** If only the data type changes, matching is already type-independent and describe needs at most a
  documented note (the type it reports is the stored type). If the source or the value changes, make the match compare
  the decoded recipient (the contact or parameter it names) rather than the stored text, and make describe report the
  same entry before and after.
- **Tests.** Package: a designer-shaped recipient (MaxSizeText typed, Lookup value) is matched by the same entry;
  describe reports the same recipient for both shapes.

## 4. Acceptance criteria mapping

| AC | Covered by |
|---|---|
| Describe issue on a source that does not run before the email, same wording | 3.1 |
| No issue through a merge | 3.1 TC-02 |
| Guide states the removal notice level | 3.2 |
| Duplicate notice, not a refusal | 3.3 |
| `[#ParameterName#]` saves and resolves, or refused listing forms | 3.4 |
| Recipient replacement decided; guide and behaviour agree | 3.5 (owner) |
| Guide does not claim `ignoreErrors` keeps a failing process alive | 3.6 |
| Refusal on a failed nested read, nested email included | 3.7a |
| Nested forward reference refused like top level | 3.7b (vacuous through the build contract; unit-pinned) |
| `Mapping` that decodes to nothing is undecodable | 3.7c |
| Recipient matching and describe stable across a designer save | 3.8 |
| ENG-95985 tests stay green | full package suite on both configurations |
