# CLI vs MCP for AI Coding Agents

## The Core Difference

Both the clio CLI and the clio MCP server call the same underlying command classes.
The business logic — fetching a page, validating a body, saving to Creatio — is identical in both.

The difference is in how results are delivered to an AI agent.

**MCP**: the full tool result is injected directly into the agent's context window.
**CLI**: the output goes to stdout. The agent decides what to do with it.

This distinction matters most when the response is large.

---

## How `get-page` Handles Large Responses

`get-page` produces two distinct payloads:

```
PageGetResponse
├── bundle   ← merged hierarchy from all parent schemas (READ-ONLY)
│   ├── viewConfig        large JsonArray, can be 50–200 KB
│   ├── viewModelConfig   JsonObject
│   ├── modelConfig       JsonObject
│   ├── resources.strings all localized strings
│   ├── handlers          JS string
│   └── converters, validators, parameters...
└── raw
    └── body  ← the only field the agent actually edits
```

The `bundle` section is read-only. The agent edits only `raw.body`.

### File-based output (MCP)

The MCP `get-page` tool always writes all payloads to disk and returns only file paths:

```json
{
  "success": true,
  "page": { "schemaName": "...", "packageName": "..." },
  "files": {
    "bodyFile":   ".clio-pages/MyPage/body.js",
    "bundleFile": ".clio-pages/MyPage/bundle.json",
    "metaFile":   ".clio-pages/MyPage/meta.json"
  }
}
```

Files are written to `.clio-pages/{schema-name}/` relative to the directory where clio MCP was started.
The agent reads them selectively with the `Read` tool using `offset`/`limit`:

```python
Read(".clio-pages/MyPage/body.js")                       # editable body, typically 5–30 KB
Read(".clio-pages/MyPage/bundle.json", offset=0, limit=50)  # inspect viewConfig structure only
```

Context consumption: the compact JSON summary plus only the lines actually read. Not 200 KB.

### Via CLI with file redirect

```bash
clio get-page --schema-name MyPage --environment local > /tmp/my-page.json
```

The CLI output goes to disk via shell redirect. The agent reads selectively from that file.
The CLI command itself is unchanged — file-based output is an MCP-only behavior.

---

## The Rule of Thumb

| Operation | Preferred interface | Reason |
|-----------|--------------------|-|
| `get-page` | MCP | Always file-based; no context pollution |
| `sync-pages` | MCP | Batch save + optional read-back in one call |
| `update-page` | MCP (fallback) | Single-page dry-run or legacy save only |
| `list-pages` | Either | Response is small regardless |

---

## When MCP Is Clearly Better

MCP wins on operations where the primary value is write semantics or batch execution.

**`update-page`**: the agent constructs the body locally, sends it, receives a small confirmation.

**`sync-pages` with `verify: true`**: saves multiple pages and reads each one back in a single MCP call.
The `verified-body-file` path in the response points to the written `body.js` — the agent reads it
with `Read` rather than receiving the body inline.

**Thread safety**: `BaseTool` serializes execution through `CommandExecutionSyncRoot`.
Concurrent agent calls are safe without external coordination.

**Structured error messages**: MCP responses carry `success` and `error` fields.
CLI requires parsing stdout and checking exit codes.

---

## Summary

CLI and MCP share the same implementation. For page work, MCP is the primary interface:

- `get-page` writes three files to `.clio-pages/{schema-name}/` and returns paths — no inline bundle.
- `sync-pages` with `verify: true` writes `body.js` after save and returns `verified-body-file` path.
- The agent reads files selectively with `Read`. Context stays clean regardless of page size.
