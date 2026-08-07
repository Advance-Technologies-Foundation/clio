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

Those commands refuse to run against an environment where the package is missing
**or older than the version bundled with clio**, and name this command in the
refusal.

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
working are different states. After a successful install the command asks the package's
own service whether it is serving — `Ping`, ungated — and fails unless it answers. No
database read can establish this: `SysPackage` records the version the environment
ACCEPTED whether or not anything compiled.

| Case | Outcome |
|---|---|
| First install, build failed | **Caught.** Nothing answers. Creatio registers service routes by reflecting over LOADED types, so with no compiled assembly there is no `ProcessDesignService` type and no route |
| Re-install of the same version | **Passes**, as it should — the package is compiled and serving, which is the whole question |
| Upgrade, build failed | **Not caught.** The previously built assembly is still loaded and answers, so the check passes while old code serves |

The last row is a deliberate limit, not an oversight. Catching it requires the shipped
version to be readable back out of the running code, and for a source-only package that
means keeping a hand-maintained copy of the version inside the sources — the assembly
version belongs to the platform (measured: a stand reported `10.1.453.0` for a package
installed as `1.1.0.1`, because the platform stamps what it compiles) and
`descriptor.json` is not present in the target's build directory (also measured). That
duplicate was judged more expensive than the case it would catch; revisit if stale-build
upgrades prove common. After an upgrade, treat the functionality working as the proof.

`clio list-packages` cannot substitute for this: it reads the version the environment
RECORDED, which moves when the archive is accepted whether or not anything compiled.
Only the package's own code answering a request can establish that it was compiled at
all — which is what this check asks, and the most it can ask.

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

That right does **not** decide this command's verdict, and the separation is
deliberate. The post-install check calls `Ping`, which is ungated, so a caller who
may install the package but may not design processes still gets a truthful answer
about the install itself.

An earlier version probed the gated `ListUserTasks` and so conflated two verdicts —
"the build did not take" and "you may not design processes". Those are different
problems with different fixes, and only the first is this command's business. A
caller lacking the right finds out at its next call, from the guard's own message,
which names the right.

Consequence worth stating plainly: **exit code 1 from this command never means a
missing permission.** It means the archive did not install, or the environment did
not compile it — and the place to look is the configuration build log.

## Notes

- The command installs the version of the package bundled with your current
  clio installation; it never downloads anything.
- Installation includes a configuration build on the target environment, so it takes
  substantially longer than a plain package install. **How long is a property of the
  environment** — its configuration size, host and load — not of clio, so no figure here
  would hold across environments. The command waits for the instance to answer its health
  check rather than for a fixed budget.
- `clio list-packages` shows the version the environment **recorded**, which is what
  clio compares against the version it ships (`clio info`). It moves on install only when the archive's
  descriptor also changed its `ModifiedOnUtc` — that field is what Creatio treats as
  "this descriptor changed", and `PackageVersion` is not part of the comparison
  (`PackageStorageComposer.ApplySourcePackageChanges` → `IsPackageDescriptorChanged` →
  `PackageDBStorage.SavePackageDescriptor`'s guard). This concerns whoever *builds* a
  package archive, not whoever installs one, and only if they edit `descriptor.json`
  by hand: `clio set-pkg-version` writes both fields, so a bump through it always
  takes effect.
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
