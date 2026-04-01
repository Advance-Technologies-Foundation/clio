# info

## Name

info - Display version information for clio and related components

## Synopsis

```bash
clio info [OPTIONS]
clio info [OPTIONS]
clio info [OPTIONS]
clio info [OPTIONS]
```

## Description

Displays version information for clio, cliogate, and the .NET runtime
environment. By default (without options), displays all component versions
and the path to the settings file.

This command is useful for troubleshooting, verifying installations, and
checking component versions for compatibility.

## Options

```bash
--all
Display all component versions (default behavior when no specific
option is provided)

--clio
Display only the clio version

--gate
Display only the cliogate version included with clio installation
(note: this may differ from version installed on Creatio instances)

--runtime
Display only the .NET runtime version

-s, --settings-file
Display the full path to the clio settings file
```

## Examples

```bash
Display all versions (default):
clio info

Display only clio version:
clio info --clio

Display cliogate version:
clio info --gate

Display .NET runtime version:
clio info --runtime

Display settings file path:
clio info -s
```

## Output Format

Default output (all versions):
clio:   8.0.1.97
gate:   2.0.0.38
dotnet: 8.0.0
settings file path: C:\Users\username\.clio\appsettings.json

Individual component output:
clio:   8.0.1.97

## Notes

- The cliogate version shown is the version included with current clio
installation
- This may differ from the version installed on a specific Creatio instance
- Use 'get-info' command to check the actual cliogate version on an
environment

## See Also

get-info - Get information about a Creatio instance
update-cli - Update clio to the latest version

- [Clio Command Reference](../../Commands.md#info)
