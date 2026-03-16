# install-application

## Purpose
`install-application` installs an application package into a Creatio environment.

Use this command when you need to deploy an application package from Application Hub or a local package archive to a registered environment or a directly specified Creatio URL.

## Usage
```bash
clio install-application <NAME> [options]
```

**Aliases**: `push-app`, `install-app`

## Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `NAME` | Yes | Application package path or name accepted by the installer |

## Options

| Option | Short | Required | Description |
|--------|-------|----------|-------------|
| `--ReportPath` | `-r` | No | Optional path to the installation log file |
| `--check-compilation-errors` |  | No | Stops installation when compilation errors are detected |
| `--Environment` | `-e` | No | Registered clio environment name |
| `--uri` | `-u` | No | Creatio application URL |
| `--Login` | `-l` | No | Creatio user login |
| `--Password` | `-p` | No | Creatio user password |
| `--Maintainer` | `-m` | No | Maintainer name |

## Examples

Install an application package into a registered environment:
```bash
clio install-application C:\Packages\application.gz -e dev
```

Use an alias and stop on compilation errors:
```bash
clio push-app C:\Packages\application.gz --check-compilation-errors true -e dev
```

Write the installation report to a file:
```bash
clio install-app C:\Packages\application.gz -r install.log -e dev
```

Connect directly without a registered environment:
```bash
clio install-application C:\Packages\application.gz -u https://my-creatio -l Supervisor -p Supervisor
```

## Output

On success the command reports `Done` and returns exit code `0`.

On failure the command returns exit code `1` and writes an error message. When `--ReportPath` is provided, the installer also writes its report to the requested file path.
