# install-gate

Install or update cliogate in Creatio.


## Usage

```bash
clio install-gate [OPTIONS]
clio install-gate [OPTIONS]
clio install-gate [OPTIONS]
clio install-gate [OPTIONS]
```

## Description

Installs the "cliogate" service package to a Creatio environment. This
package expands clio's capabilities by enabling advanced remote commands
and operations on the Creatio instance.

The cliogate package is required for:
- Workspace operations (push-workspace, restore-workspace)
- T.I.D.E. (Terribly Isolated Development Environment) functionality
- Advanced remote operations and extended API access
- Database and configuration management commands

The command automatically restarts the Creatio application after
installation to apply the changes.

## Aliases

`gate`, `installgate`, `update-gate`

## Examples

```bash
Install cliogate using configured environment:
clio install-gate -e dev

Install cliogate with direct credentials:
clio install-gate -u https://myapp.creatio.com -l administrator -p password

Update existing cliogate installation:
clio install-gate -e production

Using shortest alias:
clio install-gate -e demo
```

## Options

```bash
-e, --environment <ENVIRONMENT_NAME>
Target environment name from your configuration

Environment options (can be used instead of -e):
-u, --uri <URI>
Application URI

-l, --Login <LOGIN>
User login (administrator permission required)

-p, --Password <PASSWORD>
User password
```

## Notes

- The command installs the version of cliogate bundled with your current
clio installation
- After installation, the Creatio application will automatically restart
- Administrator permissions are required on the target environment
- Use 'clio info --gate' to check the cliogate version included with clio
- Use 'clio get-info -e <ENV>' to verify the installed cliogate version

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

## See also

- `push-pkg`
- `push-pkg`
- `get`
- `info`

- [Clio Command Reference](../../Commands.md#install-gate)
