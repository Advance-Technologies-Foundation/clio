# find-entity-schema

Find entity schemas in a Creatio environment by name, substring pattern, or UId.

## Synopsis

```
clio find-entity-schema --search-pattern <pattern> -e <env>
clio find-entity-schema --schema-name <name> -e <env>
clio find-entity-schema --uid <guid> -e <env>
```

## Description

Searches for entity schemas in the remote Creatio environment using a single DataService query on `SysSchema`. Returns schema name, owning package, package maintainer, and parent schema for every match.

Exactly one of `--schema-name`, `--search-pattern`, or `--uid` must be provided.

This command does not require knowing the package name upfront and does not require cliogate.

## Options

| Option | Required | Description |
|---|---|---|
| `--schema-name` | one of | Exact entity schema name to find |
| `--search-pattern` | one of | Case-insensitive substring to search in schema names |
| `--uid` | one of | Entity schema UId (Guid) |
| `-e` / `--Environment` | ✅ | Environment name from registered configuration |
| `-u` / `--uri` | | Application URI |
| `-l` / `--Login` | | User login |
| `-p` / `--Password` | | User password |

## Examples

### Search for schemas by substring

```bash
clio find-entity-schema -e dev --search-pattern Task
```

Output:
```
UsrTask | UsrTaskApp (Advance) | Parent: BaseEntity
UsrTaskStatus | UsrTaskApp (Advance)
```

### Look up a schema by exact name

```bash
clio find-entity-schema -e dev --schema-name UsrVehicle
```

### Look up a schema by UId

```bash
clio find-entity-schema -e dev --uid 117d32f9-aab9-4e3a-b13e-cfce62e15e4b
```

## Output format

Each result line uses the format:

```
SchemaName | PackageName (Maintainer) | Parent: ParentSchemaName
```

The `| Parent: …` suffix is omitted when there is no parent.

## Notes

- A single DataService request is used — no N+1 package enumeration.
- The search does not require cliogate to be installed.
- Use `get-entity-schema-properties` with the discovered `--package` to read full schema details.

## See also

- [`get-entity-schema-properties`](get-entity-schema-properties.md)
- [`modify-entity-schema-column`](modify-entity-schema-column.md)
