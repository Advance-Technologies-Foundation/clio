
## 2026-08-21 14:20 – odata-create side-effect state + list-sys-settings filter
Context: an E2E run of the process-designer MCP surface surfaced two tool defects. Three
`odata-create` calls into `MailboxSyncSettings` each reported `failed: 1` while every row was in
fact inserted, so the caller retried and produced three duplicate mailboxes; and a
`list-sys-settings` call passing `search-pattern` silently returned the entire 920-row catalog
because no such parameter existed.
Decision: model the create side effect the way this repo already models `section-created` —
nullable bool plus `retry-guidance` — and reserve `false` for rows rejected locally, before any
request. Every server-side failure is `null` (unknown), never "not created". Added `search-pattern`
as an ordinal-ignore-case substring over BOTH the setting code and its display name.
Discovery: a Creatio OData POST can return an error AFTER the row is written (a post-insert entity
event handler that throws), so a failed POST does NOT imply no side effect. The curated contract in
`ToolContractGetTool.cs` lists output fields by hand and does not follow the response record — it
went stale until updated explicitly, which a build cannot catch.
Files: clio/Command/McpServer/Tools/ODataCreateTool.cs,
clio/Command/McpServer/Tools/ODataCreateBatchResponse.cs,
clio/Command/McpServer/Tools/ToolContractGetTool.cs, clio/Command/SysSettingsCommand.cs,
clio/Command/SysSettingsModels.cs, clio/Command/McpServer/Tools/SysSettingsTool.cs
Impact: a consumer can now distinguish an unverified insert from a verified failure instead of
duplicating rows; and the sys-settings catalog is filterable, which removes a ~400 KB response from
the common lookup path.
