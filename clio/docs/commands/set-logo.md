# set-logo

## Command Type

    Branding commands

## Name

set-logo - apply the product logos from local image files and bind them into a package

## Description

`set-logo` writes the product logos of the target environment from local image files and binds the
applied values into a package as Creatio data bindings (`SysPackageSchemaData`), so installing that
package elsewhere reproduces the same logos instead of leaving them behind on this environment.

Use `--logo` to brand every slot from one file. A slot option overrides it for that slot, so one run
can set the whole product and still give the dark top panel its own file:

| Option | System setting | Where it appears |
|---|---|---|
| `--logo` | every slot below | — |
| `--login-logo` | `LogoImage` | login page |
| `--menu-logo` | `MenuLogoImage` | main menu |
| `--configuration-logo` | `ConfigurationPageLogoImage` | configuration page |
| `--dark-logo` | `CrtAppToolbarLogo` | the Freedom UI top panel — a dark surface; pass the light variant of the logo |

Pass at least one of them. The stock splash-screen logo is suppressed so it does not flash during
load (best-effort: a failure is a warning, not a command failure). The change applies to all users
after a page refresh and cannot be automatically reverted by clio.

Each file is uploaded as the slot's Binary sys-setting value: the environment's file-security policy
(extension allow/deny lists) is enforced client-side and a per-value size cap (10 MB) applies —
the same rules as `update-sys-setting` with `value-file-path`.

## Synopsis

```bash
clio set-logo [options]
```

## Options

```bash
--logo                          Local image file applied to every logo slot at once. A slot option below overrides it for that slot.

--login-logo                    Local image file for the logo on the login page (LogoImage).

--menu-logo                     Local image file for the main menu logo (MenuLogoImage).

--configuration-logo            Local image file for the configuration page logo (ConfigurationPageLogoImage).

--dark-logo                     Local image file for the logo on the dark Freedom UI top panel (CrtAppToolbarLogo). Pass the light variant here — a logo drawn for a white background is hard to read on the dark panel.

--package                       Package that receives the logo data bindings. When omitted, the package from the environment's CurrentPackageId system setting is used.

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Package delivery

Every applied slot is bound into `--package` under its own binding (`ClioBranding_Logo_<setting>`).
When `--package` is omitted, the package named by the environment's `CurrentPackageId` system setting
receives the bindings; when that setting points at nothing resolvable, the command stops and asks for
an explicit package rather than picking one. A binding is created when it does not exist yet and
updated in place when it does, so re-running with a new file refreshes both the environment and the
packaged snapshot.

Only the slots this run wrote — plus slots an earlier run already shipped — are bound. A slot nobody
branded stays out of the package, so installing it cannot replace the target's own logo with this
environment's stock image. A slot an earlier run shipped is refreshed on every run, and dropped
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

Apply one logo to every slot:

```bash
clio set-logo --logo C:\brand\logo.svg -e myapp
```

Apply one logo everywhere, with the light variant on the dark top panel, into a specific package:

```bash
clio set-logo --logo C:\brand\logo.svg --dark-logo C:\brand\logo-white.svg -e myapp --package UsrMyApp
```

Apply the login-page logo only:

```bash
clio set-logo --login-logo C:\brand\logo.svg -e myapp
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-logo)
