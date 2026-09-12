---
description: the SchemaNamePrefix rule for a locally generated schema is NOT enforced by push-pkg install or compile-configuration - both accept an unprefixed schema - the source-code schema designer and the file-system load path are what refuse it, Usr is only the default value of the setting, and the environment read is capped by clio at 30 s
applies-to:
  - clio/Common/SchemaNamePrefixResolver.cs
  - clio/Common/SysSettingCodes.cs
  - clio/Package/PackageCreator.cs
  - clio/Command/AddPackageCommand.cs
ticket: GH-1309
date: 2026-09-05
---

**What is true** — Creatio refuses a custom schema whose code does not start with the value of the
`SchemaNamePrefix` system setting: `The "XLocalizableStrings" code of the "XLocalizableStrings"
object must start with the "Usr" prefix`. Three things the message hides:

- **Not every path enforces it.** Measured on stand1 (10.1.725, .NET Framework, prefix `Usr`): a
  package carrying an UNPREFIXED source-code schema installed through `clio push-pkg`, and
  `clio compile-configuration` then ran clean. `SourceCodeSchemaDesignerService` DOES enforce it,
  verbatim — `clio create-schema` with an unprefixed name is refused, and case-sensitively
  (`usrFoo` is refused too, so the already-prefixed check must stay `StringComparison.Ordinal`).
  Issue #1309 hit the error at its step 4 — linking the package from the file system and compiling
  from the UI; that path needs server file-system access and has not been re-measured. Never treat a
  green install or compile as proof that a generated schema is acceptable; that is not the path that
  checks.
- `Usr` is the out-of-the-box **value of the setting**, not a platform constant — clio's own MCP e2e
  stands run with `ClioMcp_`.
- The rule applies to the **schema**, not the package: #1309's `MarketDataModel` package installed
  fine; only the schema inside it was refused.

**Why it is this way** — `add-package` is a local generator with `RequiredEnvironment => false`, so it
cannot know the prefix without asking an environment and must still work without one:
`AddPackageToolE2ETests` runs it under `McpE2E.NoEnvironment`, `EnvironmentScopedCommandExecutor`
routes an env-less `AddPackageOptions` to a container with no URI on purpose, and a fresh workspace has
no environment yet. Failing would break all three, so an unresolvable prefix degrades to
"generate unprefixed and warn".

**What breaks if you ignore it** — the package installs and compiles without complaint and then blocks
the developer the moment it is loaded from the file system, naming a prefix nobody chose. Two traps
when changing this code: hard-coding `Usr` produces a schema a `ClioMcp_` stand refuses just as hard;
and reading the setting is a network request on a command that had none.

How long that request can take was measured, not assumed. Against a host that silently drops packets:
~75 s. Against a socket that accepts the TCP connection and never answers: **115 s**, then a clean
`Network error…` warning and an unprefixed package — bounded, not unbounded. The bound is incidental,
consistent with the 100 s default `Timeout` of the HTTP stack underneath (`CreatioClient.Login()` sets
none of its own), so it is not a number clio controls. `SchemaNamePrefixResolver` therefore imposes its
own 30 s wall-clock budget (`DefaultReadBudgetSeconds`) and degrades on expiry.

Two traps in that budget. It is enforced by waiting on a task, NOT by a `CancellationToken`: an expiry
surfacing as `OperationCanceledException` would be indistinguishable from a caller cancelling the
command, and the resolver must degrade for the first and stop for the second. And a transport timeout
arrives as `TaskCanceledException`, which IS an `OperationCanceledException` — so the carve-out is
`SysSettingCodes.ClassifyReadFailure`, which calls that shape `Network`, not `Cancelled`.

One more measured trap for anyone testing this by hand: `add-package` with `-u`/`-e` outside a
populated workspace exits **1** regardless of the prefix, from the `DconfChainItem` follow-up
(`Could not find a part of the path '…/packages'`). It fires with no `--as-app` and with an explicit
prefix too, so it is not evidence about the prefix read.

`--schema-name-prefix` is the path that makes no request.
