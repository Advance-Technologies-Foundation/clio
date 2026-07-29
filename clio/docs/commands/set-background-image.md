# set-background-image

## Command Type

    Customization commands

## Name

set-background-image - set an image as the environment's shell background

## Description

`set-background-image` makes an image the shell background of the target environment: it adds the
image to the background gallery and selects it as the active background.

Pass exactly one image source:

- a local file (`--file`): the file is uploaded and applied in one step;
- an image id (positional): an image already uploaded with [`upload-image`](upload-image.md).

Passing both is an error.

The change applies to all users after a page refresh and replaces the currently configured
background. Re-running the command with the same image is safe and leaves the same background in
place.

Applying a background also turns the `UsePanelIconBackground` feature off for all users (writing its
All-Users `AdminUnitFeatureState` to `false`), because while it is on the panel's own icon background
hides the shell background. This is best-effort: if the toggle fails, the background is still applied
and a warning is logged. Pass `--keep-icon-background` to leave the feature untouched.

After a successful apply the command binds the background into a package as Creatio data bindings
(`SysPackageSchemaData`), so the background ships with the package on an install or a transfer to
another site. The target package is `--package` (default: `Custom`); the bindings are created when
they do not exist yet and updated in place when they do, so re-running after a background change
refreshes the packaged snapshot. See "Package delivery" below.

## Synopsis

```bash
clio set-background-image [<image-id>] [--file <path>] [--package <name>] [--keep-icon-background] [options]
```

## Options

```bash
<image-id>                      Id of an already-uploaded image (printed by upload-image). Pass either this or --file.

--file                          Path to a local image file to upload and set as the background in one step.

--package                       Package that receives the background data bindings (default: Custom).

--keep-icon-background          Keep the UsePanelIconBackground feature as is instead of turning it off.

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Package delivery

The bound rows: the background configuration value and its setting definition, (for an image
background) the image and its gallery membership, and the All-Users off-state of the
`UsePanelIconBackground` feature plus (by id) its feature definition.

Setting-value and feature-state bindings are keyed by their natural columns (setting/feature +
admin unit) and force-update the value, so installing the package **merges** onto the target's
existing All-Users row instead of inserting a duplicate whose id differs per environment. The image
and gallery bindings are keyed by id (clio-generated ids that have no counterpart on the target, so
a plain insert is correct).

The run output names the package and reports every delivery gap as a `Skipped:` line — read them;
each one means the package ships less than you may expect. Deliberate limits:

- **A setting defined as `SecureText`** is never bound; its value is an encrypted secret and a
  package must not carry a secret off the environment that owns it.
- **A gallery membership under a customized `shell_background` tag** is not bound: the row
  references its tag by id, which would not resolve on the target. The image itself still ships.
- **The background configuration definition is delivered by id** so the value row's reference
  resolves. If the target created that setting independently the ids differ and the install can add
  a second definition rather than merging; brand one environment and deliver outward.
- **The `UsePanelIconBackground` off-state** is bound only when the feature was turned off on this
  environment; when it never was (for example every apply ran with `--keep-icon-background`), it is
  reported as skipped.

The binding writes package data through the design-time schema-data services, so the target package
must be editable (unlocked) and the caller needs rights to modify package configuration. When the
apply succeeds but the binding fails, the command exits with an error naming the applied image —
re-run it to retry the binding.

## Examples

Upload a local image and set it as the shell background in one step (bindings land in `Custom`):

```bash
clio set-background-image --file C:\brand\background.png -e myapp
```

Set an already-uploaded image as the shell background and bind it into a specific package:

```bash
clio set-background-image aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee -e myapp --package UsrMyApp
```

Apply a background but keep the panel icon background feature untouched:

```bash
clio set-background-image --file C:\brand\background.png -e myapp --keep-icon-background
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-background-image)
