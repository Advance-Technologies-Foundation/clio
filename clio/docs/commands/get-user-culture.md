# get-user-culture

## Command Type

    Information commands

## Name

get-user-culture - Print the logged-in Creatio user's profile culture.

## Aliases

    profile-language

## Description

The `get-user-culture` command resolves and prints the culture (for example
`en-US` or `uk-UA`) set in the profile of the user that clio is connected as on
the target environment.

This is the culture clio applies to the names, labels, and captions of the
entities it creates (applications, objects, pages, sections, lookups, columns),
so the verb is the observable, testable way to confirm which language new
entities will be generated in — independent of the host machine locale and of
any third-party MCP server.

The culture is read from the standard Creatio service
`ServiceModel/ApplicationInfoService.svc/GetApplicationInfo`
(`applicationInfo.sysValues.userCulture.displayValue`). It requires only an
authenticated session — **cliogate is not required**. The resolved value is
validated as a real .NET culture name and cached per environment for a few
minutes, so repeated calls within a session do not re-probe the server.

## Options

| Option | Description |
|--------|-------------|
| `-e`, `--environment` | Name of the registered environment to query. When omitted, the default registered environment is used. |
| `-u`, `--uri`, `-l`, `--login`, `-p`, `--password`, OAuth options | Standard direct-connection options for targeting an unregistered site. |

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | The profile culture was resolved; the culture name is printed to stdout. |
| `1` | The culture could not be resolved (environment unreachable, not authorized, or the user profile culture was not returned). A user-friendly `Error: ...` message is printed. |

## Examples

Print the profile culture of the default environment:

```bash
clio get-user-culture
```

Print the profile culture of a named environment:

```bash
clio get-user-culture -e production
```

Using the alias:

```bash
clio profile-language -e production
```

## See also

- `get-info` — broader Creatio system information (requires cliogate).
