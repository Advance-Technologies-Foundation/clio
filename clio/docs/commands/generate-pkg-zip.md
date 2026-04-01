# generate-pkg-zip

Prepare an archive of creatio package.


## Usage

```bash
clio generate-pkg-zip [<Name>] [options]
```

## Description

generate-pkg-zip command compress package into *.gz archive for directory
that contain package folder

## Aliases

`comp-pkg`, `compress`

## Examples

```bash
clio generate-pkg-zip
generate-pkg-zip package to .gz file if command run from package directory

clio generate-pkg-zip <PACKAGE_NAME>
generate-pkg-zip package to .gz file if command run from package containing
directory

clio generate-pkg-zip <PACKAGE_NAME> -d "C:\STORE\<PACKAGE>.gz"
generate-pkg-zip package to specified .gz file if command run from package
containing directory

clio generate-pkg-zip "C:\DEV\PKG\<PACKAG_NAME>" -d "C:\STORE\<PACKAGE>.gz"
generate-pkg-zip package from specify directory to specify .gz file

clio generate-pkg-zip <PACKAGE_NAME_1>,<PACKAGE_NAME_2>,<PACKAGE_NAME_3>
generate-pkg-zip more than one packages to single .gz file if command run from
packages containing directory
```

## Arguments

```bash
Name
    Name of the compressed package
```

## Options

```bash
Package name (pos. 0)    Name/path of package folder

--DestinationPath       -d  Destionation path for result gz file (Optional)
```

## Command Type

    CI/CD commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#generate-pkg-zip)
