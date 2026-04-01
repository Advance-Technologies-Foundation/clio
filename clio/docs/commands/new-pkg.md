# new-pkg

## Command Type

    Development

## Name

new-pkg - create new package in a web application

## Description

new-pkg command can be used in development when you need to create
new creatio package

## Synopsis

```bash
clio new-pkg <PACKAGE_NAME>
```

## Options

```bash
Package name (pos. 0)	Name of package to create

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment	        -e          Environment name

--Maintainer            -m          Maintainer name
```

## Example

```bash
clio new-pkg <PACKAGE_NAME>
create new package in current folder

clio new-pkg <PACKAGE_NAME> -r bin
create new package in current folder and set reference on local
core assembly with using creatio file design mode with command in
Pkg directory
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#new-pkg)
