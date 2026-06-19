# PRD: User Profile Language Detection for Entity Creation

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-06-09
**Jira**: [ENG-91044](https://creatio.atlassian.net/browse/ENG-91044)

---

## Problem Statement

When a developer or AI agent uses clio (via CLI or the clio MCP server) to create Creatio
entities — applications, objects, pages, sections, lookups, columns — the caption/label
culture is currently derived from the *host machine's* `CultureInfo.CurrentCulture` (see
`EntitySchemaDesignerSupport.GetCurrentCultureName()`) or from hardcoded `en-US` literals.
This produces names/labels/captions in the wrong language whenever the operator's machine
locale differs from the language set in the connected Creatio user's profile, forcing manual
rework and inconsistent localization. This is the FULL behavior change confirmed by the
requester: clio must instead detect and use the connected environment user's profile language.

## Goals

- [ ] Goal 1 — clio can retrieve the connected environment user's profile culture server-side,
  independent of any external/third-party MCP server.
  - Success metric SM-01: 100% of supported environments (.NET Framework and .NET Core, cliogate
    NOT required) return a non-empty `cultureName` for the logged-in user via a clio-native path
    (`ApplicationInfoService.svc/GetApplicationInfo` → `sysValues.userCulture.displayValue`).
  - Counter SM-01: zero new direct `HttpClient` usages introduced (all calls go through `IApplicationClient`).
- [ ] Goal 2 — Entity/page/section/lookup/column creation uses the detected profile culture for
  all generated names/labels/captions instead of host `CurrentCulture` or hardcoded `en-US`.
  - Success metric SM-02: in entity-creation flows, the effective caption culture equals the
    resolved profile culture in 100% of test scenarios where the profile culture is retrievable.
  - Counter SM-02: `en-US` remains present in every `title-localizations` / `description-localizations`
    map and remains the fallback; no localization-contract validator regression (0 new validator failures).
- [ ] Goal 3 — The MCP agent reliably detects the profile language once per session, reuses it,
  and asks the user when it cannot be retrieved.
  - Success metric SM-03: MCP server instructions + `app-modeling` resource + entity/page/section/
    application prompts all contain the detect-once / reuse / ask-on-failure guidance (4/4 prompt
    families updated, verified by e2e assertions).
  - Counter SM-03: profile-language retrieval is invoked at most once per MCP session (no per-entity
    redundant lookups); no measurable increase in tool-call latency for subsequent creations.

## Non-goals

- Will NOT add a UI to *change* the Creatio user's profile language — clio only reads it.
- Will NOT translate or auto-localize caption text into multiple languages; clio resolves the
  single effective culture and the caller supplies the localized strings (the `en-US`-anchored
  localization map contract is unchanged).
- Will NOT change behavior of unrelated commands that do not emit captions/labels.
- Will NOT remove `en-US` as the universal default/fallback culture.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| developer | clio to use my Creatio profile language when creating entities | generated captions match the platform language without manual fixes |
| QA engineer | a deterministic, testable rule for which culture is applied | I can assert caption culture in automated tests instead of guessing host locale |
| CI pipeline author | culture resolution to be independent of the runner's machine locale | builds produce consistent localization regardless of where they run |
| AI agent (MCP) | clear guidance to detect the profile language once and ask if it fails | I never silently create entities in the wrong language |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | clio MUST obtain the connected environment user's profile culture from the Creatio **`ApplicationInfoService.svc/GetApplicationInfo`** endpoint (`CreatioServicePaths.GetApplicationInfo`), reading `applicationInfo.sysValues.userCulture.displayValue` (e.g. `en-US`, `uk-UA`), using `IApplicationClient` only. | Must |
| FR-02 | Profile-culture retrieval MUST NOT depend on any external/third-party MCP server, and MUST NOT depend on cliogate being installed (cliogate may be absent on target environments). | Must |
| FR-03 | Entity/object/page/section/lookup/column creation MUST use the resolved profile culture as the effective caption culture instead of `CultureInfo.CurrentCulture`. | Must |
| FR-04 | All hardcoded `en-US` caption-culture literals in the listed creation paths (`ClientUnitSchemaCreate.cs`, `PageCreateOptions.cs`, `ResourceStringHelper.cs`, `SchemaDesignerHelper.cs`, `EntitySchemaDesignerSupport.cs`) MUST be replaced by the resolved profile culture, while keeping `en-US` as the fallback. | Must |
| FR-05 | `en-US` MUST always remain the default fallback and MUST always be present in `title-localizations` / `description-localizations` maps (localization contract unchanged). | Must |
| FR-06 | When the profile culture cannot be retrieved, clio/MCP MUST NOT silently default to host locale; the MCP agent MUST explicitly ask the user which language to use before proceeding. | Must |
| FR-07 | MCP server instructions (`McpServerInstructions.cs`) MUST instruct the agent to detect the profile language once per session and reuse it for all subsequent creations in that session. | Must |
| FR-08 | The `app-modeling` guidance resource (`AppModelingGuidanceResource.cs`) MUST document the detect-once / reuse / ask-on-failure rule. | Must |
| FR-09 | Entity, page, section, and application prompts (`EntitySchemaPrompt.cs`, `PagePrompt.cs`, section prompt, `ApplicationPrompt.cs`) MUST include the profile-language detection guidance. | Must |
| FR-10 | Profile-culture resolution SHOULD be cached/reused for the duration of an MCP session to avoid redundant lookups. | Should |
| FR-11 | If a new/changed CLI command surfaces profile culture, its docs (`help/en`, `docs/commands`, `Commands.md`), MCP surface (Tools/Prompts/Resources), and `clio.mcp.e2e` coverage MUST be updated. | Must |
| FR-12 | The MCP capability map (`docs/McpCapabilityMap.md`) MUST be updated to reflect any new profile-culture retrieval capability/tool. | Should |
| FR-13 | Existing entity-creation flows MUST behave identically when the profile language is already correctly set (no regression). | Must |

## CLI Impact

The behavior change is primarily internal (culture resolution) and MCP-guidance driven. If an
explicit override or a dedicated retrieval verb is introduced during design, it must follow the
rules below. The Architect decides whether a new CLI surface is needed (see OQ-01).

| Change | Details | Breaking? |
|--------|---------|-----------|
| New flag (candidate, design-decided) | `--caption-culture` override on entity/page/section creation commands | No — additive, optional |
| Behavior change | caption culture source: host `CurrentCulture` / hardcoded `en-US` → resolved profile culture | Behaviorally yes (approved FULL change); no flag signature change required |
| Culture source | `ApplicationInfoService.svc/GetApplicationInfo` → `sysValues.userCulture.displayValue` (existing endpoint, no cliogate) | No — read-only reuse |

All flags: **kebab-case only** (CLIO001 enforced). No camelCase. Any renamed flag needs a hidden alias.

## Acceptance Criteria

- [ ] AC-01: Given a connected environment whose logged-in user profile culture is `uk-UA`,
  when the agent prepares to create any entity, then clio resolves the profile culture as `uk-UA`
  by reading `applicationInfo.sysValues.userCulture.displayValue` from
  `ApplicationInfoService.svc/GetApplicationInfo` via `IApplicationClient` (no cliogate dependency).
- [ ] AC-02: Given a resolved profile culture of `uk-UA`, when an application/object/page/section/
  lookup/column is created, then all generated names/labels/captions use `uk-UA` as the effective
  caption culture.
- [ ] AC-03: Given any created entity, when its localization map is produced, then `en-US` is
  present in `title-localizations` and `description-localizations` and serves as the fallback.
- [ ] AC-04: Given the profile culture cannot be retrieved (service error / missing field / cliogate
  absent), when the MCP agent is about to create an entity, then the agent explicitly asks the user
  which language to use and does NOT proceed with host `CurrentCulture` or a silent `en-US` default.
- [ ] AC-05: Given an MCP session, when multiple entities are created in sequence, then the profile
  language is retrieved at most once and reused for all subsequent creations in that session.
- [ ] AC-06: Given an environment where the profile language is already correctly set, when an
  existing entity-creation flow runs, then output is identical to pre-change behavior (regression-safe).
- [ ] AC-07: Given the MCP server initializes, when a client reads server instructions, the
  `app-modeling` resource, and the entity/page/section/application prompts, then all four contain the
  detect-once / reuse / ask-on-failure profile-language guidance (asserted by `clio.mcp.e2e`).
- [ ] AC-08: Given the codebase after the change, when grepping the listed creation files, then no
  caption-culture value is derived from `CultureInfo.CurrentCulture` and no hardcoded `en-US`
  caption-culture literal remains except as the explicit fallback constant.
- [ ] AC-ERR: Given an invalid environment or a retrieval failure surfaced via CLI, clio prints
  `Error: {message}` (user-friendly, no stack trace) and exits non-zero.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | RESOLVED — `ApplicationInfoService.svc/GetApplicationInfo` exposes `sysValues.userCulture` (logged-in user) and `sysValues.primaryCulture`/`primaryLanguage` (system). Confirmed from a live response sample. No cliogate needed. | n/a (resolved). |
| A-02 | The "profile language" is `sysValues.userCulture.displayValue` (the logged-in user's culture), not `primaryCulture`. | Wrong culture applied to captions; AC-01/AC-02 fail. |
| A-03 | The localization-contract validator already enforces `en-US` presence and remains the right anchor. | Validator regressions if changed; SM-02 counter breached. |
| A-04 | "Once per session" maps to the MCP session lifetime; the agent (not clio) is responsible for caching the resolved value per session per MCP guidance. | If clio must cache server-side, FR-10 design changes. |
| A-05 | `GetApplicationInfo` is reachable with only authentication (no cliogate, no elevated permissions); the existing `GetCreatioInfoCommand` fallback already calls this path. | If the endpoint is unauthorized/unreachable, AC-04 ask-user path triggers. |
| A-06 | No CLI flag *signature* change is strictly required; the behavior change is internal plus an optional override. | If a mandatory new verb is needed, CLI Impact and breaking-change policy expand. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Does layer 1 surface profile culture through a new internal service (e.g. `ICurrentUserCultureResolver` reusing/extending `ApplicationInfoService`), a dedicated retrieval verb, and/or an MCP tool? It MUST read from `ApplicationInfoService.svc/GetApplicationInfo`. | Architect | TBD |
| OQ-02 | RESOLVED — canonical source is `ApplicationInfoService.svc/GetApplicationInfo` → `sysValues.userCulture.displayValue`. Cross-checked against the creatio-ui frontend (`/Users/a-kravchuk/Projects/creatio-ui`). cliogate is explicitly NOT used. | Resolved | — |
| OQ-03 | Should an explicit `--caption-culture` override be added to creation commands, and on which commands? | Architect | TBD |
| OQ-04 | When the profile returns a culture not present in the supplied localization map, what is the resolution rule (use it as the effective key vs. fall back to `en-US`)? | Architect | TBD |
| OQ-05 | Is the per-session cache an MCP-agent responsibility (guidance only) or a clio server-side cache (state to design)? | Architect | TBD |

## Dependencies

- Depends on: existing infra — `EntitySchemaDesignerSupport` culture helpers, `ApplicationInfoService.svc/GetApplicationInfo`
  endpoint (`CreatioServicePaths.GetApplicationInfo`) and the `PlatformVersionResolver` factory pattern reused for
  retrieval, the MCP localization contract work (workspace-diary entries 1405–1408, 2096–2097, 2247–2255),
  `docs/McpCapabilityMap.md`. cliogate is explicitly NOT a dependency.
- Blocks: consistent localized entity creation via clio CLI and MCP; the Architect ADR (`adr-user-profile-language-detection.md`)
  and downstream stories/test plan.
