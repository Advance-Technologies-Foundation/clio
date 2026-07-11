# Story 5: `create-app` — route through the resolver, including nested caption-culture and polling calls (class c1)

**Feature**: mcp-passthrough-tool-parity
**FR coverage**: FR-01, FR-05, FR-05a, FR-10
**PRD**: [prd-mcp-passthrough-tool-parity.md](../prd/prd-mcp-passthrough-tool-parity.md)
**ADR**: [adr-mcp-passthrough-tool-parity.md](../adr/adr-mcp-passthrough-tool-parity.md)
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

CI pipeline author (AI-Platform gateway operator)

## I want

`create-app` (`ApplicationCreateTool`) to execute against the header-supplied tenant on **every** call it
makes — including the nested caption-culture resolution and the timeout/polling readback — not just its
initial client construction

## So that

creating an application under passthrough neither fails with an opaque error nor silently leaks the
active/registered tenant's culture one call deep inside the operation

---

## Merge order (shared-file constraint)

Stories 3-9 all modify `clio/Command/McpServer/Tools/ApplicationTool.cs` (and 3-5 also
`ApplicationToolArgs.cs`), so they are **serialized: 3 → 4 → 5 → 6 → 7 → 8 → 9** via `depends_on`. This
story starts only after **Story 4** merges.

## Acceptance Criteria

- [ ] **AC-01** (PRD AC-02, decision-matrix "Route — full nested graph") — Given authorized passthrough and
  no `environment-name`, when `create-app` runs, then the application is created against the header tenant —
  never `Environment name is required.`.
- [ ] **AC-02** — **Conditional requiredness (FR-05a) — blocking prerequisite for AC-01.** Given authorized
  passthrough, when `create-app` is called with no `environment-name`, then the MCP schema does **not**
  reject the call at pre-tool binding — `[Required]` is removed from `environment-name` on the corresponding
  `ApplicationCreateArgs` record (ADR "CLI flag specification" table), making it schema-optional. On
  non-passthrough transports, runtime requiredness is enforced by the existing
  `IToolCommandResolver.ResolveSettingsAndKey`'s `EnvironmentResolutionException` throw (ADR OQ-03,
  "Resolver-ROUTED tools").
- [ ] **AC-03** — **Nested caption-culture path (the crux of this story — ADR verification #4).** Given the
  same setup, when `ApplicationCreateService.CreateApplication`'s internal caption-culture resolution runs
  (`ApplicationCreateService.cs:88`, today calling `CaptionCultureResolver.Resolve(EnvironmentOptions, ...)`
  → `ISettingsRepository.GetEnvironment`), then it uses the **settings-based**
  `ICaptionCultureResolver.Resolve(EnvironmentSettings, ...)` overload from Story 2 and resolves the
  **header** tenant's culture — it must **never** read the configured active/registered environment's
  culture.
- [ ] **AC-04** — **Nested polling/readback path.** Given the same setup, when the create operation's
  timeout/polling loop calls what is today `applicationInfoService.GetApplicationInfo(environmentName, ...)`
  (`ApplicationCreateService.cs:471,484`), then it uses the settings-based `GetApplicationInfo(
  EnvironmentSettings, ...)` overload and polls the **header** tenant — not a name-based lookup against any
  other environment.
- [ ] **AC-05** — Given the same setup, when the enrichment call
  (`enrichmentService.Enrich` → `DataForgeEnrichmentBuilder.Build` → `commandResolver.Resolve<
  IDataForgeContextService>`) runs, then it is confirmed unchanged — it is already class-(b) compliant
  (`DataForgeEnrichmentBuilder.cs:53`) and must not be touched by this story.
- [ ] **AC-06** — **Mixed input (PRD AC-06).** Given authorized passthrough with both the header and an
  explicit `environment-name` naming a different registered environment, when `create-app` runs (including
  its nested calls), then it is rejected by `HasExplicitCredentialArgs` before any Creatio-reaching call in
  the entire call graph — it never uses the named environment's stored credentials, in the outer call **or**
  the nested culture/polling calls.
- [ ] **AC-07** (PRD AC-09 / SM-03) — Given stdio or registered-environment `mcp-http`, when `create-app` is
  called with `environment-name`, then behavior — including the nested calls — matches the pre-change
  baseline exactly.
- [ ] **AC-08** (PRD AC-07, concurrency isolation) — Given two concurrent `create-app` calls with different
  credentials, when both run (including their nested culture/polling calls), then each resolves a distinct
  authenticated container with no cross-tenant bleed (E2E proof owned by Story 15).
- [ ] **AC-ERR** — **Error semantics respect the ENG-93208 middleware boundary (PRD AC-ERR).**
  (a) A **malformed/unusable** header is out of scope: the middleware returns HTTP 400 **before** any tool
  is invoked (`McpHttpServerCommand.cs:317`) — the tool is never entered and must not add handling for it.
  (b) Given a **valid** header whose target operation fails, when the tool executes, then it returns the
  typed error envelope with `SensitiveErrorTextRedactor`-redacted text.

## Implementation Notes

Depends on Story 2 (`ICaptionCultureResolver` + `IApplicationInfoService` settings-based overloads).

Add the settings-based `IApplicationCreateService` overload (owned by THIS story, ADR slice 6e) whose body
is **identical** to today's name-based body **except**: it does not call
`settingsRepository.FindEnvironment(name)`, and **every** nested call it makes to
`ICaptionCultureResolver.Resolve` and `IApplicationInfoService.GetApplicationInfo` uses the new
settings-based overloads, not the name-based ones (ADR "OQ-01 — c1", the "Rule, stated explicitly so it
survives implementation").

```csharp
public interface IApplicationCreateService {
    ApplicationInfoResult CreateApplication(string environmentName, ApplicationCreateRequest request);           // unchanged
    ApplicationInfoResult CreateApplication(EnvironmentSettings environmentSettings, ApplicationCreateRequest request); // NEW
}
```

`ApplicationCreateTool` derives from `BaseTool<EnvironmentOptions>(null, logger, commandResolver)` +
`ExecuteWithCleanLog(options, ...)` (options-aware overload, `BaseTool.cs:63`), same shape as Story 3.

**Also remove `[Required]` from `environment-name`** on the `ApplicationCreateArgs` record — schema-optional
so a header-only passthrough call reaches the tool instead of being rejected at MCP binding (PRD A-02 /
FR-05a). Non-passthrough requiredness is enforced by the resolver's existing throw.

**Do not** treat "routes through the resolver" as satisfied by the outer call alone — the ADR's adversarial
review specifically found this exact tool's nested caption-culture call to be a real active-tenant leak one
level deep (verification #4). AC-03/AC-04 are not optional nice-to-haves; they are the story's core
deliverable.

Key files: `clio/Command/ApplicationCreateService.cs`,
`clio/Command/EntitySchemaDesigner/CaptionCultureResolver.cs` (already changed in Story 2; consume here),
`clio/Command/ApplicationInfoService.cs` (consume Story 2's overload here),
`clio/Command/McpServer/Tools/ApplicationTool.cs`, `clio/Command/McpServer/Tools/ApplicationToolArgs.cs`.
Pattern to follow: Story 3's tool-wiring shape; Story 2's settings-based overload contracts.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Header-only executes against header tenant (outer call, schema no longer rejects a blank `environment-name`); **separate, explicit test** asserting the nested caption-culture resolution is header-aware; **separate, explicit test** asserting the nested polling/readback call is header-aware; mixed-input rejected end-to-end (outer + nested); registered-env/stdio unchanged | `clio.tests/Command/McpServer/ApplicationCreateToolPassthroughTests.cs` |
| Integration `[Category("Integration")]` | none required — nested calls are mocked at the service-interface boundary | — |
| E2E `[Category("E2E")]` | Owned by **Story 15**: two-tenant isolation (newly routed tool) + stdio/`-e` no-regression; the FR-08 mandatory multi-tenant "section tool incl. nested caption-culture" case is carried by `create-app-section` there. Manual only — MCP e2e not in CI | `clio.mcp.e2e/` (Story 15) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] All `CLIO*` diagnostics clean in changed files — including CLIO005 (FR-10)
- [x] Targeted tests green before commit: `dotnet test clio.tests/clio.tests.csproj --filter
  "Category=Unit&(Module=McpServer|Module=Command)" --no-build` (ADR slice 9)
- [x] All new/changed MCP arguments stay kebab-case (relaxing `[Required]` does not rename `environment-name`)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing: `Category=Unit&Module=McpServer` → 2058 passed / 0 failed;
  `Category=Unit&Module=Command` → 2069 passed / 0 failed (net10.0)
- Notes:
  - `IApplicationCreateService` gained the settings-based `CreateApplication(EnvironmentSettings, ...)`
    overload (ADR slice 6e). Both public overloads converge on a private `CreateApplicationCore` that
    takes the nested calls as delegates: the name-based path keeps its pre-change name-based
    caption-culture and `GetApplicationInfo(environmentName, ...)` calls byte-for-byte (AC-07), while
    the settings-based path binds the Story-2 settings-based overloads (AC-03 culture at the guard,
    AC-04 polling/readback in `LoadApplicationInfoWithRetry`) — no settings-based overload calls a
    name-based one or `ISettingsRepository`.
  - `ApplicationCreateTool` reworked onto `BaseTool<EnvironmentOptions>(null, logger, commandResolver)`
    with the options-aware `ExecuteWithCleanLog` INSIDE the heartbeat work delegate (Story 4 pattern).
    The tenant is resolved FIRST inside the locked body, so mixed input is rejected by
    `HasExplicitCredentialArgs` before the enrichment probe or any Creatio-reaching call (AC-06).
  - Enrichment path (`DataForgeEnrichmentBuilder`) confirmed class-(b) compliant and left untouched
    (AC-05): it routes through `commandResolver.Resolve<IDataForgeContextService>` per request.
  - `ApplicationCreateArgs.EnvironmentName` is schema-optional (FR-05a); the record parameter was moved
    after the still-required `name`/`code` because C# optional parameters must follow required ones —
    all call sites use named arguments, so no positional break. Curated `get-tool-contract` entry
    aligned (required list + passthrough sentence).
  - Ratchet (architect-approved): `McpProfileGatingTests.MaxLazyToolsSerializedBytes` raised
    34816 → 35328; measured tools/list payload after this story: **34810 bytes**.
  - Nested-path tests drive a REAL `ApplicationCreateService` from the tool fixture
    (`ApplicationCreateToolPassthroughTests`) so AC-03/AC-04 are proven end-to-end, plus
    settings-overload contract tests in `ApplicationCreateServiceTests`.
