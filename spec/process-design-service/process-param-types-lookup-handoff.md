# Handoff — modify-business-process: params, types, lookup, describe (ENG-90883 / ENG-91447)

> Continuation doc for a fresh session. Self-contained: everything needed to resume without the
> originating chat. Last updated end of the session that did the work below.

## 1. Task

Backend, command-driven (non-visual) BP designer for Creatio, split across two repos:
- **clio** (CLI + MCP) — talks to the server package over HTTP; owns NO process metadata.
- **ProcessBuilder** server package `clioprocessbuilder` (WCF `ProcessDesignService` → `IProcessDesigner`) —
  owns all process-schema metadata; clio just forwards `operations` JSON.

This work added/repaired: `addParameter` / `addMapping` modify ops, friendly type mapping, **Lookup
parameters via `referenceSchema`**, optional process identity, and `type`/`referenceSchema` in
`describe-process`.

## 2. Repos, branches, state

| Repo | Path | Branch | State |
|---|---|---|---|
| clio | `C:\Projects\clio` | `feature/ENG-90883-approach1-backend-designer` (PR #715, **OPEN**) | committed, **ahead 24**, **NOT pushed** |
| server | `C:\Projects\workspace\ProcessBuilder` | `feature/ENG-91447-modify-add-params-mappings` | **UNCOMMITTED** changes |

clio commits this session (newest first): `89774bae` merge master · `a413d263` e2e for
addParameter+referenceSchema · `60a0d71a` merge remote feature · `69f41af1` the clio feature changes.

ProcessBuilder uncommitted: `packages/clioprocessbuilder/Files/src/cs/ProcessDesigner.cs`,
`ProcessDesignContracts.cs`, `tests/clioprocessbuilder/{BaseComposableAppTestFixture,ProcessDesignerModifyOperationsTests}.cs`,
`.gitignore`. **Local-only / git-ignored (do NOT commit):** `tests/clioprocessbuilder/app.config`
(binding redirect, stand-specific — but REQUIRED for the server tests to pass locally),
`.build-props/env.Debug.props`, `.build-props/env.Release.props`. (`env.dev-nf.props`/`env.dev-n8.props`
ARE tracked — canonical net472/net8 configs.)

## 3. Environment

- Stand **krestov-test** = `http://d_krestov_n.tscrm.com:1026` — IIS site "Creatio" (id 2), .NET Framework,
  MSSql, **FSM (file-system development) mode ON**.
- Local Creatio site (source build): `C:\Projects\Creatio\TSBpm\Src\Lib\Terrasoft.WebApp.Loader\Terrasoft.WebApp`.
- Package junction (FS link): `…\Terrasoft.Configuration\Pkg\clioprocessbuilder` → `C:\Projects\workspace\ProcessBuilder\packages\clioprocessbuilder`.
- Server error log: `C:\Windows\Temp\Creatio\Creatio\0\Log\<YYYY_MM_DD>\Error.log` (has full stack + line numbers).
- **Creatio source is local** at `C:\Projects\Creatio\TSBpm\Src` — use it to look up Terrasoft APIs, e.g.
  `Terrasoft.Core/DataValueType.cs`, `DataValueTypeManager.cs`, `Process/ProcessSchemaParameter.cs`,
  `Manager.cs` (`GetInstanceByUId`).
- clio MCP server is launched from `C:\Projects\clio\clio\bin\Debug\net10.0\clio.exe mcp`
  (user-scope `~/.claude.json` → `mcpServers.clio`; applies to every session). The GLOBAL tool
  `~/.dotnet/tools/clio.exe` (8.1.0.61) does NOT have these commands — do not use it for these.

## 4. The three bugs fixed (root causes — the valuable part)

1. **Single-identity call failed with generic `"An error occurred invoking 'modify-business-process'."`**
   The MCP tool declared `processName`/`processUid` as non-nullable `string` → the MCP SDK marks BOTH
   *required*, but they are mutually exclusive, so passing only one fails arg-validation before the tool
   runs (no correlation-id). **Fix (clio `ModifyBusinessProcessTool.cs`):** make them
   `string? … = null` (reordered after the required `operations`). The runtime "exactly one" check already
   handles it. Diagnostic rule: generic error + NO correlation-id = arg/transport/stale-process; a real
   server error returns `{exit-code, execution-log-messages, correlation-id}`.

2. **`addParameter` created the param but the designer showed "Data type" BLANK.**
   The type WAS persisted — but to the **abstract base `Text`** (`TextDataValueTypeUId =
   8b3f29bb-ea14-4ce5-a5c5-293a929b6ba2`). The visual designer only lists **concrete** types, so the
   abstract base renders blank. **Fix (server `ProcessDesigner.NormalizeParameterTypeName`):** map
   `Text`/`String` → `ShortText`. Note: `Float`→`Float2`, `Money`→`Money`(Currency 0.01), Integer/Boolean/
   Date/DateTime/Time/Guid/LongText already resolve to concrete types — only `Text` needed mapping.
   (Concrete text UId `ShortText = 325A73B8-0F47-44A0-8412-7606F78003AC`.)

3. **`describe-process` didn't surface `type` / `referenceSchema`.** Two independent layers:
   - **server** `ToDescribeParameter` must RESOLVE the names from the UIds (`ResolveDataValueTypeName` via
     `DataValueTypeManager.GetInstanceByUId`; `ResolveReferenceSchemaName` via
     `EntitySchemaManager.GetInstanceByUId`) — on a read-back schema the OBJECT properties
     (`parameter.DataValueType` / `ReferenceSchemaName`) are NULL; only the UIds are populated.
   - **clio** `DescribedParameter` (in `IProcessDescriber.cs`) must declare `type` + `referenceSchema`
     `[JsonPropertyName]` fields, else the command **strips** them when it re-serializes the server
     response. (This stripping caused MANY wrong "old assembly is loaded" conclusions — see lesson below.)

## 5. Feature additions

- **server `ProcessDesigner.cs`**: extracted `AddProcessParameter` (shared by build + modify) with a
  duplicate-name guard; extracted `ApplyMapping`; `ApplyOperation` (now `internal` for tests) handles
  `addparameter` / `addmapping`. `InitializeLocalizableValues()` before `SaveSchema` so param captions
  persist. **Lookup:** when `descriptor.ReferenceSchema` is set → resolve the `EntitySchema`, force type
  `Lookup`, and set **only** `parameter.ReferenceSchemaUId` (its setter marks the ref initialized + derives
  the name on read; ALSO setting `ReferenceSchemaName` resets that flag and forces a lazy Workspace lookup →
  NPE in the test harness).
- **server `ProcessDesignContracts.cs`**: `ProcessParameterDescriptor.ReferenceSchema`;
  `ProcessOperationDescriptor.Parameter` + `.Mapping`; `DescribeProcessParameter.Type` + `.ReferenceSchema`.
- **clio**: tool `[Description]` (`addParameter`/`addMapping`/`referenceSchema`), `ModifyBusinessProcessPrompt`,
  `ProcessModelingGuidanceResource`, docs `modify-business-process.{md,txt}` + `create-business-process.{md,txt}`
  (shared `parameters[]` descriptor supports `referenceSchema` too).

Contract for an agent:
```json
{ "op":"addParameter", "parameter": { "name":"AccountId", "type":"Guid", "direction":"In" } }
{ "op":"addParameter", "parameter": { "name":"City", "referenceSchema":"City", "direction":"In" } }
{ "op":"addMapping",   "mapping":   { "elementId":"task1", "elementParameter":"OwnerId", "processParameter":"AccountId" } }
```

## 6. Verified live (krestov-test, process `UsrTaskProcess`)

All types created correctly: Text→ShortText, Integer, Float→Float2, Money→Currency(0.01), Boolean, Date,
DateTime, Time, Guid, LongText, Lookup; **`PCity` → Lookup → City** (confirmed in the designer).
`describe-process` returns `type` for every param + `referenceSchema` for lookups **after the clio rebuild**.

## 7. Deploy / build / test

- **Apply a SERVER code change on the stand:** rebuild the package, then **COMPILE the package in Creatio**
  (Advanced settings → compile `clioprocessbuilder`). A pool/site restart alone does NOT pick it up
  (confirmed repeatedly). Build the package:
  `cd C:\Projects\workspace\ProcessBuilder && dotnet test tests/clioprocessbuilder/clioprocessbuilder.Tests.csproj -c dev-nf`
  (net472; **52 tests**; needs the local `tests/clioprocessbuilder/app.config` binding redirect).
- **Apply a CLIO change:** kill all `clio.exe` first (they lock `bin/Debug/net10.0`), then
  `dotnet build clio/clio.csproj -c Debug -f net10.0`; the MCP respawns on the next tool call.
- **clio tests:** `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel)"`
  (or full `Category=Unit`). The full suite has **pre-existing flaky tests** unrelated to this work
  (`OpenAppCommand`/`NewPkgCommand` normal-vs-debug error formatting via `IsDebugMode`; `Quartz …ForAllSchedulers`
  `TearDown` temp-dir `DirectoryNotFound`) — they pass in isolation; ignore.

## 8. Open items / next steps

1. **Commit ProcessBuilder server changes** (branch `feature/ENG-91447-modify-add-params-mappings`) — still uncommitted.
2. **Recompile the server package on krestov-test** so `describe-process` returns `referenceSchema: City`
   for created lookups (the `ResolveReferenceSchemaName` change is built + unit-tested 52/0 but NOT yet
   deployed; the rest — Text→ShortText, addParameter, referenceSchema-create — is already live).
3. **Push clio #715** (ahead 24, not pushed). PR #715 is OPEN; master was merged into the branch
   (`89774bae`), conflict-free.
4. (optional) clean up test params on `UsrTaskProcess` (`TestTextParam2`, `PInteger`…`PLookup`, `PCity`).
5. (optional) `addMapping` e2e — currently unit-covered; the e2e (`ModifyBusinessProcessToolE2ETests`)
   covers `addParameter` + `referenceSchema` and is env-gated/not-in-CI.
6. Separate task: Confluence research article update — see `spec/process-design-service/confluence-research-update-handoff.md`.

## 9. Lessons (do not repeat)

- Verify deployed SERVER behavior by the actual **designer / process metadata**, NOT by a freshly-added
  `describe` field whose own correctness is unproven (a buggy/absent describe field caused repeated false
  "old assembly still loaded" conclusions and a wasted `compile-all` attempt — wrong for an assembly package).
- `compile-all` is for the configuration; `clioprocessbuilder` is an assembly package — compile the PACKAGE.
- PR #715 is NOT merged to master; master does not contain the process-designer files — never branch the
  follow-up from master (it drops the whole feature). Base off the feature branch.
- After a clio rebuild, a generic `"An error occurred invoking '<tool>'."` with no correlation-id usually
  means a stale/old MCP process — restart it (kill `clio.exe`).
