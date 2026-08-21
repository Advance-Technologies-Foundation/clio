
## 2026-08-21 14:20 – odata-create reports its side effect
Context: an E2E run of the process-designer MCP surface exposed a create defect. Three
`odata-create` calls into `MailboxSyncSettings` each reported `failed: 1` while every row was in
fact inserted, so the caller retried and produced three duplicate mailboxes.
Decision: model the side effect the way this repo already models `section-created` — nullable bool
plus `retry-guidance` — and reserve `false` for rows rejected locally, before any request leaves
clio. Every server-side failure is `null` (unknown), never "not created".
Discovery: a Creatio OData POST can return an error AFTER the row is written (a post-insert entity
event handler that throws), so a failed POST does NOT imply no side effect. Separately: the curated
contract in `ToolContractGetTool.cs` lists output fields by hand and does not follow the response
record, so it went stale until updated explicitly — a build cannot catch that drift.
Files: clio/Command/McpServer/Tools/ODataCreateTool.cs,
clio/Command/McpServer/Tools/ODataCreateBatchResponse.cs,
clio/Command/McpServer/Tools/ToolContractGetTool.cs
Impact: a consumer can distinguish an unverified insert from a verified failure instead of silently
duplicating rows on the natural retry.
