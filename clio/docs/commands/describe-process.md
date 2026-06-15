# describe-process

Read an existing Creatio business process and return a **structured graph** (elements, flows, and
process parameters) instead of the raw escaped metadata — so an AI agent can explain, in plain
language, what the process does. This is the inverse of process generation (the "read & explain"
capability).

**Alias:** `dp`

## Usage

```bash
clio describe-process --process-code <code> -e <environment>
clio describe-process --process-uid <guid> -e <environment>
clio describe-process --process-caption "<caption>" -e <environment>
```

Provide **exactly one** of `--process-code`, `--process-uid`, or `--process-caption`.

## Options

| Option | Required | Description |
|---|---|---|
| `--process-code` | one-of | Process code (schema `Name`), e.g. `UsrProcess_493d4c9`. |
| `--process-uid` | one-of | Process UId (GUID). |
| `--process-caption` | one-of | Process caption (display name). |
| `--culture` | no | Culture for localized captions. Default `en-US`. |
| `-e, --environment` | yes | Registered clio environment name. |

## Output

Structured JSON:

```jsonc
{
  "code": "UsrProcess_493d4c9",
  "caption": "AI PoC Read Contact",
  "uId": "dd3a473e-736a-4957-bb6d-7315f6404bd6",
  "elements": [
    { "id": "...", "dataId": "StartEvent", "type": "Start", "label": "Start", "parameters": [] },
    { "id": "...", "dataId": "ServiceTask", "type": "Activity", "label": "Read data 1", "parameters": [ /* ... */ ] },
    { "id": "...", "dataId": "EndEvent", "type": "End", "label": "End", "parameters": [] }
  ],
  "flows": [
    { "source": "...", "target": "...", "kind": "sequence" }
  ],
  "parameters": [
    { "name": "...", "type": "Guid", "direction": "Input", "caption": "..." }
  ]
}
```

- `elements[].type` is the coarse role (Start / End / Activity / Gateway / Intermediate), resolved
  through the same `ManagerMap` taxonomy used by `validate-process-graph` and the `process-modeling`
  guidance.
- `flows[].kind` is `sequence`, `conditional`, or `default`.

## Examples

```bash
# Explain a process by code
clio describe-process --process-code UsrProcess_493d4c9 -e my-env

# By caption, using the alias
clio dp --process-caption "AI PoC Read Contact" -e my-env
```

## Notes

- Reuses the existing `ProcessSchemaRequest` read path (`generate-process-model` parsing) — it simply
  exposes the **element graph + flows** alongside the process parameters.
- **Limitation (v1):** element **filter / mapping** expressions (the heavily-escaped
  `FilterGroup` / `ParameterExpression` payloads) are **not** decoded. `describe-process` returns
  structure, element types, flows, and basic parameters only. Decoding those expressions into plain
  language is planned future work.

## Related

- [`generate-process-model`](generate-process-model.md) — generate a C# model for a process.
- `get-guidance` (MCP) name `process-modeling` — the element catalog + connection rules to narrate with.
