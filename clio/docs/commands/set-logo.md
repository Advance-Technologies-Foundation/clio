# set-logo

## Command Type

    Customization commands

## Name

set-logo - apply the product logos from local image files and bind them into a package

## Description

`set-logo` writes the product logos of the target environment from local image files — one option per
logo slot — and binds the applied values into a package as Creatio data bindings
(`SysPackageSchemaData`), so the logos ship with the package on an install or a transfer to another
site instead of living only on the source environment.

There are three white-background logo types plus the logo for a dark background:

| Option | System setting | Where it appears |
|---|---|---|
| `--logo` | `LogoImage` | login page |
| `--menu-logo` | `MenuLogoImage` | main menu |
| `--configuration-logo` | `ConfigurationPageLogoImage` | configuration page |
| `--dark-logo` | `CrtAppToolbarLogo` | the Freedom UI top panel — a dark surface; pass the white/light variant of the logo |

Pass at least one slot. After applying, the command sets `HideSplashScreenLogoImage` to `true` so the
stock splash logo does not flash during load (best-effort: a failure is a warning, not a command
failure). The change applies to all users after a page refresh and cannot be automatically reverted
by clio.

Each file is uploaded as the slot's Binary sys-setting value: the environment's file-security policy
(extension allow/deny lists) is enforced client-side and a per-value size cap (10 MB) applies —
the same rules as `update-sys-setting` with `value-file-path`.

## Synopsis

```bash
clio set-logo [--logo <path>] [--menu-logo <path>] [--configuration-logo <path>] [--dark-logo <path>] [--package <name>] [options]
```

## Options

```bash
--logo                          Local image file for the main logo, shown on the login page (LogoImage).

--menu-logo                     Local image file for the main menu logo (MenuLogoImage).

--configuration-logo            Local image file for the configuration page logo (ConfigurationPageLogoImage).

--dark-logo                     Local image file for the logo shown on a dark background — the Freedom UI top panel (CrtAppToolbarLogo). Use the white/light variant.

--package                       Package that receives the logo data bindings (default: Custom).

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Package delivery

Every applied slot is bound into `--package` (default: `Custom`) under its own binding
(`ClioBranding_Logo_<setting>`). A binding is created when it does not exist yet and updated in place
when it does, so re-running with a new file refreshes both the environment and the packaged snapshot.

Only the slots you pass — plus slots shipped by an earlier run — are bound. An unbranded slot is
never delivered, so the package cannot overwrite an install target's own logo with this
environment's stock value. A slot an earlier run shipped is refreshed on every run, and dropped
(with a report line) when its value row is gone.

Setting-value bindings are keyed by their natural columns (setting + admin unit) and force-update
the value, so installing the package **merges** onto the target's existing All-Users value instead
of inserting a duplicate whose id differs per environment.

The run output names the package and reports every delivery gap as a `Skipped:` line — read them;
each one means the package ships less than you may expect. A setting defined as `SecureText` is
never bound (a package must not carry a secret off its environment).

The binding writes package data through the design-time schema-data services, so the target package
must be editable (unlocked) and the caller needs rights to modify package configuration. When the
apply succeeds but the binding fails, the command exits with an error — re-run it to retry the
binding.

## Examples

Apply the main logo (bindings land in `Custom`):

```bash
clio set-logo --logo C:\brand\logo.svg -e myapp
```

Apply every slot, with the white variant on the dark top panel, into a specific package:

```bash
clio set-logo --logo C:\brand\logo.svg --menu-logo C:\brand\logo.svg --configuration-logo C:\brand\logo.svg --dark-logo C:\brand\logo-white.svg -e myapp --package UsrMyApp
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-logo)
