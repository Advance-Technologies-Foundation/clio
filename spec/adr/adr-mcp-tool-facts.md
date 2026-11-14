# ADR: MCP tool facts — each tool says what it does

- **Status:** Accepted
- **Date:** 2026-11-14
- **Feature:** `mcp-tool-facts`
- **Spec:** [spec-mcp-tool-facts.md](../prd/spec-mcp-tool-facts.md)
- **Related ADR:** [adr-mcp-worker-execution-boundary.md](adr-mcp-worker-execution-boundary.md) — the
  `[McpToolExecution]` declaration this one is modelled on (rule 7: a routing decision is never inferred from the
  client-facing hints; here the reverse also holds: these facts are not inferred from the hints either).

---

## Context

An MCP host that runs clio for an AI agent decides, call by call, whether to let the call through, to ask the
person, or to refuse it. To decide, it needs to know what the call does. Today clio tells it three things:

- the standard MCP annotations `readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint` on the tools in
  `tools/list` (about 40 resident tools);
- `(R)` and `(D)` on each line of the `get-tool-contract` index, for all ~250 tools; most of them are reached only
  through `clio-run` and never appear in `tools/list`;
- prose in each tool's description.

That is not enough to decide on:

- `(D)` ("may delete or overwrite data") marks `create-app`, `create-lookup`, `create-entity-schema`,
  `sync-schemas` and `sync-pages` exactly like `delete-app` and `execute-sql-script`. A host that asks the person on
  every `(D)` asks on almost every build step.
- Nothing says that a call compiles or restarts the environment, which every user of it feels.
- Nothing says that a call acts on this computer rather than on the environment it names (installing Creatio,
  restoring a local database, updating clio itself, installing the toolkit into an agent's configuration), or that
  it shows or changes clio's registered environments and their credentials.
- A composite tool is one name for a read and a write: `manage-role` with `action: list` only lists roles, and with
  `action: delete` deletes one. A host that treats the tool as a whole asks for a change approval to list roles.

So hosts keep lists of clio tool names: regexes on the verb (`get-|list-|find-…` reads, `compile|restart|delete…`
heavy), a list of machine-wide tools and a list of connection tools. Every clio release that adds or renames a tool
makes such a list wrong, and a tool the list has never seen is either refused or misjudged.

## Decision

### D1. Each tool declares four facts on its method

```csharp
[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true)]
[McpToolFacts(
    Effect = McpToolEffect.Destroy,
    EnvironmentImpact = McpToolEnvironmentImpact.None,
    Scope = McpToolScope.Environment,
    Connections = McpToolConnections.None)]
[McpToolArgumentFacts("action", "list", Effect = McpToolEffect.Read)]
public ManageRoleResponse ManageRole(ManageRoleArgs args) => ...;
```

Every field is an enum whose zero value is `Unspecified`, as in `[McpToolExecution]`, so a field left out fails the
coverage test (D6) instead of reading back as a plausible default. The facts are declared after reading what the
tool's code does, not derived from its name or hints.

| Fact | Values | Meaning |
|---|---|---|
| `effect` | `read`, `change`, `destroy` | `read` changes nothing in any environment, on the machine or in clio's settings. `change` creates something or changes only what the call names; replacing the content of the one thing it names is still a change. `destroy` removes something, overwrites more than the one thing the call names or data the caller did not name one by one (a package, a workspace, a database), or runs code, SQL or a process whose effect clio cannot bound. |
| `rebuilds-environment` | `true`, `false` | The call compiles the configuration, restarts or stops the application, installs packages or the process builder, flushes its cache or switches the file system mode. |
| `scope` | `local`, `environment`, `machine` | The widest place the call reads or changes. `local`: clio itself and the files the call names. `environment`: the Creatio environment the call names. `machine`: this computer beyond the files the call names (installed software and services, local Creatio instances, local databases, clio itself, the agents' configuration, knowledge sources, links between folders). |
| `connections` | `none`, `read`, `change` | Whether the call shows or changes clio's own connections: the registered environments and the credentials clio holds or creates for them. `read` returns them, or hands back a credential derived from one (a signed-in browser session, a token) without storing it; `change` registers, removes or edits one, or creates a lasting credential (an OAuth client) and hands it back. Resolving an environment name to sign in is `none`. |

Two things are deliberately not effects: writing a file or folder the call names in an argument (a
`get-...-to-file` tool that only reads the environment is a `read`), and clio's own caches and working files (page
baselines, the browser-session cache, logs, the telemetry outbox). Which arguments name a file on the host, and
whether clio reads or writes it, is a fact of its own, per argument; it is out of scope here (see Consequences).

### D2. The tool's facts are the widest; argument entries only narrow them

The facts on `[McpToolFacts]` are the widest the tool does over every argument. `[McpToolArgumentFacts(argument,
value)]` declares narrower facts for one value of one argument: an `action` of a composite tool, or a flag such as
`dry-run`. An entry must declare its `Effect`; a field it leaves `Unspecified` takes the tool's value, and the
published entry carries every field resolved. `WhenAbsent = true` marks the value a call gets when it leaves the
argument out.

A host matches an entry when the call's arguments carry `argument` with `value` (compared case-insensitively; a
boolean as `true`/`false`), or leave `argument` out and the entry is `when-absent`. The argument is named as the
caller writes it: inside `args` for a tool whose arguments are one args record. When several entries match, the
host takes the widest value of each fact among them. When none matches, the tool's facts apply.

Because an entry can only narrow, a host that ignores `by-argument` is never less careful than one that reads it.

### D3. Where a host reads them

- **`tools/list`**: each resident tool's `_meta["clio/facts"]`. Written once, as a post-configuration of
  `McpServerOptions`, after the SDK fills the tool collection and before the server serves a request.
- **`get-tool-contract` with `detail=facts`**: under `facts`, by tool name. Without `tool-names` it answers every
  tool the index lists, `get-tool-contract` itself, and each deprecated alias under its own name, so a host can look
  up whatever name a `clio-run` call carries. With `tool-names` it answers those, and a name that is no tool comes
  back in `not-found`. A feature-gated-off tool is absent, as it is from every MCP surface.

The published object:

```json
{
  "effect": "destroy",
  "rebuilds-environment": false,
  "scope": "environment",
  "connections": "none",
  "by-argument": [
    { "argument": "action", "value": "list", "effect": "read", "rebuilds-environment": false,
      "scope": "environment", "connections": "none" }
  ]
}
```

### D4. The agent-facing answers do not change

The line index, the `index-entries` objects and the contracts carry no facts. They are budgeted to the byte
(`ToolContractPayloadBudgetTests`): the object index sits within 256 bytes of its ceiling and ClioRing parses it;
the largest contract has 68 bytes of room. The facts of 250 tools are about 25 KB, which an agent does not need on
every discovery call. A host asks for them once.

### D5. Backward compatible

- `readOnlyHint` / `destructiveHint` keep their values and `(R)` / `(D)` their meaning, except where the review
  found a hint that under-reports. One tool was a `destroy` with `destructiveHint = false` and now says `true`,
  which only adds a `(D)`: `create-user-task`, which loads every file-system package into the database when a
  parameter direction applies. The line index pays for the marker from its legend, not from its ceiling.
  `install-gate` and `install-toolkit` stay non-destructive, as their tests pin: each replaces only the one thing
  it names (cliogate, the toolkit), so both are a `change` (`install-gate` with a rebuild).
- An older clio answers `detail=facts` with the default line index; the absence of `facts` is how a host tells.

### D6. A coverage test keeps it true

`McpToolFactsCoverageTests` fails when an enabled tool:

- declares no `[McpToolFacts]`, or leaves a field `Unspecified`;
- has `readOnlyHint = true` but an effect other than `read`, or is a `destroy` with `destructiveHint = false`;
- is a `read` that rebuilds the environment or changes connections (at tool level or in an entry);
- has an argument entry on an argument it does not take, a duplicate entry, two `when-absent` values for one
  argument, or an entry wider than the tool;
- is a method-level alias whose facts differ from its canonical tool's.

Each check also runs over synthetic tools, so a green result over the catalog is not vacuous.

### D7. The facts were reviewed from the code

All 250 tools were read from their method into the command or service it calls, against written definitions, not
inferred from names. Host-side name lists were used only as a cross-check; where they disagreed, the code won
(`update-client-unit-schema` matched a machine-wide `update-cli` prefix; `compile-status` matched `compile`;
`start-creatio` and `show-passing-infrastructure` reach this machine). Result: 117 `read`, 74 `change`, 59 `destroy`;
35 rebuild the environment; scope 21 `local`, 187 `environment`, 42 `machine`; connections 5 `read`, 8 `change`.
30 tools narrow their facts by an argument value: the `manage-*` composites by `action`, and `dry-run`, `preview`,
`confirm` / `confirmed` flags. A narrowing that needs two arguments, or a member of a nested array
(`sync-schemas` `operations[].type`), is not declared: the tool's widest facts apply.

## Alternatives considered

- **Derive the facts from the name or the hints.** That is what hosts do today, and it is the problem.
  `readOnlyHint` is `false` for every tool that writes a file the call names, and `destructiveHint` cannot tell a
  create from a delete.
- **Only the standard MCP annotations.** They exist and stay, but they have no scope, no rebuild, nothing about
  connections and nothing per argument value, and `tools/list` holds only the resident tools.
- **Put the facts on every index entry and contract.** Rejected by D4: it moves every byte budget and the shape
  ClioRing parses, for information an agent does not need on every call.
- **One `scope` set (`environment` + `machine` + `connections`).** Two fields read more simply: `scope` is where the
  call acts (the widest), `connections` is whether it touches clio's own connections. `deploy-creatio` is
  `machine` and `connections: change`, and a host can act on either.

## Consequences

- A host can classify any call of any current tool from clio's output alone, and a new clio tool works in a host
  the day it ships.
- Adding a tool now means declaring its facts; the coverage test says so by name.
- Follow-ups, each its own change: per-argument facts for arguments that name a file or folder on the host (kind,
  read or write); a guide in the knowledge library that needs a newer clio than the one serving it saying so up
  front.
