# manage-windows-features

## Command Type

    Installation and Setup commands

## Name

manage-windows-features - manage Windows features required for Creatio

## Description

manage-windows-features command allows you to check, install, or uninstall
Windows features required for running Creatio on Windows Server or Windows
desktop operating systems. Starting with Windows 11 26H1 (build 28000),
the command does not check or install .NET Framework 3.5 Feature on Demand
components because Windows no longer exposes them through Windows Features.

This command is only available on Windows operating system. Administrator
rights are required for install and uninstall operations. When executed on
macOS or Linux, the command will return an error message with exit code 1.

The command operates in three modes:

Check mode (-c):
Verifies the installation status of all required Windows features and
reports which features are missing or properly installed.

Install mode (-i):
Installs all missing Windows features required for Creatio. Requires
administrator privileges.

Uninstall mode (-u):
Removes all Windows features required for Creatio. Requires
administrator privileges.

For detailed information about required Windows components, visit:
https://academy.creatio.com/docs/user/on_site_deployment/application_server_on_windows/check_required_components/enable_required_windows_components

## Synopsis

```bash
manage-windows-features [options]
```

## Options

```bash
--Check         -c          Check status of required Windows features

--Install       -i          Install all missing required features
(requires administrator rights)

--Uninstall     -u          Uninstall all required features
(requires administrator rights)
```

## Example

```bash
clio manage-windows-features -c
checks which Windows features are installed and which are missing

clio manage-windows-features -i
installs all missing Windows features (run as administrator)

clio manage-windows-features -u
uninstalls Windows features required for Creatio (run as administrator)

Example output for check mode (all features installed):
Check started:
OK : Static Content
OK : Default Document
OK : HTTP Errors
OK : ASP.NET 4.8
OK : HTTP Activation
OK : IIS Management Console
OK : Windows Authentication

All required components installed

Example output for check mode (missing features):
Check started:
OK : Static Content
Not installed : ASP.NET 4.8
Not installed : HTTP Activation

Windows has missed components:
Not installed : ASP.NET 4.8
Not installed : HTTP Activation
```

## Exit Codes

    0               Operation completed successfully
    1               Operation failed (missing features detected in check mode,
                    command run on non-Windows OS, or error during
                    install/uninstall)

## Aliases

mwf, mng-win-features

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#manage-windows-features)
