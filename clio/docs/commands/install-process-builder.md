# install-process-builder

## Name

install-process-builder - Install the bundled process-builder package to a Creatio environment

## Synopsis

```bash
clio install-process-builder [OPTIONS]
clio update-process-builder [OPTIONS]
clio installprocessbuilder [OPTIONS]
```

## Description

Installs the "CrtProcessBuilder" package to a Creatio environment. The package
ships inside clio and serves ProcessDesignService, the backend endpoint that
builds and edits business processes from a declarative descriptor.

The package is required by the process-designer capability:
- create-business-process / modify-business-process
- describe-business-process
- list-user-tasks
- validate-process-graph

Those commands refuse to run against an environment where the package is
missing or older than the version bundled with clio, and name this command in
the refusal.

The package ships as source, without a compiled assembly, and the target
environment compiles it during installation against its own core. One archive
therefore serves every runtime, and no application restart is needed: the
configuration build that compiles the package also loads the result.

Because the assembly is produced by the target rather than shipped, installing
and working are different states. After a successful install the command calls
ListUserTasks and fails if ProcessDesignService does not answer, so an
environment that accepted the package but never compiled it is reported instead
of looking like a success.

A compatible installation is detected and left alone, so re-running the command
on an up-to-date environment does nothing and does not make it recompile.

## Options

    -e, --environment <ENVIRONMENT_NAME>
        Target environment name from your configuration

    Environment options (can be used instead of -e):
        -u, --uri <URI>
            Application URI

        -l, --Login <LOGIN>
            User login (administrator permission required)

        -p, --Password <PASSWORD>
            User password

## Examples

Install the package using a configured environment:

```bash
clio install-process-builder -e dev
```

Install with direct credentials:

```bash
clio install-process-builder -u https://myapp.creatio.com -l administrator -p password
```

Update an existing installation:

```bash
clio update-process-builder -e dev
```

## Prerequisites

- Administrator permissions on the target environment. Every
  ProcessDesignService method is gated on CanManageSolution.
- Read access to SysPackage through DataService, which is how clio detects
  whether the package is already installed.

## Notes

- The command installs the version of the package bundled with your current
  clio installation; it never downloads anything.
- Installation includes a configuration build on the target environment, so it
  takes longer than a plain package install — roughly 15 to 75 seconds depending
  on the environment's speed.
- Use 'clio list-packages -e <ENV>' to verify the installed version.
- Installing does not unlock maintainer packages, even on an environment with
  developer mode enabled.
- If the command reports that ProcessDesignService does not answer, the package
  installed but the environment did not compile it. Check the environment's
  configuration build log. The Application Hub can roll such an install back
  through its own restore step; from the command line use
  `clio restore-configuration`.

## See Also

install-gate - Install or update cliogate in Creatio
list-packages - List packages in a Creatio environment
restart-web-app - Restart a Creatio application

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-process-builder)
