# AGENTS.md

## Repository structure

This is a repository created by `clio` for `Creatio` CRM development. Use this file as a workspace-level template for any Clio-based project.

### /.application

`.application` contains reference binaries and configuration used for build/test in local workspaces.

- [./.application/net-framework/](./.application/net-framework/) for `net472` based targets.
- [./.application/net-core/](./.application/net-core/) for `netstandard2.0` and modern targets.

Treat `.application` as dependency/reference input. Do not treat it as the primary place for product changes unless explicitly requested.

use clio mcp server to download configuration from relevant environment


### /.clio

`.clio` contains configuration files for `clio`.

- [./.clio/clioignore](./.clio/clioignore) controls packaging exclusions.
- [./.clio/workspaceSettings.json](./.clio/workspaceSettings.json) defines workspace packages and related settings.
- [./.clio/workspaceEnvironmentSettings.json](./.clio/workspaceEnvironmentSettings.json) defines environment-level settings.

## packages

`packages` is the main source root. Each Creatio package is under:

- `./packages/<PACKAGE_NAME>/`

Typical source locations:

- Backend C#: `./packages/<PACKAGE_NAME>/Files/src/`
- Entry point web services: `./packages/<PACKAGE_NAME>/Files/src/cs/EntryPoints/WebService/`
- Frontend/client code: package-specific `Schemas` or frontend folders, depending on package structure.

Typical tests location:

- `./tests/<PACKAGE_NAME>/`

## Non negotiable rules

- Never throw for expected business/validation flow. Return error as value (for example, via `ErrorOr`) when that pattern exists in the workspace.
- Always create or update unit tests for any new or changed production code unless explicitly instructed otherwise.
- Always build and run tests for your changes unless explicitly instructed otherwise.
- Follow path-level `AGENTS.md` files when present (they override this file for their subtree).

## Build and test environment variables

Before running builds or unit tests, set the required environment variables.

For net472:

```plain text
dotnet build .\MainSolution.slnx -c dev-nf -v d
```

For everything else:

```plain text
dotnet build .\MainSolution.slnx -c dev-n8 -v d
```

## Agent usage guidance

- When working with custom configuration web services or their tests, use the `$creatio-config-webservice` skill.
- Trigger this skill for changes under:
  - `packages/<PACKAGE_NAME>/src/cs/EntryPoints/WebService`
  - `packages/<PACKAGE_NAME>/Files/src/cs/EntryPoints/WebService`
  - `tests/<PACKAGE_NAME>/EntryPoints/WebService`

## Workspace diary

Keep a persistent engineering diary to speed up future tasks.

Canonical diary file:
- `./.codex/workspace-diary.md`

Mandatory agent behavior:
- For any non-trivial task, read the latest relevant diary entries before implementing changes.
- After completing non-trivial work, append a new diary entry.
- Keep entries concise, factual, and path-referenced.
- Do not rewrite history; append only.
- If a task is exploratory and no code changes are made, still record key discoveries.

Entry format:
```markdown
## YYYY-MM-DD - <short title>
Context: <why this work happened>
Decision: <important decision or approach>
Discovery: <important behavior/constraint learned>
Files: <path1>, <path2>
Impact: <how this helps future tasks>

```
