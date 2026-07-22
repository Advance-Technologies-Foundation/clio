# ADR: Decompose clio into Core / CLI / MCP assemblies (extract the embedded MCP server)

- **Status:** Accepted (open questions Q1–Q5 resolved 2026-07-22 — maintainer accepted the recommended answers)
- **Date:** 2026-07-22
- **Feature:** `clio-modularization`
- **Author:** Architect Agent (BMAD Phase 2)
- **PRD source:** none — this is an internal architecture decision; the problem statement and
  requirements are embedded in Context below so the ADR is self-contained.
- **Related:** root `AGENTS.md` — "ClioRing MCP compatibility gate", "Feature toggles", "CLIO analyzer handling";
  [adr-mcp-durable-invocation.md](./adr-mcp-durable-invocation.md) (the stdio call-tool-handler seam this must preserve);
  [adr-mcp-http-standard-authorization.md](./adr-mcp-http-standard-authorization.md) (the JwtBearer/ASP.NET dependency that this ADR moves off core).
- **Inputs:** direct code inspection of `clio/clio.csproj`, `clio/Program.cs`, `clio/BindingsModule.cs`,
  `clio/Command/McpServer/**`, `clio/Common/**`, and `clio-ring/**` (2026-07-22); LOC/coupling measurements reproduced below.

---

## Context

The `clio` core project has become cluttered. The Model Context Protocol (MCP) server was grown
**inside** the same project as the CLI core; ClioRing was later added as a companion; and the shape
that now feels correct is a **base/core** library with the other concerns as separate projects.

The decision to record is an **internal modularization**: split the single `clio` project into
cohesive assemblies (`Clio.Core`, `Clio.Cli`, `Clio.Mcp`) so the dependency graph is one-directional
and the plain CLI stops carrying MCP/ASP.NET dependencies. This is **not** a product split — exactly
**one** shipped artifact remains: the `clio` dotnet tool, with the MCP server **still bundled** inside
it. Distributing MCP as a separately-versioned package is explicitly out of scope (see Alternative A).

The asymmetry motivating this: `cliogate`, `Clio.Analyzers`, and the four `clio-ring` projects are
**already** separate projects in `clio.slnx`; only MCP was grown inside core. Fixing that asymmetry is
the whole job — but it is non-trivial because the coupling between core and MCP is **bidirectional**.
MCP→Core is heavy and legitimate (MCP is a front-end over core services). Core→MCP is the real
problem and is what makes a naive file move impossible (it would create a circular assembly reference).

### Confirmed evidence (verified by inspection, 2026-07-22)

**One shipped tool today.**
- `clio/clio.csproj:3` `<OutputType>Exe</OutputType>`, `:7` `<PackAsTool>true</PackAsTool>`, `:8`
  `<PackageId>clio</PackageId>`, `RootNamespace=Clio` (`:30`). Targets `net8.0`, and `net10.0;net8.0`
  when the SDK supports it (`:5-6`).
- Internals are exposed only to test/e2e assemblies: `InternalsVisibleTo` `clio.tests`,
  `clio.mcp.e2e`, `DynamicProxyGenAssembly2` (`clio.csproj:49-59`).

**MCP is ~30% of the project, in the same csproj.**
- `clio/Command/McpServer/`: **286 `.cs` files, 54,302 LOC**.
- clio core **excluding** `McpServer/`: **129,373 LOC across 784 files**.
- Whole `clio` project: **183,675 LOC** ⇒ McpServer ≈ **29.6%**.
- Peers already isolated: `clio-ring` is **4 projects** in `clio.slnx` (`ClioRing`, `ClioRing.Desktop`,
  `ClioRing.Ipc`, `ClioRing.Tests`; ≈17.3K LOC incl. tests), `cliogate` (≈2.4K LOC), `Clio.Analyzers`
  (≈1.5K LOC) — all separate. Only MCP is inside core.

**ASP.NET / MCP-SDK dependencies are forced onto the plain CLI today.**
- `clio.csproj:135-140` — `FrameworkReference Microsoft.AspNetCore.App` + `Microsoft.AspNetCore.Authentication.JwtBearer`,
  under the literal label "ASP.NET Core hosting for MCP HTTP transport".
- `clio.csproj:166-167` — `ModelContextProtocol` and `ModelContextProtocol.AspNetCore`.
  None of these are needed by a plain CLI verb; they exist for the MCP stdio/HTTP host.

**MCP → Core coupling (heavy, legitimate — keep it).**
- 111 files under `McpServer/` reference `Clio.Common` (101 raw `using Clio.Common` occurrences), plus
  `using Clio.Command` (14), `Clio.Command.Theming` (9), `Clio.UserEnvironment` (8),
  `Clio.Command.OAuthAppConfiguration` (5), and `Clio.Workspaces` / `Clio.Package` / `Clio.Theming`.
- MCP consumes core through **public** interfaces (spot-checked: `IApplicationClient`,
  `ISettingsRepository`, `IWorkspace`, `IPackageArchiver`, `ServiceUrlBuilder`, `ConsoleLogger` are all
  `public`). This direction becomes a clean project reference `Clio.Mcp → Clio.Core`.

**Core → MCP coupling (the root cause — 15 files + a static flag).** 15 files outside `McpServer/`
import `Clio.Command.McpServer*` or `ModelContextProtocol`, plus `Common/ConsoleLogger.cs` reads a
static on `Program`. These decompose into four "leak buckets":

1. **Run-mode flag.** `Program.IsMcpServerMode` (`clio/Program.cs:272` `internal static bool`; set
   `:1240`; read `:1446`, `:1610`) is read by `clio/Common/ConsoleLogger.cs` at **7 sites**
   (`:195,218,232,245,256,481,514`) to suppress console output under MCP. 11 sites total, **0 inside
   McpServer** — a cross-cutting run-mode concern that leaked into `Common`.
2. **StageEvent / progress contract.** Six files under `clio/Command/McpServer/Progress/`
   (`ClioStageEvent.cs`, `ClioStageEventContract.cs`, `IStageEventSource.cs`, `StageEventEmitter.cs`,
   `StageEventProgressForwarder.cs`, `StageIds.cs`). The contract types are **already `public`**
   (`ClioStageEvent` = `public sealed record` `:27`; `ClioStageEventContract` = `public static class`
   `:15`; `IStageEventSource` `:15`, `IStageEventEmitter` `:32` = `public interface`). They are
   **emitted by core** long-running commands: `CreatioInstallerService.cs:106` (implements
   `IStageEventSource`, injects `IStageEventEmitter` `:138`), `InstallerCommand.cs:246`,
   `UninstallCreatio.cs:96`, `Common/CreatioUninstaller.cs:108`. A shared contract that merely *lives*
   under `McpServer/`.
   - **Sub-leak 2b (distinct):** `StartCommand.cs:12` and `StopCommand.cs:14-15` import the raw MCP SDK
     (`using ModelContextProtocol;`) and expose `event EventHandler<ProgressNotificationValue> StatusChanged`
     using the **SDK DTO** `ProgressNotificationValue` (`StartCommand.cs:37`, `StopCommand.cs:58`, and
     many step sites). That is a direct core→MCP-SDK type dependency Core must not keep.
3. **Misplaced utilities under `McpServer/Tools/` used by core:**
   - `CompactPrimitiveArrayJsonElementConverter` (`.../Tools/CompactPrimitiveArrayJsonElementConverter.cs`)
     → used by core `Command/ComponentInfoCommand.cs:59`.
   - `McpToolExecutionLock` / `CwdLock` (`.../Tools/McpToolExecutionLock.cs`) → used by core
     `Command/PageBaselineGuard.cs`, `Command/PageDesignerPresence/PageDesignerPresenceNotifier.cs`,
     `Command/PageFileWriter.cs`.
   - `IToolCommandResolver` / `ToolCommandResolver` (`.../Tools/ToolCommandResolver.cs`, DI-registered at
     `BindingsModule.cs:605`) → used by core `Command/Theming/BuildThemeCommand.cs`, and imported by
     `Command/ComponentRegistryRefreshCommand.cs` and `Command/ListPrintablesCommand.cs`.
   - Two references are **doc-comment `<see cref>` only** (cosmetic, not compile deps):
     `Command/ProcessModel/IProcessGraphValidator.cs:81`, `Common/EnvironmentNotFoundError.cs:10`.
4. **Config templating (soft/textual, not a type dependency).** `MergeClioMcpServer` in
   `clio/Common/Skills/` (`CodexTomlConfigEditor.cs:30`, `JsonConfigEditor.cs:51`) does **not** reference
   any McpServer type — it embeds the literal `mcp-server` launch verb as config args
   (`CodexTomlConfigEditor.cs:41`, `JsonConfigEditor.cs:64`). It stays valid as long as the `mcp-server`
   verb survives (it will — `McpServer/McpServerCommand.cs:11` `[Verb("mcp-server", Aliases=["mcp"])]`).

**Internal-visibility surface (the feared risk is small).** Splitting MCP into `Clio.Mcp` surfaces every
core type MCP uses that is not `public`. Measured across core (outside `McpServer/`): **0 explicit
`internal`** types and only **4 implicitly-internal** top-level types out of **2,541** declarations —
i.e. **≈99.8% of core types are already `public`**. The four are `ValidationPackageCommand`
(`Command/PackageCommand/ValidationPackageCommand.cs:29`), `AssemblyCommand`
(`Command/AssemblyCommand.cs:30`), `EnvironmentResult` (`Environment/EnvironmentResult.cs:6`),
`MarketplaceApplicationModel` (`Marketplace/MarketplaceApplicationModel.cs:6`). (For contrast,
`McpServer/` itself has 52 internal types — moving *out* is what is affected, not moving in.)

**DI wiring already survives an assembly boundary.**
- `BindingsModule.Register(..., bool registerMcpHost = false)` (`:145`); when `true` it registers the
  stdio host and calls `RegisterMcpServer` (`:149-156`). `RegisterMcpServer` is `internal static`
  (`:988`) → `services.AddMcpServer(...)` (`:994`) → `McpFeatureToggleFilter.RegisterEnabledPrimitives(...)`
  (`:1000`).
- `McpFeatureToggleFilter.RegisterEnabledPrimitives(IMcpServerBuilder, Assembly assembly, isEnabled)`
  (`McpFeatureToggleFilter.cs:127-141`) **already takes an `Assembly`** and scans it for
  `[McpServerToolType]/[McpServerResourceType]/[McpServerPromptType]`, passing `IEnumerable<Type>` to
  `WithTools/WithResources/WithPrompts` (never `Type[]`/`*FromAssembly` — the known silent-no-op trap).
- The stdio call-tool handler is registered at the stdio call-site, **not** inside the transport-neutral
  `RegisterMcpServer` (`BindingsModule.cs:149-156` comment; matches adr-mcp-durable-invocation D1).
- `Program.cs` dispatches `McpServerCommandOptions => Resolve<McpServerCommand>` (`:594`),
  `McpHttpServerCommandOptions => McpHttpServerCommand.Run` (`:595`), and threads
  `registerMcpHost: IsMcpServerMode` (`:1610`).

**Compatibility surfaces that must not move.**
- ClioRing consumes the StageEvent wire contract but keeps its **own** DTO copy —
  `clio-ring/ClioRing.Ipc/ClioStageEvent.cs` + `ClioStageEventAdapter.cs` — pinned by the committed
  fixture `clio-ring/ClioRing.Tests/Fixtures/ClioStageEvent.contract.ndjson`. Ring does **not** import
  clio's C# type; it depends only on the JSON emitted into `_meta.clioStageEvent`.
- `WorkspaceTemplateGuidanceDriftTests` (`clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs`)
  guards the shipped templates `clio/tpl/workspace/AGENTS.md` and `clio/tpl/workspace/.mcp.json`, which
  name MCP tools.

---

## Decision

Decompose the single `clio` project into three internal assemblies with a strictly one-directional
dependency graph, extracting the embedded MCP server. Ship exactly one product — the `clio` dotnet
tool — with MCP still bundled.

```
                 Clio.Cli  (Exe, PackAsTool, PackageId=clio)  ── the ONE shipped artifact
                  │   │
     references   │   └────────────► Clio.Mcp   (Tools/Prompts/Resources + stdio/HTTP host;
     Core         │                    │          owns ModelContextProtocol[.AspNetCore] +
                  ▼                    ▼          Microsoft.AspNetCore.App + JwtBearer)
                Clio.Core ◄────────────┘
              (behavior library; Common, services, environment/workspace/package/theming,
               + neutral run-mode abstraction + StageEvent/progress contract.
               NO ASP.NET, NO MCP SDK. Not an Exe.)

  Unchanged, already separate:  cliogate    clio-ring (4 projects)    Clio.Analyzers
```

Nothing in `Clio.Core` references `Clio.Mcp`. Only the `Clio.Cli` composition root references
`Clio.Mcp`, and only to register it as the `mcp-server` / `mcp-http` verbs.

### D1 — Target topology: `Clio.Core` / `Clio.Cli` / `Clio.Mcp`

- **`Clio.Core`** — behavior library (today's `Common`, services, `Environment`, `Workspaces`,
  `Package`, `Theming`, command services, etc.) **plus** the two neutral shared contracts lifted out of
  `McpServer/` (run-mode abstraction, D3; StageEvent/progress contract, D4). No `Microsoft.AspNetCore.App`,
  no `ModelContextProtocol*`. `OutputType=Library`.
- **`Clio.Cli`** — the thin executable that **is** the packaged tool: `Program.cs`, command options,
  the DI composition root. Keeps `OutputType=Exe`, `PackAsTool=true`, `PackageId=clio`. References
  `Clio.Core` (and `Clio.Mcp` only at the composition root).
- **`Clio.Mcp`** — the 286 `McpServer/` files (Tools/Prompts/Resources + the stdio and HTTP hosts).
  This is where `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`,
  `FrameworkReference Microsoft.AspNetCore.App`, and `Microsoft.AspNetCore.Authentication.JwtBearer`
  move — **off** `Clio.Core`. References `Clio.Core`.
- `cliogate`, `clio-ring`, `Clio.Analyzers` are untouched.

### D2 — One shipped product; MCP stays bundled (mandate, not a detail)

`Clio.Cli` carries `PackAsTool`/`PackageId=clio`; `CopyLocalLockFileAssemblies=true` (already set,
`clio.csproj:31`) bundles `Clio.Core.dll` and `Clio.Mcp.dll` into the packed tool. MCP is **not**
separately versioned or distributed. `mcp-server` and `mcp-http` remain verbs of the same `clio`
binary. Separate distribution is Alternative A (deferred).

### D3 — Lift bucket 1: replace `Program.IsMcpServerMode` with an injectable run-mode service in Core

Introduce a Core-owned run-mode abstraction (e.g. `IExecutionContext` / `IRuntimeMode` exposing
`IsMcpServerMode`), registered in DI, and have `ConsoleLogger` consume it by constructor injection
instead of reading the static `Program.IsMcpServerMode`. `Program` sets the run-mode value into the
service during startup. **This is a full-unit-suite trigger** per the smart-regression policy
(`Common/ConsoleLogger.cs` + `Program.cs`/`BindingsModule.cs`). Must not introduce CLIO001/CLIO005.

### D4 — Lift bucket 2: move the StageEvent/progress contract into Core, preserving wire parity

- Move the six `McpServer/Progress/*` files into `Clio.Core` (proposed namespace `Clio.Core.Progress`).
  The types are already `public`, so this is a relocation + `using` update, not a rewrite.
- **Preserve JSON wire/byte/schema parity** of the `clioStageEvent` envelope: keep every serialized
  property name, casing, enum string value, and field order. Ring depends only on the JSON
  (its own DTO + `ClioStageEvent.contract.ndjson`), so the C# namespace move is invisible to Ring
  **iff** serialization output is byte-identical. Keep additive/unknown-field tolerance.
- The **MCP-specific** forwarder that pushes events into MCP `_meta`
  (`StageEventProgressForwarder`, and anything referencing `ModelContextProtocol`) **stays in `Clio.Mcp`**;
  only the neutral contract (`ClioStageEvent`, `ClioStageEventContract`, `IStageEventSource`,
  `IStageEventEmitter`, `StageIds`) moves to Core.
- **Sub-leak 2b:** decouple `StartCommand`/`StopCommand` from the SDK type `ProgressNotificationValue`
  — route their status through the Core-owned progress contract (or a neutral Core progress DTO) so
  `Clio.Core` no longer imports `ModelContextProtocol`; the MCP tool wrappers translate to the SDK type
  inside `Clio.Mcp`.

### D5 — Lift bucket 3 (utilities) and neutralize bucket 4 (config templating)

- Relocate into `Clio.Core` (Common): `CompactPrimitiveArrayJsonElementConverter`,
  `McpToolExecutionLock`/`CwdLock`, and `IToolCommandResolver`/`ToolCommandResolver`. Keep their
  interfaces and DI registrations (e.g. `IToolCommandResolver` at `BindingsModule.cs:605`) so no CLIO005
  dead-registration and no CLIO001 manual-`new` is introduced. Behavior classes keep interface + DI
  registration across the boundary.
- Convert the two doc-comment `<see cref>` references (`IProcessGraphValidator.cs:81`,
  `EnvironmentNotFoundError.cs:10`) to text or to a type that lives in the referencing assembly, so they
  do not force a project reference.
- Bucket 4 (`MergeClioMcpServer`) needs **no structural change** — it has no McpServer type dependency;
  just assert in a test that the emitted launch args still spell the `mcp-server` verb.

### D6 — Internal-visibility policy: `InternalsVisibleTo`, not a blanket public promotion

Core is ~99.8% public already, so the surfaced surface is tiny. Policy:
1. Add `[InternalsVisibleTo("Clio.Mcp")]` to `Clio.Core` — mirroring the existing IVT to `clio.tests` /
   `clio.mcp.e2e` (`clio.csproj:49-59`) — as the pragmatic, low-churn bridge for any internal member MCP
   genuinely consumes.
2. **Promote to `public`** only the small set the compiler actually flags (start from the 4 named
   implicit-internal top-level types) when the type is legitimately part of Core's API; do **not** do a
   blanket public sweep.
3. Update the IVT set: types tested by `clio.tests` / `clio.mcp.e2e` now live in `Clio.Core` and
   `Clio.Mcp`, so add the matching `InternalsVisibleTo` to each of those assemblies (not only the old
   `clio` assembly).

The exact list is discoverable at compile time (the compiler enumerates every leak); the IVT safety net
makes an incomplete up-front list a non-blocker rather than a rework.

### D7 — Re-wire DI at the boundary; keep the feature-toggle registration intact

- Keep `McpFeatureToggleFilter.RegisterEnabledPrimitives(...)` exactly as-is, but the `Clio.Cli`
  composition root must pass the **`Clio.Mcp` assembly** (e.g. `typeof(BaseTool).Assembly` or a marker
  type in `Clio.Mcp`) rather than the entry/executing assembly, so the attribute scan finds the tool
  types in their new home. **Do not** reintroduce `WithToolsFromAssembly`/`WithResourcesFromAssembly`/
  `WithPromptsFromAssembly` and **do not** pass `Type[]` — that binds the SDK generic overload and
  registers nothing (project-context.md "MCP registration caveat").
- Keep the stdio call-tool handler registered at the stdio call-site (not in the transport-neutral
  `RegisterMcpServer`).
- `ExperimentalCommand` enumerates MCP primitive types via `McpFeatureToggleFilter.GetAttributedTypes`
  over the MCP assembly; since it lives in `Clio.Cli` which references `Clio.Mcp`, keep it pointed at the
  `Clio.Mcp` assembly.
- `BindingsModule.cs`/`Program.cs` are the DI composition root and a full-suite trigger; run the full
  unit suite and keep CLIO001/CLIO005 clean.

### D8 — Pure move: preserve the MCP tool contract and all compatibility gates

- This is a **pure assembly/namespace move**: it must **not** rename or remove any MCP tool, nor change
  any tool's arguments, defaults, destructive classification, result content, or error envelope. If a
  rename ever becomes necessary, use `McpToolCompatibilityCatalog` (do not leave a dangling name).
- The shipped templates `clio/tpl/**` (`AGENTS.md`, `.mcp.json`) reference tool **names**, which do not
  change; `WorkspaceTemplateGuidanceDriftTests` must stay green with no template edits.
- **ClioRing MCP compatibility gate (mandatory):** preserve `_meta.clioStageEvent` byte/schema parity
  and unknown-field tolerance; run `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`
  and the Windows x64 NativeAOT publish
  `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true`.
  State `ClioRing compatibility reviewed` + results, since the moved surface includes a Ring-consumed
  contract (D4). NativeAOT source-generated `System.Text.Json` on the Ring side is a release invariant.

### D9 — Sequencing: extract `Clio.Mcp` first; defer the Core/Cli split

Land the leak-bucket refactors (D3–D5) and the **`Clio.Mcp` extraction** (D6–D7) **first**, on top of
today's single `clio` assembly (i.e. `clio` temporarily plays both Core and Cli). This removes the
maintainer's actual pain — the ASP.NET/MCP-SDK deps on the plain CLI and the wrong-direction coupling —
with the smallest blast radius, because once the four buckets are lifted the remaining coupling is purely
MCP→Core (a clean one-way reference).

**Defer** the physical `Clio.Core` ↔ `Clio.Cli` split to a follow-up: with `Clio.Mcp` already out and no
core→MCP edges left, separating Core from Cli is largely a project-file + reference exercise (namespaces
stay `Clio.*`, so cross-assembly `using`s are unaffected — see Consequences) and can be done
incrementally with far lower risk than doing all three at once. This is a recommendation, not a
constraint (Open Question Q2).

---

## Alternatives considered

- **A — Separate distributable products** (MCP shipped as its own package/tool, versioned
  independently). **Deferred.** It touches the release pipeline and the Ring compatibility surface with
  no current need; the mandate is one shipped `clio` tool. Cleanly separated assemblies (this ADR) are
  the prerequisite that makes A cheap to revisit later if a real need appears.
- **B — Status quo** (leave MCP in core). **Rejected.** The clutter, the wrong dependency direction, and
  the ASP.NET/OAuth/MCP-SDK dependencies forced onto every plain-CLI install are exactly the problem.
- **C — Move the 286 files to a new assembly without first lifting the four leak buckets.**
  **Rejected / infeasible.** Core references `McpServer` types (buckets 1–3) while MCP references core
  everywhere; a straight move yields a **circular** `Clio.Core ↔ Clio.Mcp` project reference, which does
  not compile. The bucket lifts (D3–D5) are the enabling prerequisite.
- **D — Make `Clio.Core` reference the MCP SDK / ASP.NET** (so the StageEvent forwarder and
  `ProgressNotificationValue` can stay). **Rejected.** It defeats the purpose: the plain CLI would still
  drag ASP.NET/MCP-SDK, and Core would still know about MCP. Neutral contracts in Core + SDK translation
  in `Clio.Mcp` (D4) is the correct seam.
- **E — Blanket-promote all core types to `public`** to dodge the internals question. **Rejected.**
  Unnecessary given ~99.8% are already public; `InternalsVisibleTo("Clio.Mcp")` (D6) covers the
  remainder without widening the intended API surface.

---

## Consequences

**Positive**
- The plain `clio` CLI no longer compiles/carries `Microsoft.AspNetCore.App`, `JwtBearer`,
  `ModelContextProtocol`, or `ModelContextProtocol.AspNetCore` — those move to `Clio.Mcp`.
- One-directional dependency graph (`Clio.Cli → Clio.Core`, `Clio.Mcp → Clio.Core`, `Clio.Cli → Clio.Mcp`
  at the composition root only); the "clutter" of a bidirectional edge is gone.
- `McpServer/` (≈30% of the code) becomes independently navigable/buildable/testable as `Clio.Mcp`,
  matching how `cliogate` / `clio-ring` / `Clio.Analyzers` already live.
- Run-mode and the StageEvent contract land where they belong (Core), so a genuinely core concern is no
  longer imported from a folder named `McpServer`.
- Sets up (without committing to) later independent distribution of MCP (Alternative A).

**Negative / trade-offs**
- Multi-project build/restore for what was one project; `BindingsModule.cs`/`Program.cs` composition-root
  churn is a full-unit-suite trigger (accepted; run the full suite).
- The `Clio.Mcp` extraction step temporarily leaves `clio` as both Core and Cli (D9) until the follow-up
  split — a deliberate, lower-risk intermediate state.
- A one-time `InternalsVisibleTo` re-point across `clio.tests` / `clio.mcp.e2e` and a small handful of
  type promotions.

**Breaking change: No (for tool users).** Exactly one `clio` dotnet tool still ships, same `PackageId`,
same verbs, same MCP tools/args/contracts (D8). **Internal-only caveat:** the assembly layout changes.
Anything that launches the binary or references it by assembly file name — e.g. the AGENTS.md build/deploy
steps that call `dotnet clio/bin/Release/net10.0/clio.dll …` — depends on the tool assembly file name;
Q1 (below) decides whether to keep it `clio.dll`. No `RELEASE.md`-worthy user-facing behavior changes.

**Namespace stability (important simplification).** An assembly may contain any namespace, so types keep
their `Clio.*` namespaces when they move (`Clio.Common` can live in `Clio.Core.dll` while staying
`namespace Clio.Common`). Therefore the ~101 `using Clio.Common` statements in MCP and most other
cross-assembly `using`s are **unaffected** by the move — only project references change. The only forced
namespace edits are the leak-bucket types deliberately renamed for clarity (e.g. StageEvent →
`Clio.Core.Progress`), which is mechanical.

---

## Scope / work items (for stories)

Ordered; each item maps to one or more future stories. Phases 1–4 are the `Clio.Mcp` extraction; Phase 5
is the deferred Core/Cli split.

1. **Phase 1 — run-mode service (bucket 1).** Add Core `IExecutionContext`/`IRuntimeMode`
   (`IsMcpServerMode`), register in DI, inject into `ConsoleLogger`; `Program` sets it at startup; remove
   the `Program.IsMcpServerMode` static reads from `Common`. Unit tests for logger suppression on/off.
   **Full-unit-suite trigger.**
2. **Phase 2 — StageEvent contract to Core (bucket 2 + 2b).** Relocate the six `Progress/*` files to
   `Clio.Core` (`Clio.Core.Progress`), keep the MCP `_meta` forwarder in `Clio.Mcp`; decouple
   `StartCommand`/`StopCommand` from `ProgressNotificationValue`. Add a serialization byte/schema-parity
   test against the current envelope; run the ClioRing gate (D8).
3. **Phase 3 — relocate utilities + config-templating check (buckets 3, 4).** Move
   `CompactPrimitiveArrayJsonElementConverter`, `McpToolExecutionLock`/`CwdLock`,
   `IToolCommandResolver`/`ToolCommandResolver` into Core/Common (keep interfaces + DI registrations);
   convert the two `<see cref>` doc refs; add a test asserting `MergeClioMcpServer` still emits the
   `mcp-server` verb.
4. **Phase 4 — create `Clio.Mcp` project.** New `Clio.Mcp.csproj`; move `Command/McpServer/**` into it;
   relocate the ASP.NET + MCP-SDK package/framework references off core onto `Clio.Mcp`; apply the
   internal-visibility policy (D6: `InternalsVisibleTo("Clio.Mcp")` on the core assembly + minimal public
   promotions); re-point `McpFeatureToggleFilter.RegisterEnabledPrimitives` and `ExperimentalCommand`
   at the `Clio.Mcp` assembly; keep the stdio call-tool handler at the stdio site; re-point
   `clio.tests` / `clio.mcp.e2e` `InternalsVisibleTo`. Add an MCP host-startup / tool-count validation
   test so a mis-pointed assembly scan fails loudly instead of registering zero tools.
5. **Phase 5 — create `Clio.Core` / `Clio.Cli` boundary (deferred, follow-up).** Split the remaining
   single assembly into `Clio.Core` (library) and `Clio.Cli` (`Exe`, `PackAsTool`, `PackageId=clio`,
   references `Clio.Core` + `Clio.Mcp`). Verify the packed tool still bundles all three DLLs and packs as
   `clio`. Namespaces stay `Clio.*`.
6. **Cross-cutting — verification & docs.** Full unit suite (composition-root touched); ClioRing gate
   commands (D8); confirm `WorkspaceTemplateGuidanceDriftTests` green with no template edits; MCP review
   note "pure move, no tool-contract change"; update any csproj/solution docs. Confirm net8.0 + net10.0
   dual-target holds for all new projects.

---

## Risks

- **R1 — internal-visibility surprises beyond the 4 named types** (member-level/nested internals MCP
  touches). *Mitigation:* the D6 `InternalsVisibleTo("Clio.Mcp")` safety net makes this a compile-time
  itemization, not a blocker; promote only what is legitimately public API.
- **R2 — StageEvent JSON parity regression breaks ClioRing.** *Mitigation:* freeze serialization
  attributes/property names on the move; parity test + `ClioStageEvent.contract.ndjson` + NativeAOT
  publish (D8).
- **R3 — feature-toggle scan silently registers zero MCP primitives** (the `Type[]`/`*FromAssembly`
  trap, or pointing the scan at the wrong assembly). *Mitigation:* pass the `Clio.Mcp` assembly
  explicitly; keep `IEnumerable<Type>`; add a host-startup tool-count assertion.
- **R4 — CLIO005 dead registrations / CLIO001 manual-`new`** introduced while relocating DI wiring.
  *Mitigation:* move interface + registration together; keep constructor injection; full-suite run.
- **R5 — assembly-identity ripple** to `InternalsVisibleTo` consumers (`clio.tests`, `clio.mcp.e2e`) and
  to anything that names `clio.dll`. *Mitigation:* Q1 decision + update the IVT sets in Phase 4/5.
- **R6 — pack/tool regression.** *Mitigation:* `CopyLocalLockFileAssemblies=true` already bundles project
  refs; verify `dotnet pack` output contains `Clio.Core.dll` + `Clio.Mcp.dll` and the tool runs
  post-split on both target frameworks.

---

## Resolved decisions (Q1–Q5 — maintainer accepted the recommended answers, 2026-07-22)

All five open questions were resolved by the maintainer accepting the architect's recommendations. They
are now binding inputs for story-writing / implementation:

- **Q1 — Tool assembly name → RESOLVED: keep `clio.dll`.** The packed tool keeps assembly file name
  `clio.dll` (preserves `dotnet clio.dll …` invocations in AGENTS.md/CI and minimizes `InternalsVisibleTo`
  churn); the project is named `Clio.Cli`; `PackageId` stays `clio`.
- **Q2 — Sequencing → RESOLVED: D9 accepted.** Extract `Clio.Mcp` first (Phases 1–4) on top of today's
  single `clio` assembly; defer the physical `Clio.Core` ↔ `Clio.Cli` split (Phase 5) to a follow-up.
- **Q3 — StageEvent namespace → RESOLVED: rename to `Clio.Core.Progress`.** The moved contract adopts the
  `Clio.Core.Progress` namespace (mechanical `using` churn in the ≈4 emitters + MCP consumers), preserving
  JSON wire/byte/schema parity per D4.
- **Q4 — MCP HTTP host → RESOLVED: `Clio.Mcp` owns ASP.NET for v1.** `Microsoft.AspNetCore.App` +
  `JwtBearer` live in `Clio.Mcp`; **no** separate `Clio.Mcp.Http` assembly for now. A stdio-only-footprint
  split may be revisited later if warranted; it does not block this ADR.
- **Q5 — Dual-target → RESOLVED: confirmed.** All new projects multi-target `net8.0` + conditional
  `net10.0` (matching `clio.csproj:5-6`); `ModelContextProtocol.AspNetCore` + `Microsoft.AspNetCore.App`
  resolve on both frameworks in `Clio.Mcp` only.
