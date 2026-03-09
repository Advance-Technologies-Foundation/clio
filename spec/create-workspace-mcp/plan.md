# Add `create-workspace` MCP Tool for Empty Workspace Bootstrap

## Summary
Expose the `clio create-workspace` command to MCP as an empty-workspace bootstrap tool only for the first slice.

The implementation should keep the MCP layer thin by moving path-selection behavior into the command:
- add a new global `Settings` property `[JsonProperty("workspaces-root")]`
- add an optional command option for an explicit target base directory
- when the option is omitted, `create-workspace --empty` should fall back to `Settings.workspaces-root`
- the MCP tool should only map arguments to command options and call `InternalExecute(...)`

`WorkspacePathes` was validated as a real property, but it lives on `EnvironmentSettings`, not on the `create-workspace` command or global settings. Per the decision in this planning session, do not use `EnvironmentSettings.WorkspacePathes` for this tool. Introduce the new global `Settings.workspaces-root` instead.

## Key Changes
- Command behavior:
  - Extend `CreateWorkspaceCommandOptions` with an optional absolute-path argument for the base directory, for example `--directory`.
  - Restrict that new option to the empty-workspace flow. If it is provided without `--empty`, fail with a clear command error.
  - Update `CreateWorkspaceCommand` so empty mode resolves its base directory in this order:
    1. explicit `--directory`
    2. global `Settings.workspaces-root`
  - If neither exists, fail.
  - If the resolved base directory does not exist, fail.
  - Keep existing empty-mode rules unchanged after base-directory resolution:
    - `WorkspaceName` required with `--empty`
    - `WorkspaceName` must be relative
    - destination must stay under the resolved base directory
    - `--force` behavior stays unchanged

- Settings and DI:
  - Add `WorkspacesRoot` to `Clio.Settings` with `[JsonProperty("workspaces-root")]`.
  - Inject `ISettingsRepository` into `CreateWorkspaceCommand` so the command can read the global setting instead of relying on MCP logic or current process cwd tricks.
  - Do not add MCP-only filesystem behavior.

- MCP surface:
  - Add a new tool class under `clio/Command/McpServer/Tools` for `create-workspace`.
  - Define the tool name as a visible constant on the tool class so tests can reference it.
  - Tool contract for v1:
    - required `workspace-name`
    - optional `directory`
  - The tool should always invoke the empty flow by mapping to:
    - `WorkspaceName = workspace-name`
    - `Empty = true`
    - `Directory = directory`
  - Use the direct `InternalExecute(options)` path, not the environment-aware resolver path.
  - Add a matching MCP prompt describing the same contract.
  - No MCP resource is needed for this slice unless implementation reveals an existing command resource pattern that must be kept aligned.

- Docs:
  - Review and update command docs because the CLI contract changes.
  - Update `clio/help/en/create-workspace.txt`.
  - Update `clio/Commands.md`.
  - Add the canonical detailed doc file `clio/docs/commands/create-workspace.md` if it is still missing, and document the new `--directory` option plus `workspaces-root` fallback.

## Public Interfaces / Contracts
- `Clio.Settings`
  - new property: `workspaces-root`
- `CreateWorkspaceCommandOptions`
  - new optional CLI option: absolute base directory for empty workspace creation
- New MCP tool contract
  - `workspace-name`: required
  - `directory`: optional, absolute path only
- New MCP prompt
  - should describe the same two arguments and the fallback to global `workspaces-root` when `directory` is omitted

## Test Plan
- Command unit tests:
  - `--empty` + explicit absolute directory creates under that directory
  - `--empty` + no directory uses `Settings.workspaces-root`
  - missing `workspaces-root` with no directory fails
  - nonexistent `workspaces-root` with no directory fails
  - relative `directory` fails
  - `directory` without `--empty` fails
  - existing empty-mode validation still holds

- MCP unit tests:
  - tool maps `workspace-name` and optional `directory` into `CreateWorkspaceCommandOptions`
  - tool uses the centralized tool-name constant
  - omitted optional `directory` is covered explicitly

- MCP E2E tests:
  - success case with explicit temporary absolute directory
  - failure case with missing/nonexistent explicit directory
  - success output includes an `Info` message
  - failure output includes an `Error` message
  - use command-based Allure feature naming: `create-workspace`
  - do not depend on mutating the developer’s real global `workspaces-root`; cover that fallback in command/unit tests instead

## Assumptions And Defaults
- First MCP slice is empty-workspace only. It does not expose the environment-backed restore/download modes of `create-workspace`.
- The new CLI option should be absolute-path only.
- The global fallback should come from new `Settings.workspaces-root`, not from `EnvironmentSettings.WorkspacePathes`.
- If both explicit `directory` and global `workspaces-root` exist, explicit `directory` wins.
- MCP reviewed: add a tool and prompt; no resource expected unless implementation finds a concrete need.
