# compile-configuration

## Command Type

    Development commands

## Name

compile-configuration - compile Creatio configuration with real-time progress monitoring

## Aliases

cc, compile-remote

## Description

The compile-configuration command compiles the Creatio configuration remotely,
providing real-time progress monitoring of the compilation process. It tracks
each project being compiled, displays compilation duration, and reports any
errors or warnings that occur during compilation.

The command monitors the CompilationHistory table to provide live feedback on:
- Which project is currently being compiled
- Duration of each project compilation
- Errors and warnings for each project
- Overall compilation time
- Final compilation status

Special attention is given to key projects:
- Terrasoft.Configuration.ODataEntities.csproj
- Terrasoft.Configuration.Dev.csproj

## Synopsis

```bash
compile-configuration [options]
```

## Options

```bash
--all                               Compile all configurations (full rebuild)
Default: false

--timeout                           Request timeout in milliseconds
Default: Infinite (compilation can take time)

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name

--clientId                          OAuth client id

--clientSecret                      OAuth client secret

--authAppUri                        OAuth app URI
```

## Examples

```bash
clio compile-configuration -e development
Compiles configuration for the development environment with progress tracking

clio compile-configuration -e production
Compiles configuration using the short alias

clio compile-configuration --all -e staging
Performs a full rebuild of all configurations

clio compile-configuration -u "https://myapp.creatio.com" -l "admin" -p "password"
Compiles using direct connection parameters

clio compile-configuration -e dev --timeout 300000
Compiles with a custom 5-minute timeout (though default is infinite)
```

## Output

    The command provides detailed real-time output including:
    - Compilation start time
    - Progress updates for each project compiled
    - Duration of each project compilation (color-coded):
        * Green: < 5 seconds
        * Yellow: 5-10 seconds
        * Red: > 10 seconds
    - Highlighted output for OData and Dev projects
    - Errors and warnings with file locations and line numbers
    - Total compilation time
    - Final success/failure status

## Prerequisites

- Valid Creatio environment with accessible web services
- Appropriate credentials (admin permission)
- Network connectivity to the target Creatio instance
- cliogate must be installed on the target environment

## Notes

- The command uses an infinite timeout by default as compilation can take
significant time depending on configuration size
- Real-time progress is tracked by monitoring the CompilationHistory table
- Compilation errors include detailed information: error code, file name,
line number, and error description
- The command distinguishes between warnings (yellow) and errors (red)

## Return Values

    0       Compilation completed successfully
    1       Compilation failed or an error occurred

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#compile-configuration)
