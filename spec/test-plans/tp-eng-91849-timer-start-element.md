# Test plan: Timer start element (ENG-91849), clio side

**Spec**: [spec-eng-91849-timer-start-element.md](../prd/spec-eng-91849-timer-start-element.md)
**ADR**: [adr-eng-91849-timer-start-element.md](../adr/adr-eng-91849-timer-start-element.md)

The package's own unit tests (create per kind and variant against the designer captures, the default zone, the
validation rules, the normalizer, the describe decode and round trip, `setTimer`, the scheduler read, the version
re-sync, the message-start handler fix) live in the crt-process-builder PR; the guide's tests live in the
clio-knowledge PR. This plan covers clio.

## Unit (clio.tests)

Run: `dotnet test clio.tests/clio.tests.csproj -f net10.0 --filter "TestCategory=Unit&(Module=McpServer|Module=ProcessModel|Module=Command|Module=Common)"`

| ID | What | Test |
|---|---|---|
| TC-U-01 | `timerStart` and `timerstart` resolve to the timer start event type, never UNKNOWN; the designer's existing data id keeps resolving | `ManagerMapResolveDataIdTests` |
| TC-U-02 | The `timer` block passes the key check on create, `addElement` and `setTimer` | `ProcessBlockKeyCheckTests.Check_Should_AcceptTheTimerBlock_OnEveryRoute` |
| TC-U-03 | An unknown `timer` key is refused with its path and the block's accepted keys | `ProcessBlockKeyCheckTests.Check_Should_RefuseAnUnknownTimerKey_NamingItsPath` |
| TC-U-04 | An unknown key inside `timer.weekday` is refused under its own label | `ProcessBlockKeyCheckTests.Check_Should_RefuseAnUnknownKeyInsideTheTimerWeekday` |
| TC-U-05 | A described block replays: `storedCron`, `designerKind` and `scheduled` are tolerated read-backs, on `addElement` and on the `setTimer` route (asserted by its own path) | `ProcessBlockKeyCheckTests.Check_Should_TolerateTheReadBackFields_OfADescribedTimerBlock` |
| TC-U-06 | `setTimer`'s `timer` payload is walked like the create block (op matched case-insensitively); `setElement` with `timer` is refused with the "use setTimer" pointer. clio checks no operation-level keys: an extra key beside `op`, `elementName` and `timer` is refused by the SERVER (`ExtensibleRequestContract` on the operation), not here | `ProcessBlockKeyCheckTests.Check_Should_WalkTheSetTimerOp`, `Check_Should_PointSetElementTimerToSetTimer` |
| TC-U-06b | A timer shape the server's serializer cannot read is refused before sending, on create, `addElement` and `setTimer`: `timer` not one object, `weekday` not an object, `daysOfWeek` not a list of strings (a non-string entry the server reads but refuses by its day-name rule), `every`/`fromHour`/`toHour`/`month` neither a whole Int32 nor a string holding one, `dayOfMonth` neither a whole number nor a string, a list or object in `schedule`/`at`/`cron`/`start`/`end`/`timeZone`/`workday`; the readable shapes (numeric or string `dayOfMonth`, a quoted whole number such as `"9"` in an Int32 member, nulls) pass | `ProcessBlockKeyCheckTests.Check_Should_RefuseATimerShapeTheServerCannotRead`, `Check_Should_RefuseATimerShape_OnTheSetTimerOp`, `Check_Should_AcceptTheTimerShapesTheServerReads` |
| TC-U-07 | The key sets equal the bundled contracts; the read-backs are describe-only members | `ProcessBlockKeyCheckTests.KeySets_Should_EqualTheMembersOfTheBundledServerContracts`, `ReadBackFields_Should_BeDescribeOnlyMembers` |
| TC-U-08 | The create call shape names `timer (timerStart)` and the modify short form names `setTimer`; both match the bundled contracts | `ToolContractShortFormTests` |
| TC-U-09 | Describe reads `timer` with `scheduled`, keeps unknown members in the bags, leaves it null when absent | `ServerProcessDescriberTests.Describe_ShouldReadTheTimerMember_WhenServerReportsIt`, `Describe_ShouldKeepUnknownTimerMembers_InTheBag`, `Describe_ShouldLeaveTheTimerMemberNull_WhenServerOmitsIt` |
| TC-U-10 | The summary digest lists timer starts in `startTimers[]` with the stored cron as `storedCron` (never `cron`), over EVERY element even when the budget cut drops the timer from `elements[]`; a process without a timer start has no `startTimers` | `ProcessDigestTests.BuildSummary_ShouldListTimerStarts`, `BuildSummary_ShouldListEveryTimerStart_WhenTheBudgetCutDropsItsElement`, `BuildSummary_ShouldOmitStartTimers_WhenTheProcessHasNoTimerStart` |
| TC-U-11 | The checksum and the write digest's change list are unchanged when only `timer.scheduled` differs, and change when the schedule differs | `ProcessDigestTests.ChecksumAndChanges_ShouldIgnoreTheTimerScheduleState_AndTrackTheSchedule` (the checksum, and the change list through `ProcessDigest.BuildChanges`, which the write digest calls) |
| TC-U-12 | Start detection: a `timerstart` element counts as a start; an element with a null `buildType` and a message-start runtime `Type` counts as a start; a null `buildType` with a non-start `Type` does not | `ProcessParameterBindingWarningsTests` |
| TC-U-13 | create contract and prompt: the type, the block, the confirm-before-save sentence (a timer runs unattended); no schedule vocabulary member (`daysOfWeek`, `repeatOnMisfire`, `fromHour`, `toHour`, `dayOfMonth`, `workday`, `weekday`) in the contract. **Merge gate:** the `process-timer-start` guide pointer, asserted here, lands only with the clio-knowledge release that publishes the guide | `CreateBusinessProcessToolTests` |
| TC-U-14 | modify contract and prompt: `addElement` type list, `setTimer`, the `setElement` pointer, the confirm-before-save sentence, the kind-change rule in the prompt, no schedule vocabulary. **Merge gate:** the guide pointer, as in TC-U-13 | `ModifyBusinessProcessToolTests` |
| TC-U-14b | modify-business-process-as-new-version: the description says a FAILED save on a family with timer starts re-registers the active version's timers and a past-start timer may run once; the tool stays `Destructive=false` | `ModifyProcessAsNewVersionToolTests.ModifyProcessAsNewVersion_Description_ShouldStateTheFailedSaveTimerEffect` |
| TC-U-15 | describe contract and prompt: the `timer` read-back clause with `storedCron`, `designerKind`, `scheduled`; no schedule vocabulary in the contract | `DescribeProcessToolTests` |
| TC-U-16 | validate-process-graph contract and prompt name `timerStart` as buildable; timer starts leave the not-buildable sentence (message starts and intermediate events stay) | `ValidateProcessGraphToolTests` |
| TC-U-17 | Per-contract ceiling and wire bundle hold, no ceiling raise; the swaps are named in the test comment | `ToolContractPayloadBudgetTests` |
| TC-U-18 | Floor literals name the PR's final cut | `ProcessDesignerRequiresPackageAttributeTests` (with the rebundle) |
| TC-U-19 | Archive pins (SHA, version, stamps including the hand-set `ExpectedSchemaDescriptorModifiedOnUtc`, producing commit, security counts) | `BundledProcessBuilderPackageTests` |
| TC-U-20 | The bundled archive declares every timer wire name clio sends (a numeric floor alone cannot prove the block is there), and the describe DTOs' own `[JsonPropertyName]`s (`DescribedTimer`, `DescribedTimerWeekday`, `DescribedTimerSchedule`, read by reflection) equal the server's `DataMember` names | `BundledProcessBuilderPackageTests.BundledArchive_ShouldDeclareTheTimerStartWireNamesClioReads` |

The guidance-name oracle `UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` checks that the
contracts point only at published guidance; `curated-knowledge-names.json` names `process-timer-start` after the
re-pin.

**Merge gate (not a follow-up).** The `get-guidance name=process-timer-start` pointer in the create and modify
contracts (TC-U-13, TC-U-14) and the `curated-knowledge-names.json` re-pin wait for the clio-knowledge release that
publishes the guide; the PR does not leave draft or merge before both are in. The bytes are reserved:
`ToolContractPayloadBudgetTests` names the swaps that paid for the confirm-before-save sentence and the headroom kept
for the pointer (create and modify; describe's pointer needs a swap of its own).

## Integration (clio.mcp.e2e, category `McpE2E.ProcessDesigner`)

Files: `CreateBusinessProcessToolE2ETests.cs`, `ModifyBusinessProcessToolE2ETests.cs`,
`ValidateProcessGraphToolE2ETests.cs`.

**Far-future window rule.** Every e2e timer gets a far-future window: `start` 2090-01-01T00:00 for a recurring kind, a
2090 instant for `once`. It is inside the package's never-fires horizon (it gets the "first run more than a year away"
notice), so the job exists and `scheduled.nextFireTimeUtc` can be asserted, while no run can happen even if cleanup is
lost. Every process a case creates is deleted in a finally block. A case that needs a past or near date is a refusal
case and creates nothing. On a stand whose scheduler read reports `schedulerUnavailable` the scheduler assertion
Ignores the case with that reason (it runs last in each case, after the shape checks).

| ID | What |
|---|---|
| TC-I-01 | Build a `day` timer -> describe in the write shape -> `scheduled.nextFireTimeUtc` set and in 2090; the post-save notice says scheduled |
| TC-I-02 | Build `week`, `month` (by `weekday`), `month` with a NUMERIC `dayOfMonth: 15` (reads back as the string `"15"`), `cron` and `once` and read each back in its write shape (`storedCron` present, `cron` only for `schedule: cron`) |
| TC-I-03 | describe -> rebuild round trip: the described block, copied into a new process, reads back the same, and the server's "Read-only fields copied from describe were ignored" warning names the read-backs |
| TC-I-04 | Add a `timerStart` next to an existing Simple start (`addElement` + `addFlow`); both starts read back |
| TC-I-05 | Replace the Simple start with a timer (`removeElement` + `addElement` + `addFlow`); the process has one timer start |
| TC-I-06 | `setTimer` changes the schedule in place: same element UId and name, new `storedCron`; `setElement` with `timer` is refused with the pointer |
| TC-I-07 | `timeZone` omitted: `timer.timeZone` equals the e2e login's OWN `SysAdminUnit.TimeZoneId` (row selected by the registered login, Code compared case-insensitively), else the Code of the `TimeZone` the All-Users `DefaultTimeZone` value points at (`SysSettingsValue` of "All employees", not the caller's effective value), and the notice names the source; with neither set, the Q2 refusal |
| TC-I-08 | validate-process-graph accepts a `timerStart` element, no UNKNOWN finding |
| TC-I-09 | `run-process` on a timer-only process is refused by the platform's manual-start check: status `not-started`, the error carries the `ProcessCannotBeManuallyStartedException` explanation ("has no manual entry point"), no `processId`. Any other launch failure fails the case. To be measured on a stand next |
| TC-I-10 | Refusals that create nothing: a past `once`, Feb 30, `week` + `every: 2`, `day` + `every: 14`, `cron` `0 0 8 */14 * ? *` (the last three with the Q12 reason) |

## Manual (Jira; TC-0X blocks go to Jira after approval)

- TC-01 Build a daily timer process and see the next run in describe.
- TC-02 Build each kind once and open it in the designer.
- TC-03 Add a timer start next to an existing Simple start; asked to replace it instead, the agent describes the
  process, queries `SysSubProcessInProcess` for sub-process callers, asks the user about manual starts and asks for
  confirmation first.
- TC-04 Change a schedule in place with setTimer.
- TC-05 A once timer in the past is refused.
- TC-06 An invalid cron is refused with the field named.
- TC-07 An unknown time zone is refused.
- TC-08 A schedule that never fires (Feb 30) is refused.
- TC-09 Disabled process: saved, the notice says not scheduled.
- TC-10 A timer fires and the run completes as the System operations user.
- TC-11 Describe of a designer-built timer no longer says startevent, and a describe -> rebuild keeps the schedule; a
  designer Day timer every 14 days is described and refused on rebuild with the Q12 reason.
- TC-12 "Every N weeks" is refused with the reason, as `week` + `every`, as `day` + `every: 14` and as `cron`
  `0 0 8 */14 * ? *`.
- TC-13 Time zone omitted: the notice names the zone and its source; with neither a profile zone nor `DefaultTimeZone`
  the request is refused.
- TC-14 Before any action that leaves a new or changed timer in the enabled, active version, the agent states the
  schedule, zone, first run, run-as user and the data the process changes, and waits for a yes; a caption edit of a
  process whose timer is already scheduled gets no schedule confirmation.
- TC-15 Describe of a message-start process reports no `buildType`, and a describe -> create clone no longer turns it
  into a Simple start (Q7).
