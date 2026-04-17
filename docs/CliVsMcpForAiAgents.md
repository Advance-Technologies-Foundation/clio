# CLI vs MCP for AI Coding Agents

## The Core Difference

Both the clio CLI and the clio MCP server call the same underlying command classes.
The business logic — fetching a page, validating a body, saving to Creatio — is identical in both.

The difference is in how results are delivered to an AI agent.

**MCP**: the full tool result is injected directly into the agent's context window.
**CLI**: the output goes to stdout. The agent decides what to do with it.

This distinction matters most when the response is large.

---

## Why `get-page` Is A Problem For MCP

`get-page` returns two distinct payloads in one response:

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

The `bundle` section is read-only. It represents the merged result of parent schemas and cannot be submitted to `update-page`. The agent edits only `raw.body`.

There is no `fields` or `include-bundle` parameter. The response is always the full payload.

### Via MCP

The entire response — bundle included — lands in the tool result and consumes context window space. For a complex page with a deep inheritance chain, this is routinely 100–200 KB of JSON that the agent cannot edit and may never need to read.

### Via CLI with file redirect

```bash
clio get-page --schema-name MyPage --environment local > /tmp/my-page.json
```

The Bash tool result contains only the shell command line. The 200 KB goes to disk.
The agent then reads selectively:

```python
Read("/tmp/my-page.json", offset=240, limit=50)   # only raw.body
Grep('"body"', "/tmp/my-page.json")               # locate the section first
```

Context consumption: the command line plus the few lines actually read. Not 200 KB.

---

## The Rule of Thumb

| Operation | Preferred interface | Reason |
|-----------|--------------------|-|
| `get-page` on a complex page | CLI + file | Bundle is large, agent only needs `raw.body` |
| `get-page` on a simple page | Either | Bundle may be small enough |
| `update-page` | MCP | Response is compact: `success`, `bodyLength`, a few fields |
| `sync-pages` with `verify: true` | MCP | Batch save + read-back in one call, no extra round trip |
| `list-pages` | Either | Response is small regardless |

A rough signal: if the response contains a `bundle` or nested JSON that you will not submit back, prefer CLI and redirect to a file.

---

## Can MCP Save to a File Instead?

Not by protocol. MCP tools return results to the calling client. There is no side-channel that bypasses context injection.

A clio MCP tool could theoretically accept an `output-file` argument, write the full payload to disk, and return only a small summary such as:

```json
{ "success": true, "saved-to": "/tmp/my-page.json", "body-length": 15420 }
```

This would give MCP the same context efficiency as CLI redirect. As of writing, `get-page` does not support this parameter. If large-page editing is a consistent bottleneck for your agent, this would be a worthwhile feature request.

---

## When MCP Is Clearly Better

MCP wins on operations where the response is compact and the primary value is write semantics or batch execution.

**`update-page`**: the agent constructs the body locally, sends it, receives a small confirmation.
No large read involved.

**`sync-pages` with `verify: true`**: saves multiple pages and reads each one back in a single MCP call.
Without batch support, the equivalent CLI flow requires N `update-page` calls followed by N `get-page` calls.

**Thread safety**: `BaseTool` serializes execution through `CommandExecutionSyncRoot`.
Concurrent agent calls are safe without external coordination.

**Structured error messages**: MCP responses carry `success` and `error` fields.
CLI requires parsing stdout and checking exit codes.

---

## Summary

CLI and MCP share the same implementation. Choose based on response size and what you do with the result.

- Large read responses: CLI + file redirect keeps context clean.
- Write operations and batch workflows: MCP is more efficient and purpose-built for agents.
- Both: if you need selective reading from a large response, the file-based approach wins regardless of where the data originates.
