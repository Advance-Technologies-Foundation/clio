# set-logo

## Command Type

    Branding commands

## Name

set-logo - apply the product logos and the browser-tab favicon from local image files and bind them into a package

## Description

`set-logo` writes the product logos and the browser-tab favicon of the target environment from local
image files and binds the applied values into a package as Creatio data bindings.

Use `--logo` to brand every slot from one file. A slot option overrides it for that slot, so one run
can set the whole product and still give the dark top panel its own file:

| Option | System setting | Where it appears |
|---|---|---|
| `--logo` | every slot below | — |
| `--login-logo` | `LogoImage` | login page |
| `--menu-logo` | `MenuLogoImage` | main menu |
| `--configuration-logo` | `ConfigurationPageLogoImage` | configuration page |
| `--dark-logo` | `CrtAppToolbarLogo` | the Freedom UI top panel — a dark surface; pass the light variant of the logo |

`--favicon` brands the browser tab and is never taken from `--logo`, which is not required to be
square. It writes `FaviconImage` and turns on the `UseFaviconFromSysSettings` gate, without which the
platform keeps the stock icon. Because the icon is inert without the gate, a gate that can neither be
turned on nor already reads as on fails the run. Pass a square icon: clio uploads the file as it is,
without resizing or converting it.

Pass at least one of them or `--favicon`. The stock splash-screen logo is suppressed so it does not
flash during load (best-effort: a failure is a warning). The change applies to all users after a page
refresh and cannot be automatically reverted by clio. The favicon needs more
than a refresh: users must sign out and back in, and an already-open browser tab may keep the old icon
until it is closed and reopened.

Each file is uploaded as its Binary sys-setting value: the environment's file-security policy is
enforced client-side and a per-value size cap (10 MB) applies.

When the environment refuses one image and accepts another, the run keeps what already succeeded:
the splash suppression and the package delivery still run for the applied images, and the command
then exits with an error naming every refused image. A non-zero exit does **not** mean nothing
changed — read the `Applied:` line and the package line before re-running. When nothing applies at
all, the run stops there and touches no package.

Only the images this run applied are bound, together with their companion settings — the splash
suppression when a logo slot applied, the `UseFaviconFromSysSettings` gate when the favicon did — so
a slot nobody branded stays out of the package and installing it cannot replace the target's own
logo with this environment's stock image. Installing the package **merges** onto the target's
existing values instead of duplicating them.

With `--package` omitted the bindings land in the package named by the environment's
`CurrentPackageId` system setting; when that setting points at nothing resolvable the command stops
and asks for an explicit package rather than picking one.

Read the run output: every gap between what was applied and what the package will actually deliver is
reported as a warning. A warning means the package ships less than expected, not that the apply
failed. When the binding itself fails, the error names the cause and the package line names what the
package already carries; those stay in it. Every setting is written in place, so re-running refreshes
what landed instead of duplicating it.

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

--favicon                       Local image file for the browser-tab icon (FaviconImage). Pass a square icon; ICO, PNG and SVG are the safest formats. Not taken from --logo.

--package                       Package that receives the logo data bindings. When omitted, the package from the environment's CurrentPackageId system setting is used.

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Notes

- The binding writes package data through the design-time schema-data services, so the target package
  must be unlocked and the caller needs rights to modify package configuration.
- The binding folder names are reserved: ownership is decided by folder name plus entity schema, so a
  binding you created by hand under one of them for the same schema is refreshed or dropped by this
  command as if it were its own. Pick a different name for bindings you maintain yourself.
- A setting defined as `SecureText` is never bound; its value is an encrypted secret and a package must
  not carry a secret off the environment that owns it.
- A slot this run applied whose value row turns out to be unreadable is dropped from the package with a
  warning. A binding an earlier run shipped for a slot outside this run is left as it is.
- A run without `--favicon` never touches either favicon setting or their bindings.

## Examples

Apply one logo to every slot:

```bash
clio set-logo --logo C:\brand\logo.svg -e myapp
```

Apply one logo everywhere, with the light variant on the dark top panel:

```bash
clio set-logo --logo C:\brand\logo.svg --dark-logo C:\brand\logo-white.svg -e myapp
```

Apply the login-page logo only and bind it into UsrMyApp:

```bash
clio set-logo --login-logo C:\brand\logo.svg -e myapp --package UsrMyApp
```

Apply the logos and the browser-tab icon in one run:

```bash
clio set-logo --logo C:\brand\logo.svg --favicon C:\brand\favicon.ico -e myapp
```

Rebrand the browser tab alone, leaving the logos as they are:

```bash
clio set-logo --favicon C:\brand\favicon.ico -e myapp
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-logo)
