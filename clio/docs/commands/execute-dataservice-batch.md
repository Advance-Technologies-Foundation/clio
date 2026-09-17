# execute-dataservice-batch

Write 1–100 explicit records in one native DataService BatchQuery request. Existing single-record commands remain available. Each operation targets a schema and record UUID; update/delete cannot use broad filters.

```shell
clio execute-dataservice-batch -e development --input operations.json
```

The input file is a JSON array:

```json
[
  {
    "operation": "update",
    "schema-name": "Contact",
    "record-id": "11111111-1111-1111-1111-111111111111",
    "values": { "Name": { "data-value-type": 1, "value": "Sample contact" } }
  }
]
```

Operations are `insert`, `update`, or `delete`. Insert/update require 1–50 values; delete has no values. `Id` comes from `record-id` and cannot appear in values. Supported scalar DataService types: 0 UUID; 1–3 text; 4 integer; 5 decimal; 6 money; 7–9 date/time strings; 10 lookup UUID; 11 enum integer; 12 boolean. JSON null clears a value where the platform permits it. Schema and column names must be identifiers. Input and encoded request are each limited to 200000 UTF-8 bytes.

The batch continues after item failures. Atomicity is not guaranteed. Each result includes the zero-based input index, record UUID, `completed`, `failed` or `unknown` state, optional rows affected and sanitized error. Aggregate counts describe only established outcomes. Missing or ambiguous native results are unknown; no retry is made. A zero-row successful update is reported as completed with zero rows affected, not proof the record exists. Results are platform acknowledgements, not independent readback. Verify affected records before resubmitting unknown items.

CLI exits zero only when every item completed. MCP uses `execute-dataservice-batch` through `clio-run` with `environment-name` and `operations`. The tool is destructive and non-idempotent. Writes use normal DataService permissions and listeners. Use native enrollment for sequence participant lifecycle changes; never manufacture Active participants or activities through generic writes.

Each item also includes optional `diagnostic` context with operation, entity, input index, write-attempt and transport boundaries, side-effect certainty and sanitized message. A failed native item retains unknown side effects even when its failed acknowledgement is certain. No HTTP status or offending field is guessed.
