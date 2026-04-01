# install-application

Install an application package into Creatio.


## Usage

```bash
clio install-application <NAME> [options]
```

## Description

The install-application command installs an application package file into
a Creatio environment. It supports registered environments and direct
connection arguments inherited from EnvironmentOptions.

## Aliases

`install-app`, `push-app`

## Examples

```bash
clio install-application C:\Packages\application.gz -e dev
install an application package into the registered dev environment

clio install-application C:\Packages\application.gz --check-compilation-errors true -e dev
install an application package and stop when compilation errors are detected

clio install-application C:\Packages\application.gz -r install.log -u https://my-creatio
install an application package and write the command report to install.log
```

## Options

```bash
Name (pos. 0)            Application package path or name

--ReportPath             -r          Optional path to the installation log file

--check-compilation-errors           Check compilation errors during installation

--uri                    -u          Application uri

--Password               -p          User password

--Login                  -l          User login (administrator permission required)

--Environment            -e          Environment name

--Maintainer             -m          Maintainer name
```

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-application)
