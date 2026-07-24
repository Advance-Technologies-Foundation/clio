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

## Synopsis

```bash
clio set-background-image [<image-id>] [--file <path>] [options]
```

## Options

```bash
<image-id>                      Id of an already-uploaded image (printed by upload-image). Pass either this or --file.

--file                          Path to a local image file to upload and set as the background in one step.

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Examples

Upload a local image and set it as the shell background in one step:

```bash
clio set-background-image --file C:\brand\background.png -e myapp
```

Set an already-uploaded image as the shell background:

```bash
clio set-background-image aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee -e myapp
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-background-image)
