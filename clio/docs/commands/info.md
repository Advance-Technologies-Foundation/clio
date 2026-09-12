# info

## Name

info - Display version information for clio and related components

## Synopsis

```bash
clio info [OPTIONS]
clio ver [OPTIONS]
clio get-version [OPTIONS]
clio i [OPTIONS]
```

## Description

Displays version information for clio, cliogate, the bundled `CrtProcessBuilder`
package, installed knowledge bundles, toolkit versions per coding agent, and the .NET runtime
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
process-builder:   1.0.0.0
knowledge (creatio-curated):   1.14.10
toolkit (claude):   1.10.0
toolkit (codex):   1.10.0
toolkit (cursor):   not installed
toolkit (copilot):   not installed
dotnet: 8.0.0
settings file path: C:\Users\username\.clio\appsettings.json

Individual component output:
clio:   8.0.1.97

## Notes

- Toolkit versions are read locally for the global installations managed by `clio update-toolkit`:
  Claude, Codex, Cursor, and Copilot. Each agent gets a separate line. Absent installations show
  `not installed`; unreadable or missing version metadata shows `unknown (metadata unavailable)`.
  No agent CLI or remote update check is run. Component-only options skip toolkit inspection.
  `CODEX_HOME` and `CLAUDE_CONFIG_DIR` overrides are honored when set.

- Knowledge versions are read from local installation records, including disabled sources, without
  checking publishers for updates. This reports GitHub Release and NuGet bundles; use `info-knowledge`
  for Git checkouts. Each installed source is named. If no readable record exists,
  the line shows `knowledge: not installed or unavailable`. Use `info-knowledge` for detailed validity
  and source status.

- The `process-builder` version is the `CrtProcessBuilder` package this clio **carries**, read out of the
  bundled archive itself rather than from any environment. It is the same value the process-designer
  commands compare against, so comparing it with `clio list-packages -e <environment>` tells you whether
  that environment is behind what this clio would install.
- If that line reads `unavailable — …`, this clio installation cannot read its own bundled archive. The
  message names the file and the remedy is to reinstall or update clio; nothing done to an environment can
  help. Gated commands still work against an environment that already has the package — they simply stop
  offering to update it.
- The cliogate version shown is the version included with current clio
installation
- This may differ from the version installed on a specific Creatio instance
- Use 'get-info' command to check the actual cliogate version on an
environment

## See Also

get-info - Get information about a Creatio instance
update-cli - Update clio to the latest version

- [Clio Command Reference](../../Commands.md#info)
