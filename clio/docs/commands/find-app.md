# find-app

Find installed applications and their sections in a Creatio environment by name, code, or substring pattern.

## Synopsis

```
clio find-app --search-pattern <pattern> -e <env>
clio find-app --code <app-code> -e <env>
clio find-app -e <env>
```

## Description

Finds installed applications **and their sections** behind a single call. The command loads all
applications with one `SysInstalledApp` query, then loads each application's sections with a
per-application `ApplicationSection` query filtered by `ApplicationId`, and returns every matching
application together with its sections.

> `ApplicationSection` returns no rows for an unfiltered query, so sections are read per application
> (the same path `list-app-sections` uses). The internal query count is therefore `1 + N`
> (N = application count), or `1 + 1` when an exact `--code` is supplied.

The optional `--search-pattern` is a case-insensitive substring matched across the application
name, code, description, and the caption/code of each section. The optional `--code` narrows the
result to one application by exact code. Omit both filters to list every application with its
sections in a single call.

This lets an agent map an imprecise application name (for example `Customer Request Management`) to
its real code (for example `CrtCaseManagementApp`) without the N+1 pattern of `list-apps` followed
by `list-app-sections` for every application.

This command does not require cliogate.

## Options

| Option | Required | Description |
|---|---|---|
| `--search-pattern` | | Case-insensitive substring matched against application name, code, description, and section captions/codes |
| `--code` | | Exact installed application code to match |
| `--json` | | Output indented JSON instead of a table |
| `-e` / `--Environment` | ✅ | Environment name from registered configuration |
| `-u` / `--uri` | | Application URI |
| `-l` / `--Login` | | User login |
| `-p` / `--Password` | | User password |

## Examples

### Find applications and sections matching a fuzzy term

```bash
clio find-app -e dev --search-pattern case
```

Output:
```
App: Case Management (CrtCaseManagementApp) v1.0.0 | Sections: 1
  - Cases (Cases) -> Case
```

### Resolve a single application by exact code

```bash
clio find-app -e dev --code CrtCaseManagementApp
```

### List every application with its sections as JSON

```bash
clio find-app -e dev --json
```

## Output format

In table mode each application is printed as:

```
App: <Name> (<Code>) v<Version> | Sections: <count>
  - <Section caption> (<Section code>) -> <EntitySchemaName>
```

The ` -> <EntitySchemaName>` suffix is omitted when the section has no bound entity. Use `--json`
for the full structured payload.

## Notes

- The whole sweep runs behind a single tool call: the agent makes one `find-app` call instead of
  `list-apps` followed by a `list-app-sections` call per application. Internally clio issues one
  `SysInstalledApp` query plus one `ApplicationSection` query per application (because
  `ApplicationSection` returns nothing unfiltered).
- An empty search (no `--search-pattern` and no `--code`) returns every application, each with its
  sections — a superset of `list-apps`.
- The MCP `find-app` tool returns the same data as a structured `{ success, applications }`
  envelope. MCP callers should use the structured fields (application `code`, section `code`,
  `entity-schema-name`, …) directly instead of parsing the CLI text form.
- The returned application `code` and section `code` can be passed directly to follow-up commands
  such as `get-app-info`, `list-app-sections`, or `create-app-section`.

## See also

- [`list-apps`](list-apps.md)
- [`get-app-info`](get-app-info.md)
- [`list-app-sections`](list-app-sections.md)
- [`find-entity-schema`](find-entity-schema.md)
