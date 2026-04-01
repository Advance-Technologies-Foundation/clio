# unlock-package

Unlock a package in Creatio.


## Usage

```bash
clio unlock-package [package-names] [options]
clio unlock-package [package-names] [options]
```

## Description

unlock-package - Unlock packages in Creatio environment

## Aliases

`up`

## Examples

```bash
Unlock a single package:
clio unlock-package MyPackage -e dev
clio unlock-package MyPackage -e dev

Unlock multiple packages:
clio unlock-package Package1,Package2,Package3 -e dev

Unlock all packages:
clio unlock-package -m Creatio -e dev

Output:
Setting Maintainer sys setting to 'Creatio'.
Setting SchemaNamePrefix sys setting to an empty value.
Unlocking all packages in environment 'dev' for maintainer 'Creatio'.
Done

Using direct authentication:
clio unlock-package MyPackage --uri https://myapp.com -l admin -p pass
```

## Arguments

```bash
    package-names
        Comma-separated list of package names to unlock. If omitted, unlocks
        all packages in the environment.
        Example: MyPackage,AnotherPackage,ThirdPackage
```

## Options

```bash
-m, --maintainer <NAME>
Maintainer value used to set Maintainer system setting and clear
SchemaNamePrefix before unlocking all packages.
Required when unlocking all packages (without package-names).

-e, --environment <NAME>
Environment name from configuration (recommended)

-u, --uri <URI>
Creatio application URI (alternative to environment)

-l, --login <LOGIN>
Username for authentication

-p, --password <PASSWORD>
Password for authentication

--clientid <ID>
OAuth Client ID (alternative authentication)

--clientsecret <SECRET>
OAuth Client Secret (alternative authentication)

--authappuri <URI>
OAuth Authentication App URI (alternative authentication)
```

## Requirements

This command requires cliogate package version 2.0.0.0 or higher installed
on the target Creatio environment.

To install cliogate:
clio install-gate -e <ENVIRONMENT_NAME>

To check cliogate version:
clio get-info -e <ENVIRONMENT_NAME>

## Notes

- Package names are case-sensitive and must match exactly
- Use environment configuration for easier command usage
- Remember to lock packages after completing changes
- Administrator permissions required on target environment

## See also

- `lock`
- `push-pkg`
- `push-pkg`
- `get`

- [Clio Command Reference](../../Commands.md#unlock-package)
