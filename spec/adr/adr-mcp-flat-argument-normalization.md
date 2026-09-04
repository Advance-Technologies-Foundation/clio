# ADR: Accept the canonical flat argument shape for resident MCP tools

- **Status:** Accepted
- **Date:** 2026-09-04
- **Feature:** `mcp-flat-argument-normalization`
- **Jira:** [ENG-95885](https://creatio.atlassian.net/browse/ENG-95885)
- **Related ADR:** [adr-read-only-mcp-response-deadline.md](adr-read-only-mcp-response-deadline.md) — the other
  behaviour layered onto the same `McpToolErrorFilter.HandleCallToolErrors` seam; the two run in a fixed
  order (normalization first) and are otherwise independent.
- **Related ADR:** [adr-mcp-durable-invocation.md](adr-mcp-durable-invocation.md) — the forgiving
  unmatched-*name* handler. This ADR is the forgiving argument-*shape* layer, and deliberately does not
  extend to the durable path.

---

## Context

A fresh-context agent's first `tools/call` on a resident clio tool almost always sends the canonical
**flat** payload — `{"environment-name":"x"}` — instead of the published wrapper —
`{"args":{"environment-name":"x"}}`. Until this change that call simply failed and the agent burned
1–7 turns rediscovering the wrapper. In the measured Applicant run this was **the largest single class
of MCP tool errors**.

Teaching the shape in guidance was tried and disproven in that same run: 26 of the 35 observed failures
had already received a correct in-band example at the point of failure, and the class still recurred.
An agent's first call is generated from its prior about how MCP tools look, not from the schema it was
just handed. That is not a documentation defect, so it does not have a documentation fix.

### Constraints

- `tools/list` publishes the wrapper as `required: ["args"]`. The MCP SDK binds a tool's single
  composite parameter from that key; a flat payload never reaches the tool method.
- Most resident args records carry **no `[JsonExtensionData]` overflow bag** (verified on
  `ApplicationTool`, `GetPkgListTool`, `EntitySchemaTool`, `PageGetTool`, `PageValidateTool`,
  `ShowWebAppListTool`, `DataForgeTool`). `System.Text.Json` silently ignores unmapped members, so a
  blindly wrapped payload containing a typo would materialize the record with **defaults** and the tool
  would answer a validation mistake with a plausible-but-wrong list or default payload **as a success**.
  For an agent that is strictly worse than a hard failure.
- `clio-run` is multi-parameter (`command` + `args`) and already owns its own wrapped/flat recovery in
  `ClioRunExecutor.RecoverWrappedCall`. Two mechanisms rewriting the same arguments object would fight.
- All call-tool invocations already flow through one request filter,
  `McpToolErrorFilter.HandleCallToolErrors`, registered once in the transport-neutral
  `RegisterMcpServer`, so stdio and `mcp-http` share whatever it does.
- ClioRing is an independently released **consumer** of this contract (`clio/AGENTS.md`, ClioRing
  compatibility gate). Its call shapes must keep working byte-for-byte.

## Decision

**Accept the flat shape at runtime, in one central seam, by classifying the payload — never by wrapping
it unconditionally — and leave the published schema unchanged.**

`McpToolErrorFilter.TryRefuseCallArguments` runs before argument binding and, for a matched tool whose
only bindable parameter is a composite `args` record, decides one of five outcomes:

| payload | outcome |
|---|---|
| already wrapped (only the wrapper key) | untouched, byte-compatible pass-through |
| canonical-flat (every top-level key is a wire property) | **all** top-level keys moved into the wrapper |
| any unknown key — whole payload unknown, **or** a real field beside a typo | refused, naming the canonical fields |
| hybrid (wrapper object plus extra top-level keys) | refused as an ambiguous shape |
| empty `{}` | unchanged, unless the tool declares `[McpAcceptsEmptyArguments]` |

Three properties of the decision are load-bearing:

1. **Classification, not unconditional wrapping.** The partial-unknown case
   (`{"environment-name":"dev","filer":"x"}`) is refused for the same reason as the all-unknown case:
   the good field does not make the typo safe, and for a record with no overflow bag wrapping it would
   convert a validation mistake into a silent default-success.
2. **The trigger gate is shared by construction.** `McpToolArgumentSupport.TryGetSingleCompositeParameter`
   is the single definition of "exactly one bindable non-framework composite parameter", used by both
   the normalizer and `ClioRunTool`. `clio-run`'s flat `(command, args)` call is therefore never
   normalized and can never collide with `RecoverWrappedCall`.
3. **`Arguments` is replaced on the EXISTING `Params` instance.** Building a new
   `CallToolRequestParams` would drop `_meta`, the progress token and task metadata, breaking
   `notifications/progress` and the `_meta.clioStageEvent` stream ClioRing consumes.

Opting a tool out of a refusal is **explicit and fail-closed**, via two attributes in
`Tools/McpFlatArgumentContract.cs` applied to the tool method — never inferred from the generated
schema, whose required-property set is a weak proxy for runtime semantics (e.g.
`DataForgeMaintenanceArgs.EnvironmentName` is schema-optional yet `EnsureRequired`-checked):

- `[McpAcceptsEmptyArguments]` — `{}` is a real call. Today: `list-apps`, `get-request-info`.
- `[McpRecoversUnknownArguments]` — the tool binds an overflow bag *and inspects it*, so an
  unknown-key payload is forwarded to its richer diagnosis. Today: `get-tool-contract`.

### Framework-parameter exclusion is keyed on the SDK assembly, not on a namespace name

`McpToolArgumentSupport.IsFrameworkOwnedType` decides which parameters the SDK injects (and which
therefore do not count toward "exactly one bindable parameter"). It excludes `CancellationToken` and
`IServiceProvider` by type, anything declared in the **MCP SDK's own assembly** (`McpServer`,
`IMcpServer`, `RequestContext<T>`, `ProgressToken`, and every future SDK-injected type), and anything
assignable to `McpServer` — which catches a host-defined subclass declared outside that assembly.

A namespace-prefix match (`type.Namespace.StartsWith("ModelContextProtocol")`) was tried and rejected:
it silently swallows an unrelated type whose namespace merely begins with those characters, and
silently misses an `McpServer` subclass declared elsewhere. Either error moves the bindable-parameter
count and hands the normalizer a payload it must not rewrite. Assembly identity plus `IsAssignableFrom`
cannot drift that way at the next SDK upgrade, and the boundary is pinned by
`McpToolArgumentSupportTests`.

### Scope boundary: the durable long-tail path stays wrapped-only

`McpDurableCallToolHandler` / `IClioRunExecutor.InvokeResolvedAsync` are **not** normalized. The filter
needs `MatchedPrimitive` to reflect a parameter contract, and that is null for a tool outside
`tools/list`. The long tail is reached through `clio-run`, which owns its own recovery, and the measured
ENG-95885 run contained zero durable-handler outcomes of this error class. Widening the scope would add
risk with nothing to fix.

## The one-way door

**Once agents observe that the flat shape works, it can never be rejected again.** This is the
consequential half of the decision and it is accepted deliberately, at the plan gate, as RISK6.

Stated precisely, so a future maintainer does not have to reconstruct it:

- **The accepted runtime input set is intentionally WIDER than the published `tools/list` schema.**
  `required: ["args"]` stays. This is a tolerant runtime compatibility layer, not a schema change.
- **Tightening `tools/list` back to reject a flat payload is an explicit NON-GOAL — permanently.** Any
  future change that would make a canonical-flat call fail is a breaking change to a contract real
  agents depend on, regardless of what the schema says. Treat "the schema never allowed this" as an
  argument that has already been heard and rejected here.
- The two statements above are asserted, not merely documented: the completeness fixture
  `McpFlatArgumentNormalizationCompletenessTests` derives its population at runtime from
  `McpCoreToolProfile.CoreToolTypes` united with `AlwaysOnLazyToolTypes`, so a newly resident
  single-composite-args tool is covered with no test edit and a regression that stops normalizing it
  fails the build.
- The canonical agent-facing statement of the accepted shapes is
  `ToolContractGetTool.AcceptedArgumentShapesHint`. `McpServerInstructions` stays pointer-only.

The reason to accept the door rather than avoid it: the alternative is not "keep the option to tighten
later", it is "keep paying 1–7 wasted turns on the first call of every fresh agent session, forever".
The divergence is one-directional (accept more, never less), so nothing an agent can learn from it
becomes wrong later.

## Alternatives considered

- **Teach the shape in guidance only.** Rejected — *disproven by measurement*, not by argument: 26 of
  35 failures already had a correct in-band example at the point of failure.
- **Change `tools/list` to publish the flat shape.** Rejected. It breaks every existing wrapped caller
  (ClioRing included), and the SDK's binding model wants the composite parameter, so the wrapper would
  have to be reconstructed anyway. Publishing both shapes doubles every tool's schema and the context
  cost that `McpCoreToolProfile` exists to control.
- **Wrap any flat payload unconditionally.** Rejected — the silent default-success failure mode above.
  This is the single most important thing this ADR chose *not* to do.
- **Per-tool recovery (an overflow bag plus a flat-shape branch on every args record).** Rejected: 20+
  duplicated implementations, a SonarCloud duplication cost, and a new tool would silently miss it.
  It also makes the accepted contract a per-tool accident instead of one reviewable rule.
- **Infer no-arguments capability from the schema's required-property set.** Rejected — the set is a
  weak proxy for runtime semantics, and inferring it would turn a clear missing-argument error into a
  deeper, less actionable failure. Hence the explicit fail-closed attribute.
- **Normalize the durable long-tail path too.** Rejected for this change: no `MatchedPrimitive`, no
  measured occurrences. Recorded in a code comment at the null check so the gap is discoverable.
- **Canonicalize non-canonical field names (`environment` to `environment-name`) while we are here.**
  Rejected. Aliases stay **rejection-only**: a wrong spelling yields a rename hint, never a silent
  binding, so the accepted field set stays exactly the canonical kebab-case one.

## Consequences

Positive:

- The largest measured class of MCP tool errors is removed at the seam, for the whole resident set,
  with no per-tool edits and no `tools/list` growth.
- A typo is still a hard failure, and now a *better* one: the refusal names the offending key and lists
  the canonical fields.
- `get-tool-contract` gains `[McpRecoversUnknownArguments]`, closing a real defect — a flat name-only
  call used to return the **full tool index** as a success instead of the named contract.

Negative / accepted:

- The published schema and the accepted input set permanently differ (the one-way door above).
- **A mixed payload's fate depends on the tool's declaration.** With `[McpRecoversUnknownArguments]` the
  payload is forwarded and the *tool* is responsible for diagnosing the unknown key; without it the
  payload is refused. The normalizer itself never drops a key silently. If a future tool gained the
  attribute *without* actually inspecting its overflow bag, the serializer would drop the stray key —
  which is why `McpFlatArgumentNormalizationCompletenessTests` fails the build when the attribute sits
  on an args record with no overflow bag.
- Agent-facing error text changed: a JSON-encoded object argument now returns a precise shape-naming
  error instead of the raw `BytePositionInLine` serializer message. One existing e2e assertion was
  widened to accept it.
- Reflection (`GetParameters` / `GetProperties` / `GetCustomAttribute`) runs per call on the
  classification path. Uncached, matching the pre-existing deserialization preflight on the same seam;
  negligible against a tool call that does an HTTP round-trip to a Creatio tenant. A
  `ConcurrentDictionary<MethodInfo, …>` cache is the obvious optimization if it ever shows up in a
  profile.

## Open questions

- `get-component-info` was deliberately **not** given `[McpRecoversUnknownArguments]`, so its flat
  `{"component-name":…}` call gets the filter's unknown-argument error rather than the tool's own richer
  `Rename: component-name -> component-type` hint. Revisit if the per-tool wording proves more useful in
  the field.
- ENG-95885 **does not close on merge.** Per the plan gate decision it stays open until the wrapper error
  class is measured at or near zero on a further Applicant run after clio's upstream release.
  Merged-with-evidence is not sufficient.
