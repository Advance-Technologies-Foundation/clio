# describe-process

Read an existing Creatio business process and return a **structured graph** (elements, flows, and
process parameters) instead of the raw escaped metadata — so an AI agent can explain, in plain
language, what the process does. This is the inverse of process generation (the "read & explain"
capability).

The read is performed **server-side** by the `ProcessDesignService` package: element types come from
the real object model (the runtime class plus the specific user-task schema name, including custom
user tasks) and each parameter carries its value source. Requires the `clioprocessbuilder` package on
the target environment.

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
  "name": "UsrProcess_493d4c9",
  "caption": "AI PoC Read Contact",
  "schemaUId": "dd3a473e-736a-4957-bb6d-7315f6404bd6",
  "elements": [
    { "id": "...", "name": "StartEvent1", "caption": "Start", "type": "ProcessSchemaStartEvent", "buildType": "startevent", "position": "60;185", "parameters": [] },
    {
      "id": "...", "name": "task1", "caption": "Do task",
      "type": "ProcessSchemaUserTask", "buildType": "usertask", "userTaskName": "ActivityUserTask", "position": "240;173",
      "parameters": [
        { "name": "Recommendation", "uid": "...", "source": "Script", "value": "[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{...}]#]" },
        { "name": "Duration", "uid": "...", "source": "ConstValue", "value": "20" }
      ]
    },
    { "id": "...", "name": "EndEvent1", "caption": "End", "type": "ProcessSchemaTerminateEvent", "buildType": "endevent", "position": "420;185", "parameters": [] }
  ],
  "flows": [
    { "source": "...", "target": "...", "kind": "sequence" }
  ],
  "parameters": [
    { "name": "MyText", "uid": "...", "source": "None" }
  ]
}
```

- `elements[].type` is the element's **runtime class name** (e.g. `ProcessSchemaUserTask`,
  `ProcessSchemaStartEvent`) and is **not** consumable by build. `elements[].buildType` is the
  **descriptor token** to feed back into `create-business-process` / `modify-business-process`
  (e.g. `usertask`, `endevent`, `signalstart`, `startevent`) — the round-trippable counterpart of
  `type`. For user tasks `userTaskName` carries the specific user-task schema (e.g. `ReadDataUserTask`,
  `ActivityUserTask`, or a custom one).
- `elements[].parameters[]` and the top-level `parameters[]` list each value-bearing parameter with its
  `source` (`None` / `ConstValue` / `Mapping` / `Script` / `SystemValue` / …) and the raw `value`
  expression (for a formula source, the `[#…#]` expression).
- `flows[].kind` is `sequence`, `conditional`, or `default`.

## Examples

```bash
# Explain a process by code
clio describe-process --process-code UsrProcess_493d4c9 -e my-env

# By caption, using the alias
clio dp --process-caption "AI PoC Read Contact" -e my-env
```

## Notes

- The read is delegated to the server-side `ProcessDesignService.DescribeProcess` (the
  `clioprocessbuilder` package), which reads the schema through the platform managers (runtime
  instance, with a design-time fallback for file-design-mode / uncompiled processes). The target
  environment must have the package installed.
- Element typing is **universal** — taken from the real object model rather than a client-side GUID
  map — so custom user tasks resolve to their actual schema name.
- Parameter `value` expressions are surfaced **verbatim** (including formula `[#…#]` references).
  Translating those expressions into plain-language descriptions is planned future work.

## Related

- [`generate-process-model`](generate-process-model.md) — generate a C# model for a process.
- `get-guidance` (MCP) name `process-modeling` — the element catalog + connection rules to narrate with.
