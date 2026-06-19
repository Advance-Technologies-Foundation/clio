# create-business-process

## Name

create-business-process - Build a business process on a Creatio environment from a declarative JSON descriptor

## Description

Builds and saves a real, interpretable business process from a declarative JSON descriptor,
delegating serialization to the `ProcessDesignService` package on the environment (the caller
never produces process metadata). The descriptor declares the process name/caption/package, its
elements (start, signal-start, and end events plus user tasks), the sequence flows between them,
process-level parameters, and value mappings that bind element parameters to process parameters,
constants, or formulas. Provide the descriptor as a file (`--descriptor`) or inline (`--descriptor-json`).

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
| `elements` | Array of `{ id, type, caption, userTaskName?, signal?, filter? }`. `type` is `startEvent` \| `signalStart` \| `endEvent` \| `userTask` (aliases `readData`, `performTask`). For a record trigger ("run on save"), use a `signalStart` element with `signal: { entity, on }` where `on` is one of `added` \| `modified` \| `deleted` (one event) — the platform-native alternative to a client-side page save handler. Add a `filter` (see [Filter](#filter)) to a `signalStart` to fire the trigger only for records matching a condition. |
| `flows` | Array of `{ source, target }` referencing element ids. |
| `parameters` | Array of `{ name, type, direction, caption, referenceSchema? }` (process inputs / variables). `referenceSchema` (an object name, e.g. `City`) makes the parameter a Lookup to that object. |
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

Record-triggered variant — a process that starts when a `UsrTestRunButton` record is saved:

```json
{
  "name": "UsrOnSaveProcess",
  "caption": "On Save",
  "packageName": "Custom",
  "elements": [
    { "id": "SignalStart1", "type": "signalStart", "signal": { "entity": "UsrTestRunButton", "on": "modified" } },
    { "id": "task1", "type": "performTask", "caption": "Do task" },
    { "id": "EndEvent1", "type": "endEvent" }
  ],
  "flows": [
    { "source": "SignalStart1", "target": "task1" },
    { "source": "task1", "target": "EndEvent1" }
  ]
}
```

## Filter

A `filter` on a `signalStart` restricts the trigger to records matching a condition. The server
serializes the platform `Terrasoft.FilterGroup` — never hand-write filter JSON.

| Field | Description |
|---|---|
| `object` | Root object (entity) the columns belong to; defaults to the signal entity. |
| `logicalOperation` | `and` (default) or `or`. |
| `conditions` | Array of `{ column, comparison, <value> }`. `column` is the entity column name and may be a lookup dot-path (e.g. `Account.Code`). `comparison`: `equal` (default), `notEqual`, `greater`, `greaterOrEqual`, `less`, `lessOrEqual`, `contains`, `startWith`, `endWith`, `isNull`, `isNotNull`. The right-hand value is exactly one of `value` (constant), `processParameter`, `elementParameter` (`{ elementId, parameter }`), or `expression`; `isNull`/`isNotNull` take none. |
| `groups` | Optional nested groups, each with its own `logicalOperation` (recursive AND/OR). |

Fire only when `Name` (column `UsrName`) equals `Start`:

```json
{
  "name": "UsrOnSaveStart",
  "caption": "On save when Name = Start",
  "packageName": "Custom",
  "elements": [
    { "id": "SignalStart1", "type": "signalStart",
      "signal": { "entity": "UsrTestRunButton", "on": "modified" },
      "filter": { "object": "UsrTestRunButton",
        "conditions": [ { "column": "UsrName", "comparison": "equal", "value": "Start" } ] } },
    { "id": "task1", "type": "performTask", "caption": "Show task" },
    { "id": "EndEvent1", "type": "endEvent" }
  ],
  "flows": [
    { "source": "SignalStart1", "target": "task1" },
    { "source": "task1", "target": "EndEvent1" }
  ]
}
```

> Data tasks (Read/Add/Modify/Delete data) also accept a `filter`, but their target object / read
> config is not buildable yet, so a data-task filter is not end-to-end usable in this increment — only
> the `signalStart` filter is.

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
