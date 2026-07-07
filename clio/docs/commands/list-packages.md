# list-packages

## Command Type

    Package Management commands

## Name

list-packages - Get list of packages installed in Creatio environment

## Synopsis

```bash
list-packages [OPTIONS]
```

## Description

Retrieves and displays a list of all packages installed in the specified
Creatio environment. The command returns package information including name,
version, and maintainer for each installed package.

The command can filter results by package name and return data in either
table format (default) or JSON format for programmatic use.

This command is useful for:
- Auditing installed packages in an environment
- Finding specific packages by name
- Exporting package lists for documentation or comparison
- Integration with CI/CD pipelines and automation scripts

## Options

```bash
-e, --Environment       Environment name from the registered configuration
The environment must be registered using
'reg-web-app' command

-f, --Filter           Filter packages by name (case-insensitive partial match)
Shows only packages containing the specified text

-j, --json             Return results in JSON format (default: false).
With --json the output is the unified command envelope.

--legacy-form          Compatibility escape hatch (only with --json): emit the
historical {value, success, errorInfo} shape instead of the unified envelope.

Standard environment options are also available:
-u, --uri              Application URI
-l, --Login            User login (administrator permission required)
-p, --Password         User password
```

## Examples

```bash
# Get all packages from registered environment
clio list-packages -e MyEnvironment

# Get packages with "clio" in the name
clio list-packages -e MyEnvironment -f clio

# Get packages in JSON format for automation
clio list-packages -e MyEnvironment -j

# Filter and return as JSON
clio list-packages -e MyEnvironment -f Custom -j

# Using short form alias
clio list-packages -e MyEnvironment
```

## Aliases

- packages

## Output Format

Table format (default):
Name                Version         Maintainer
──────────────────────────────────────────────
PackageName1        1.0.0          Company
PackageName2        2.1.3          Developer

JSON format (`--json`) — unified envelope (default):

```json
{
  "schemaVersion": "1.0",
  "ok": true,
  "command": "list-packages",
  "data": [ { "descriptor": { "name": "...", "packageVersion": "...", "maintainer": "..." } } ],
  "error": null
}
```

On failure: `ok=false`, `data=null`, `error={ "code": "<stable-code>", "message": "..." }`.

JSON format (`--json --legacy-form`) — legacy shape (deprecated):

```json
{ "value": [ /* packages */ ], "success": true, "errorInfo": null }
```

> **Migration note:** the default `--json` shape changed from `{value,success,errorInfo}` to the
> unified envelope `{schemaVersion,ok,command,data,error}`. Consumers not yet migrated can pass
> `--legacy-form` to keep the old shape during the transition. The non-JSON (table) output is unchanged.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See Also

pull-pkg, push-pkg, delete-pkg-remote, new-pkg

- [Clio Command Reference](../../Commands.md#list-packages)
