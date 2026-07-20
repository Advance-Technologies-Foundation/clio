# upload-image

## Command Type

    Theming commands

## Name

upload-image - upload a local image file to the environment's SysImage table

## Description

`upload-image` uploads a local image file into the target environment's
`SysImage` table through the platform image API (`ImageAPIService/upload`) on
an authenticated clio session, and prints the created record's `imageId`.

The `SysImage` binary column cannot be written through the OData JSON surface
(the stream stays empty), so this command is the supported programmatic write
path for images such as the Freedom UI shell background (referenced by the
`CrtBackgroundConfig` system setting).

The upload is additive only: every call creates a new `SysImage` record and
never overwrites existing data. After the upload the command reads the image
back through the platform image read endpoint and reports an error rather than
a false success when the binary did not persist. The image API is addressed
runtime-aware: under the `/0` alias on .NET Framework environments and at the
site root on .NET Core environments.

Supported formats: `png`, `jpg`, `jpeg`, `gif`, `bmp`, `webp`, `svg`. The
payload is capped at 10 MB (the same cap as Binary system-setting uploads).
The environment must have forms-auth credentials (login/password) registered;
OAuth-only environments are not supported.

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
clio upload-image C:\brand\background.svg -e myapp
upload the SVG and print the created SysImage record's imageId
```

## Notes

- To apply an uploaded image as the Freedom UI shell background, register it in
  the Appearance gallery (`SysImageInTag`, shell-background tag) and point the
  `CrtBackgroundConfig` system setting at its `imageId`.
- Product logos are not `SysImage` records; write them as Binary system
  settings (`LogoImage`, `MenuLogoImage`, `ConfigurationPageLogoImage`,
  `CrtAppToolbarLogo`) with [`set-syssetting`](set-syssetting.md).

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#upload-image)
