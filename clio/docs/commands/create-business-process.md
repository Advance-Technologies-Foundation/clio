# create-business-process

## Name

create-business-process - Build a business process on a Creatio environment from a declarative JSON descriptor

## Description

Builds and saves a real, interpretable business process from a declarative JSON descriptor,
delegating serialization to the `ProcessDesignService` package on the environment (the caller
never produces process metadata). The descriptor declares the process name/caption/package, its
elements (start/end events and user tasks), the sequence flows between them, process-level
parameters, and value mappings that bind element parameters to process parameters, constants, or
formulas. Provide the descriptor as a file (`--descriptor`) or inline (`--descriptor-json`).

## Synopsis

```bash
clio create-business-process --descriptor <FILE> -e <ENVIRONMENT_NAME> [--package-name <PACKAGE>]
clio create-bp --descriptor-json <JSON> -e <ENVIRONMENT_NAME>
```

## Options

```bash
--descriptor <FILE>
Path to a JSON file with the process descriptor. Provide this or --descriptor-json.

--descriptor-json <JSON>
Inline JSON process descriptor (alternative to --descriptor).

--package-name <PACKAGE>
Overrides the target package from the descriptor.

-e, --Environment <ENVIRONMENT_NAME>
Target environment name (registered via reg-web-app)
```

## Descriptor

A JSON object with the following fields:

| Field | Description |
|---|---|
| `name` | Unique schema code of the process to create. |
| `caption` | Human-readable caption. |
| `packageName` | Target package the process is created in. |
| `elements` | Array of `{ id, type, caption, userTaskName? }`. `type` is `startEvent` \| `endEvent` \| `userTask` (aliases `readData`, `performTask`). |
| `flows` | Array of `{ source, target }` referencing element ids. |
| `parameters` | Array of `{ name, type, direction, caption }` (process inputs / variables). |
| `mappings` | Array of `{ elementId, elementParameter }` plus one of `processParameter` \| `value` \| `expression`. |

Example descriptor:

```json
{
  "name": "UsrSampleProcess",
  "caption": "Sample Process",
  "packageName": "Custom",
  "parameters": [
    { "name": "MyText", "type": "Text", "direction": "In", "caption": "My Text" }
  ],
  "elements": [
    { "id": "StartEvent1", "type": "startEvent" },
    { "id": "task1", "type": "performTask", "caption": "Do task" },
    { "id": "EndEvent1", "type": "endEvent" }
  ],
  "flows": [
    { "source": "StartEvent1", "target": "task1" },
    { "source": "task1", "target": "EndEvent1" }
  ],
  "mappings": [
    { "elementId": "task1", "elementParameter": "Recommendation", "processParameter": "MyText" }
  ]
}
```

## Examples

```bash
clio create-business-process --descriptor process.json -e production
Build the process described in process.json on 'production'

clio create-business-process --descriptor process.json --package-name MyApp -e production
Build into a specific package, overriding the descriptor's packageName
```

## See Also

list-user-tasks - Discover valid userTaskName values for an environment
describe-process - Read an existing process and return its structured graph

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#create-business-process)
