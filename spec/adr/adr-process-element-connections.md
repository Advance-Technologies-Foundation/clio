# ADR: Activity connections ("Connected to") for process elements

**Status**: Proposed
**Author**: Architect (session 564600f6)
**Analysis**: [process-element-connections-plan.md](../process-element-connections/process-element-connections-plan.md)
**Created**: 2026-08-11
**Repos affected**: `clio` (MCP surface, guidance, docs), `ProcessBuilder` (`CrtProcessBuilder` package)

---

## Context

The process designer's **"Connected to"** block lets a process element bind the Activity it creates to
other records — Account, Contact, Opportunity, and so on. `CrtProcessBuilder` builds processes from a
declarative description but has no notion of these connections: an agent can create a Perform task and
cannot say which records the resulting Activity should point at.

Three findings from the analysis frame every decision below.

**1. The set is computed, not stored.** "Connected to" is
*(rows in the `EntityConnection` registry for the host entity)* ⋈ *(columns that exist on the compiled
host object)*, plus `Project` when that column exists. The registry is keyed by
`(SysEntitySchemaUId, ColumnUId, Position)` and ships rows for **six** root schemas — Activity
(`c449d832-a4cc-4b01-b9d5-8a12c42a9f89`), Call, Document, Invoice, Order, Bonus. A registered row whose
column does not exist is invisible; a column with no row is written at runtime but invisible in the
designer and ignored by the features that read the registry (record-page detail, Next Steps, email
auto-relations, quick-add).

**2. The write already works.** Measured end to end on `krestov-test` (plan §5.1): a process built by
the package, `addMapping { elementName, elementParameter: "Account", processParameter }`, then a run —
the created Activity carries `AccountId` = the mapped value while the unmapped `ContactId` stays
`NULL`. The persisted shape is `Source = Script` plus a metapath, byte-equivalent to what the designer
produces. **So this feature is not about capability. It is about enforcement, ergonomics, and read-back.**

**3. Every gap produces the same failure class.** A connection can be persisted, compile, run green and
write nothing — an unregistered column, a `CreateActivity` left at its `false` default, a `CallUserTask`
whose runtime bypasses both write channels, an `expression` of the wrong macro family (stored verbatim
with no type check). Silent inertness, not errors, is the hazard this design is built against.

---

## Decisions

### D1 — A dedicated connections contract, minimal

Two `modify` operations (`setConnections`, `clearConnections`) plus a catalog and a `describe`
projection, shipped as **one** delivery.

*Rationale.* The Activity-specific knowledge — which columns are connections **per element** (static
sets differ per user task), the allow-list, the `CreateActivity` precondition, the deprecation rules,
the three-state diagnosis, the value-dialect type rules — has to live somewhere. The only alternatives
were "unenforced, inside the guidance article" or "smeared into the general-purpose `addMapping`", i.e.
a general operation acquiring domain knowledge about `Activity`. Second, a validating operation converts
the silent-inert failures above into refusals with a reason.

*Non-goals, recorded so they are not revisited:* no bind/create operation split (see D6); no
`clearConnections` that removes parameters (removal is unsafe — a parameter may be referenced by other
mappings); no attempt to reproduce designer visibility of **unbound** rows; and **`addMapping` is not
deprecated** — it remains the general primitive, with guidance naming the preferred path.

#### D1a — `setConnections` is an upsert keyed on `column`, not a collection replace

Each item is `{ column, <exactly one source> }`. Columns present in the request are set or re-set;
columns **absent from the request are left alone**. Clearing is only ever explicit, through
`clearConnections`.

*Rationale, from precedent rather than taste.* Every existing member of the operation family —
`setElement`, `setParameter`, `setFilter`, `addMapping` — mutates the named target and leaves its
siblings untouched; `addMapping` in particular already overwrites per target rather than accumulating.
A replace-the-collection reading of `set*` would be unique in the family. It would also be the same
class of hazard this whole design fights: a caller who sends only `Account` would silently **clear**
`Contact`, and because a cleared connection is `Source = None` and `describe` filters those out, the
damage is invisible in the very artefact meant to verify it. Replace semantics would additionally force
a `describe` before every edit.

**Changing an existing connection is therefore the same call with a new source**, including across
dialects — a connection currently bound to a process parameter is re-pointed at a fixed record by
sending `recordId` for that column. The mechanism is the platform's own: `ProcessSchemaParameter`'s
`SourceValue` setter performs a schema-wide `Mappings.FindByTargetUId(UId)` and replaces the existing
mapping, so no duplicate accumulates. Combined with find-or-reuse (T-1) the operation is idempotent:
re-sending an unchanged request rewrites the same shape.

Two reporting requirements follow. `clearConnections` must state in its result that it cleared a
binding, because "cleared" and "never bound" are indistinguishable in `describe` afterwards. And
re-setting a connection whose column is **unregistered** must still return the state-(2) warning
(§4.4) — a caller editing an already-invisible connection would otherwise read success as a fix.

#### D1b — Deleting means *unbind*, never *remove the parameter*

`clearConnections` sets `Source = None` and leaves the element parameter in place. Removing the parameter
is deliberately not offered.

*Rationale.* For a **static** connection the parameter is not ours to delete: the platform created it when
`SchemaUId` was set, it belongs to the user task's contract, and `SynchronizeParameters` /
`FillNewSchemaParameters` would re-create it — so deletion is a no-op at best and a desync at worst. For
**any** parameter, another mapping may target it: `SourceValue`'s setter resolves mappings schema-wide via
`FindByTargetUId`, so removing the parameter can leave a dangling target. Only a parameter this feature
created is genuinely ours, and even there deletion risks breaking generated code that references the
property. Unbinding is safe and reversible; removal is neither, and the caller's actual intent — "stop
writing this column" — is fully served by unbinding.

Three consequences to pin with tests. Clearing an already-unbound column is an **idempotent no-op, not an
error**. After clearing, the column **must not be written at runtime** — worth an explicit test rather than
an assumption, because the value carried `ModifiedInSchemaUId` stamped to the process schema and the codegen
`isOverride` condition keys on exactly that; whether the generated property disappears or is emitted empty
is an implementation detail, but "not written" is the required outcome (channel A already skips an empty
Guid before `SetColumnValue`). And removing the **element** is a different operation — `removeElement` takes
its connections with it, which is the only path that legitimately makes a connection parameter go away.

### D2 — Parameterise the host entity as an internal seam only

The code takes a host entity (default `Activity`); the **wire format gains nothing** — the field is
`connections`, with no host member.

*Rationale.* Three of the four layers are already generic: the registry is keyed by
`SysEntitySchemaUId`, the tag is derived (`BaseProcessUserTaskUtilities.cs:89` —
`$"{column.ParentSchema.Name}Connection"`), and the runtime resolves columns against `entity.Schema`.
Only the *consumer* layer hard-codes Activity — a narrowing the process designer invented, which the
classic-UI mixin, the CTI panel (host = `Call`) and the Freedom UI service all avoid. But there is **no
known second host** for this feature: every connection-capable user task creates an Activity. So the
generality is not paid for; what D2 buys is that adding a host later is **additive** (an optional
member defaulting to Activity) rather than a breaking rename.

*Binding consequences:* field named `connections`, never `activityConnections` (the former stays correct
once a host exists); tag always derived, never the literal `"ActivityConnection"` — doubly bad because
`Activity` has a column of exactly that name; classes `EntityConnection*`, not `ActivityConnection*`;
the Activity root UId kept as a **named default**, not an inline literal.

### D3 — Refuse when connections would not take effect; the rule lives in `ConnectionCapability`

`setConnections` refuses when the element's effectiveness rule fails, rather than silently setting
`CreateActivity` itself.

*Rationale.* `modify-business-process` takes an **ordered array**, so a refusal costs the caller one
array element — `[{setParameter CreateActivity=true}, {setConnections …}]` — not a round trip, which
removes implicit-set's only real advantage. And turning `CreateActivity` on is a **visible product
change**: an extra Activity per send, appearing in the Activities section, in connected records'
timelines, and in the email "processed" criterion. An operation named `setConnections` must not opt a
user into that, nor mutate a parameter outside its own name.

The rule itself lives next to the allow-list so element knowledge stays in one place:

| User task | Connections take effect when |
|---|---|
| `ActivityUserTask` | **always** — it has no `CreateActivity` parameter |
| `EmailTemplateUserTask` | `CreateActivity == true` **or** the send mode is *manual* — `ManualEmailUserTaskSender.cs:56-69` has no gate, unlike `AutoEmailUserTaskSender.cs:112` |
| `AutoGeneratedPage`, `PreconfiguredPage`, `UserQuestion`, `OpenEditPage` | `CreateActivity == true` |

The trigger includes the **`false` schema default** — that default produced the live inert artifact that
motivated this work.

### D6 — The package does not create the `Activity` column

`setConnections` refuses when the column does not exist. Registration lives outside `CrtProcessBuilder`.

*Rationale.* A process-schema edit is scoped to one schema, idempotent and reversible. Adding a column
to `Activity` creates a replacing schema in a package, needs `SaveSchemaDBStructure`, a declared package
dependency the auto-applier (`internal`) cannot supply, a product-wide registry row, and can break
`Activity` for the **whole environment** through a codegen `ValidateException` on a same-name collision.
Different scope, different reversibility, different privilege — a different bounded context, and the
package's stated purpose is the former. The decisive practical signal that the boundary was drawn wrong:
writing bound data requires a **non-foreign** target package, so the capability would exist only on some
environments — something a process-design operation must never depend on.

*Where registration lives instead.* Almost nothing needs to be built, because clio already ships the
pieces: **`update-entity-schema`** adds the lookup column (its own description: *"Applies a batch of
**add**, modify, and remove column operations to a remote Creatio entity schema"*; the operation model
carries `ReferenceSchemaName`, and it publishes and rebuilds OData without a compile);
**`add-package-dependency`** declares the dependency on `CrtCoreBase`; **`create-data-binding`** +
**`add-data-binding-row`** create the registry row and its package binding. The artefact this analysis called the **only genuinely
missing one** was cache invalidation — `ProcessUserTaskSchemaManager.reset` clears the server contract cache
and the ESQ cache, without which the designer shows the old list even after a compile. **Re-scoped during
story 5:** the runtime write channel matches parameters to columns by UId-then-name and never opens
`EntityConnection`, so the reset is designer ergonomics, not correctness — and the code that did ship is the
cross-package name pre-check instead. Its home is
cliogate (a privileged endpoint with `CheckCanManageSolution` first) or a thin clio tool.

### D7 — One authorization gate

`CanManageProcessDesign` only, unconditional. A direct consequence of D6: the package never mutates the
data model, so there is no second privilege level and no gate whose contract depends on request content.

### D8 — The stale-package detector is the convergence rule, not a new mechanism

An environment whose `CrtProcessBuilder` predates this feature is caught by
**`IBundledPackageConvergence`** — the existing rule that refuses when the environment carries an older
version of a bundled package than the running clio distribution ships. No second mechanism, and no version
literal on `[RequiresPackage]`.

*Why a detector is required at all.* Measured: a request carrying a future-shaped `connections` array is
answered **normally** by an older package, with the member silently ignored — no contract implements
`IExtensibleDataObject`, checked across the package's `[DataContract]` types (25 when measured, 27 after this feature), so the serializer drops unknown members
at every nesting level. An old package plus a new field is therefore a green log and a wrong process, which is
the worst outcome this whole design is built against.

*Why convergence rather than the other two options.* A version literal on the attribute (option ii) was
rejected twice over: the attribute states what the CODE needs, convergence states what environments should be
brought to, and the two can legitimately disagree — merging them makes both unsayable separately. The pin test
asserts the **absence** of a version literal on all five process-designer gates for exactly that reason, and
`spec/adr/adr-bundled-package-version-source-of-truth.md` records why a version constant in this assembly is a
claim about bytes it no longer describes. Making the package reject unknown members (option iii) would put the
detector in the component that is by definition the *old* one on a stale environment — it cannot report a
field it was built before.

*What arms it.* The rebundle's `-Version` bump, which is already mandatory on every rebundle. Convergence
compares the archive's own descriptor version against what the environment recorded, so raising the shipped
version is what turns every gated call on a stale environment into a refusal naming `install-process-builder`.

*Residual the write path does NOT close: a system-setting expression.* `EnsureCompatibleMacro` refuses the
four typed-constant families that provably cannot hold a record reference, but a `[#SysSettings...#]`
expression is **accepted with a warning** rather than refused — reading a setting's value type at design time
is a capability the package does not have. So the failure mode the analysis named (a text setting bound to a
lookup connection, leaving the column empty at run time) is made LOUD, not closed. Closing it needs a
`SysSettings` value-type read; until then the warning is the whole guard, and both shipped tool descriptions
name it.

*What it does not cover, stated so it is not mistaken for complete.* An environment at or ahead of the
bundled version passes, as it must — so a hand-installed package that is *newer by version* but built before
this feature would not be caught. That is not reachable through any shipped path (the archive is the only
thing clio installs) and no cheap check distinguishes it, so it is accepted rather than guarded.

That exact case then occurred, on the feature's own verification stand, and it is worth recording because
what it produced is *not* the silent failure this design exists to prevent. `krestov-test` carried a
hand-built `1.1.0.1` — a higher version than the bundled `1.1.0.0`, with pre-feature code — so convergence
correctly saw no regression and the call went through. The answer was
`Operation 'setConnections' is not supported. Supported: addElement, …, setElement`: a refusal from the
package's own operation dispatcher, which enumerates what it does support. So the uncovered case degrades to
a LOUD error, and the reason is a real asymmetry worth keeping in mind — an unknown **member** of a known
contract is dropped in silence (T-10, the premise of this whole decision), while an unknown **operation
name** is rejected by name, because the executor dispatches on the `op` token and answers an unknown one by
enumerating the tokens it does have. So a capability delivered as a new OP TOKEN is self-diagnosing even
where the detector is blind — which is what these two operations are. That is NOT a general property of the
write path: a future capability delivered as a new MEMBER on an existing op would be dropped exactly as
silently as the read path, since `ProcessOperationDescriptor` is a flat `[DataContract]` like every other.
The read path is already in that position — a stale package returns a descriptor with no `connections` array
at all, which is indistinguishable from "nothing is bound". Fixing it by hand is
`install-process-builder --force`, whose `--force` exists for precisely this backwards move.

Two further gaps, named because a reader would otherwise assume the detector is unconditional. Convergence
declines to DECIDE — warning and allowing — when clio's own archive cannot be read or declares a
pre-release suffix, so a defective distribution disarms it by design: blocking there would turn clio's defect
into the user's, and the install command refuses such a distribution separately. And a rebundle must never
reuse a version: the rule is "the environment records an OLDER version than the archive carries", so re-cutting
changed sources under an unchanged version leaves every environment already on it comparing as converged, with
the `connections` array silently dropped and nothing red anywhere. The rebundle script refuses that; the
pre-release carve-out its docs describe applied only before the package had shipped and is now void.

### D9 — One deprecation predicate, read with a metadata fallback

`deprecated ⟺ usageType == None ∥ name ∈ {SendEmailUserTask, EmailUserTask}`.

Both disjuncts are mandatory, measured: `CallUserTask` carries `FK2 = 0` (`None`) and is caught by data;
`SendEmailUserTask` has **no `FK2` at all** (so it defaults to `Advanced`) and `EmailUserTask` is
`General` — neither is detectable from `UsageType`, so the name literal is a necessity, and it mirrors
the platform's own list rather than inventing one. The two-disjunct form is **complete, not a
simplification**: the client's third signal `element.isDeprecated` has no server counterpart and is
anyway the *output* of `getIsElementObsolete`.

**The metadata fallback is load-bearing.** The data half only works if `UsageType` is populated on the
instance the package holds, and a compiled instance is known to shed metadata — that is how `Tag` was
lost. Resolve as `FindInstanceByUId(uid) ?? FindInstanceFromMetaData(uid)`
(`ProcessSchemaElementManager.cs:562-563`) — **correction from implementation: `FindInstanceFromMetaData(Guid)`
is `internal virtual` and unreachable from a configuration package, so the shipped code uses the public
`FindRuntimeSchemaFromMetaData` to the same effect. Do not re-derive the unreachable one from here.** Without it the predicate looks correct and silently returns
`Advanced` for everything, letting `CallUserTask` through.

*Scope, as a consequence rather than a separate choice:* the predicate does **not** drive connections
refusals — those come from `ConnectionCapability`'s allow-list, which already excludes all three retired
schemas for independent **mechanical** reasons (`CallUserTask` writes nothing at runtime;
`EmailUserTask` has 0 `EntityColumnValue` tags; `SendEmailUserTask` has no connections at all).
Deprecation and connection-capability coincide today but are different questions, so they live in
different classes.

### D10 — The package writes no bound data

Resolved by D6. Binding is a step of the registration recipe, in package sources, via the existing
data-binding tools — so column and row land in the same package by construction.

### D11 — `describe` read-back is hybrid

Per bound connection, `describe` emits **both** the raw persisted value verbatim and a decoded source in
exactly the shape `setConnections` accepts:

| Persisted macro | Decoded source |
|---|---|
| `[#Lookup.{schemaUId}.{recordId}#]` | `{ recordId, referenceSchema }` |
| `[#…[Element:{e}].[Parameter:{p}]#]` | `{ sourceElement, sourceElementParameter }` |
| `[#…[Parameter:{p}]#]` | `{ processParameter }` |
| anything else, or any of the above whose identifiers do not resolve | `{ expression: "<raw>" }` |

*Rationale.* Cross-reference by `uid` genuinely works for the element-output and process-parameter
dialects — verified on the live §5.1 payload — but is **inapplicable** to the lookup-record dialect,
where neither GUID appears anywhere in the payload. And emitting a metapath while accepting a `recordId`
on write would force the caller to know two representations of one thing, reintroducing the platform
trivia D1 removed. The hybrid also makes the field forward-compatible: a new platform macro degrades to
`expression` instead of breaking `describe`, and the decoder cannot lose information.

Two invariants: the decoder **never fails and never loses information** (no half-decoded sources), and a
new macro degrades rather than breaking. New pinned acceptance criterion: a
`describe → setConnections → describe` round-trip test **per dialect**, plus one unrecognised-macro case.

---

## Consequences

**Positive.** The silent-inert failure class becomes a refusal with a reason in every case the analysis
found. The caller never needs an entity-schema UId to bind a fixed record. `describe` round-trips
connections, which the package's own design goal requires. Adding a second host entity later is additive.
Deprecation becomes data-driven, so a future retired task with `UsageType.None` is caught without a
package change.

**Costs accepted.** Two ways exist to set the same thing (`addMapping` remains) — documented, not
deprecated. A greenfield "connect my new section" request is two calls across two surfaces rather than
one; that scenario is inherently two-phase (change the data model, then design the process), and pretending
otherwise is what created the authorization muddle. Element-specific knowledge concentrates in
`ConnectionCapability`, which must be kept as the single place it lives.

**Detector required (T-10), and it exists.** Measured: a request carrying unknown members — including a
future-shaped `connections` array — is answered normally with the members **silently ignored**; no contract
implements `IExtensibleDataObject` (checked across the package's `[DataContract]` types (25 when measured, 27 after this feature)), so this holds at every
nesting level. An old package plus a new `connections` field is therefore a green log and a wrong process, and
`[RequiresPackage]` is presence-only with a pin test asserting the **absence** of a version literal. **D8
resolves this onto the convergence rule** (above), armed by the rebundle's mandatory version bump.

---

## Alternatives rejected

| Rejected | Why |
|---|---|
| Guidance + `describe` only, writes left on `addMapping` (D1) | The guards would have nothing to hang on, leaving every silent-inert mode open for the whole interim |
| Read side first, operations later (staging of the above) | Same objection — enforcement is the point, and it cannot be deferred without shipping the hazard |
| Set `CreateActivity` implicitly (D3) | Mutates a parameter outside the operation's name, and silently opts the user into a visible product change |
| Blanket "parameter exists and is not `true` → refuse" (D3) | No cheaper to build, and blocks manual-send email, a legitimate configuration |
| Single high privilege gate for all connections work (D6/D7) | Removes the common case from callers who hold only `CanManageProcessDesign` and need no schema change |
| Conditional privilege check inside `setConnections` (D6/D7) | Makes an operation's contract depend on its content, adds a second gate to a component that deliberately centralised its one, and invents an atomicity question |
| Two operations inside the package, one per privilege level (D6) | Still smuggles data-model mutation into the wrong component |
| Name the wire field `activityConnections`, or carry the host up front (D2) | Creates exactly the rename it was meant to avoid; B1 makes the host a late addition instead |
| A `[RequiresPackage]` version literal for the stale-package detector (D8) | Restates a delivery policy where it cannot track the archive, and the pin test asserts its absence for that reason |
| Make the package reject unknown members (D8) | Puts the detector in the component that is by definition the OLD one on a stale environment |
| Raw metapath in `describe` + documentation (D11) | Breaks round-trip for the fixed-record dialect and forces the caller to resolve schema UIds |

---

## Open items

**Decisions still to take (none block the first delivery).** D4 — user tasks created by `add-user-task`
structurally cannot carry connections: document or extend. D5 — `list-user-tasks` advertises retired
schemas with no marker (confirmed on live data: 23 tasks including all three); fix here or as its own
task. D12 — MCP-only, or add a CLI verb and its full doc surface. (**D8 is taken** — see the decision
above.)

**Residual inside D9.** Whether to honour the `ProcessObsoletedElements` feature flag, which *inverts*
the predicate. Recommended: honour it, so an environment that deliberately re-enabled retired elements is
not blocked by us while the designer permits them. Two lines; decide at implementation.

**Deferred to implementation-time verification.** A created (dynamic) connection on
`AutoGeneratedPageUserTask` run to task completion (reverse-sync path); a package-built process re-saved
in the designer and exported (mappings created by the package carry `Name = null` — established harmless,
since no reader exists anywhere in `Terrasoft.Core`, `Terrasoft.Core.Process` or PackageStore, but the
designer's own client-side save path is not falsifiable by inspection); cache staleness after a write
(the registry ESQ shares a `CacheItemName` with the designer's); whether `update-entity-schema` cleanly
adds a column to `Activity` specifically (its docs address *inherited*-column edits, and adding to a base
schema from another package means creating a replacing schema); and the upgrade path for processes already
built by the package.

**Test-layer facts that shape the plan.** The unit layer is writable: the mock lookup column carries a
non-empty `UId` and `ReferenceSchemaUId`, so the `ColumnUId`-keyed registry join is testable — but
`Caption` is an **empty** `LocalizableString`, so no test may assert caption content, and the extension
helper `manager.CreateLookupColumn(target, column, lookupSchema)` should be preferred because it
auto-creates the referenced schema. Exercising parameter materialisation additionally needs a mocked
`ProcessUserTaskSchemaManager`. The E2E surface is **43 tests across 9 files**; connections extend three
of them — Modify (the new operations), Describe (the projection plus the per-dialect round-trip: six new
cases on a file that has two today), and Create if `connections` is accepted in the build descriptor.
**What actually shipped:** all six landed in ONE fixture, Modify (16 → 22), because each needs a process
built and then edited, and splitting them would have duplicated the arrange and left neither half meaningful
alone. Describe stayed at 2 and Create at 14 — a build descriptor carries no connections. The six also cover
the OPERATIONS (decode read-back, macro synthesis, upsert, clear, two refusals) rather than the per-dialect
round trip this paragraph specified: that is pinned at the UNIT layer, in the package's
`ConnectionRoundTripTests` (five dialects plus an unrecognised macro, each asserting the re-read value is
byte-identical). No E2E case re-applies a described connection.
