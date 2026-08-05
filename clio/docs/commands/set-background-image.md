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

After a successful apply the command binds the background into a package as Creatio data bindings, so
installing that package elsewhere reproduces the same background. The bindings are created when they
do not exist yet and updated in place when they do, so re-running after a background change refreshes
what the package carries. What is bound: the background configuration, the image and its gallery
membership, and the All-Users off-state of the `UsePanelIconBackground` feature. Installing the
package **merges** onto the target's existing rows instead of duplicating them.

With `--package` omitted the bindings land in the package named by the environment's `CurrentPackageId`
system setting; when that setting points at nothing resolvable the command stops and asks for an
explicit package rather than picking one.

Read the run output: every gap between what was applied and what the package will actually deliver is
reported as a warning. The command still succeeds — a warning means the package ships less than
expected, not that the apply failed. When the binding itself fails, the error names both the cause and
the parts that were already bound before the failure; those stay in the package.

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

## Notes

- The binding writes package data through the design-time schema-data services, so the target package
  must be unlocked and the caller needs rights to modify package configuration.
- The binding folder names are reserved: ownership is decided by folder name plus entity schema, so a
  binding you created by hand under one of them for the same schema is refreshed or dropped by this
  command as if it were its own. Pick a different name for bindings you maintain yourself.
- A setting defined as `SecureText` is never bound; its value is an encrypted secret and a package must
  not carry a secret off the environment that owns it.
- A gallery membership under a customized `shell_background` tag is not bound, because the row
  references its tag by id and that id would not resolve on the target. The image itself still ships.
- The background configuration is withheld when the image row is not bound, because the configuration
  names the image by id; any configuration folder an earlier run shipped is dropped along with it.
- The `UsePanelIconBackground` off-state ships only when the feature is turned off on this environment.
  If it was never turned off here, or the apply ran with `--keep-icon-background`, the off-state stays
  out of the package and says so in a warning, and the panel can then still hide the background on the
  install target. If the target defines this feature on its own, the off-state may not take there
  either: brand one environment and deliver outward.

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
