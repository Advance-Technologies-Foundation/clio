# install-dashboards-migrator

## Name

install-dashboards-migrator - Install the bundled dashboards-migrator package to a Creatio environment

## Synopsis

```bash
clio install-dashboards-migrator [OPTIONS]
clio update-dashboards-migrator [OPTIONS]
```

## Description

Installs the `CrtDashboardsMigratorApp` package — the **Dashboards migrator** app — to a Creatio
environment. The package ships inside clio and converts Classic UI (7.x) dashboards into Freedom UI
dashboards. After installing, run the migration from System Designer (**Dashboards migration**) and
review the result in the **Dashboards migration log** section.

The package requires **Creatio 8.3.1 or later**. The command does not check this up front: an older
instance accepts the archive and fails the configuration build, which the outcome check below reports.

The package ships **prebuilt**, like cliogate: one archive carries the package assembly for both .NET
Framework (`Files/Bin`) and .NET (`Files/Bin/netstandard`), so the target does not compile the package.
The platform still runs its configuration build for the package's schemas and restarts afterwards — the
platform recycles itself on .NET Framework, the package installer issues the restart on .NET — and the
command waits for the instance to answer its health check before checking the result.

After a successful install the command asks the package's own service whether it is serving —
`DashboardsMigratorService/Ping`, ungated — and fails unless it answers, so a package that was accepted
but is not serving is reported instead of looking like success. The check is **liveness, not identity**:
on an upgrade a stale assembly that still answers passes, so after an upgrade treat the migration working
as the proof.

The command **always installs except in two cases**, and re-running is otherwise safe; it costs one
configuration build on the target. Both exceptions are about moving an environment **backwards**, and
`--force` overrides both:

1. **A downgrade.** The environment already carries a newer version than this clio ships.
2. **A malformed distribution.** This clio's own bundled version carries a pre-release suffix.
   Nothing about the target is wrong — reinstall or update clio.

Reinstalling the SAME version is not a downgrade and is always allowed.

The install flow is shared with [`install-process-builder`](install-process-builder.md); that page
carries the full reasoning behind each step.

## Options

    -e, --environment <ENVIRONMENT_NAME>
        Target environment name from your configuration

    --force
        Install despite either backwards-move refusal. Reinstalling the SAME
        version never needs this flag.

    Environment options (can be used instead of -e):
        -u, --uri <URI>
            Application URI

        -l, --Login <LOGIN>
            User login (administrator permission required)

        -p, --Password <PASSWORD>
            User password

## Examples

```bash
clio install-dashboards-migrator -e dev
```

```bash
clio update-dashboards-migrator -e dev
```

## Prerequisites

- Creatio 8.3.1 or later on the target.
- Permission to install a package on the target environment (the install itself runs a
  configuration build and restarts the instance).
- Running the migration afterwards requires the `CanMigrateDashboard` operation, which the package
  registers for System administrators on install.

## Notes

- The command installs the version bundled with your current clio installation (`clio info`, line
  `dashboards-migrator`); it never downloads anything.
- `clio list-packages` shows the version the environment **recorded**, which is what the downgrade
  check compares against.
- If the command reports that the Ping route does not answer, the package installed but the
  environment did not compile it. Check the environment's configuration build log;
  `clio restore-configuration` rolls the install back.
- Measured once, on a shared net472 stand (2026-09-10, source archive): a repeat install took under two
  minutes end to end, dominated by the target's configuration build. An anecdote, not a budget — the
  duration belongs to the environment.

## See Also

install-process-builder - Install or update the other bundled package
list-packages - List packages in a Creatio environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-dashboards-migrator)
