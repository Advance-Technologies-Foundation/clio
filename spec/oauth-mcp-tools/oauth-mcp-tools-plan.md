# oauth-mcp-tools — implementation plan (s2s slice)

> Owner: claude (implements) · Reviewer: codex · Branch: stacked on `krylov/deploy-identity`
> (feature branch — one big PR). Feature toggle: reuse `deploy-identity` key.
> No BMAD (Kirill standing order). Keep: targeted tests, secret-safety, MCP+docs maintenance, diary.

## Goal

Primitive MCP tools to configure OAuth on an **already-deployed / remote** Creatio **via REST only**
(the user may have no filesystem or DB access to the target). Phase 1 = server-to-server
(`client_credentials`). Phase 2 (later) = authorization_code / on-behalf-of-user.

Validated already: AddClient + `/connect/token` (client_credentials) + bearer DataService all work
against remote cloud env `c-dev` end-to-end (REST-only).

## Base that this stacks on (lives on `krylov/deploy-identity`)

- `ServiceUrlBuilder` routes 46/47/48 → `OAuthConfigService/GetIdentityServerClientSecret | CreateTechnicalUser | AddClient`.
- `IIdentityServiceCreatioClient` (REST): `GetDesignerClientSecret()`, `CreateTechnicalUser(name)`,
  `CreateClioClient(options, systemUserId)` → `OAuthClientCredentials(ClientId, ClientSecret)`.
  AddClient body MUST be wrapped: `{ "addClientRequest": { name, applicationUrl, description, systemUserId, grantType:"client_credentials", allowedGrantTypes:["client_credentials"] } }`.
- MCP pattern: `BaseTool<TOptions>` + `InternalExecute<TCommand>(options)` (env-sensitive → fresh per-call command).
  See `Tools/DeployIdentityTool.cs` for the exact shape (args record, `[McpServerTool]` safety flags, `[FeatureToggle]`).

## CRITICAL design constraint — REST-only, no DB/filesystem

`IdentityServiceSystemUserResolver` and `IdentityServiceRoleGrantService` on the branch are **DB-direct**
(Npgsql/SqlClient + `ConnectionStrings.config`). They CANNOT be reused for remote Creatio.
The new tools must resolve/identify users over **DataService REST** (e.g.
`POST /0/DataService/json/SyncReply/SelectQuery` on `SysAdminUnit`, filter by `Name`, select `Id`),
through `IApplicationClient` — never raw `HttpClient`, never DB.

## The 5 tools (phase 1)

1. **get-identity-service-config** (ReadOnly)
   - Reads `OAuth20IdentityServerUrl` (+ `OAuth20IdentityServerClientId`) via REST sys-settings.
   - Fallback: derive `-is` host from the Creatio host when the setting is empty
     (e.g. `186843-crm-bundle.creatio.com` → `…-is.creatio.com`).
   - Reports token endpoint `{base}/connect/token` and discovery `{base}/.well-known/openid-configuration`.
   - Result: identityServerUrl, source (setting|derived|none), tokenEndpoint, discoveryEndpoint, reachable(bool).

2. **resolve-oauth-system-user** (ReadOnly)
   - REST DataService SelectQuery on `SysAdminUnit` by name (default `Supervisor`) or id.
   - Result: systemUserId, name, found(bool). NO DB.

3. **create-oauth-technical-user** (Destructive, opt-in)
   - Wraps route 47 CreateTechnicalUser; returns systemUserId.
   - Open question for review: role-grant for the new user is DB-direct on the branch and won't run remote.
     For s2s, decide whether a role grant is needed at all, or whether it must be a REST path. Flag, don't guess.

4. **create-server-to-server-oauth-app** (Destructive)
   - Wraps route 48 AddClient (client_credentials), systemUserId from tool 2/3 (default Supervisor).
   - Returns clientId + clientSecret **only in the structured tool result** — NEVER logged, NEVER echoed in-room,
     never persisted to clio appsettings by this tool (that's deploy-identity's job, not this primitive).

5. **verify-oauth-app** (ReadOnly)
   - POST `{base}/connect/token` grant_type=client_credentials with clientId/secret → expect access_token.
   - Bearer smoke test: `POST {creatioBase}/0/DataService/json/SyncReply/SelectQuery` (small select, e.g. Contact top 1)
     with `Authorization: Bearer …` → expect 200.
   - Result: tokenAcquired(bool), dataServiceStatus(int), ok(bool). NEVER return the token text.

## Maintenance obligations (repo policy)

- MCP: tools under `Command/McpServer/Tools/`, gate with `[FeatureToggle("deploy-identity")]`; add prompt if warranted;
  unit tests in `clio.tests/Command/McpServer/`; **mandatory** e2e in `clio.mcp.e2e/`.
- Docs: if any of these are also CLI verbs, update `help/en/*.txt`, `docs/commands/*.md`, `Commands.md`, `Wiki/WikiAnchors.txt`.
  (Decide per-tool whether each is CLI-exposed or MCP-only.)
- Tests: `[Category("Unit")]`, `MethodName_Should…_When…`, AAA + `because` + `[Description]`; prefer `BaseCommandTests<TOptions>`.
- CLIO analyzers clean; kebab-case options; DI registration in `BindingsModule.cs`; no `new` for behavior classes.

## Sequencing / blocker

Base plumbing is currently **uncommitted in codex's main checkout** — not on the branch tip.
BLOCKED until codex commits deploy-identity WIP to `krylov/deploy-identity`. Then reset worktree to tip and build.
