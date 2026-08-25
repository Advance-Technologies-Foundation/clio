# add-custom-logging

## Name

add-custom-logging - Add package-specific NLog file routing to a local Creatio environment

## Description

`add-custom-logging` reads the generated `Constants.LoggerName` from a Creatio package and configures a dedicated NLog file route. It adds a logger rule to `nlog.config` and a file target to `nlog.targets.config`.

The environment must be registered locally with `EnvironmentPath`. Net8 installations keep both files at that path. .NET Framework installations keep them in its `Terrasoft.WebApp` folder.

The command validates both XML documents before writing either one. It rejects duplicate or conflicting same-name entries, preserves unrelated text and file encoding, and rolls back a partially committed two-file update. Re-running an exact configuration is a no-op.

## Synopsis

```bash
clio add-custom-logging --package-name <PACKAGE> [-e <ENVIRONMENT>] [OPTIONS]
```

## Options

```bash
--package-name <PACKAGE>
Package containing Files/src/cs/Constants.cs with LoggerName (required)

-e, --environment <ENVIRONMENT>
Registered local environment with EnvironmentPath configured. When omitted, clio uses the active environment.

--min-level <LEVEL>
Trace, Debug, Info, Warn, Error, Fatal, or Off (default: Info)

--file-name <FILE>
Simple file name beneath ${TodayLogPath}; .log is appended when omitted

--restart-environment
Restart the environment after updating logging (default: false)
```

Use the long `--package-name` form. The inherited `-p` option means `--password`, but this command rejects direct connection and runtime overrides (including `--password`, `--uri`, and `-i`) because both the file edit and optional restart must resolve from one registered environment.

## Result

For a generated logger named `UsrCodexVirtualEntityApp`, the default route is equivalent to:

```xml
<logger name="UsrCodexVirtualEntityApp"
        writeTo="usrCodexVirtualEntityAppender"
        minlevel="Info"
        final="true" />
```

```xml
<target name="usrCodexVirtualEntityAppender"
        xsi:type="File"
        layout="${DefaultLayout}"
        fileName="${TodayLogPath}/UsrCodexVirtualEntity.log" />
```

The command prints the registered environment name and configured log path. Unless `--restart-environment` is supplied, it also explains that a restart is required before the route is guaranteed to be active.

The generated logger rule is final, so matching package messages are routed to the dedicated file instead of continuing to the common catch-all logger.

## Examples

```bash
clio add-custom-logging --package-name UsrCodexVirtualEntity -e dev
```

Configure the default `Info` route without restarting.

```bash
clio add-custom-logging --package-name UsrCodexVirtualEntity -e dev --min-level Debug --file-name virtual-entity.log
```

Use `Debug` as the minimum level and override the file name.

```bash
clio add-custom-logging --package-name UsrCodexVirtualEntity -e dev --restart-environment
```

Configure the route and explicitly restart Creatio.

## Errors and safety

- The package and exactly one generated `LoggerName` constant must exist.
- Both NLog files must be well-formed and have recognized `rules` and `targets` sections.
- Existing same-name entries must exactly match the requested route; conflicts are never overwritten.
- `--file-name` accepts a simple file name only. Paths and NLog layout expressions are rejected.
- Both originals are backed up before writing. If either saved document fails verification, both originals are restored.
- If the registered environment does not store credentials needed by restart, configure without `--restart-environment` and run `clio restart` with the required credentials separately.

## See Also

- [add-package](add-package.md)
- [list-environments](list-environments.md)
- [restart](restart.md)
- [Clio Command Reference](../../Commands.md#add-custom-logging)
