# generate-pkg-zip

## Summary
pkg-zip

## Description
generate-pkg-zip command compress package into *.gz archive for directory
    that contain package folder

## Aliases
None

## Options

| Name | Short | Description |
|------|-------|-------------|
| `Package name (pos. 0)` | `` | Name/path of package folder |
| `` | `` |  |
| `--DestinationPath` | `-d` | Destionation path for result gz file (Optional) |
| `` | `` |  |
| `` | `` |  |

## Examples

```bash
clio generate-pkg-zip
        compress package to .gz file if command run from package directory

    clio generate-pkg-zip <PACKAGE_NAME>
        compress package to .gz file if command run from package containing
        directory

    clio generate-pkg-zip <PACKAGE_NAME> -d "C:\STORE\<PACKAGE>.gz"
        compress package to specified .gz file if command run from package
        containing directory

    clio generate-pkg-zip "C:\DEV\PKG\<PACKAG_NAME>" -d "C:\STORE\<PACKAGE>.gz"
        compress package from specify directory to specify .gz file

    clio generate-pkg-zip <PACKAGE_NAME_1>,<PACKAGE_NAME_2>,<PACKAGE_NAME_3>
        compress more than one packages to single .gz file if command run from
        packages containing directory
```