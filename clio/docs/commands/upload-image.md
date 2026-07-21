# upload-image

## Command Type

    Branding commands

## Name

upload-image - upload a local image to an environment and print its image id

## Description

`upload-image` uploads a local image file to the target environment, verifies it was stored, and
prints the created image id.

Each call stores a new image. Use the printed image id to reference the image, for example with
[`set-background-image`](set-background-image.md) to make it the shell background.

Supported formats: `png`, `jpg`, `jpeg`, `gif`, `bmp`, `webp`, `svg`. The file is capped at 10 MB. The
environment must have login/password credentials registered; OAuth-only environments are not
supported.

## Synopsis

```bash
clio upload-image <file> [options]
```

## Options

```bash
<file>                          Path to the local image file to upload (png, jpg, jpeg, gif, bmp, webp, or svg).

--uri               -u          Application uri

--Password          -p          User password

--Login             -l          User login (administrator permission required)

--Environment       -e          Environment name

--Maintainer        -m          Maintainer name
```

## Example

```bash
clio upload-image C:\brand\background.png -e myapp
upload the image and print its image id
```

## Notes

- To set an uploaded image as the shell background, run
  [`set-background-image`](set-background-image.md).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#upload-image)
