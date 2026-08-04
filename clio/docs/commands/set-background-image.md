# set-background-image

## Command Type

    Branding commands

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

So the new background is actually visible, the command also turns off the panel's own icon background
(the `UsePanelIconBackground` feature, written as an All-Users `AdminUnitFeatureState` of `false`) —
while it is on it can cover the shell background. This is best-effort: if the write fails, the
background is still applied and a warning is logged. Pass `--keep-icon-background` when the panel icon
background must stay.

After a successful apply the command binds the background into a package as Creatio data bindings
(`SysPackageSchemaData`), so installing that package elsewhere reproduces the same background. The
bindings are created when they do not exist yet and updated in place when they do, so re-running after
a background change refreshes what the package carries. See "Package delivery" below.

## Synopsis

```bash
clio set-background-image [<image-id>] [options]
```

## Options

```bash
<image-id>                      Id of an already-uploaded image (printed by upload-image). Pass either this or --file.

--file                          Path to a local image file to upload and set as the background in one step.

--package                       Package that receives the background data bindings. When omitted, the package from the environment's CurrentPackageId system setting is used.

--keep-icon-background          Keep the panel's own icon background instead of turning it off. By default the command turns it off, because it can cover the shell background and leave the new background invisible.

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Package delivery

When `--package` is omitted, the package named by the environment's `CurrentPackageId` system setting
receives the bindings; when that setting points at nothing resolvable, the command stops and asks for
an explicit package rather than picking one.

The bound rows: the background configuration value, (for an image background) the image and its
gallery membership, and the All-Users off-state of the `UsePanelIconBackground` feature plus (by id)
its feature definition.

Setting-value and feature-state bindings are keyed by their natural columns (setting/feature +
admin unit) and force-update the value, so installing the package **merges** onto the target's
existing All-Users row instead of inserting a duplicate whose id differs per environment. The image
and gallery bindings are keyed by id (clio-generated ids that have no counterpart on the target, so
a plain insert is correct).

The run output names the package and reports every delivery gap as a warning — read them; the
command still succeeds, but each warning means the package ships less than you may expect.
Deliberate limits:

- **The binding folder names are reserved.** Ownership is decided by folder name plus entity schema, so a
  binding you created by hand under one of these names for the same schema is refreshed or dropped by this
  command as if it were its own. Pick a different name for bindings you maintain yourself.
- **A setting defined as `SecureText`** is never bound; its value is an encrypted secret and a
  package must not carry a secret off the environment that owns it.
- **A gallery membership under a customized `shell_background` tag** is not bound: the row
  references its tag by id, which would not resolve on the target. The image itself still ships.
- **The background configuration is withheld when the image row is not bound.** The configuration
  names the image by id, so shipping it alone would install a background the target cannot render;
  any configuration folder an earlier run shipped is dropped along with it.
- **The feature definition is delivered by id.** The `UsePanelIconBackground` definition travels by
  its own id so the state row referencing it resolves. If the target created that feature
  independently the ids differ and the install can add a second row rather than merging, leaving the
  target's own feature on. Brand one environment and deliver outward.
- **The `UsePanelIconBackground` off-state** is bound only when the All-Users state row on this
  environment is confirmed to read as off. A missing row (the feature was never toggled here), a row
  that still reads as on (every apply ran with `--keep-icon-background`, or the toggle failed), and a
  row whose `FeatureState` is no on/off answer at all are all reported as warnings, and any binding
  an earlier run shipped for the slot is dropped. Its `Feature` definition folder follows the same
  decision, because it exists only to keep the state row's reference resolvable.

The binding writes package data through the design-time schema-data services, so the target package
must be editable (unlocked) and the caller needs rights to modify package configuration. When the
apply succeeds but the binding fails, the command exits with an error naming the applied image and
the parts that were already bound before the failure — those stay in the package. Every part is
written in place, so a later run refreshes what landed instead of duplicating it.

## Examples

Upload a local image and set it as the shell background in one step:

```bash
clio set-background-image --file C:\brand\background.png -e myapp
```

Set an already-uploaded image as the shell background and bind it into a specific package:

```bash
clio set-background-image aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee -e myapp --package UsrMyApp
```

Set the shell background but leave the panel icon background in place:

```bash
clio set-background-image --file C:\brand\background.png -e myapp --keep-icon-background
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-background-image)
