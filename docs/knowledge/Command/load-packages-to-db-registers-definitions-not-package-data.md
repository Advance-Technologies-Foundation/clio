---
description: pkg-to-db (LoadPackagesToDB) registers package definitions in the configuration database and never installs Data/ binding rows into their target table; before this record's change every one of its failure branches also exited 0
applies-to:
  - clio/Package/FileDesignModePackages.cs
  - clio/Command/LoadPackagesToDbCommand.cs
  - clio/Command/LoadPackagesToFileSystemCommand.cs
  - clio/Command/TurnFsmCommand.cs
  - clio/tpl/workspace/AGENTS.md
ticket: gh-952
date: 2026-09-06
---

**What is true** — `pkg-to-db` posts `AppInstallerService.svc/LoadPackagesToDB`, which registers package
CONTENT (schemas, resources, descriptors) in the configuration database. It never installs package DATA.
The endpoint answers with a bare `BaseResponse` (`success` + `errorInfo`), so no row count exists for it to
report. Data installation lives on a different service — `PackageInstaller` posts
`PackageInstallerService.svc/InstallPackage` with `installPackageData`, surfaced as
`push-pkg --install-package-data`. On an FSM environment, where the shipped workspace template forbids
`push-workspace`, the operation that applies a binding row is `create-data-binding-db` /
`upsert-data-binding-row-db`, which write the live row and register it in the package.

Until this record's change `LoadPackagesToStorage` returned `void` and reported every failure through
`ILogger.WriteLine`, so `pkg-to-db` and `pkg-to-file-system` exited 0 in all four branches: success,
FSM disabled, a platform error, and a failed file-design-mode probe. Over MCP that meant `exit-code: 0`
with `message-type: "None"` — both published failure signals of the `command-execution-result` contract
negative — while nothing had been loaded.

The loader now answers with `FileDesignModeLoadResult`, not a bool, because one caller must react to the
three failure causes differently. `turn-fsm off` imports the packages BEFORE it writes the configuration;
an environment that already reports file design mode as disabled had nothing to import and is that
command's goal state, so it continues to `set-fsm-config` and exits 0, while a refused load or an
unreadable file design mode state aborts with the configuration untouched. The enum's zero value is
deliberately a failure (`LoadRefused`): NSubstitute returns `(TEnum)0` for an unstubbed call, and a zero
meaning "completed" would push every test with an unstubbed loader onto the happy path without a word.

**Why it is this way** — the two operations belong to different platform services, and the FSM import
service has no data-installation switch to turn on. Reporting a row count from `pkg-to-db`, as GitHub
issue #952 proposed, cannot be built on this endpoint at all. The silence was the older habit of treating
a remote refusal as a log line rather than a command verdict.

**What breaks if you ignore it** — an agent edits `Data/<Binding>/data.json` in an FSM workspace, runs
`pkg-to-db`, is told the command succeeded, and reads the target table back empty. It then either retries
the same no-op or invents a workaround (a raw OData insert) that leaves the row unregistered in the
package, so the next package install silently drops it.
