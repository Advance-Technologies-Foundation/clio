# Skills Plugins Distribution Spec

## Goal

Define a delivery model for skills and plugins distribution that stays independent from the core standalone `clio mcp-server` usability work completed in `ENG-87775`.

The output of this stream is a scoped proposal with clear phases, compatibility rules, and acceptance criteria.

## Current State

- `install-skills`, `update-skill`, and `delete-skill` work only inside a clio workspace.
- Managed skills are copied into `.agents/skills`.
- Managed install metadata is stored in `.agents/skills/.clio-managed.json`.
- The default source repository is `https://creatio.ghe.com/engineering/bootstrap-composable-app-starter-kit`.
- MCP tools and prompts exist only for the workspace-local skills flow.
- There is no plugin distribution model, plugin manifest support, or plugin install/update/delete command surface in `clio`.

## Problem

The current workspace-local skills flow is useful, but it mixes two different concerns:

- local project bootstrap for a specific workspace
- reusable AI distribution for user-wide skills and plugin bundles

Treating both concerns as one feature would make the standalone MCP usability work too broad and would lock `clio` into the bootstrap starter repository as the only source of truth.

## Recommendation

1. Keep workspace-local skills as the backward-compatible baseline.
2. Add user-global skills as a separate follow-up phase instead of replacing the workspace model.
3. Keep plugin distribution separate from skill distribution at the command level.
4. Move the default curated distribution source out of `bootstrap-composable-app-starter-kit` into a `clio`-owned distribution repository or bundle.
5. Do not block existing-app MCP usability work on plugin packaging decisions.

## Proposed Distribution Model

## Skills

### Workspace scope

- Remains the default behavior for existing commands.
- Uses `.agents/skills/<skill-name>`.
- Keeps `.agents/skills/.clio-managed.json` as the managed-state file.
- Continues to require running from inside a clio workspace.

### User scope

- New explicit scope for reusable skills outside a specific workspace.
- Should use a user-level agent home directory instead of a workspace path.
- Must keep its own managed-state manifest separate from workspace manifests.
- Must support the same install, update, and delete lifecycle as workspace skills.

### Scope rule

- `workspace` remains the default for backward compatibility.
- New behavior should be opt-in through an explicit scope parameter such as `--scope user`.
- MCP tools must expose the same scope choice instead of inventing a different targeting model.

## Plugins

### Plugin packaging model

- Plugins should not be installed through `install-skills`.
- Plugins should use a dedicated plugin manifest, for example `.codex-plugin/plugin.json`.
- Plugin install/update/delete should be separate commands and separate MCP tools.
- Plugins may contain or reference skills, but plugin lifecycle must stay independent from direct skill lifecycle.

### Plugin scope

- Plugins should support the same `workspace` and `user` scope split as skills.
- Workspace plugins belong under `.agents/plugins`.
- User-global plugins belong under the user-level agent home.

## Default Source Strategy

- Replace the hardcoded starter-kit repository as the default curated source.
- The new default source should be owned by `clio` and should contain only distribution-ready skills and plugin bundles.
- `--repo` must remain supported so teams can install from private or experimental repositories.

## Compatibility Rules

- Existing workspace-local skill commands must keep working without new arguments.
- Existing MCP skill tools must remain valid for the workspace-local flow.
- Adding user scope must be additive, not a breaking rewrite.
- Plugin support must not repurpose the current skill manifest or overload skill commands with plugin semantics.

## Delivery Phases

### Phase 1: Scope foundation for skills

- Add explicit `workspace` and `user` scope support to skill install/update/delete flows.
- Introduce user-scope root resolution and managed-state storage.
- Keep workspace scope as the default.

### Phase 2: Curated source decoupling

- Move the default source from the starter-kit repository to a `clio`-owned distribution source.
- Update CLI help, command docs, MCP prompts, and MCP tool descriptions to reflect the new source.

### Phase 3: Plugin distribution

- Add dedicated plugin commands and MCP tools.
- Support plugin manifests, managed state, and workspace/user scope targeting.
- Define how bundled plugin assets and plugin-owned skills are installed and updated.

## Acceptance Criteria

### Phase 1 acceptance

- Users can install, update, and delete managed skills in both `workspace` and `user` scope.
- Existing workspace-only command invocations continue to behave the same way.
- CLI docs, MCP prompts, MCP tool contracts, unit tests, and MCP E2E coverage all reflect the new scope model.

### Phase 2 acceptance

- Omitting `--repo` uses the new `clio`-owned curated source instead of the starter-kit repository.
- Existing explicit `--repo` flows continue to work unchanged.
- Docs and MCP surfaces no longer describe the starter-kit repository as the default curated source.

### Phase 3 acceptance

- Users can install, update, and delete managed plugins independently of skills.
- Plugin commands and MCP tools support both `workspace` and `user` scope.
- Plugin metadata, lifecycle, docs, unit tests, and MCP E2E coverage are delivered in the same task.

## Non-Goals

- Expanding read-model payloads for existing-app discovery.
- Reworking the standalone MCP guidance added for `ENG-87775`.
- Converting all workspace-local bootstrap features into user-global features in one step.
