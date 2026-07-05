# Minimum Creatio version gate — implementation plan

Status: **planning (no code yet)**
Branch: `feature/min-creatio-version-attribute`
Author: Kirill's Claude (peer review by Codex per team conduct)

## Goal

Some clio commands only work against Creatio **10+**. Add a declarative gate that
checks the **target environment's Creatio core version** before a command runs.
If the command needs a newer Creatio than the environment provides, **block
execution** and tell the user their Creatio is too old and to update — exactly
analogous to the existing **ClioGate** minimum-version gate, but for the core
application.

## What already exists (reuse, don't reinvent)

- **ClioGate gate = generic `[RequiresPackage(name, version)]`** + `IRequiredPackageChecker`
  (`clio/Common/RequiresPackageAttribute.cs`, `RequiredPackageChecker.cs`,
  `PackageRequirementException.cs`). Enforced at:
  - CLI dispatch chokepoint `Program.cs ExecuteCommandWithOption` (order: feature gate →
    package gate → execute; zero-cost pre-check via `RequiresPackageAttribute.IsDefinedOn(Type)`).
  - MCP `BaseTool.EnforcePackageRequirements()` — resolves `IRequiredPackageChecker`
    from the **per-call environment-scoped** container, before the execution lock.
- **Core version source**: `get-info`/`describe` (`GetCreatioInfoCommand`) +
  `PlatformVersionResolver` already dual-probe (cliogate `GetSysInfo.CoreVersion`,
  fallback `ApplicationInfoService` `coreVersion`) and parse with `System.Version`
  (4-part → 3-part normalisation helper exists).
- **Precedent for the message + dev bypass**: `DataForgePlatformVersionGuard`
  (requires 10.0.0, bypasses dev build `0.0.0.0`, caches result, friendly text).

## Recommended design — sibling attribute, shared plumbing

Add `[RequiresCreatioVersion("10.0.0", Hint = "...")]` on a command's **options class**
(same placement convention as `[FeatureToggle]` / `[RequiresPackage]`), plus an
`ICreatioVersionChecker` service. **Not** a reuse of `[RequiresPackage]`, because the
core version does NOT come from the installed-package list
(`IApplicationPackageListProvider`) — it comes from SysInfo/ApplicationInfo. A sibling
attribute keeps the version-source seam clean while reusing the **exact** enforcement
plumbing.

Components:

| Component | File | Notes |
|---|---|---|
| Attribute | `clio/Common/RequiresCreatioVersionAttribute.cs` | `(string minVersion, string Hint=null)`; `IsDefinedOn(Type)` static pre-check |
| Checker | `clio/Common/CreatioVersionChecker.cs` (+ `ICreatioVersionChecker`) | fetch CoreVersion, `Version.TryParse`, compare; dev-build `0.0.0.0` bypass; cache per env |
| Version source | reuse `PlatformVersionResolver` probe (cliogate GetSysInfo → ApplicationInfo fallback) | works with or without cliogate |
| Failure | reuse `PackageRequirementException` (or sibling) | "This command requires Creatio {min}+. Your environment runs {actual}. Update Creatio and retry." + Hint |
| CLI enforcement | `Program.cs ExecuteCommandWithOption` | new step: feature gate → **creatio-version gate** → package gate → execute; zero-cost when attribute absent |
| MCP enforcement | `BaseTool` env-sensitive path | resolve `ICreatioVersionChecker` from per-call env container (like the package checker), enforce before exec lock |
| DI | `BindingsModule.cs` | register `ICreatioVersionChecker` |

## Decisions to confirm with Kirill

1. **Fail-open vs fail-closed when version is undeterminable** (no cliogate, probe fails):
   block (safe) or allow-with-warning? `RequiresPackage` fails closed; the MCP
   platform-version resolver degrades to "latest". **Recommend fail-closed for a hard
   gate**, with a distinct "could not determine version" message.
2. **Comparison granularity**: full `System.Version` (`10.0.0` ≤ `10.0.0.751`) vs Major-only.
   Recommend full `System.Version` with missing parts clamped to 0.
3. **Attribute placement** on options class — confirm (matches existing conventions).
4. Reuse `PackageRequirementException` vs a dedicated `CreatioVersionRequirementException`.

## Mandatory surfaces (repo policy)

- **MCP**: env-scoped checker in `BaseTool`; gated tools' `[Description]` mentions the
  version requirement; add `clio.mcp.e2e` coverage.
- **Docs**: contributor docs for the attribute; per-command `help/en`, `docs/commands`,
  `Commands.md` only where a gated command's behavior changes.
- **Tests** (`[Category("Unit")]`, AAA + because + `[Description]`): checker
  (compatible / too-old / dev-build / unparseable / undeterminable), CLI chokepoint
  (blocked → exit 1 + message), MCP BaseTool path. Targeted filter
  `Category=Unit&(Module=Common|Module=Command|Module=McpServer)`.
- **Analyzers**: keep CLIO* clean; register checker in DI (CLIO005).

## Commit cadence (frequent, for reviewers)

1. this plan doc · 2. attribute + exception · 3. checker + version source + DI ·
4. CLI chokepoint wiring + unit tests · 5. MCP wiring + E2E · 6. docs.
Push after each; Codex reviews.
