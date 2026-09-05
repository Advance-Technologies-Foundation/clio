---
description: the SchemaNamePrefix rule for a locally generated schema is NOT enforced by push-pkg install or compile-configuration - both accept an unprefixed schema - it fires on the file-system load path, and Usr is only the default value of the setting
applies-to:
  - clio/Common/SchemaNamePrefixResolver.cs
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
  `clio compile-configuration` then ran clean. Issue #1309 hit the error at its step 4 — linking the
  package from the file system and compiling from the UI. Never treat a green install or compile as
  proof that a generated schema is acceptable; that is not the path that checks.
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
and reading the setting is a network request on a command that had none, measured at ~75 s against a
host that silently drops packets and unbounded against one that accepts the connection and never
answers, because `CreatioClient.Login()` sets no timeout. `--schema-name-prefix` is the path that
makes no request.
