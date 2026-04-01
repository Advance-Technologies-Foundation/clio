# lock-package

## Description

lock-package - Lock packages in Creatio environment

## Synopsis

```bash
clio lock-package [package-names] [options]
clio lock-package [package-names] [options]
```

## Description

Locks one or more packages in a Creatio environment to prevent
modifications and unintended changes. Use this command to protect
packages after completing development work, ensuring system stability.

## Arguments

```bash
package-names
Comma-separated list of package names to lock. If omitted, locks
all packages in the environment.
Example: MyPackage,AnotherPackage,ThirdPackage
```

## Options

```bash
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

## Examples

```bash
Lock a single package:
clio lock-package MyPackage -e dev
clio lock-package MyPackage -e dev

Lock multiple packages:
clio lock-package Package1,Package2,Package3 -e dev

Lock all packages:
clio lock-package -e dev

Complete development cycle:
clio unlock-package MyPackage -e dev
# Make changes...
clio push-workspace -e dev
clio lock-package MyPackage -e dev

Using direct authentication:
clio lock-package MyPackage --uri https://myapp.com -l admin -p pass
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
- Always lock packages after completing development work
- Administrator permissions required on target environment
- Include package locking as final step in deployment pipelines

## See Also

unlock-package    Unlock packages to enable modifications
install-gate      Install or update cliogate package
push-workspace    Push workspace changes to environment
get-info          Check environment and cliogate information

- [Clio Command Reference](../../Commands.md#lock-package)
