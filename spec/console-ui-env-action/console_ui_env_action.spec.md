## Purpose

Add environment actions to the existing console UI menu so users can quickly perform common operations on a selected environment without leaving the interactive flow.

## Context

There is already an environment console UI menu. The new actions must be added to that existing menu as a new group and must be available for the currently selected environment.

## User goals

- Run common environment operations from a single interactive menu.
- Reduce the need to remember CLI syntax.
- Keep actions discoverable and consistent with existing command behavior.

## Proposed menu additions

Add a new menu section (for example, “Actions”) to the existing environment menu. The section should include these items:

1. Restart environment
	- Command mapping: `restart-web-app`
2. Clear Redis database
	- Command mapping: `clear-redis-db`
3. Open environment in browser
	- Command mapping: `open` (if Windows only, the UI must explain the limitation)
4. Ping environment
	- Command mapping: `ping`
5. Healthcheck
	- Command mapping: `healthcheck`
6. Get environment info
	- Command mapping: `get-info`
7. Compile configuration
	- Command mapping: `compile-configuration`

## UI behavior

- Actions are shown only after an environment is selected.
- Each action must show a concise confirmation or result message.
- Errors must be surfaced in a user-friendly way, matching existing CLI output style.
- If an action is not supported on the current OS, show a clear message and return to the menu.

## Non-goals

- No new CLI commands are introduced.
- No changes to command behavior or options.

## Implementation plan

1. Locate the existing environment console UI menu and identify where environment selection is stored.
2. Add a new menu section named “Actions” under the existing environment menu.
3. Create a menu item for each action and map it to the existing command execution pipeline.
4. Ensure the selected environment name is passed to each command consistently.
5. Add OS capability checks for `open` and show a friendly message on unsupported platforms.
6. Ensure success and error outputs are displayed in the UI after each action, then return to the menu.
7. Add or update unit tests for the menu routing and command invocation.
8. Update documentation if the menu list is surfaced in help output or docs.