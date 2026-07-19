# list-knowledge-examples

## Command Type

    Integrations & tools

## Name

list-knowledge-examples - Discover registered reference examples without cloning them

## Synopsis

```bash
clio list-knowledge-examples [--source <alias>] [--search <text>] \
    [--capability <tag>] [--status <status>] [--json]
```

## Description

Lists all matching reference examples registered in active local knowledge catalogs. A NuGet
catalog carries verified package/signature/digest provenance; a Git catalog carries its configured
trusted repository and exact resolved-commit provenance after catalog validation. The catalog
contains the example repository URL, immutable revision, entry points, use case, capabilities,
compatibility, and publisher trust metadata. An example repository does **not** need to be cloned or
otherwise installed locally to appear in the result.

Discovery is deliberately local-only. The command does not contact Git, NuGet, or an example
repository, and it never clones example code. Use `install-knowledge` or `update-knowledge` when a
trusted source's local catalog is missing or stale; then run this command to decide which example,
if any, should be pulled separately.

Human-readable output shows the source, example ID, title, status, primary use case, repository,
immutable revision, and capabilities. `--json` returns the full catalog metadata and provenance,
including source priority, catalog sequence and digest, entry points, compatibility, trust, and
notes.

## Options

| Option | Required | Description |
|---|---:|---|
| `--source <alias>` | No | Inspect one configured source alias. Matching is case-insensitive. Omit it to inspect every active source. |
| `--search <text>` | No | Case-insensitive text matched across example IDs, titles, primary use cases, source alias/library ID, and supporting capabilities. |
| `--capability <tag>` | No | Match one supporting-capability tag, case-insensitively. |
| `--status <status>` | No | Match one catalog publication status, case-insensitively. |
| `--json` | No | Emit full example metadata as indented JSON. Defaults to `false`. |

Each text filter is trimmed and cannot exceed 200 characters. Filters are combined with `AND`: an
example must satisfy every supplied filter.

## Examples

List every discoverable example from active cached catalogs:

```bash
clio list-knowledge-examples
```

Find Kafka examples or all examples demonstrating native-library management:

```bash
clio list-knowledge-examples --search kafka
clio list-knowledge-examples --capability native-library-lifecycle
```

Limit discovery to one knowledge publisher and catalog status:

```bash
clio list-knowledge-examples --source creatio --status published
```

Return complete machine-readable metadata before deciding whether to clone a repository:

```bash
clio list-knowledge-examples --search pubsub --json
```

## MCP

The read-only MCP tool `list-knowledge-examples` exposes the same four filters: `source`, `search`,
`capability`, and `status`. It returns structured full metadata, so an agent can discover an example
and its immutable repository revision before deciding whether to pull it. The MCP call is also
local-only and never clones or contacts the referenced repository.

## Exit Codes

    0   Matching examples were listed, or no examples matched
    1   A filter or cached catalog entry was invalid

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#list-knowledge-examples)
