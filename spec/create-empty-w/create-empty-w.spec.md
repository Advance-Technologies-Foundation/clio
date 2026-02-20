# create-empty-w — Create Empty Workspace in a Subfolder

## Summary
Add a way to create a **new workspace in a new subfolder** from any directory (including a non-empty directory), **without connecting to any Creatio environment**.

Target command:

```bash
clio createw <workspace-name> --empty
```

Where `<workspace-name>` is the name of a subfolder to create in the current directory.

## Motivation
Today, `createw` can mis-detect a parent workspace (because `.clio` is searched upward) and/or require an empty directory.
For quick scaffolding (especially on macOS/Linux), users want to create a workspace structure locally without any environment credentials.

## Goals
- Allow running `createw` in any directory (even non-empty).
- Create a **new folder** `<workspace-name>/` and scaffold a workspace inside it.
- Ensure **zero environment connectivity** in `--empty` mode:
	- No requests to Creatio.
	- No reading/using active/default environment credentials.
	- No password prompt.
	- No package list download.
	- No package download.

## Non-goals
- Do not download packages from an environment in `--empty` mode.
- Do not attempt to auto-detect packages by AppCode in `--empty` mode.
- Do not change workspace template contents beyond what is needed for the feature.

## CLI Contract

### New syntax
```bash
clio create-workspace <workspace-name> --empty
clio createw <workspace-name> --empty
```

### Options
- `--empty` (boolean):
	- Creates a workspace structure only.
	- Performs no environment-related actions.
	- Implies zero environment connectivity.

### Backward compatibility
- Existing invocations must continue to work:
	- `clio createw` (no args)
	- `clio createw -e <env>`
	- `clio createw --uri <url> ...`

## Behavior

### Main flow: create empty workspace in a subfolder
Given the user runs:

```bash
clio createw my-workspace --empty
```

Then:
1) The command creates `./my-workspace/` if it does not exist.
2) The workspace template is copied into `./my-workspace/`.
3) Minimal workspace config files are created inside `./my-workspace/.clio/` (workspace settings + workspace environment settings).
4) The command finishes successfully.

**Important:** The command must not try to connect to Creatio or use any environment settings.

### Running from a non-empty directory
The current directory can contain files/folders. This must not block the command because the workspace is created in a new subfolder.

### If `<workspace-name>` folder already exists
Two acceptable behaviors (choose one and document it):

Option A (safer, default):
- If `./<workspace-name>/` exists (any contents), return error:
	- “Destination folder already exists: <path>”.

Option B:
- If `./<workspace-name>/` exists but is empty, proceed.
- If it is not empty, return error.

### If `<workspace-name>` already contains a workspace
If `./<workspace-name>/.clio/workspaceSettings.json` exists, return error:
- “Workspace already exists in <path>”.

### Environment / credentials
In `--empty` mode:
- Do not resolve `EnvironmentSettings`.
- Do not call any logic that depends on:
	- `-e/--Environment`
	- `-u/--uri`
	- `--Login/--Password`
	- OAuth settings
- Ignore `--AppCode` (either error or ignore; simplest: ignore with a warning).

## Output (user-facing)
- Success message: `Done`
- Errors must be actionable, in English.

## Exit codes
- `0` on success.
- `1` on any failure.

## Examples

Create empty workspace in a subfolder:
```bash
clio createw my-workspace --empty
```

Create empty workspace without any environment connectivity:
```bash
clio createw my-workspace --empty
```

## Acceptance criteria
- Running `clio createw my-workspace --empty` inside a non-empty directory creates `my-workspace/` and scaffolds a workspace inside it.
- No password prompt occurs.
- No network calls are made.
- If `my-workspace/` already exists (non-empty), the command fails with a clear error.
- The resulting workspace contains `.clio/workspaceSettings.json` and `.clio/workspaceEnvironmentSettings.json`.

## Notes / Open questions
- Should `--empty` also skip local restore steps (NuGet restore, solution creation, build-props)?
	- Simplest interpretation: `--empty` only scaffolds filesystem structure.
- Should `<workspace-name>` support nested paths (e.g., `foo/bar`)?
	- Default: allow relative paths under current directory, but reject absolute paths for safety.