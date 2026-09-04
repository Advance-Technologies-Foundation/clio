# ENG-95891 — runbook, indexed by symptom

The other sixteen documents in this folder are organised by DECISION: why the validator agrees with the
platform's declared type, why the shape guard was removed, what the corpus measurements were. That is the
right shape for someone changing the feature and the wrong shape for someone paged about it.

This one is indexed by what you SEE. Every entry names the check, the file, and what the answer means.

---

## "The agent says it set a condition and the branch is not taken"

Three different causes, in the order they are worth checking.

**1. The branch is decided by the activity's RESULT, not by the formula.**

`ProcessSchemaConditionalFlow.CreateSequenceFlowElement` reads `ProcessActivitiesSelectedResults` first and
only falls back to `ConditionExpression` when that map is empty. With a result selected, the condition text is
stored, reported, and never evaluated.

*Check:* `describe-business-process` and read `flows[].branchesOnActivityResult`. `true` means this is it.

*If the field is absent* the environment is on a package older than the one that added it — that is itself the
answer for a stale environment, and `list-packages -e <env> | grep cliogate` plus the convergence refusal will
say so.

*Scale:* 337 of the 1 522 conditional flows in the shipped 7.8.0 corpus carry a populated map, across 150
schemas. This is the most likely cause on designer-authored content.

*Fix:* clear the result selection on that connector in the process designer, then set the condition. clio
refuses to write one while the selection stands, so a NEW condition cannot land in this state — only one
written before that refusal shipped, or one written through the designer.

**2. The condition names a parameter by NAME rather than by UId.**

`[#Price#]`, `[#Process parameters.Price#]`, or a typo in a real family such as `[#SysSettingz.Foo#]`, is read
as an unrecognised macro FAMILY. The whole engine layer is then skipped, and for a flow condition nothing
downstream catches it: a sequence flow is not a parametrized element, so the platform's pre-save gate never
walks it.

*Check:* read the condition text out of `describe`. If it contains a `[#…#]` whose body is not
`[Parameter:{<36 chars>}]` or `[Element:{…}].[Parameter:{…}]`, this is it.

*Fix:* rebuild the token from `describe`'s `uid`. A new condition in this shape is refused now, so again this
is either pre-refusal content or designer-authored.

**3. Sibling precedence.** Two conditional flows leave the same element and the first true one wins.
Precedence is nothing but the order of `schema.FlowElements` — it is recorded in no field.

*Check:* `describe` reports flows in stored order. If an earlier sibling's condition is also true for the case
you are testing, it takes the branch.

*Fix:* the order can only be changed by removing and re-adding the siblings in the order you want. See
`docs/knowledge/ProcessModel/conditional-flow-rekind-must-be-in-place.md` — `removeFlow` + `addFlow` appends,
so it moves the flow to LAST and silently changes which branch runs.

---

## "The same formula is accepted on one environment and refused on another"

**A missing system setting.** `[#SysSettings.X#]` conversion asks the platform for the setting's value TYPE,
which throws for an unknown code. The validator catches broadly, raises a notice, and returns BEFORE the engine
layer — so the formula is stored with its references checked and its result type unchecked. On an environment
where the setting exists, the same text is fully validated and may be refused there.

*Check:* the `Warning` entries in `execution-log-messages`. The notice says "could not be fully checked … its
result type was not". Then `query-sys-settings -e <env>` for the code.

**A platform feature flag.** `GlobalAppSettings.FeatureUseTypeCastExpressionValidationInProcess` decides
whether the type-conversion map is applied at all. With it off, accept/reject changes. Nothing in the package,
the tool descriptions or the guidance mentions it — it is the cleanest "works on my stand" generator in this
feature.

*Check:* the flag's value in the target environment's configuration.

**A saturated worker.** Every metapath regex carries a 100 ms match timeout and a timeout becomes a hard
refusal whose message blames the formula ("Simplify it, or shorten the references in it"). Under load this is a
false refusal that looks like a real one. If the same text passes on a retry, that was it.

---

## "A worker crashed and there is nothing in the application log"

A formula deep enough to exhaust the parser's stack. The engine parses by recursive descent and
`StackOverflowException` cannot be caught in .NET: the worker dies, `finally` never runs so the design session
is not released, and nothing reaches the application log — only a host-level crash record.

This is a PLATFORM defect, reachable identically from the visual designer through three server doors, and a
guard against it was deliberately written and then removed from this package. Full analysis, including the
measured inflation curve and why a client-side guard cannot work, is in
`docs/knowledge/platform/formula-depth-crash-is-reachable-from-the-designer-too.md`.

*Check:* whether a formula was being saved at the time. Nothing else links the crash to this feature.

---

## "`create-business-process` says a process already exists, but the user never created one"

A previous create failed and its rollback failed too. `ProcessBuildHandler` composes the message "the draft has
been rolled back" BEFORE calling `Rollback`, and `ProcessSchemaRepository.Rollback` swallows failure into a
`_log.Warn`. So a failed rollback reports a successful one and leaves an orphan schema behind.

*Check:* the Creatio application log for the `Warn` from `Rollback`, around the time of the earlier failure.

*Fix:* delete the orphan schema in the designer. There is no API route.

---

## "The modify failed and I cannot tell which operation"

Read `appliedOperations` in the response — it is reported on failure as well as on success, and it is the count
that applied BEFORE the one that refused. So operation `appliedOperations + 1` is the culprit.

Several structural guards name neither endpoint of the flow they refuse ("The flow between these elements …"),
which is why that count matters. If it is absent the environment predates the change that reports it.

---

## "Did the failed modify leave the process half-changed?"

No, and this is worth knowing rather than guessing. Every operation runs against the schema IN MEMORY; there is
one save, at the end, after layout and the platform's pre-save validation. If any operation throws, nothing was
persisted — `grep -rE "new (Insert|Update|Delete)" packages/` in the package repository returns zero, so no
operation reaches the database on its own.

The residue is a design session that stays checked out if its release also failed (a `Warn` in the application
log), which blocks the next edit of that schema until it times out.

---

## Things that are NOT causes, so you can rule them out fast

- **A formula's SHAPE.** Deep nesting, long operator runs and long conditional chains are accepted deliberately
  — the visual designer accepts them too. If someone tells you clio refused a formula for being too complex,
  they are on a package from before that guard was removed.
- **A stale bundled package on the machine running clio.** An install resolves the archive from the BUILD
  OUTPUT directory, not the repository, so a Debug build can ship an older package than the one committed.
  `clio.tests/Common/BundledProcessBuilderPackageTests.cs` pins what should be there.
- **The 2048-character formula cap and the 256 KB per-request budget.** Both refuse loudly and name themselves.
  If the message does not mention a character count, this is not it.
