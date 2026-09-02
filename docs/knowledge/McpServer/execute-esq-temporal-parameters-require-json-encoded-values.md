---
description: execute-esq temporal parameter values for dataValueType 7 8 and 9 must be outer JSON strings containing an inner JSON-encoded date string; a plain ISO value reaches Creatio as null and produces an opaque ArgumentNullException
applies-to:
  - clio/Command/McpServer/Tools/ExecuteEsqTool.cs
ticket: 1321
date: 2026-09-02
---

**What is true** — the DataService SelectQuery contract does not accept a plain ISO string as the
`value` of a DateTime (7), Date (8), or Time (9) parameter. The outer query JSON must carry a string
whose content is another JSON string, for example `"value": "\"2026-01-01T00:00:00.000Z\""`.
`ExecuteEsqTool` validates this recursively before resolving an environment and forwards a valid
encoded value unchanged.

**Why it is this way** — Creatio's SelectQuery parameter deserializer performs a second JSON decode
for temporal parameter values. This was reproduced on Creatio 10.0.0.858 and is also the shape emitted
by the Freedom UI filter designer and documented by the `esq-filters-frontend` guidance. Clio does not
rewrite the value because choosing a timezone or changing Date versus DateTime intent belongs to the
caller.

**What breaks if you ignore it** — forwarding `"value": "2026-01-01T00:00:00.000Z"` reaches Creatio
as a null temporal value and returns `ArgumentNullException: Value cannot be null. Parameter name:
value`. The message falsely suggests that the caller omitted the field. Keep the preflight validation
and its exact query path when changing `ExecuteEsqTool`; do not normalize temporal strings silently.
