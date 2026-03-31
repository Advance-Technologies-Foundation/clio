# Skills Plugins Distribution Plan

## Objective

Turn `ENG-87794` into a separate delivery stream that does not block the standalone MCP usability work.

## Proposed Execution Order

1. Land the architecture decision and phase split in repo documentation.
2. Implement skill scope support first.
3. Decouple the default curated source.
4. Implement plugin distribution after the skill scope model is stable.

## Phase 1: Skill Scope Support

### Implementation tasks

- Add a scope abstraction for skill operations.
- Introduce user-scope root resolution and managed manifest storage.
- Extend `install-skills`, `update-skill`, and `delete-skill` with explicit scope selection while keeping workspace as the default.
- Update MCP skill tools and prompts to accept the same scope model.
- Update command docs and help for the new scope options.

### Validation

- Unit tests for workspace and user scope path resolution.
- Unit tests for manifest isolation between workspace and user scope.
- MCP unit tests for new arguments and descriptions.
- MCP E2E tests for workspace and user scope flows.

## Phase 2: Curated Source Decoupling

### Implementation tasks

- Replace the hardcoded default repository constant.
- Introduce a `clio`-owned curated source for distribution-ready skills.
- Update docs, prompts, and help text that currently reference the starter-kit repository as the default.

### Validation

- Unit tests for default source resolution.
- Docs/help review for all touched commands and MCP prompts.

## Phase 3: Plugin Distribution

### Implementation tasks

- Define plugin manifest detection and managed-state storage.
- Add `install-plugin`, `update-plugin`, and `delete-plugin` commands.
- Add matching MCP tools, prompts, and tests.
- Support both workspace and user scope for plugins.

### Validation

- Unit tests for plugin install/update/delete flows.
- MCP E2E tests for plugin commands.
- Docs/help updates for all new command and MCP surfaces.

## Delivery Notes

- Phase 1 should be the first implementation target because it reuses the existing skill-management architecture.
- Phase 2 should not start until Phase 1 decides the stable distribution source abstraction.
- Phase 3 should not overload the current skill commands; plugin lifecycle stays separate on purpose.
