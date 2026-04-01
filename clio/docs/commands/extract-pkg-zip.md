# extract-pkg-zip

Extract a packaged application or package archive.


## Usage

```bash
clio extract-pkg-zip [<Name>] [options]
```

## Description

extract-pkg-zip command unzip package from *.gz archive to directory
that contain package folder

## Aliases

`extract`, `unzip`

## Examples

```bash
clio extract-pkg-zip MyPackage
extract-pkg-zip MyPackage.gz in current folder to directory MyPackage in current folder

clio extract-pkg-zip c:\MyPackage.gz -f c:\App\Pkg
extract-pkg-zip MyPackage.gz file to c:\App\MyPackage folder
```

## Arguments

```bash
Name
    Name of the compressed package
```

## Options

```bash
Package name (pos. 0)    Name/path of package acrhive file

--DestinationPath       -d  Destionation path for extract package (Optional)
```

## Command Type

    CI/CD commands

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#extract-pkg-zip)
