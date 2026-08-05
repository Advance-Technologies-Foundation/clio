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
missing, and name this command in the refusal. The requirement is
**presence-only** — see Notes for why there is no version floor.

The package ships as source, without a compiled assembly, and the target
environment compiles it during installation against its own core. One archive
therefore serves every runtime.

You never need to run a restart yourself, but a restart does happen — and it comes
from a different place on each runtime:

| Runtime | Who restarts |
|---|---|
| .NET Framework | the **platform recycles itself**, because the workspace assembly changed (`Workspace assembly changed - Run restart application` in its Application log) |
| .NET | the **package installer** issues the restart, because the target is a .NET host |

Either way that restart outlives the install call, so this command waits for the
instance to answer its health check before checking the service. Probing sooner
would either fail while the app warms up or, when upgrading, be answered by the
outgoing app domain still serving the old assembly.

Because the assembly is produced by the target rather than shipped, installing and
working are different states. After a successful install the command calls
`ListUserTasks` and fails if ProcessDesignService does not answer, so an
environment that accepted the package but never compiled it is reported instead of
looking like a success.

The command **always installs** — there is no skip. Re-running is safe; it costs one
configuration build on the target.

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

- Permission to install a package on the target environment (the install itself
  runs a configuration build and restarts the instance).
- Read access to SysPackage through DataService, which is how the process-designer
  commands check that the package is present.

Once installed, using the process-designer commands additionally requires the
`CanManageProcessDesign` operation and a General (non-portal) user — the gate
ProcessDesignService enforces in its own handlers. That is deliberately stricter
than cliogate's `CanManageSolution`, which is broader and does not check the
connection type, so granting `CanManageSolution` does not grant process design.

## Notes

- The command installs the version of the package bundled with your current
  clio installation; it never downloads anything.
- Installation includes a configuration build on the target environment, so it
  takes longer than a plain package install — roughly 15 to 75 seconds depending
  on the environment's speed.
- Do **not** use `clio list-packages` to decide whether the package needs
  installing. Creatio does not rewrite a package's `SysPackage` row when it
  re-installs a package it already has, so the recorded version stays whatever the
  *first* install wrote and says nothing about what is running. That is also why the
  process-designer commands require the package by **presence** only, with no
  version floor: a floor could never be satisfied by an environment that was
  upgraded correctly.
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
