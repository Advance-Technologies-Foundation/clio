# extract-pkg-zip

## Summary
pkg-zip

## Description
extract-pkg-zip command unzip package from *.gz archive to directory
    that contain package folder

## Aliases
None

## Options

| Name | Short | Description |
|------|-------|-------------|
| `Package name (pos. 0)` | `` | Name/path of package acrhive file |
| `` | `` |  |
| `--DestinationPath` | `-d` | Destionation path for extract package (Optional) |
| `` | `` |  |
| `` | `` |  |

## Examples

```bash
clio extract-pkg-zip MyPackage
        unzip MyPackage.gz in current folder to directory MyPackage in current folder

    clio extract-pkg-zip c:\MyPackage.gz -f c:\App\Pkg
        extract MyPackage.gz file to c:\App\MyPackage folder
```