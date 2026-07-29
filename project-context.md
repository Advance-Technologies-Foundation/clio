# Clio Project Context

> This file is loaded by every BMAD agent at activation. It contains Clio-specific rules
> that cannot be derived from reading the code alone. Keep it current.

---

## Project Identity

| Field         | Value                                              |
|---------------|----------------------------------------------------|
| Project       | Clio — CLI tool for Creatio platform integration   |
| Language      | C# 12 / .NET 10                                    |
| Type          | Global dotnet tool plus optional ClioRing companion |
| Repo          | `Advance-Technologies-Foundation/clio`             |
| Main solution | `clio.slnx` (ClioRing is the `/clio-ring/` group)  |

---

## Architecture Rules (non-obvious)

### Command pattern
- **Do NOT use MediatR.** MediatR has been fully removed from the codebase — do not
  add new `Request` + `Handler` pairs and do not route new commands through MediatR.
- New CLI commands derive from `Command<TOptions>` (see `clio/Command/Command.cs`),
  take their dependencies via **constructor injection**, and call collaborating
  **services** directly. Existing command families such as the skill commands
  (`clio/Command/SkillCommands.cs`) are the reference pattern.
- Commands are registered via DI in `clio/BindingsModule.cs` and wired as verbs in
  `clio/Program.cs` — never forget to register.
- `IApplicationClient` / `CreatioClient` is the ONLY way to talk to Creatio HTTP API — never use raw `HttpClient`
  - **Narrow sanctioned exception — binary uploads/reads the JSON surface cannot carry.**
    `IApplicationClient` only sends/receives string bodies, and `Creatio.Client.UploadFile`
    cannot set a caller-chosen `fileId` (it appends its own query string). Flows that need a raw
    binary body with `Content-Range`/`Content-Disposition`/CSRF headers or a byte-exact binary
    read may use a named `IHttpClientFactory` client with cookies from `ICreatioAuthClient`.
    `Clio.Common.SysImageUploader` is the sanctioned reference example; any new deviation must
    justify itself against this bullet and follow the same pattern.

### CLI flag naming — HARD RULE
- **All CLI option names must be kebab-case**: `--package-name`, not `--packageName` or `--PackageName`
- This is enforced by Roslyn analyzer CLIO001; violations fail the build
- ~65 existing violations are tracked in `spec/cli-naming/camelcase-violations.md` — do not add new ones
- When renaming a flag, add an alias for the old name (breaking change policy)

### Roslyn analyzers (CLIO001-CLIO005)
- CLIO001: CLI option naming (kebab-case)
- CLIO002-CLIO004: see `Clio.Analyzers/` for details
- CLIO005: DI service registered but never injected/resolved (catches dead DI registrations)
- All analyzer warnings are treated as errors in CI

### Using [ResolvedDynamically]
- **WHAT:** `Clio.Common.ResolvedDynamicallyAttribute` marks a class or interface that IS used
  but is resolved by a mechanism CLIO005 (a single-compilation analyzer) cannot see — reflection /
  assembly scanning, or resolution from another assembly (e.g. `clio.mcp.server`). It tells CLIO005
  the registration is alive.
- **WHEN to use:** ONLY when CLIO005 flags a registration that is genuinely consumed via
  reflection / cross-assembly / dynamic resolution with no statically-visible injection or
  `GetService` / `GetRequiredService` / `GetServices<T>` call. Apply `[ResolvedDynamically]` to the
  service or the implementation type.
- **WHEN NOT to use:** do NOT use it to silence CLIO005 on a genuinely-dead registration. If nothing
  actually resolves the type, REMOVE the DI registration (and the dead type) instead. Prefer fixing
  (inject/resolve, or delete) over suppressing.
- CLIO005 already auto-exempts types reflection-instantiated via `[McpServerTool]` methods and types
  that implement a consumed interface — so the attribute is only for cases outside those.

### MCP server
- `clio.mcp.server/` exposes CLI commands over Model Context Protocol
- When adding a CLI command, consider whether it should also be an MCP tool
- MCP tool names follow the same kebab-case convention
- MCP capability map: `docs/McpCapabilityMap.md`

### Feature toggles (experimental / not-for-public commands)
- **WHAT:** mark a command's **options class** (the one carrying `[Verb]`) with `[FeatureToggle("feature-key")]` to hide it behind a runtime flag. While the flag is off the command is fully invisible and unreachable on every surface — it behaves exactly like a non-existent verb. A command with **no** `[FeatureToggle]` is always available (default, unchanged).
- **Key, not verb:** the string is a stable *feature key*, decoupled from the verb. One key can gate several commands, and the verb can be renamed without touching the flag. Keys are compared **case-insensitively**.
- **Opt-in / fail-closed:** an absent, false, or malformed flag ⇒ disabled. Never rely on a default-on flag for something experimental.
- **Where flags live:** the `features` object in clio's `appsettings.json`. There is **no** environment-variable override — manage flags with `clio experimental` (lists all known keys + state), `clio experimental --name <key> --enable`, and `... --disable`. Enable a feature locally before testing a gated command.
- **The single rule** is `Clio.Command.IFeatureToggleService.IsEnabled(Type)`. It is enforced at four enumeration surfaces, all sharing that one predicate — never re-implement the check:
  1. CLI argument parsing (`Program.CommandOption` is filtered before `Parser.ParseArguments`).
  2. CLI help **and** generated public docs (`CommandHelpRenderer`; docs use a deterministic export baseline so committed docs never depend on local flags — gated commands are omitted from `Commands.md` / help / wiki until the feature ships).
  3. The dispatch chokepoint `Program.ExecuteCommandWithOption` (this also blocks the scenario runner, which dispatches around the parser).
  4. MCP tool/resource/prompt registration.
- **MCP requires its own attribute — this is the easy thing to get wrong.** MCP tool/resource/prompt classes are discovered by attribute scan **separately** from CLI options classes. Marking only the options class hides the command from the CLI but leaves its MCP tool exposed. To gate the MCP surface too, put the **same** `[FeatureToggle("feature-key")]` on the corresponding `[McpServerToolType]` / `[McpServerResourceType]` / `[McpServerPromptType]` class.
- **MCP registration caveat (do not regress):** gated MCP types are registered via `McpFeatureToggleFilter.RegisterEnabledPrimitives`, which passes `IEnumerable<Type>` to the SDK's `WithTools` / `WithResources` / `WithPrompts`. Never pass a `Type[]` to those methods and never revert to `*FromAssembly`: a `Type[]` binds to the SDK's generic `WithX<T>(T)` overload and silently registers **nothing**. Do not add an abstract/open-generic type exclusion — the SDK enumerates such types and `BaseTool<T>` is inert by design.
- **Do not** put `[FeatureToggle]` on the management command (`experimental`) itself, and do not gate a shipping command unless you intend it to be hidden by default.

### ClioRing companion architecture (internal preview)

ClioRing is the optional Windows desktop companion under `clio-ring/`. It is an internal
employee preview, not another way to package the `clio` global tool. The experiment may be
removed if user feedback does not justify maintaining it, so its boundary with clio must remain
deliberately narrow and reversible.

#### Identity

- The product, command, executable, release assets, and intended .NET identity are **ClioRing** / `clio-ring`.
- The project group is `ClioRing`, `ClioRing.Desktop`, `ClioRing.Ipc`, and `ClioRing.Tests`.
  Keep project paths, assembly identities, namespaces, tests, workflow paths, and solution entries
  aligned with that identity. Do not introduce alternative product-family names.
- Ring has independent `0.x` preview versions. A clio tool release and a Ring release are not
  required to share a version or ship together.

#### Ownership and dependency direction

- `clio/` owns the CLI, MCP server, environment configuration, and the feature-gated `clio ring`
  lifecycle bootstrap. It must not reference Avalonia or Ring application assemblies.
- The Ring application owns UI, interaction state, pipeline rendering, and desktop-only
  orchestration. It consumes clio through supported process/MCP contracts; it must not call
  command classes, DI internals, or Creatio clients by project reference.
- The desktop host owns OS startup and packaged configuration. Keep platform-specific behavior
  at this edge rather than in view models or protocol DTOs.
- The IPC project is a quarantine boundary around the reflection-heavy ModelContextProtocol SDK.
  AOT projects may consume its plain interfaces and immutable record DTOs, but SDK types,
  reflection-based serialization, and MCP client implementation details must not escape it.
- Tests and harnesses may reference application and IPC contracts, but production assemblies
  must never reference test or harness projects.

The intended dependency flow is one-way:

```text
clio-ring desktop -> Ring UI/application -> Ring IPC contract -> clio process/MCP surface
clio CLI/bootstrap -------------------------------------------> GitHub Ring release assets
```

There is no reverse dependency from clio core to the Ring UI. Shared behavior belongs in an
explicit stable protocol contract, not in a new shared implementation assembly.

Independent release cadence requires consumer-driven contract validation: clio provider changes
that touch a Ring-consumed MCP tool, nested `clio-run` command, progress event, receipt, or error
envelope must run Ring compatibility tests before release. This is protocol governance, not code
coupling. Ring must remain compatible with the prior released clio contract where practical, and
clio must preserve the prior released Ring through additive evolution or an explicitly versioned
transition; the two products must never require an atomic upgrade.

#### Protocol and NativeAOT rules

- **NativeAOT compatibility is a release invariant, not an optional optimization.** The shipped
  Windows x64 application is the output of `dotnet publish ... -p:PublishAot=true`. Every Ring
  change must preserve a successful NativeAOT publish with zero IL2026/IL3050 trim/AOT warnings.
  A normal JIT build or passing unit tests does not prove a Ring change is complete.
- Typed MCP `_meta` stage events and deployment receipts are the pipeline source of truth. Do
  not scrape console text to infer progress or success.
- MCP progress callbacks can be concurrent and out of order. Consumers must de-duplicate by
  `(runId, sequence)` and buffer until the next contiguous sequence; never implement a simple
  `sequence <= maxSeen` filter because it can discard the manifest at sequence zero.
- A run is successful only after its explicit successful terminal event/receipt. Process exit,
  silence, or the presence of ordinary `message` fields must never fabricate success.
- All JSON used by NativeAOT code must use source-generated `System.Text.Json` metadata. Do not
  introduce reflection-based serialization, runtime assembly scanning, dynamic code generation,
  or silence trimming/AOT warnings in Ring production projects.
- Keep DTOs backwards tolerant: ignore unknown fields, make additive fields optional, and treat
  removal/semantic changes as a coordinated clio/Ring protocol migration.

#### Environment ownership, privacy, and destructive actions

- Clio's environment settings are the source of truth. Ring may cache only for presentation and
  must refresh on summon, manual refresh, watched settings-file changes, and successful
  deploy/uninstall completion. Ring must not invent a second environment store.
- Never log or persist passwords, auth headers, connection strings, decrypted credentials, or
  unredacted secrets. Ring has **no telemetry** while it is an internal preview.
- Deploy and uninstall require an explicit user gesture and immutable confirmation of the target.
  Agents, probes, startup recovery, and background refresh must never initiate a real destructive
  operation. Automated happy-path harnesses must require an explicit disposable target/build.

#### Distribution and deletion boundary

- `clio` remains delivered as a global dotnet tool. Ring is a Windows x64 NativeAOT ZIP hosted on
  GitHub Releases and installed under `%LOCALAPPDATA%\Creatio\clio-ring` by `clio ring`.
- GitHub release authority for `Advance-Technologies-Foundation/clio` is the preview publisher
  trust root. Bootstrap changes must retain exact-host/asset validation, SHA-256 verification,
  archive and download bounds, ZIP traversal protection, downgrade refusal, lifecycle locking,
  same-version repair, and locked-executable uninstall preflight.
- Do not add clio configuration or environment migrations solely for Ring. Removing the experiment
  must remain possible by deleting `clio-ring/`, its release workflow, and the small Ring lifecycle
  command/service/docs/test surface without changing existing clio environments.

---

## Testing Rules (critical)

### Test categories — THREE TIERS ONLY

| Category      | Attribute                   | Meaning                                          | Runs on          |
|---------------|-----------------------------|--------------------------------------------------|------------------|
| `Unit`        | `[Category("Unit")]`        | No I/O, no external deps, NSubstitute mocks only | Every push       |
| `Integration` | `[Category("Integration")]` | File system, DB, IIS, K8s stubs                  | PR merge         |
| `E2E`         | `[Category("E2E")]`         | Real clio process, MCP protocol, real Creatio    | Release / manual |

**NEVER use**: `[Category("UnitTests")]`, `[Category("CommandTests")]`, or any other string.
These are legacy violations — do not replicate them.

### Test naming — MANDATORY format
```
MethodName_ShouldExpectedBehavior_WhenCondition
```
Examples: `Execute_ShouldReturnPackageList_WhenEnvironmentIsValid`
Never: `Test1`, `TestMethod`, `ShouldWork`, `MyTest`.

### Test framework
- **NUnit 4.5.1** — test runner
- **FluentAssertions 7.2.0** — assertions (`result.Should().Be(expected)`)
- **NSubstitute 5.3.0** — mocking (`Substitute.For<IInterface>()`)
- Coverage: Coverlet (OpenCover format), no gates enforced yet

### Test file locations
- Unit + Integration: `clio.tests/{FeatureName}Tests.cs`
- MCP E2E: `clio.mcp.e2e/`
- MCP E2E tests are NOT in CI yet — always flag this in test plans

---

## Code Style

- StyleCop rules: see `stylecop.json`
- No hardcoded strings for user-facing messages — use constants or resource references
- Error messages must be user-friendly: "Error: package 'Foo' not found" not stack traces
- All public APIs need XML doc comments if they are part of the public interface

---

## CI/CD

- CI: GitHub Actions, self-hosted Windows runner
- Workflows: `.github/workflows/`
- Build script: `build.ps1` (PowerShell) / `build.cmd`
- PR check: `check-pr.sh` / `check-pr.ps1`
- Release: `create-release.ps1` / `create-release.sh`
- NuGet package version: managed in `Directory.Packages.props`

---

## Key Files for Agents

| Purpose               | Path                                                    |
|-----------------------|---------------------------------------------------------|
| QA strategy           | `spec/qa-automation-strategy/qa-automation-strategy.md` |
| CLI naming violations | `spec/cli-naming/camelcase-violations.md`               |
| MCP capability map    | `docs/McpCapabilityMap.md`                              |
| Contributing guide    | `CONTRIBUTING.md`                                       |
| Release notes guide   | `RELEASE.md`                                            |
| BMAD sprint status    | `spec/sprint-status.yaml`                               |

---

## What NOT to do

- Do not use `HttpClient` directly — use `IApplicationClient`
- Do not add `[Category("UnitTests")]` — use `[Category("Unit")]`
- Do not add camelCase CLI flags — always kebab-case
- Do not create tests without a category attribute — uncategorized tests run on every push unexpectedly
- Do not skip Roslyn analyzer fixes — they are build errors in CI
- Do not write bare `catch (Exception)` — handle specifically or rethrow
