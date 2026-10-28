# ENG-95985 Send email attachments: stand measurements (EM0-EM15)

Written 2026-10-28.

Basis:
- crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125): PB, PBT.
- clio `origin/master` `358e9db52`: CLIO.
- clio-knowledge `origin/master` `534e913`: KB.
- CrtProcessDesigner 7.8.0 (PackageStore branch), the PackageStore 7.8.0 corpus, core `TSBpm` source.
- Stand `Creatio` (a registered clio environment): read-only inventory of 2026-10-28 (EM0, part done).
- Research inputs r0-r10 of 2026-10-28 (local, not part of the plan); the critic's S0-S14 map onto EM0-EM15 (gate
  table, column "Research ids").

Paths legend (every evidence path is repository-relative after its prefix):

| Prefix | Root |
|---|---|
| `PB/` | `packages/CrtProcessBuilder/Files/src/cs/` (crt-process-builder repository) |
| `PBT/` | `tests/UnitTests/CrtProcessBuilder.Tests/` (crt-process-builder repository) |
| `PD/` | `CrtProcessDesigner/branches/7.8.0/` (PackageStore repository) |
| `PS/` | the PackageStore repository root |
| `TS/` | `TSBpm/Src/Lib/` (core repository) |
| `CLIO/` | the clio repository root |
| `KB/` | the clio-knowledge repository root |
| `R/` | the local research notes (not in the repository; see README; cited only for what was measured there) |

Tags: [source] read in code or data; [corpus] shipped process metadata; [doc] documentation; [measured <date>] observed
on stand `Creatio` on that date; [traced, not measured] a run-time or designer expectation derived from source;
[inferred] my inference.

Sibling files: [README](README.md) · [decisions](eng-95985-send-email-attachments-decisions.md) (Q1-Q20) ·
[facts](eng-95985-send-email-attachments-facts.md) · [serialization](eng-95985-send-email-attachments-serialization.md)
(the comparison rule ACR R1-R11, what EM1 records) · [design](eng-95985-send-email-attachments-design.md) ·
[plan](eng-95985-send-email-attachments-plan.md) (tasks P-A1..P-C6, KB-1..KB-6, CL-1..CL-8) ·
[test plan](eng-95985-send-email-attachments-test-plan.md) · [estimate](eng-95985-send-email-attachments-estimate.md).

## 0. Summary

- Sixteen measurement ids; two are dropped and keep their ids: EM6 (Q17 agreed 2026-10-28) and EM12 (Q9 agreed
  2026-10-28). Four block code: EM0 (read-only, part done) before EM1, and EM1 (the designer capture, four
  saves) before P-B2/P-B3 (slot numbering, stamps, sources; slice A and C1 may proceed on the agreed answers, and
  EM1's rows feed the P-C5 parity pattern B); EM2's save, pre-save-gate and stored-bytes steps and EM3 right after the first experimental cut,
  as the plan's STOP gate (EM2's describe steps need P-C3 and run in its re-run on the final cut). The run-time set
  (EM4 with EM4c, EM5, EM8, EM10, EM11, EM13, EM15, and EM7 on stand `Creatio` once the owner confirms that mail
  sending works there, Q17) must pass before the PRs leave draft. EM9 and EM14 are optional.
- EM8, EM10 and EM11b need no package code: they can run on designer-built probes before code, which settles Q3, the
  AC6 wording (Q7) and the Q16 follow-up earlier (EM10's reachability-warning check needs the cut). The plan
  schedules them after the claimed cut; either order works.
- Every expected result below is a source trace unless tagged measured. Two are least certain and worth reading
  first: EM9 (no supported route produces the shape at all), EM11b (the platform trace says the pre-save gate refuses a
  dangling source, a package comment reports an analogous case that saved green).
- Stand `Creatio` had no mailbox on 2026-10-28 (0 `MailboxSyncSettings` rows, measured). Manual mode is observable
  through `ActivityFile` rows. Auto mode is proven by EM7 on stand `Creatio` after the owner configures real mail
  sending there and confirms it works (Q17 agreed 2026-10-28); until then it rests on the unit tests and the shared
  run-time path.
- Section 1 holds the stand-safety rules, section 2 what is already measured, section 3 the read and cleanup toolkit,
  section 4 the gate table, section 5 the measurements, section 6 a session order, section 7 the leftovers register,
  section 8 the corrections to the research.

## 1. Stand-safety rules

1. **Every write needs the owner's explicit go-ahead, each time.** A designer save, a builder create or modify, a
   process run, a seeded record, a feature-state change, a cleanup delete and a package install are all writes. An
   approval covers the one step it was given for; it does not carry to the next step or the next session.
2. **Schema writes strictly one at a time.** Never two builder calls, a builder call and a designer save, or two
   deletes in parallel: a parallel burst takes the .NET Framework app pool down
   (`CLIO/clio.mcp.e2e/ProcessFileElementToolE2ETests.cs:41-42`; `CLIO/spec/eng-92719-file-processing-element/README.md:462-468`) [source].
   Runs of different probes also go one after another, so a run's `ActivityFile` rows are not mixed with another's.
3. **Installing a package restarts the site for every connected user.** `install-process-builder` builds the
   configuration and restarts the site (`CLIO/clio/Common/BundledPackages.cs:82-84, 102`) [source]. Each install needs
   the owner's yes, also for a re-install of the same cut. Installs happen only for a cut claimed in the ENG-95985
   claim comment (Q13 agreed; plan section 6). Rebuild clio before installing: the install reads the archive from clio's build output.
4. **Browser checks run in the owner's Chrome through Claude in Chrome, and the owner logs in.** The agent never types
   a password and never presses a login button. In Chrome the stand opens only through the local address mapping the owner
   set up (the stand's own host name is refused there); ask the owner for it, and do not write it into any file.
5. **The Process Designer in a hidden tab ignores real clicks.** Dispatch mouse events from JavaScript on the
   `g.djs-element .djs-hit` node of the element, and read the right panel's inputs from the DOM. Keep each JavaScript
   call under 45 s. A screenshot that times out does not mean the designer failed to load; read the DOM instead.
6. **The designer address on this .NET Framework stand** is `/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<schemaUId>`
   relative to the stand root; a guessed shell route wedges the session
   (`CLIO/docs/knowledge/platform/the-process-designer-has-one-url-and-guessing-wedges-the-shell.md`) [measured earlier, recorded there].
7. **Cleanup never uses HTTP DELETE.** The stand answers HTTP DELETE with 405; `odata-delete` never works there
   (`CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-open-questions.md:605-612`)
   [measured 2026-09-27]. Rows go through `execute-dataservice-batch` (DeleteQuery over POST). The `run-process`
   option `cleanup: true` deletes through OData DELETE (`CLIO/clio/Command/RuntimeVerification/RecordDeletionService.cs:14-24, 144-152`)
   [source], so it is not used on this stand; `watch` is still used to discover what a run created.
8. **Process deletes use the CLI with a long timeout:** `clio delete-schema <name> --remote -e Creatio --timeout 600000`.
   A remote delete took about 6 minutes on 2026-09-27 and 7-50 s on 2026-10-02; through MCP it dies at the 100 s default
   (`CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-open-questions.md:605-606`;
   `CLIO/clio.mcp.e2e/Support/Mcp/ProcessDesignerE2ESupport.cs:31-52`) [measured, source].
9. **Before any builder probe, check that the session's clio is the one the probe needs.** `get-tool-contract` must
   show the expected arguments: an older client drops unknown arguments silently; a clio built from 2026-10-13 on
   refuses `email.attachments` as an undeclared key (`CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:185-193`,
   added that day) [source], and an older clio forwards it.
   An older package silently drops an unknown member and reports success (`PB/Contracts/VersionContracts.cs:23-26`)
   [source], so a builder probe is judged by the stored bytes, never by the success line alone.
10. **Read stored state only.** `SysSchema.MetaData` and `SysLocalizableValue` through `execute-sql-script` (or
    `execute-esq` if SQL reads are denied, `CLIO/docs/knowledge/platform/execute-sql-script-reads-can-be-denied-while-writes-still-work.md`).
    Never take describe or an open design session as the capture.
11. **Open every element's card before a designer save.** The designer re-serializes only elements whose card was
    materialized (`CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md:786-787`) [measured earlier, recorded there].
12. **Never run the EM1 capture.** It is the AC7 oracle and the e2e fixture `UsrSeAttachmentsDesignerFixture` (test
    plan TC-I-09); run history would make later saves create versions, and it would own data. Run-time probes use their
    own processes.
13. **EM7 sends real email** from the mailbox the owner configures on stand `Creatio` (Q17 agreed). The recipient is a
    mailbox the owner names as their own test mailbox; each send needs the owner's yes. No other measurement sends mail
    on purpose.
14. **Any role-level feature change runs on a disposable stand only**, never on shared stand `Creatio`; none is
    planned since EM12 was dropped (Q9 agreed). A per-user change does not work (EM12 keeps the reason).
15. **No stand address, credential, token or connection string goes into any file**, including these notes and the
    leftovers register. Record ids and names only.

## 2. Facts already measured

| # | Fact | Date | Evidence |
|---|---|---|---|
| M-01 | CrtProcessBuilder 1.6.6.125 (ModifiedOn 2026-10-27), CrtProcessDesigner 7.8.0, CrtBase 7.8.0 | measured 2026-10-28 | `R/r0-lead-facts.md:55` (read-only SQL) |
| M-02 | `UseProcessEmailAttachments`: `AdminUnitFeatureState` = 1 for "All employees" and "All external users" (the package's own seed, `PD/Data/AdminUnitFeatureState_UseProcessEmailAttachments/data.json:7-21, 29-43`); `Feature.DefaultState` = 1, no row with state 0 | measured 2026-10-28 | `R/r0-lead-facts.md:56`; DefaultState: decisions Q9 (history of the toggle) |
| M-03 | `LoadAttachmentsInSameProcess`: state 1 for "All employees". Unrelated to Send email: read only by inbound Exchange sync | measured 2026-10-28 | `R/r0-lead-facts.md:57-58`; `R/r10-critic.md:47` |
| M-04 | `MailboxSyncSettings`: 0 rows. Auto mode with a sender could not be built or observed here on that date; the owner configures real mail sending for EM7 (Q17 agreed) | measured 2026-10-28 | `R/r0-lead-facts.md:59` |
| M-05 | Processes whose metadata holds `Recipient1`: 11; holding an `Attachments<N>` slot or a FileLocator slot: 0. No attachment specimen on the stand | measured 2026-10-28 | `R/r0-lead-facts.md:60` |
| M-06 | The three ProcessTests corpus processes with attachments (`ProcessFileSendMail`, `ProcessFileReport`, `MailConnectionsProcess`) are not installed on the stand | measured 2026-10-28 | `R/r8-testing.md:34` |
| M-07 | Core 10.1.37, .NET Framework (not re-read on 2026-10-28; EM0 re-reads it) | measured 2026-10-01 | `CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md:8` |
| M-08 | Five MS Word printables, Invoice among them (`e1f1a474-1a77-82f8-4bc7-b1da23699e13`); CrtInvoice installed | measured 2026-10-01 | same file `:815-816` |
| M-09 | The palette offers only the Object Process file; the "What is the source of the file?" selector is shown while `EnableReportFileProcessingUserTask` is on; the run time never reads that feature | measured 2026-10-01 | `CLIO/docs/knowledge/platform/the-process-file-palette-offers-only-the-object-variant.md` |
| M-10 | On the Process file element, a nested-only binding to a set single File copies exactly one file | measured 2026-10-19 | `CLIO/docs/knowledge/platform/a-nested-only-files-binding-reads-one-row.md`; `PB/FileProcessing/ProcessFileProcessingBinder.cs:27-29` |
| M-11 | On the Process file element, the same binding with the File unset fails the element with a NullReferenceException in `FileFactory.CreateFileInstance` | measured 2026-10-19 | `CLIO/docs/knowledge/platform/an-unset-single-file-fails-a-process-parameter-file-element.md` |
| M-12 | On the Process file element, the item alone bound to an ITEM of a collection reads null and fails with the same NullReferenceException | measured 2026-10-26 | `PB/FileProcessing/ProcessFileProcessingDescriber.cs:181-186` |
| M-13 | "Use in process" report files are `SysProcessFile` rows; present while the instance is parked, gone when it completes (top-level process) | measured 2026-10-19 | `CLIO/docs/knowledge/platform/use-in-process-report-files-are-deleted-with-their-process.md` |
| M-14 | A run that fails inside an element writes the exception to the root `SysProcessLog.ErrorDescription` before RunProcess returns; RunProcess itself says only "check the process log" | measured (record dated 2026-10-02) | `CLIO/docs/knowledge/platform/runprocess-hides-an-element-exception-behind-a-generic-message.md` |
| M-15 | No SQL route reads a run's parameter values; `SysProcessElementLog` is the persistent record of which elements ran | measured 2026-09-22 | `CLIO/docs/knowledge/platform/a-running-process-parameter-value-is-not-readable-from-sql.md` |
| M-16 | HTTP DELETE answered 405; remote `delete-schema` 6 min (2026-09-27) and 7-50 s (2026-10-02) | measured 2026-09-27, 2026-10-02 | `CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-open-questions.md:605-612` |
| M-17 | A designer-built Process file task changes once on its next builder save that does not rewrite it; later saves are byte-identical | measured 2026-10-15 | `CLIO/docs/knowledge/platform/the-first-builder-save-normalizes-a-designer-built-file-task.md` |

## 3. Toolkit: seed data, names, reads, cleanup

### 3.1 Names

- Package `Custom`. EM1 process `UsrSeAttachmentsDesignerFixture` (caption "SE attachments designer fixture"), the
  name the test plan's e2e reads. Every other probe `UsrSeEm<n><Short>`, caption "SE EM<n> <short>".
- Element captions equal the names used across the plan: `OF1` (Object attachments), `RF1` (Generated report), `PF1`
  (Process parameter variant), `SE1`, `SE2` (Send email). Display values embed captions
  (`PS/ProcessTests/branches/7.8.0/Resources/ProcessFileSendMail.Process/resource.en-US.xml:12-15`) [corpus], so a
  builder twin must use the same captions.
- `OF1` always reads the attachments of ONE record: EM1's filter Contact = `PContact`, and every run passes
  `PContact` = D1. The builder refuses a Process file source with no record scope and no selecting filter, Object
  attachments and Generated report alike, as an attachment source, judged at the end of the request (Q20 agreed
  2026-10-28; design 4.9: the email reads the files with rights disabled and sends them out), so designer-built probes
  keep the same conditions (OF1 on `PContact`, RF1 on `PInvoice`) and their builder twins stay buildable. No EM uses
  an unscoped source; that refusal is proven by the refusal tests of design §5.1 (test plan TC-U-78, 78a-c), not on
  the stand.
- Process parameters: `PContact`, `PTarget`, `PInvoice` (Lookup: Contact, Contact, Invoice), `Docs` (file collection,
  item `File`), `Doc` (File).
- Subject token per run: `SEA-EM<n>-<8 hex>` on the Send email element's own Subject. The activity title is the
  message subject (`PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:297, 304`;
  `PD/Schemas/ManualEmailUserTaskSender/ManualEmailUserTaskSender.cs:78-79`), and in template mode the element's
  non-empty Subject wins over the template's
  (`PD/Schemas/EmailTemplateUserTaskMessageProvider/EmailTemplateUserTaskMessageProvider.cs:157-158`) [source].

### 3.2 Seed data

Prefer data that already exists (read-only search in EM0). Every seed that must be created is a write.

| # | Seed | Needed by | How |
|---|---|---|---|
| D1 | a Contact with exactly 2 (or few) `ContactFile` rows of type File | EM4, EM4c, EM5, EM7, EM8, EM10, EM11, EM13 | RB-10a finds one. If none, the owner uploads two small files on a test contact in the UI (write). clio cannot seed a file: `execute-dataservice-batch` takes data-value-types 0-12 only, no Blob (test plan TC-I-10, `CLIO/clio/Command/DataServiceBatchCommand.cs:153`) |
| D2 | an Invoice record and the Invoice MS Word printable | EM1, EM4, EM7, EM15 | RB-10b and `list-printables` (read-only) |
| D3 | an email template of type "Email template" (`74ff0cee-6593-482f-a62f-6dde6e17ab5e`, `CLIO/clio.mcp.e2e/Support/Mcp/EmailTemplateE2EArrange.cs:22-27`) | EM1 (SE2), EM5, EM7 | RB-10c finds one. If none, one `execute-dataservice-batch` insert with an explicit id, as the e2e helper does (`EmailTemplateE2EArrange.cs:39-74`) (write) |
| D4 | a second Contact `PTarget` that receives PF1's copies | EM4, EM5, EM13 | an existing test contact (read-only search); never `PContact`, or every run adds files to the source and the counts drift |
| D5 | dropped with EM6 (Q17 agreed 2026-10-28): no test-only `MailboxSyncSettings` record is created | - | - |
| D6 | stand `Creatio` with real mail sending configured by the owner (a `MailboxSyncSettings` mailbox), and the owner's own test recipient | EM7 only | the owner configures it and says that mail sending works; the lead then reads `SELECT COUNT(*) FROM MailboxSyncSettings` (read-only) and records the count only, never a mailbox address |
| D7 | a record of an object without its own `<X>File` (for example Account address) with one `SysFile` row | EM14 only | RB-10d finds one. If none, one builder-made Process parameter element run that saves a D1 file to that record (a write, the SysFile save path) |

### 3.3 Reads (all read-only)

Run through `clio execute-sql-script "<sql>" -e Creatio -v json -d <scratch>\<file>.json` (cliogate 2.0.0.41 or newer,
`CLIO/clio/help/en/execute-sql-script.txt:12`). Keep every raw read in the local scratch folder; nothing is committed.

| Id | Reads | SQL |
|---|---|---|
| RB-1 | stored metadata | `SELECT s.Id, s.UId, s.ModifiedOn, CAST(s.MetaData AS VARCHAR(MAX)) AS M FROM SysSchema s WHERE s.Name = '<process>' AND s.ManagerName = 'ProcessSchemaManager'` |
| RB-2 | resources of the slots | `SELECT lv.[Key], c.Name AS Culture, lv.Value FROM SysLocalizableValue lv JOIN SysCulture c ON c.Id = lv.SysCultureId JOIN SysSchema s ON s.Id = lv.SysSchemaId WHERE s.Name = '<process>' AND lv.[Key] LIKE '%.Attachments%'` |
| RB-3 | the email activity | `SELECT Id, Title, StatusId, CreatedOn FROM Activity WHERE Title LIKE '%<token>%'` (or the `created` list of `run-process` `watch`) |
| RB-4 | its attachments | `SELECT Id, Name, Size, TypeId, CreatedOn, DATALENGTH(Data) AS Bytes FROM ActivityFile WHERE ActivityId = '<activity>' ORDER BY CreatedOn` |
| RB-5 | the run | `SELECT TOP 5 l.Id, l.StartDate, l.CompleteDate, l.StatusId, l.ErrorDescription FROM SysProcessLog l JOIN SysSchema s ON s.Id = l.SysSchemaId WHERE s.Name = '<process>' ORDER BY l.StartDate DESC` |
| RB-6 | which elements ran | `SELECT Caption, StatusId, StartDate, CompleteDate FROM SysProcessElementLog WHERE SysProcessId = '<run>' ORDER BY StartDate` |
| RB-7 | report temp files of the run | `SELECT COUNT(*) FROM SysProcessFile WHERE SysProcessId = '<run>'` (`TS/Terrasoft.Core.Process/ProcessFileStorage.cs:23, 124-130`) |
| RB-8 | PF1's copies | `SELECT Id, Name, CreatedOn FROM ContactFile WHERE ContactId = '<PTarget>' AND CreatedOn >= '<run start, UTC>'` |
| RB-9 | feature states | `SELECT f.Code, f.DefaultState, a.Name AS AdminUnit, s.FeatureState FROM Feature f LEFT JOIN AdminUnitFeatureState s ON s.FeatureId = f.Id LEFT JOIN SysAdminUnit a ON a.Id = s.SysAdminUnitId WHERE f.Code IN (...)` |
| RB-9b | what the per-user feature read answers for one user: the highest state across the user's roles, as `DbFeatureProvider` reads it (`TS/Terrasoft.Core/FeatureToggling/DbFeatureProvider.cs:355-367`) | `SELECT TOP 1 s.FeatureState FROM AdminUnitFeatureState s JOIN Feature f ON f.Id = s.FeatureId JOIN SysAdminUnitInRole r ON r.SysAdminUnitRoleId = s.SysAdminUnitId WHERE f.Code = 'UseProcessEmailAttachments' AND r.SysAdminUnitId = '<user's SysAdminUnit id>' ORDER BY s.FeatureState DESC` |
| RB-10a | D1 | `SELECT TOP 10 ContactId, COUNT(*) AS Files FROM ContactFile WHERE TypeId = '529bc2f8-0ee0-df11-971b-001d60e938c6' GROUP BY ContactId HAVING COUNT(*) BETWEEN 2 AND 5` (File type id: `PS/CrtBase/branches/7.8.0/Data/FileType/data.json:7-11`) |
| RB-10b | D2 | `SELECT TOP 3 Id, Number FROM Invoice ORDER BY CreatedOn DESC`, plus `list-printables` |
| RB-10c | D3 | `SELECT TOP 5 Id, Name, Subject FROM EmailTemplate WHERE TemplateTypeId = '74ff0cee-6593-482f-a62f-6dde6e17ab5e'` |
| RB-10d | D7 | `SELECT TOP 10 RecordSchemaName, RecordId, COUNT(*) FROM SysFile GROUP BY RecordSchemaName, RecordId` |

Status ids: `SysProcessLog` Running `ed2ae277-b6e2-df11-971b-001d60e938c6`, Done `815c9586-b6e2-df11-971b-001d60e938c6`,
Error `f942c08d-b6e2-df11-971b-001d60e938c6` (`PS/CrtBase/branches/7.8.0/Data/SysProcessStatus/data.json:7-11, 19-23, 31-35`;
enum order `TS/Terrasoft.Core/Process/ProcessEnum.cs:174-194`). Activity Finished `4bdbb88f-58e6-df11-971b-001d60e938c6`,
Canceled `201cfba8-58e6-df11-971b-001d60e938c6` (`PS/CrtBase/branches/7.8.0/Data/ActivityStatus/data.json:39-47, 55-63`) [source].

### 3.4 Cleanup operations (each a write)

| Id | Operation | How |
|---|---|---|
| CU-1 | end a run parked on a manual Send email | `execute-dataservice-batch` update of the activity: `StatusId` = Canceled (lookup, data-value-type 10). The process listens on the activity's status (`PD/Schemas/ManualEmailUserTaskSender/ManualEmailUserTaskSender.cs:152-157`), so the instance resumes and ends [inferred: the same entity layer an OData PATCH goes through]. Confirm with RB-5 |
| CU-2 | delete rows | `execute-dataservice-batch` delete items: `ActivityFile` rows first, then the `Activity`, then PF1's `ContactFile` copies (RB-8), then seeded templates or mailbox records. Re-run the read afterwards. A batch reports per-item outcomes; a batch-level error can omit them (`CLIO/docs/knowledge/platform/dataservice-batch-stop-errors-can-omit-item-outcomes.md`), so always read back |
| CU-3 | delete a probe process | `clio delete-schema <name> --remote -e Creatio --timeout 600000`, one at a time, newest version first. If a process with run history is refused, record it in the register and leave it for the owner |
| CU-4 | restore a feature state (no planned use since EM12 was dropped) | `clio set-feature <code> <state> --sys-admin-unit-name "<role>" -e Creatio` (`CLIO/clio/Command/FeatureCommand.cs:26, 101-116`; it clears the feature cache) |

## 4. Gate table

"Plan task" names the plan's step that waits for the measurement; "DoD" the plan's definition-of-done row it feeds.

| EM | When | Blocks (plan task / DoD) | Settles or confirms | Research ids |
|---|---|---|---|---|
| EM0 | before EM1 (read-only) | every EM; plan 0.5 | versions recorded for pattern-B rows; seed availability; the feature states the runs stand on | r10 S0 |
| EM1 | before P-B2/P-B3 (slice A and C1 may proceed on the agreed answers) | plan 0.6; slice B (P-B2 numbering and stamps, P-B3 sources); P-C3 issue texts; P-C5 pattern B; DoD AC7 | Q5, Q3 shape, ACR R6, serialization SQ1-SQ8 | r10 S1; r3 P1, P3, P4; r4 P1 |
| EM2 | after the first experimental cut (STOP gate: save, pre-save gate and stored-bytes steps); re-run on the final cut with the describe steps, after P-C3 | slice C start; P-C5; P-C6; DoD AC1, AC7 | pre-save gate accepts the slot (SQ11); twin equals EM1 under ACR; Q4 replace parity; a reused slot number keeps no stale outer DisplayValue (design 4.3); describe round trip on the re-run | r10 S2; r4 P2, P8 |
| EM3 | after the first experimental cut (STOP gate) | slice C start; DoD AC1, AC7 | designer renders and keeps builder slots (SQ9, SQ12) | r10 S3; r3 P5; r4 P9 |
| EM4 | before PR ready (designer baseline EM4a optional before code) | CL-7 evidence; DoD AC2, AC3, AC4 | manual + custom delivers the sum of rows; all three variants at run time; an unset FileCollection gives no rows | r10 S4; r4 P1, P3 (TypeId); r8 ST-E1 |
| EM4c | before PR ready on the claimed cut, after EM4b | DoD AC2 | a process FileCollection filled by a parent process attaches every file at run time | none (added after the review) |
| EM5 | before PR ready | DoD AC4 | manual + template delivers the same; a mode switch keeps the slots | r10 S4; r8 ST-E2 |
| EM6 | dropped (Q17 agreed 2026-10-28: the owner configures real mail on stand `Creatio`) | - | - (its Create activity case runs as EM7c) | r10 S5; r4 P3 (now EM7c) |
| EM7 | before PR ready, on stand `Creatio`, once the owner confirms that mail sending works there (Q17 agreed) | DoD AC4 | real delivery, auto + custom and auto + template; with Create activity on, the activity carries the files (EM7c) | r10 S6, S5; r3 P6; r4 P3; r8 ST-E3 |
| EM8 | before PR ready (may run before code, designer-built) | KB-1 wording; P-C4 notice text (TC-U-43); Q16 per-recipient pattern | Q3 A or B | r10 S8, G6; r3 P3, P4 |
| EM9 | optional | P-C3 issue wording (TC-U-64 row 1) only | SQ15 loud or silent | r10 S7; r4 P4; r2 P-1 |
| EM10 | before PR ready (the runs may go before code, designer-built; the warning check needs the cut) | KB-1 (TC-K-03); Q19 AC6 text; P-C2 warning text | AC6 flow-order fact; the text of Q6 C's reachability warning (design §5.2) | r10 S9; r4 P7; r3 P7 |
| EM11 | EM11a before PR ready; EM11b may run before code | DoD AC2; Q16 follow-up | one collection, two emails; `removeElement` of a source (SQ14) | r10 S10; r3 P8 |
| EM12 | dropped (Q9 agreed 2026-10-28: no build-time check of `UseProcessEmailAttachments`) | - | - (the KB troubleshooting line rests on the run-time trace, EM12 section) | r10 S11; r4 P10; r8 ST-E5 |
| EM13 | before PR ready (EM13a may run before code) | TC-U-71 / TC-I-08 confidence; a clone re-stamp task only if it fails | SQ13: the clone's slots stay owned and still attach | r10 S12, G3 |
| EM14 | optional | TC-U-20 confidence | SysFile-root Object source attaches | r10 S13; r3 P6 |
| EM15 | before PR ready (rides on EM4) | KB-1 (TC-K-06) | a report "use in process" file stays readable on the activity after the process ends | r10 S14; r4 P5; r8 ST-E4 |

## 5. Measurements

### EM0 Read-only baseline (part done 2026-10-28)

| | |
|---|---|
| Question | Which versions, switches and seed data do the later measurements stand on; do the read paths work? |
| Gates | every EM; AC7 records the platform version the oracle is taken on; the feature states the runs stand on |
| When | before code |
| Writes | none |

**Done on 2026-10-28:** M-01 to M-06 (section 2).

**Remaining steps:**
1. `clio list-packages -e Creatio`: CrtProcessBuilder, CrtProcessDesigner, CrtBase, CrtEmailSender, cliogate. Note the
   cliogate version against 2.0.0.41 (SQL) and 2.0.0.46 (`export-schema`).
2. `clio get-info -e Creatio`: core version and runtime (.NET Framework expected, M-07).
3. RB-9 for `UseProcessEmailAttachments`, `EnableReportFileProcessingUserTask`, `UseProcessPerformerAssignment`,
   `OldEmailIntegration`, `OldEmailSend`, `EmailTemplateUserTaskMultiLanguageV2`. They decide: the designer control
   (`PD/Schemas/EmailTemplateUserTaskPropertiesPage/EmailTemplateUserTaskPropertiesPage.js:365-367`), the report and
   process-parameter selector (M-09), which manual activity path runs (both copy the files,
   `ManualEmailUserTaskSender.cs:62-69`), the default-sender branch of auto mode with an empty sender, which EM7 avoids
   by naming the configured mailbox (`PD/Schemas/AutoEmailUserTaskSender/AutoEmailUserTaskSender.cs:71-93, 105-108`),
   and the template lookup of EM5
   (`EmailTemplateUserTaskMessageProvider.cs:139-145`) [source].
4. Record that `Feature-UseFileStorageInProcessUserTasks` (application config, default `true`,
   `TS/Terrasoft.Core/GlobalAppSettings.cs:2031, 2706-2707`) is not readable from the database: the measurements assume
   the default file-storage read path (`EmailTemplateUserTask.cs:87-90`) [inferred].
5. Seeds: RB-10a (D1), RB-10b and `list-printables` (D2), RB-10c (D3), a second contact for D4, RB-10d (D7).
6. The profile culture of the user who will save EM1 (the owner's or the lead's login): resources land in the
   authoring culture only (serialization §2.3).
7. Baselines for later diffs: `SELECT COUNT(*) FROM SysProcessFile`, `SELECT COUNT(*) FROM ActivityFile`.
8. `get-tool-contract` for `create-business-process`, `modify-business-process`, `execute-dataservice-batch`,
   `run-process`: the session reaches them, and note the client version.

**Expected:** versions as M-01 and M-07; `UseProcessEmailAttachments` on (M-02); `EnableReportFileProcessingUserTask` on
(M-09); seeds D1-D3 exist without writes [inferred from M-08 and the stand's age].

**If not:**

| Outcome | What changes |
|---|---|
| core is not 10.1.37 | record it; pattern-B rows name the version EM1 ran on |
| `EnableReportFileProcessingUserTask` off | EM1 cannot reach RF1/PF1 through the selector (M-09); ask the owner to switch it on (a write) or capture RF1/PF1 elsewhere |
| SQL reads denied | use `execute-esq` for every RB read |
| no D1 contact | owner's upload (a write) before EM4 |
| `UseProcessEmailAttachments` not on for "All employees" (RB-9), or RB-9b answers 0 for the user the clio environment `Creatio` logs in as | runs started through clio send no files (the run time honours the switch; there is no build-time check, Q9 agreed): tell the owner before EM4 (a role-row change is a write for every user of that role) |

**Cleanup:** none.

### EM1 Designer capture: the AC7 oracle

| | |
|---|---|
| Question | What does the current designer store for 2+ slots, a gap, the Process-parameter variant, a process file collection, a process File, auto + template, and a stale parent? |
| Gates | AC7 (pattern B), AC2 (third variant), AC3; Q5; Q3 shape; serialization SQ1-SQ8, SQ10; slice B; P-C3 issue texts |
| When | before P-B2/P-B3 (slot numbering, stamps, sources); slice A and C1 may proceed on the agreed answers; its rows feed the P-C5 parity pattern B |
| Writes | four designer saves (S1-S4), each with the owner's yes; the process stays as the e2e fixture |
| Who | Q18 (agreed): the lead through Claude in Chrome with the owner logged in, or the owner by hand (about 30-45 min) |

**Preconditions:** EM0 done; D2 and D3 found; `PContact` and `PTarget` need no values (never run).

**Steps (designer):**
1. Process library, new process in the classic designer. Code `UsrSeAttachmentsDesignerFixture`, caption "SE
   attachments designer fixture", package Custom.
2. Process parameters: `PContact`, `PTarget` (Lookup, Contact), `PInvoice` (Lookup, Invoice), `Doc` (Other, File),
   `Docs` (Other, Collection of records; on it add an item: Other, File, named `File`). This is the menu path the
   ENG-92719 capture used (`CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md:821-826`).
3. Elements, chained Start -> OF1 -> RF1 -> PF1 -> SE1 -> SE2 -> End, so every source comes before its consumer.
   Captions in the resources: `PD/Resources/BaseFileProcessingUserTaskPropertiesPage.ClientUnit/resource.en-US.xml:12-18`,
   `PD/Resources/EmailTemplateUserTaskPropertiesPage.ClientUnit/resource.en-US.xml:11, 17, 20, 28-29, 32-33`.

| Element | Settings |
|---|---|
| OF1 | Process file. "What is the source of the file?" = Object attachments; which object = Contact (Contact attachment); filter Contact = `PContact`; "What to do with file?" = Use in process |
| RF1 | Process file; source = Generated report; report = Invoice; filter Id = `PInvoice`; Use in process |
| PF1 | Process file; source = Process parameter; Files = OF1 -> Collection of files -> File; save to Contact (Contact attachment), record `PTarget` |
| SE1 | Send email. "How is the message sent?" = Send email manually; Custom message; To = constant `sea-em1@example.com`; Subject `SEA-EM1`. "Add attachments" five times, binding each row through the field's menu, "Process parameter" window: row 1 element tab OF1 -> Collection of files -> File; row 2 RF1 -> Collection of reports -> File; row 3 PF1 -> Collection of files -> its file item (the package has no en-US caption for it, `PD/Resources/ProcessFileProcessingUserTask.ProcessUserTask/resource.en-US.xml:10-11`: record what the window shows); row 4 process tab `Docs` -> its item (record whether the window offers the item, the collection itself, or both, and which one was picked: serialization SQ3); row 5 process tab `Doc` |
| SE2 | Send email automatically; Template message; template = D3; To = constant; From left empty (no mailbox: the card marks the field, the save still goes through, `PD/Schemas/BaseProcessSchemaElementPropertiesPage/BaseProcessSchemaElementPropertiesPage.js:553-558` per `R/r3-designer.md:138`); "Add attachments" once: RF1 -> Collection of reports -> File |

   Note (avoid in the capture): keep every recipient and other top-level parameter of SE1 and SE2 a constant or
   bound to a top-level value, never to an ITEM of a collection. Such a mapping makes the designer convert the Send
   email to multi-instance (`PD/Schemas/MappingEditMixin/MappingEditMixin.js:981-995, 1031-1035`; decisions Q16),
   which moves every root parameter, the slots included, into the item properties of `InputRecordCollection` /
   `OutputRecordCollection`; the tool refuses `email.attachments` on such an element (Q16 agreed), so the capture
   would no longer be an oracle for it.

4. Open every element's card (rule 11). Optional, read-only: the in-memory dump of the slots before the save
   (adapt the snippet of `CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md:849-858`
   to elements whose `managerItemUId` is `184dbb27-ce13-4d37-8dff-c2ff1df9cf19`, `PD/Schemas/EmailTemplateUserTask/descriptor.json:3`).
5. **S1:** save (write 1). Agent: RB-1, RB-2, records 1-8 of serialization §7.2.
6. **S2:** open SE1, clear row 2's value with the field's clear icon, close the card, save (write 2). RB-1, RB-2.
7. **S3:** open SE1, "Add attachments", bind it to OF1 -> Collection of files -> File, save (write 3). RB-1, RB-2.
8. **S4 (EM1d):** open SE1, re-bind row `Attachments1` from OF1's File to process `Doc`, save (write 4). RB-1, RB-2.
9. Record once: the UIds of the process, SE1, SE2, every source collection and its item (serialization §7.1).

**Expected (S1):**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| slot names | `Attachments1`..`Attachments5` on SE1, `Attachments1` on SE2 | lowest free number from 1 per element, a name taken by any of the element's parameters blocking its number whatever the stamp: `PD/Schemas/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:465-479`, called over `processElement.parameters` at `PD/Schemas/ProcessFilesControlSchema/ProcessFilesControlSchema.js:244-249, 260` | [source] |
| outer slot | `L1` CompositeObjectList `651ec16f-...`, `L12` 0, `A3` = `A4` = process UId, `IL2` = SE UId, one `L18` item | `ProcessFilesControlSchema.js:279-294`; corpus `PS/ProcessTests/branches/7.8.0/Schemas/ProcessFileSendMail/metadata.json:2930-2961` | [source] [corpus] |
| item | `A2` = `<slot>FileLocator`, `L1` FileLocator `a33c9252-...`, `L12` 0, same stamps | `ProcessFilesControlSchema.js:262-278` | [source] [corpus] |
| rows 1-3 (element collections) | both levels Script: outer to the collection, item to its file item, `GS5` = process UId | the parent is bound when the item takes a collection item: `PD/Schemas/MappingEditMixin/MappingEditMixin.js:947-963, 981-996`; corpus as above | [source] [corpus]; row 3 has no corpus row |
| row 4 (`Docs`) | outer Script `[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{<Docs>}]#]`, item to the `Docs` item; or item bound to `Docs` itself with the outer unbound, as the Process file element stored on 2026-10-20 | `TS/Terrasoft.Nui/Resources/Terrasoft/designers/process-schema-designer/process-constants.js:131`; serialization §3 row "item bound to the COLLECTION itself" | [inferred]; the second shape is [measured 2026-10-20] on another element |
| row 5 (`Doc`) | outer has no value (`{}` or `{"GS5":<P>}`), item Script to `Doc` | `MappingEditMixin.js:1075-1096` does not touch the parent of a single-instance element; the row starts with source None, `ProcessFilesControlSchema.js:272-274, 289-291` | [source] |
| `BK15` | no mapping row references a slot or item | corpus (each slot UId occurs once, `R/r3-designer.md:194`) | [corpus] |
| resources | four keys per row in the authoring culture: captions "Attachments collection" / "Attachment"; DisplayValue `[#<el>.<coll>#]` / `[#<el>.<coll>:<item>#]` | `EmailTemplateUserTaskPropertiesPage.js:381-387`; `PS/ProcessTests/branches/7.8.0/Resources/ProcessFileSendMail.Process/resource.en-US.xml:12-15` | [source] [corpus] |
| SE2 | the same slot shape in auto + template | the control sits outside the mode containers; corpus `MailConnectionsProcess` is auto + template (`R/r3-designer.md:44, 174`) | [source] [corpus] |

**Expected (S2-S4):**

| Save | Expected | Evidence | Tag |
|---|---|---|---|
| S2 | `Attachments2` and its item gone; slots 1, 3, 4, 5 keep names and UIds; row 2's four resource keys gone or orphaned (SQ6; the builder's side is EM2 steps 4 and 6) | an empty item value removes the parent with its item: `ProcessFilesControlSchema.js:376-387, 392-405, 410-419` | [source]; resources [inferred] |
| S3 | new slot named `Attachments2`, fresh UIds, placed after `Attachments5` in `BP2` | `ProcessSchemaUserTaskUtilities.js:465-479`; `parameters.add` at `ProcessFilesControlSchema.js:237` | [source]; position [inferred] |
| S4 | item -> `Doc`; outer keeps OF1.ObjectFiles (stale parent) | `MappingEditMixin.js:1090-1094` fires only `collectionMappingReset`, a no-op for a single-instance element | [source] |

**If not:**

| Outcome | What changes |
|---|---|
| a collection row's outer stays unbound | the "both levels" rule is wrong for the current designer: slice B binds the item only for that kind, the ACR and pattern-B rows follow EM1, and the run-time read of shape E (one null row) becomes the risk to measure in EM4 |
| S3 names the new slot `Attachments6` (max + 1) | Q5 flips to max + 1; P-B2 and serialization §5 change |
| row 4 stores the item bound to `Docs` itself | the builder's both-level form has no designer twin: the owner chooses (serialization SQ3); describe gets an issue for the designer's form, which reads as one row holding a list and throws `InvalidCastException` at run time (`TS/Terrasoft.Common/CompositeObject.cs:125`) [traced, not measured] |
| new keys (`A5`, `BL11`, `L6`, `GS4`) or `BK15` rows appear | ACR R7/R11 and P-B2 follow the current designer, not the 2021-2022 corpus |
| row 5's outer carries `{"GS5":<P>}` or a value | ACR R6 settles to that form; the slot writer writes it |
| removed row's resource keys stay | parity: the package deletes no `SysLocalizableValue` row either (design 4.3); EM2 step 6 shows whether a reused number then carries a stale outer DisplayValue |
| the item's `L1` changed after binding (the `change:Value` handler copies the picked type, `ProcessFilesControlSchema.js:340-346`) | SQ5: the writer copies the designer's type per source kind |

**Cleanup and leftovers:** the process stays as `UsrSeAttachmentsDesignerFixture` until the clio PR's e2e evidence run
(TC-I-09) is recorded; then CU-3 on the owner's yes. Never run (rule 12), so it owns no data.

### EM2 Server-built twin

| | |
|---|---|
| Question | Does the platform's pre-save gate accept a builder-made slot, and does the twin equal EM1 under the comparison rule (ACR, serialization §6.2)? |
| Gates | AC1, AC7; serialization SQ11; Q4 and Q5 parity; the resource rows of a reused slot number (design 4.3); the plan's STOP gate before slice C |
| When | STOP-gate run after the first experimental cut (installed on the owner's yes, rule 3): steps 1, 2, 4, 5, 6 (save, pre-save gate, stored bytes). Steps 3 and 7 (describe) need P-C3 of slice C: they run in the EM2 re-run on the final cut, which repeats every step on a fresh twin |
| Writes | per run: one create, three modifies |

**Preconditions:** the experimental cut installed (the final cut for the re-run); the session's clio is the feature
build (rule 9); EM1 S1-S4 recorded.

**Steps (builder, in words):**
1. `create-business-process`, one request: process `UsrSeEm2Twin`, the same five process parameters as EM1; elements in
   flow order with EM1's captions: OF1, RF1, PF1 as `fileProcessing` blocks with EM1's settings (OF1 keeps its record
   condition, section 3.1); SE1 as `sendEmail` manual, custom, the same To and Subject, `attachments` = [OF1
   `ObjectFiles`, RF1 `ReportFiles`, PF1 `ObjectFiles`, process `Docs`, process `Doc`] (each entry `sourceElement` +
   `sourceElementParameter` or `processParameter`); SE2 auto, template D3, `attachments` = [RF1 `ReportFiles`]. Record
   the notices of the answer.
2. RB-1, RB-2 on the twin. Diff SE1 and SE2 against EM1 S1 under the ACR (R1-R11).
3. Re-run only: `describe-business-process UsrSeEm2Twin`: `email.attachments` returns the five and one entries in the
   write shape, with `bindingLevels` both x4 and itemOnly for `Doc`.
4. `modify-business-process` `setElement` SE1 with the list minus entry 2 (replace, Q4). RB-1, RB-2; diff against S2.
   Record the notices.
5. `setElement` SE1 with the four kept entries plus OF1 `ObjectFiles` again. RB-1, RB-2; diff against S3 (the new slot
   must be `Attachments2`).
6. Reuse in one save: `setElement` SE1 with [OF1 `ObjectFiles`, OF1 `ObjectFiles`, `Docs`, `Doc`, `Doc`]. It drops
   the collection-sourced `Attachments3` (PF1) and adds a second single-File row, which takes the freed number
   `Attachments3` in the same save. RB-1, RB-2: read the key `BaseElements.SE1.Parameters.Attachments3.DisplayValue`
   (serialization §2.1). The designer cannot make this case: it frees a number only when it saves
   (`ProcessFilesControlSchema.js:376-387`), so a row added in the same session gets a new number.
7. Re-run only, read-only: `describe-business-process UsrSeAttachmentsDesignerFixture`: S4's row comes back with the
   stale-parent issue (test plan TC-I-09). S4 has no builder twin: the builder never writes a stale parent.

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| create | succeeds, so the pre-save gate passed: `EnsureValidForSave` throws on any validation error | `PB/Validation/ProcessSchemaValidator.cs:56-71` | [source] |
| nested Script on the item | compiles against `IFileLocator`; a byte-identical designer formula validates | `TS/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:329-331`; nested items are registered `TS/Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs:402-416, 434-464` | [traced, not measured] |
| ACR diff S1 | equal, display values included: the slot writer writes the designer form on slots (outer `[#<el>.<coll>#]`, item `[#<el>.<coll>:<item>#]`, single File `[#<param>#]`) | design §3; serialization §6.2-6.3 | [inferred] |
| replace (step 4) | slots 1, 3, 4, 5 keep names and UIds; `Attachments2` and its item gone; its resource rows recorded, not assumed gone (the package deletes no `SysLocalizableValue` row, design 4.3) | design 2.3 | [inferred] |
| re-add (step 5) | new slot `Attachments2` | Q5 | [inferred] |
| reuse (step 6) | new `Attachments3`, item-only to `Doc`, item DisplayValue `[#Doc#]`; the outer DisplayValue key absent or empty, never PF1's old `[#PF1.<collection caption>#]` | design 4.3, 2.3 | [inferred] |
| notices (re-run only: the notices are P-C4, slice C) | step 1: the single-File notice for `Doc` (no feature notice: Q9 agreed, no build-time check); step 4: the removal notice with RF1's entry in the write shape, the single-File notice again (it fires for kept entries too); step 5: the duplicate notice for the two OF1 entries; step 6: the removal notice for PF1's entry, duplicate notices for OF1 and `Doc` | design 5.2 | [inferred] |

**If not:**

| Outcome | What changes |
|---|---|
| the pre-save gate refuses (for example `ParameterNotFound` or an expression type error on the item) | STOP: the owner hears the message; the slot writer's value form changes (for example the item's Script text) before slice C; the estimate is redone (plan section 0) |
| a must-match key differs | fix the writer in slice B before the next cut; never widen the ACR without the owner's yes |
| a display value differs | the slot writer's designer form is wrong (AD1/AD2): fix in slice B |
| step 6's reused outer keeps PF1's DisplayValue | the writer writes an explicit empty outer DisplayValue for every reused or unbound outer (design 4.3); fix in slice B |
| re-run: describe does not round-trip | P-C3 is not done; the read-back members change |
| replace renumbers or loses UIds | P-B3's reconcile is wrong; fix before slice C |

**Cleanup and leftovers:** keep `UsrSeEm2Twin` for EM3 and for the final-cut re-run; CU-3 for it and the re-run's
fresh twin after the PR is ready.

### EM3 The designer opens the builder-made element

| | |
|---|---|
| Question | Does the designer's files control render builder-made rows with display values, and does a no-op designer save keep every compared key? |
| Gates | AC1, AC7; serialization SQ9, SQ12; AD1/AD2; the STOP gate |
| When | after the first experimental cut, right after EM2 |
| Writes | one designer save |

**Preconditions:** EM2 passed; RB-1/RB-2 of `UsrSeEm2Twin` kept as "before".

**Steps:**
1. Open `UsrSeEm2Twin` in the designer (rule 6). Open SE1's card: five attachment rows, each with its display value;
   SE2: one row. Screenshot or DOM read of the rows (rule 5).
2. Open every other element's card (rule 11), change nothing, save (write).
3. RB-1, RB-2 ("after"). Diff before/after under the ACR, slots only.
4. Optional, informative: in a fresh designer session, try to delete OF1 and record whether the designer refuses
   because SE1's slots read it (the item's `forceRemove` exists only in the creating session, `ProcessFilesControlSchema.js:275-276`; `R/r3-designer.md:74`). Cancel instead of saving.

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| rows found | every slot whose `A3` is the process, which has no parent, and whose name matches `^Attachments\d+$` | `ProcessFilesControlSchema.js:308-324`; `getIsDynamic` = `createdInSchemaUId === parentSchema.uId` (`R/r3-designer.md:50`) | [source] |
| row content | the row is the slot's FIRST item | `ProcessFilesControlSchema.js:223` | [source] |
| display text | the item's `sourceValue.displayValue` (its DisplayValue resource); without one, the raw `[#...#]` value | the row's view model takes `parameter.getMappingValue()` (`PD/Schemas/ProcessFlowElementPropertiesPage/ProcessFlowElementPropertiesPage.js:735, 835-837`), which returns `displayValue: displayValue \|\| value \|\| null` (`TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-schema-parameter.js:790-797, 808-819`) | [source] |
| no-op save | `_saveFiles` re-sets each item's value (`ProcessFilesControlSchema.js:376-387`): compared keys equal; any change is a designer normalisation to name | - | [traced, not measured] |

**If not:**

| Outcome | What changes |
|---|---|
| a row is missing | a stamp, the item order or the name is wrong in the writer: fix in slice B |
| a row shows the raw `[#...#]` formula | the item's DisplayValue resource is missing or keyed wrongly: fix the slot writer's display values (AD2) |
| a row shows a bare caption | the writer wrote the package's bare caption instead of the designer form (AD1/AD2): fix the writer |
| the save changes a compared key | either the writer adopts the designer's form, or the owner names the deviation |
| deleting OF1 is allowed and leaves SE1's slots dangling | a guidance sentence; no package change (the designer is not ours) |

**Cleanup and leftovers:** none beyond EM2's process.

### EM4 Run: manual + custom, 2+ slots

| | |
|---|---|
| Question | Does a manual Send email with N slots put the sum of all rows on the created email activity, for every source kind? |
| Gates | AC2 (three variants; a filled process FileCollection is EM4c), AC3, AC4; CL-7 evidence; serialization says nothing about run time |
| When | before PR ready on the claimed cut (EM4b); a designer-built baseline EM4a may run before code |
| Writes | build (one create, or one designer save for EM4a); one run; cleanup |

**Preconditions:** D1 with exactly 2 files (so n = 2), D2, D4; `UseProcessEmailAttachments` on (M-02).

**Steps:**
1. Build `UsrSeEm4Run` (EM4b: `create-business-process`; EM4a: the designer, as EM1 minus SE2): Start -> OF1 -> RF1 ->
   PF1 -> SE1 -> End with EM1's settings; SE1 manual, custom, Subject `SEA-EM4-<token>`, attachments OF1, RF1, PF1,
   `Docs`. No `Doc` entry: an unset File fails the element (EM8), and run-process cannot pass a file locator.
2. `run-process` with `PContact` = D1, `PInvoice` = D2, `PTarget` = D4, `watch: ["Activity", "ActivityFile", "ContactFile"]`,
   short `wait-timeout`. Record the run id.
3. RB-5, RB-6, RB-3, RB-4, RB-8, RB-7.
4. Keep the run parked for EM15; then CU-1, and RB-5 again.

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| run state | `running`, parked on SE1 | manual `Execute` returns false after creating the activity: `ManualEmailUserTaskSender.cs:163-181` | [traced, not measured] |
| activity | one Activity, title = the token | `EmailTemplateUserTask.cs:296-327` | [source] |
| `ActivityFile` rows | 2 (OF1) + 1 (RF1, one invoice) + 2 (PF1 copies) + 0 (`Docs` unset) = 5, each with the activity's id | slots by regex + own stamp (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:78-99`, check `:94`); one attachment per row (`EmailTemplateUserTask.cs:107-119, 280-288`); copied when the activity is created (`:207-222, 327`; `:121-129`); an unset collection gives 0 rows (`TS/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:454-468`) | [traced, not measured] |
| names | OF1 rows = the contact's file names; RF1 = the report file; PF1 = the copies' names | PF1's output holds the copies (`CLIO/docs/knowledge/platform/after-saving-object-files-points-at-the-source-files.md`) | [source] |
| `TypeId` | File (`529bc2f8-0ee0-df11-971b-001d60e938c6`) on both paths | file-storage path: the copy sets `ActivityId` and `Name` (`EmailTemplateUserTask.cs:125-126`), and the file API stores File on every new file (`TS/Terrasoft.File/Metadata/EntityFileMetadataStorage.cs:27, 232-237`); the source's `TypeId` is a base column, never copied as an attribute (`:28-38, 112-121`; `TS/Terrasoft.File/File.cs:172-185`). Legacy path: `EmailTemplateUserTask.cs:138` | [traced, not measured] |
| PF1 copies | 2 `ContactFile` rows on D4 | PF1 saves to `PTarget` | [traced, not measured] |

**If not:**

| Outcome | What changes |
|---|---|
| 0 rows, run fine | check the feature state for the run user (RB-9, RB-9b; the KB troubleshooting line, Q9) and each slot's `A3`; a wrong stamp means the writer is wrong (P-B2) |
| element error | RB-5 `ErrorDescription`: a null locator means a shape problem (EM1 "If not" first row); fix before PR |
| only some slots attach | the missing slot's shape differs (RB-1 of that slot) |
| PF1 slot attaches the SOURCE files instead of the copies | the guidance sentence on PF1 changes; no package change |
| `TypeId` empty or not File | escalate: it contradicts the file API trace |

**Cleanup and leftovers:** after EM15 and EM13: CU-1 (if still parked), CU-2 (5 `ActivityFile`, 1 Activity, 2 `ContactFile`
on D4), CU-3 for `UsrSeEm4Run` (EM4a keeps it for EM11b first).

### EM4c Run: a process FileCollection filled by a parent process

| | |
|---|---|
| Question | Does a Send email whose attachment entry is a process FileCollection attach every file a parent process passes in? |
| Gates | AC2 (DoD: a process FileCollection passed from a parent process); Q2 (a process FileCollection is how files reach an email from a parent process) |
| When | before PR ready on the claimed cut, after EM4b |
| Writes | two creates (callee first), one run, cleanup |

**Preconditions:** D1 with exactly 2 files (n = 2); the claimed cut installed; `UseProcessEmailAttachments` on (M-02).

**Steps (builder):**
1. `UsrSeEm4cCallee`: process parameter `Docs` (FileCollection, direction In); Start -> SE1 -> End; SE1 manual, custom,
   To constant, Subject `SEA-EM4C-<token>`, `attachments` = [ { `processParameter`: `Docs` } ].
2. `UsrSeEm4cParent`: parameter `PContact` (Lookup, Contact); Start -> OF1 (as in EM1: Object attachments on Contact,
   Contact = `PContact`, use in process) -> SP1 (a Sub-process element calling `UsrSeEm4cCallee`) -> End; one mapping
   onto SP1's `Docs.File` from `OF1.ObjectFiles.File`: an item from an item binds both levels (P1,
   `PB/Mappings/CollectionBindingRules.cs:42-47`) [source; on a sub-process input inferred, not tried; if the builder
   refuses it, bind it in the designer and record that]. RB-1 of the parent: SP1's `Docs` bound to `OF1.ObjectFiles`,
   its item to `File`.
3. `run-process` the parent with `PContact` = D1, `watch: ["Activity", "ActivityFile"]`. RB-5 for both processes,
   RB-3, RB-4.

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| callee's `Docs` | 2 rows: the sub-process start reads SP1's parameter values through the element parameter reader, which gives one row per row of a bound collection | `TS/Terrasoft.Core/Process/ProcessComponentSet.cs:1289-1306`; `TS/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:453-457, 470-485` | [traced, not measured] |
| callee run | `running`, parked on SE1 | `ManualEmailUserTaskSender.cs:163-181` | [traced, not measured] |
| `ActivityFile` rows | 2, the D1 contact's files, on the callee's activity | as EM4 | [traced, not measured] |

**If not:**

| Outcome | What changes |
|---|---|
| 0 rows, runs fine | RB-1 of SP1's `Docs` (both levels bound?) and of the callee's slot; if both are right, the parent-to-callee route does not carry the collection: the guidance stops promising it and the owner hears it (AC2 wording, Q19) |
| element error in the callee | RB-5 `ErrorDescription`: a null locator means `Docs`' item was not filled per row |

**Cleanup and leftovers:** CU-1 on the callee's activity (the callee ends, then the parent), CU-2 (1 Activity,
2 `ActivityFile`), CU-3 for the parent first, then the callee.

### EM5 Run: manual + template

| | |
|---|---|
| Question | Does template mode attach exactly as custom mode, and does switching the mode keep the slots? |
| Gates | AC4; test plan TC-U-16/17 confidence |
| When | before PR ready, after EM4b |
| Writes | one modify, one run, cleanup |

**Preconditions:** EM4b's process; D3.

**Steps:**
1. RB-1 of `UsrSeEm4Run` (before). `modify-business-process` `setElement` SE1 to the template message with D3; keep
   the Subject token `SEA-EM5-<token>` on the element if the block keeps it (record what happens to Subject).
2. RB-1 (after): the slot bytes are unchanged.
3. `run-process` as EM4 step 2. RB-3 (or `watch`), RB-4, RB-5.

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| slots after the switch | byte-equal on the compared keys | template mode writes `BodyTemplateType`, `EmailTemplateId` and `EmailTemplateEntityId`, and clears `Body` and a constant `Subject` the block does not re-send; nothing else (`PB/Email/SendEmailApplier.cs:101-103, 417-454`) | [source] |
| `ActivityFile` rows | 5, as EM4 | both providers share `CreateEmailMessage` (`PD/Schemas/EmailTemplateUserTaskSender/EmailTemplateUserTaskSender.cs:206-234`, gate `:227-229`); the template provider overrides content only (`EmailTemplateUserTaskMessageProvider.cs:137-160`) | [traced, not measured] |
| activity title | the element's Subject if non-empty, else the template's | `EmailTemplateUserTaskMessageProvider.cs:157-158` | [source] |

**If not:**

| Outcome | What changes |
|---|---|
| "Localizable template not found" (`EmailTemplateUserTaskMessageProvider.cs:155`) | a seed problem (template content per language), not attachments: pick another D3 and re-run |
| slots changed by the switch | the mode switch must keep slots (P-B3 test); fix |
| fewer files than EM4 | escalate: the shared path assumption is wrong |

**Cleanup and leftovers:** CU-1, CU-2 (5 `ActivityFile`, 1 Activity, 2 `ContactFile`).

### EM6 Auto mode without delivery (dropped)

**Dropped (Q17 agreed 2026-10-28: the owner configures real mail on stand `Creatio`).** No test-only
`MailboxSyncSettings` record (D5) is created. Its auto + Create activity case runs with a real mailbox as EM7c. The
traces it rested on stay true and are the facts file's OF-8: with a named sender and `CreateActivity` on, the activity
is created before the send and the files are copied to it, and `IgnoreErrors` swallows a failed send
(`PD/Schemas/AutoEmailUserTaskSender/AutoEmailUserTaskSender.cs:112-113, 135-151, 165-181`); with an empty sender the
default-sender path creates no activity (`:105-108, 68-94`) [traced, not measured].

### EM7 Real delivery

| | |
|---|---|
| Question | Does a builder-made auto Send email deliver every attachment, in custom and template mode, and with Create activity on does the activity carry the files? |
| Gates | AC4 (auto) per Q17 (agreed 2026-10-28); KB-1 wording |
| When | before PR ready, on stand `Creatio`, on the claimed or final cut already installed there, once the owner confirms that mail sending works (Q17 agreed); until then auto mode rests on the unit tests and the shared run-time path, and the package PR stays draft, because DoD AC4 cites EM7 |
| Writes | build, two modifies (template mode, Create activity), three runs that SEND mail (rule 13), cleanup |

**Preconditions:** the owner has configured real mail sending on stand `Creatio` and says it works (D6); the claimed
cut installed (rule 3); D1, D2, D3.

**Steps:**
1. Build `UsrSeEm7Send` with the builder: OF1 (as in EM1, Contact = `PContact`) + RF1 (as in EM1, Id = `PInvoice`) ->
   SE1 auto, custom, From = the mailbox the owner configured, To = the owner's test mailbox (a recipient is required:
   with none, auto mode returns when `IgnoreErrors` is on, or throws, AFTER building the message,
   `PD/Schemas/AutoEmailUserTaskSender/AutoEmailUserTaskSender.cs:98-104`), Subject `SEA-EM7-<token>`, two attachments
   (OF1 `ObjectFiles`, RF1 `ReportFiles`). "Ignore email errors" stays on and "Create activity" off (the defaults,
   `PD/Resources/EmailTemplateUserTask.ProcessUserTask/resource.en-US.xml:44-45, 58-59`).
2. EM7a: run with `PContact` = D1, `PInvoice` = D2, on the owner's yes. RB-5.
3. EM7b: `setElement` SE1 to template mode with D3 (slots unchanged, as EM5); run as step 2. RB-5.
4. EM7c: switch "Create activity" on. The `email` block has no `CreateActivity` member
   (`CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:185-188`), so the slots stay builder-made and
   `CreateActivity` is set by a raw constant `true` mapping or in the designer [inferred, not tried]. Run as step 2.
   RB-5, RB-3, RB-4.
5. The owner reads the three received mails.

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| From | the configured mailbox's `SenderEmailAddress`, read by an ESQ with `UseAdminRights = false`, i.e. without a rights check (`TS/Terrasoft.Core/Entities/EntitySchemaQuery.cs:1085-1088, 2491, 2503-2505`), so the run user need not see the record | `PD/Schemas/EmailTemplateUserTaskSender/EmailTemplateUserTaskSender.cs:129-148` | [source] |
| each received mail | 3 files (2 from OF1, 1 report), names as the sources | the message carries every slot's files (`EmailTemplateUserTaskSender.cs:227-229`) | [traced, not measured] |
| EM7a, EM7b | no `ActivityFile`: `CreateActivity` is off | `AutoEmailUserTaskSender.cs:114-116, 183-187` | [traced, not measured] |
| EM7c | one Activity, created before the send, with 3 `ActivityFile` rows; the mail delivered | `AutoEmailUserTaskSender.cs:112-113, 165-181` (activity first, `:176`); the copy `PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:207-222, 327` | [traced, not measured] |
| limits | no size or count check in the Creatio code on this path; a limit of the email service or the mail server is outside the tree | `PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:68-142`; `PS/CrtEmailSender/branches/7.8.0/Schemas/EmailSender/EmailSender.cs:316-320, 690-693`; `PS/IntegrationV2/branches/7.8.0/Files/cs/EmailClient.cs:163-175` | [source]; the service limit [unverifiable; facts OF-20] |

**If not:**

| Outcome | What changes |
|---|---|
| a missing file in the received mail with every file present in the message | points at the email service, not the builder |
| a missing file everywhere | the case goes back to EM4's shape checks |
| no `ActivityFile` in EM7c | the guidance must not say the auto-mode activity carries the files |
| an element error despite `IgnoreErrors` | the failure is outside `TrySendEmailWithRetry` (`AutoEmailUserTaskSender.cs:135-151`), for example in the copy: record the message; guidance sentence |

**Cleanup and leftovers:** the received mails stay with the owner; CU-2 (EM7c's Activity and 3 `ActivityFile`), CU-3.

### EM8 A single File (item-only)

| | |
|---|---|
| Question | Does a slot whose item alone is bound to a set File attach one file, and does an unset File fail the element? |
| Gates | Q3 (A or fall back to B); KB-1 and P-C4 notice text (TC-U-11, TC-U-43, TC-K-06); Q16 per-recipient pattern |
| When | before PR ready; may run before code on a designer-built probe |
| Writes | one designer save (or builder create after the cut), three runs, cleanup |

**Preconditions:** D1; a `ContactFile` id of D1 (RB-10a).

**Steps:**
1. Build `UsrSeEm8Single` in the designer: parameters `Doc` (File), `PFileId` (Unique identifier), `PSetDoc` (Boolean),
   `PContact`; Start -> script task -> OF1 (as in EM1, Contact = `PContact`) -> SE1 -> End. Script task body:
   `if (Get<bool>("PSetDoc")) { Set("Doc", new Terrasoft.File.EntityFileLocator("ContactFile", Get<Guid>("PFileId"))); } return true;`
   (`TS/Terrasoft.File/EntityFileLocator.cs:1, 12`; the M-10 probe set its File the same way). SE1 manual, custom, two
   rows: row 1 `Doc`; row 2 first bound to OF1 -> Collection of files -> File, then re-bound to `Doc` (EM1d's stale
   parent). The save compiles the script task [inferred]. RB-1: row 1 outer unbound, row 2 outer = OF1.ObjectFiles.
2. EM8a: run with `PSetDoc` = true, `PFileId` = a D1 file, `PContact` = D1. RB-3, RB-4.
3. EM8b: run with `PSetDoc` = false, `PContact` = D1. RB-5 (`ErrorDescription`), RB-6, RB-3.
4. Optional EM8c (only row 2 matters, already in EM8a): count row 2's copies.

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| EM8a row 1 | exactly 1 attachment: an unbound outer with a bound item is read as ONE row | `ProcessInstanceParametersDataReader.cs:444-452` | [traced, not measured]; analog M-10 [measured 2026-10-19] |
| EM8a row 2 (stale parent) | 2 attachments, both `Doc`'s file: one per row of OF1's collection, the item evaluated per row | `ProcessInstanceParametersDataReader.cs:454-458, 471-489` | [traced, not measured] |
| EM8b | element Error, `NullReferenceException` at `FileFactory.CreateFileInstance`; no Activity at all, because manual mode builds the message before it creates the activity | `TS/Terrasoft.Common/CompositeObject.cs:120-127`; `TS/Terrasoft.File/FileFactory.cs:44-46, 70-75`; `ManualEmailUserTaskSender.cs:56-60` | [traced, not measured]; analog M-11 [measured 2026-10-19] |

**If not:**

| Outcome | What changes |
|---|---|
| EM8a row 1 attaches nothing | Q3 falls back to B (refuse a single File, "wrap it in a FileCollection"); TC-U-11 becomes a refusal; Q16's per-recipient pattern is dropped |
| EM8b completes without error | the notice and guidance say "sends without the file" instead of "fails" |
| EM8b leaves an Activity | CU must include it; guidance mentions the half-made activity |
| row 2 gives 1 file | describe's stale-parent issue text (TC-U-64 row 4) changes |

**Cleanup and leftovers:** CU-1 for EM8a, CU-2 (Activity, 3 `ActivityFile`), CU-3. EM8b's failed run stays in the logs.

### EM9 Outer-only slot (optional)

| | |
|---|---|
| Question | An outer slot bound to a collection with its item unbound: loud error or silent nothing? |
| Gates | P-C3 issue wording (TC-U-64 row 1) and the article's troubleshooting line only. Either outcome delivers no file (`R/r10-critic.md:59`) |
| When | optional |
| Writes | depends on the route |

**Producing the shape (the open problem):** no supported route stores it today [source]:
- the designer drops a row whose item is empty on save (`ProcessFilesControlSchema.js:383-385`), unless lazy parameters
  are not loaded or `canRemoveParameter` refuses (`:396-403`), which cannot be arranged on purpose;
- the builder has no operation that clears an element's nested item: `addMapping` has "deliberately no remove/clear
  counterpart" (`PB/Operations/MappingOperations.cs:10-12`), and on 1.6.6.125 a raw `addMapping` onto the outer level
  of an existing designer row leaves that row's item bound; the feature refuses every non-block mapping onto a slot
  (Q8, design 4.5), so that route closes;
- a direct `SysSchema.MetaData` edit would bypass the schema manager's cache; not proposed.
So EM9 runs only if the owner accepts a non-released measurement build of the feature branch that writes the outer
level alone (its own claimed number per plan section 6), or it is skipped and the issue text says "fails or attaches
nothing".

**Expected (if produced):** every row reaches the email as `{<slot>FileLocator: null}` and the element fails with a
NullReferenceException (`ProcessInstanceParametersDataReader.cs:481-487`; `CompositeObject.cs:120-127`;
`FileFactory.cs:44-46`) [traced, not measured]. **If not:** silent (0 files) changes only the issue's verb.

**Cleanup and leftovers:** CU-1/CU-2/CU-3 as for EM4, if run.

### EM10 A source that has not run

| | |
|---|---|
| Question | What does the email do when its source element did not run: on an untaken gateway branch, or after the email on the flow? |
| Gates | AC6 (KB-1, TC-K-03: the article states EM10's result with its date); Q19 AC6 text; the text of Q6 C's reachability warning (P-C2, design §5.2) |
| When | before PR ready; the runs may go before code on designer-built probes; step 3 needs the claimed cut |
| Writes | two designer saves, two runs, one builder create and one modify (step 3), cleanup |

**Steps:** OF1 is as in EM1 (Contact = `PContact`) in every probe, and every run passes `PContact` = D1.
1. EM10a `UsrSeEm10Branch` (designer): parameters `PTake` (Boolean, default false), `PContact`; Start -> exclusive
   gateway; the conditional flow `PTake = true` -> OF1 -> merge; the default flow -> merge; merge -> SE1 manual (one
   row from OF1) -> End. Run with `PTake` = false. RB-6 (OF1 absent), RB-3, RB-4, RB-5.
2. EM10b `UsrSeEm10After` (designer): Start -> SE1 manual (one row from OF1) -> OF1 -> End. Run. RB-3, RB-4.
   On the build path the agreed guard refuses only a source DECLARED later in `elements[]` (Q7), so this flow
   order is buildable by the builder when OF1 is declared first, with a warning (Q6 C); EM10b shows what such a process
   does.
3. After the claimed cut: build EM10b's twin `UsrSeEm10AfterB` with the builder (OF1 declared first in `elements[]`,
   flows Start -> SE1 -> OF1 -> End) and record the answer's reachability warning; then `setElement` SE1 re-sending
   the same `attachments` list and record the answer again (the warning fires on modify too, also for a kept entry).
   Check that the warning's consequence clause matches what steps 1-2 measured. No run.

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| both | an Activity with 0 `ActivityFile` rows, no error | a null or empty source gives an empty list (`ProcessInstanceParametersDataReader.cs:454-468`); `AddEmailAttachments` returns on an empty list (`EmailTemplateUserTask.cs:207-211`) | [traced, not measured]; "not run yet evaluates to null" is [inferred] |
| step 3, both answers | the reachability warning naming the entry, OF1 and SE1: "...does not lie on a path before 'SE1'. If OF1 has not run when the email runs, the email goes out without these files and without an error." EM10a's graph would raise none (OF1 reaches SE1 through the merge: reachability is a hint, not a proof) | design 4.6, 5.2; the walk reuses `ProcessGraphBuilder.Traverse` (`PB/Graph/ProcessGraphBuilder.cs:2228-2250`) | [inferred] |

**If not:**

| Outcome | What changes |
|---|---|
| element error (an unset output that is not enumerable gives a null list and a `foreach` over null, `ProcessInstanceParametersDataReader.cs:459-466`, `EmailTemplateUserTask.cs:284-285`) | the article and the reachability warning say "fails" instead of "goes out without these files"; whether the warning becomes a refusal goes back to the owner (Q6 B was rejected for gateway branches and loops) |
| files attached in EM10b | impossible by the trace; re-check that OF1 really ran after SE1 (RB-6) |
| step 3 builds without the warning, or warns only on build | C6's reachability check misses this graph or the modify path: fix before PR ready |

**Cleanup and leftovers:** CU-1, CU-2 (2 Activities), CU-3 for the three processes.

### EM11 One collection, two emails; removing the source

| | |
|---|---|
| Question | (a) Do two Send email elements reading one collection both get every file? (b) What does the builder's `removeElement` of the source do to the slots that read it? |
| Gates | AC2 second half (DoD); Q16 (a `removeElement` guard follow-up only if the platform saves a dangling reference green); serialization SQ14 |
| When | EM11a before PR ready (builder-made); EM11b may run before code (1.6.6.125 has `removeElement`) |
| Writes | EM11a: create, run, two activity updates, cleanup. EM11b: one modify attempt (a write if accepted) |

**Steps:**
1. EM11a `UsrSeEm11Fanout` (builder): OF1 (as in EM1, Contact = `PContact`) -> SE1 manual -> SE2 manual -> End, both
   with one entry OF1 `ObjectFiles`. Run with `PContact` = D1. RB-3, RB-4 for SE1's activity. CU-1 with Finished instead of Canceled on SE1's activity, so the flow reaches SE2.
   RB-3, RB-4 for SE2's activity. CU-1 on SE2's activity.
2. EM11b on a disposable process with slots reading OF1 (`UsrSeEm4Run` of EM4a after EM4a, EM13a and EM15 are
   recorded): `modify-business-process` `removeElement` OF1. Record the answer. RB-1.
3. Optional EM11c (after the cut): `setElement` OF1 with another `fileProcessing` group: the refusal names SE1 and SE2
   if the dependency snapshot sees slot references (`PB/Operations/FileProcessingConfigApplier.cs:72-73`) [inferred].

**Expected:**

| Observation | Expected | Evidence | Tag |
|---|---|---|---|
| EM11a | SE1's and SE2's activities each carry n `ActivityFile` rows, separate copies with their own ids | reading a source writes nothing; each copy gets a fresh id (`EmailTemplateUserTask.cs:80-84, 121-129`) | [traced, not measured] |
| EM11b, what the builder does | removes OF1's flows and the mappings whose TARGET is OF1, and nothing else; the slots keep their Script values | `PB/Graph/ProcessGraphBuilder.cs:1882-1919`; no guard in `PB/Operations/ElementOperations.cs:188-203` | [source] |
| EM11b, what the gate does | refuses the save: the slot's token names a parameter of an element that no longer exists, `ParameterNotFound` is raised and `EnsureValidForSave` throws | `TS/Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs:287-297, 580-603, 605-620, 491-501, 785-805`; the feature is on by default (`TS/Terrasoft.Core/GlobalAppSettings.cs:343`); `PB/Validation/ProcessSchemaValidator.cs:56-71` | [traced, not measured] |

Contrary hint: a package comment states that dropped parameters with live readers on a Sub-process element make the
process "save cleanly and refuse to START" (`PB/Contracts/ModifyContracts.cs:502-505`). The basis of that sentence is
not given; it is why EM11b is worth running.

**If not:**

| Outcome | What changes |
|---|---|
| EM11a: SE2 gets fewer files | escalate: an unexpected consumption of the source |
| EM11b saves green | propose the Q16 follow-up (a `removeElement` guard naming the readers) and a describe issue for a dangling slot; then run the process to see whether it refuses to start |

**Cleanup and leftovers:** CU-2 (2 Activities and their files), CU-3 for `UsrSeEm11Fanout`; EM11b's process is
deleted with EM4a's cleanup.

### EM12 Feature off for the executing user (dropped)

**Dropped (Q9 agreed 2026-10-28).** There is no build-time check of `UseProcessEmailAttachments` to observe (design
4.8; the former component C8 and its notice are removed), and a role-level switch-off on a disposable stand is not
worth it for a rollout toggle that is on by default, on for "All employees" and "All external users", re-asserted by
every install and never switched off (decisions Q9; M-02). The KB article keeps one troubleshooting line, "an email
without its files: check feature UseProcessEmailAttachments for the user who runs the process". The facts that line
rests on stay:
- **Silent drop at run time:** with the feature off for the executing user, the message build skips attachments
  (`PD/Schemas/EmailTemplateUserTaskSender/EmailTemplateUserTaskSender.cs:227-229`) and the copy to the activity is
  gated again (`PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:208-211`): an Activity with 0
  `ActivityFile` rows and no error [traced, not measured].
- **Why not per user:** the platform takes the HIGHEST state across the user's roles: the old path orders by
  `FeatureState` descending and takes the top row (`TS/Terrasoft.Core/FeatureToggling/DbFeatureProvider.cs:355-367`),
  the descriptor path takes `MAX` (`:302-307`) [source]. A user-level 0 therefore loses to "All employees" = 1, which
  is how `clio set-feature ... --sys-admin-unit-name <user>` would write it (`CLIO/clio/Command/FeatureCommand.cs:26`).
  The only effective switch is the role rows, which affects every user of the stand (rule 14).
- **A read without a user** answers the feature's `DefaultState`
  (`TS/Terrasoft.Core/FeatureToggling/DbFeatureProvider.cs:810-827`) [source], which is 1 on stand `Creatio` (M-02).

Nothing to run and nothing to clean up.

### EM13 Modify as a new version keeps the slots owned

| | |
|---|---|
| Question | After `modify-business-process-as-new-version`, do both levels of every slot carry the NEW version's UId, and do the files still attach? |
| Gates | serialization SQ13; TC-U-71 / TC-I-08 confidence; a clone re-stamp task only if it fails |
| When | before PR ready on `UsrSeEm4Run` (EM4b); EM13a may run before code on EM4a's process (the clone path exists on 1.6.6.125) |
| Writes | one new version, one run, cleanup |

**Steps:**
1. `modify-business-process-as-new-version` (`CLIO/clio/Command/McpServer/Tools/ProcessDesigner/ModifyProcessAsNewVersionTool.cs:20`)
   on `UsrSeEm4Run` with one harmless operation (for example SE1's Subject to `SEA-EM13-<token>`). Record the new
   version's name (the client prepends the schema prefix, `CLIO/docs/knowledge/platform/process-version-name-gets-the-schema-prefix-prepended.md`).
2. RB-1 of the new version: `A3`, `A4`, `GS5` of both levels per slot.
3. `run-process` with the NEW version's code (run-process runs exactly the version named, `CLIO/clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs:131-152`). RB-3, RB-4.

**Expected:** stamps = the new version's UId on both levels: the clone text-replaces the source UId across the
serialized metadata (`PB/Design/ProcessVersionCloneFactory.cs:110`), and the slot tokens name only element and
parameter UIds. 5 `ActivityFile` rows, as EM4 [traced, not measured].

**If not:** an old UId on the outer slot makes the run time skip it silently (`ProcessUserTaskExtensions.cs:94`): the
clone path needs a re-stamp of attachment slots (new package task), and the guidance warns until it ships.

**Cleanup and leftovers:** CU-1, CU-2, CU-3 for the new version first, then the base.

### EM14 SysFile-root Object source (optional)

| | |
|---|---|
| Question | Does an Object attachments source on an object that keeps its files in `SysFile` feed the email? |
| Gates | TC-U-20 confidence; AC2 for SysFile storage |
| When | optional |
| Writes | D7 seed if needed, build, run, cleanup |

**Steps:** build `UsrSeEm14SysFile`: Object attachments on Account address (a SysFile-stored object,
`CLIO/docs/knowledge/platform/attachment-storage-is-chosen-per-entity-by-the-designer.md`), filter on the D7 record,
use in process -> SE1 manual, one row. Run. RB-3, RB-4.

**Expected:** 1 `ActivityFile` row. The locator names `SysFile` (`PD/Schemas/FileProcessing/FileProcessing.cs:209-224`
per `R/r4-runtime.md:81`), and the email reads it through the file factory with rights disabled
(`EmailTemplateUserTask.cs:77-85`) [inferred: SysFile resolves through the same file factory].

**If not:** a `FileNotFoundByLocatorException` or a missing row means SysFile sources need a refusal or a guidance
exclusion (owner decides).

**Cleanup and leftovers:** CU-1, CU-2 (Activity, `ActivityFile`, and D7 if seeded), CU-3.

### EM15 A report "use in process" file after the process ends

| | |
|---|---|
| Question | In manual mode, is the generated report still readable from `ActivityFile` after the process ends and its temporary files are deleted? |
| Gates | KB-1 (TC-K-06) |
| When | before PR ready; rides on EM4 (b, or a before code) |
| Writes | none beyond EM4's CU-1 |

**Steps:** while EM4's run is parked: RB-7 (1 temp file expected), RB-4 (RF1's row: `Size` and `Bytes`). Then CU-1 on
the activity with Finished. RB-5 (Done), RB-7 again, RB-4 again.

**Expected:** RB-7 goes 1 -> 0 (M-13); RF1's `ActivityFile` row keeps `Size` > 0 and its content, because the copy
was made while the element executed, before it parked (`EmailTemplateUserTask.cs:121-129, 327`;
`TS/Terrasoft.Core.Process/ProcessFileStorage.cs:109-118` deletes only `SysProcessFile` rows) [traced, not measured].
`DATALENGTH(Data)` is meaningful only with database content storage [inferred]; the owner can also open the file from
the activity in the UI.

**If not:** an empty or missing file means manual mode cannot use report temp files: the article must say "save the
report first", and the builder may warn for an RF1 use-in-process source on a manual email (owner decides).

**Cleanup and leftovers:** with EM4.

## 6. Suggested session order

| Session | When | Order | Owner approvals (approx.) |
|---|---|---|---|
| 1 | before P-B2/P-B3 | EM0 rest (read-only) -> EM1 S1-S4 -> optional baselines on designer-built probes: EM4a + EM15, EM8, EM10a/b, EM13a, EM11b -> cleanup of the probes (EM1 stays) | 4 EM1 saves; per probe 1 save + 1-3 runs + cleanup |
| 2 | first experimental cut | install -> EM2 steps 1, 2, 4, 5, 6 -> EM3 (STOP gate) -> cleanup none (twin stays) | install, 4 builder writes, 1 designer save |
| 3 | claimed cut after gate 1 | install -> EM4b + EM15 -> EM4c -> EM5 -> EM8 (if not in session 1) -> EM10 (runs same; step 3 here) -> EM11a -> EM13 -> cleanup (EM6 dropped, Q17) | install, about 15 writes |
| 4 | final cut | install -> EM2 re-run on a fresh twin, every step (describe steps 3 and 7 included) -> EM4b quick re-run -> EM7 on stand `Creatio` once the owner confirms that mail sending works (Q17; it may run in session 3 instead if the owner confirms earlier) -> final cleanup, EM1 after the clio e2e evidence run | install, about 12 writes |

## 7. Leftovers register

Template, filled as each measurement runs. Ids only; no stand address. "Kept" rows say until when.

| EM | Kind | Name or id | Created (date, by) | Removal | Removed (date) | Note |
|---|---|---|---|---|---|---|
| EM1 | process | `UsrSeAttachmentsDesignerFixture` | | CU-3 after the clio e2e evidence run | | kept: AC7 oracle and TC-I-09 fixture; never run |
| EM2 | process | `UsrSeEm2Twin`; the re-run's fresh twin | | CU-3 after PR ready | | kept for EM3 and the final re-run |
| EM3 | designer save | `UsrSeEm2Twin` | | with EM2 | | no new object |
| EM4 | process | `UsrSeEm4Run` (a and b) | | CU-3 | | EM4a also hosts EM11b, EM13a |
| EM4 | run | `<run id>` | | CU-1 | | parked until EM15 is read |
| EM4 | rows | Activity `<id>`; `ActivityFile` x5; `ContactFile` x2 on D4 | | CU-2 | | |
| EM4 | temp | `SysProcessFile` x1 | | deleted by the platform on completion | | check RB-7 = 0 |
| EM4c | processes, rows | `UsrSeEm4cParent`, `UsrSeEm4cCallee`; Activity; `ActivityFile` x2 | | CU-1, CU-2, CU-3 (parent first) | | |
| EM5 | rows | Activity; `ActivityFile` x5; `ContactFile` x2 | | CU-2 | | |
| EM6 | - | dropped (Q17 agreed): no D5 seed, no process | | | | |
| EM7 | process, rows | `UsrSeEm7Send`; EM7c's Activity; `ActivityFile` x3 | | CU-3, CU-2 | | the received mails stay with the owner; the mailbox configuration is the owner's and stays |
| EM8 | process, rows | `UsrSeEm8Single`; Activity; `ActivityFile` x3 | | CU-3, CU-2 | | EM8b's failed run stays in the logs |
| EM9 | process, rows | if run | | | | |
| EM10 | processes, rows | `UsrSeEm10Branch`, `UsrSeEm10After`, `UsrSeEm10AfterB`; 2 Activities | | CU-3, CU-2 | | `UsrSeEm10AfterB` never runs |
| EM11 | process, rows | `UsrSeEm11Fanout`; 2 Activities; `ActivityFile` x2n | | CU-3, CU-2 | | EM11b changes EM4a's process only if accepted |
| EM12 | - | dropped (Q9 agreed): no feature-state change | | | | |
| EM13 | process version | `<new version name>`; Activity; `ActivityFile` x5; `ContactFile` x2 | | CU-3 (version first), CU-2 | | |
| EM14 | seed, process, rows | D7 `SysFile` row if seeded; `UsrSeEm14SysFile`; Activity | | CU-2, CU-3 | | |
| any | run logs | `SysProcessLog` / `SysProcessElementLog` rows | | not deleted | | history of the stand |
| D1/D3 | seeds | uploaded files or a created template, if any | | CU-2 (template); files: owner | | |

## 8. Corrections to the research

| # | Report and claim | What the source says |
|---|---|---|
| 1 | r4 §2 (`R/r4-runtime.md:124-125`) and §1.3 (`:82`): `ProcessFileStorage.DeleteProcessTempFiles` at `ProcessFileStorage.cs:94-103`, `CreateTempFile` at `:19, 109-114` | `DeleteProcessTempFiles` is `:109-118`, `CreateTempFile` `:124-130`, the `SysProcessFile` constant `:23`; `:82-107` are the two `GetTempFiles` overloads (`TS/Terrasoft.Core.Process/ProcessFileStorage.cs`) |
| 2 | r10 §1 AC4 row and §3(a) (`R/r10-critic.md:32, 72`): `EmailUserTaskSenderFactory.cs:12-24`, `EmailUserTaskMessageProviderFactory.cs:13-18` | the switches are `:13-25` and `:13-21` (`PD/Schemas/EmailUserTaskSenderFactory/...`, `PD/Schemas/EmailUserTaskMessageProviderFactory/...`); r4's `:13-26` / `:13-22` are right |
| 3 | r10 S11 (`R/r10-critic.md:184`), r4 P10 (`R/r4-runtime.md:412`): turn `UseProcessEmailAttachments` off "for the executing user only" | not achievable while a role row says 1: the platform takes the highest state across the user's roles (`TS/Terrasoft.Core/FeatureToggling/DbFeatureProvider.cs:302-307, 355-367`). EM12 would have switched the role rows on a disposable stand; it is dropped (Q9 agreed) |
| 4 | r4 P3 (`R/r4-runtime.md:405`), r3 P6: auto + `CreateActivity`, "the copy is visible before the send fails" on a stand without a mailbox | with 0 `MailboxSyncSettings` rows `From` resolves empty (`PD/Schemas/EmailTemplateUserTaskSender/EmailTemplateUserTaskSender.cs:129-148`) and the default-sender path runs, which creates no activity (`PD/Schemas/AutoEmailUserTaskSender/AutoEmailUserTaskSender.cs:105-108`); a mailbox record is needed (r10 S5 has it right; EM7c runs it on the mailbox the owner configures, Q17 agreed). Auto mode also needs a recipient: with none it returns (`IgnoreErrors` on) or throws, but only after it has built the message and read the attachments (`:98-104`) |
| 5 | r8 §7 (`R/r8-testing.md:210-212`): the activity "is findable by a unique subject token [inferred]" | source-confirmed: the activity title is the message subject (`PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:297, 304`; `PD/Schemas/ManualEmailUserTaskSender/ManualEmailUserTaskSender.cs:78-79`), and a non-empty element Subject wins over a template's (`PD/Schemas/EmailTemplateUserTaskMessageProvider/EmailTemplateUserTaskMessageProvider.cs:157-158`) |
| 6 | r10 G6 (`R/r10-critic.md:211-215`): an unset single File fails "inside manual activity creation" | manual mode builds the message BEFORE it creates the activity (`ManualEmailUserTaskSender.cs:56-60`), so the NullReferenceException leaves no Activity behind (EM8b expects none) |
| 7 | r10 S10 (`R/r10-critic.md:183`): whether the pre-save gate refuses the dangling Script after `removeElement` "is traced only" | the trace holds (`TS/Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs:287-297, 434-464, 605-620`), but a package comment reports an analogous Sub-process case that saved green (`PB/Contracts/ModifyContracts.cs:502-505`); EM11b keeps both outcomes. The method spans `PB/Graph/ProcessGraphBuilder.cs:1882-1919` (decisions Q16 and r2 cite `:1882-1917`/`:1918`), and `PB/Operations/ElementOperations.cs:188-203` adds no guard either |
| 8 | r8 §7 (`R/r8-testing.md:216`): sequential writes at `ProcessFileElementToolE2ETests.cs:38-40` | the sentence is at `CLIO/clio.mcp.e2e/ProcessFileElementToolE2ETests.cs:41-42` |
| 9 | r8 §7 cleanup and the test plan's TC-I-10 fallback: `run-process` with `cleanup: true` | that route deletes with OData DELETE (`CLIO/clio/Command/RuntimeVerification/RecordDeletionService.cs:14-24, 144-152`), which stand `Creatio` answers with 405 (M-16); it is not a fallback on this stand |
| 10 | r10 S5 and decisions Q17 imply the Create activity probe (then EM6, now EM7c) can be built by the package | the `email` block has no `CreateActivity` member (`CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:185-188`); EM7c builds the slots with the builder and sets `CreateActivity` by a raw constant mapping or in the designer (decisions Q17) [inferred, not tried] |
| 11 | computed task text: "modify-as-new-version" | the MCP tool is `modify-business-process-as-new-version` (`CLIO/clio/Command/McpServer/Tools/ProcessDesigner/ModifyProcessAsNewVersionTool.cs:20`) |
| 12 | r4 open question 4: whether the file-storage copy to `ActivityFile` sets `TypeId` | it does: the file API stores `TypeId` = File on every new file (`TS/Terrasoft.File/Metadata/EntityFileMetadataStorage.cs:27, 232-237`), and base columns are never copied from the source (`:28-38, 112-121`; `TS/Terrasoft.File/File.cs:172-185`); EM4 now expects File on both paths |
