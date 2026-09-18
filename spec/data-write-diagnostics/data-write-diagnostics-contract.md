# Write diagnostic context

Native sequence enrollment, DataService batch writes and retained OData write responses include optional `diagnostic` context. Existing success, error, count and record fields remain compatible. Prefer DataService for record writes; use native sequence enrollment for participant lifecycle changes.

| Field | Meaning |
|---|---|
| `operation`, `entity` | Requested write and schema, with unsafe text sanitized |
| `item-index` | Zero-based input position when an individual item is identifiable |
| `write-attempted` | Whether execution crossed the write-call boundary |
| `transport-outcome` | `not-attempted`, `response-received`, or `unknown`; not an HTTP status |
| `side-effect` | `not-attempted`, `acknowledged`, or `unknown` |
| `retry-advice` | Whether to correct input or inspect affected records first |
| `message` | Bounded, redacted and fenced diagnostic text |

`acknowledged` is a native acknowledgement, not independent readback or proof every requested value persisted. A failed response can follow committed side effects; it therefore retains `unknown`. A local preflight refusal can establish `not-attempted` even if a read-only metadata request happened. Transport failures never establish rollback. No offending field or HTTP status is inferred from prose. No automatic write retry is introduced.
