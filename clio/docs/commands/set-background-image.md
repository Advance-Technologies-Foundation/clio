# set-background-image

## Command Type

    Branding commands

## Name

set-background-image - set a previously uploaded image as the environment's shell background

## Description

`set-background-image` makes an uploaded image (see [`upload-image`](upload-image.md)) the shell
background of the target environment: it adds the image to the background gallery and selects it
as the active background.

The change applies to all users after a page refresh and replaces the currently configured
background. Re-running the command with the same image id is safe and leaves the same background
in place.

The image must already exist in the environment; upload it first with `clio upload-image`, which
prints the image id this command takes.

## Synopsis

```bash
clio set-background-image <image-id> [options]
```

## Options

```bash
<image-id>                      Id of the uploaded image to set as the background (printed by upload-image).

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Example

```bash
clio upload-image C:\brand\background.png -e myapp
clio set-background-image aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee -e myapp
upload the image, then set it as the shell background
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#set-background-image)
