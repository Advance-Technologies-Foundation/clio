# compile-package

## Command Type

    Development commands

## Name

compile-package - compile one or more Creatio packages on a target environment

## Aliases

comp-pkg

## Description

The compile-package command recompiles one or more packages in a Creatio
environment. It invokes the remote package rebuild endpoint for each package
name provided and prints start/end progress messages for every package.

You can pass a single package name or a comma-separated list of package names
as the first positional argument.

The build result Creatio returns is read. When a package does not compile, the
command prints the compiler diagnostics (CSxxxx code, file, line, column,
message), states that the new code was not loaded, and exits with
code 1 instead of printing `Done`.

By default the command returns as soon as the environment's compilation activity
pauses for 5 seconds, which on some hosts is before the whole build has finished.
Use `--wait` to block until the build has finished.

## Synopsis

```bash
clio compile-package <PACKAGE_NAME>[,<PACKAGE_NAME>...] [OPTIONS]
```

## Arguments

```bash
<PACKAGE_NAME>[,<PACKAGE_NAME>...]
Required. Package name or comma-separated package names to compile.
```

## Options

```bash
--wait                              Block until the environment reports that the
                                    build finished (its build result, or compilation
                                    activity that has stopped), instead of returning
                                    when the build is accepted. Default: false

--wait-timeout                      Max seconds to wait for each package build when
                                    --wait is set. Default: 600, max: 3600

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
clio compile-package MyPackage -e dev
Rebuilds the MyPackage package in the dev environment

clio compile-package MyPackage -e test
Rebuilds a package using the short alias

clio compile-package PkgOne,PkgTwo -e production
Rebuilds two packages sequentially in the production environment

clio compile-package MyPackage -e dev --wait
Rebuilds MyPackage and returns only after the build has finished, so a probe or
restart run next sees the new assembly
```

## Output

    For each package the command prints:
    - Start rebuild packages (<PACKAGE_NAME>)
    - Compilation history: <PROJECT> built in <N> s, succeeded|failed, for every
      compilation-history row written while the build runs (another trigger on the
      same environment can write one too)
    - End rebuild packages (<PACKAGE_NAME>)
    - Done

    On a compile error it prints instead, and exits with code 1:
    - (CSxxxx) in <FILE> at (<LINE>,<COLUMN>): <MESSAGE>, one line per error
    - Package compilation failed for '<PACKAGE_NAME>' (build result <N>). The new
      code was not loaded: the environment keeps running the previous build until
      the errors are fixed and the package is compiled again.

Without `--wait`, when the build ended only because compilation activity paused, a
warning says the result was not reported yet and suggests `--wait`. When the
environment returns no build result at all (older hosts), a warning says so and the
outcome is taken from the compilation history.

## Prerequisites

- Valid Creatio environment configured with -e or direct connection options
- Credentials with permission to compile packages in the target environment
- Network connectivity to the target Creatio instance

## Notes

- On an interactive terminal the command first warns that compilation is a heavy
operation that forces a runtime reload affecting every connected user, and asks
whether to compile now or postpone. Declining postpones the compilation (nothing is
compiled, exit code 2) and prints how to run it later. Non-interactive hosts
(scripts, CI, redirected stdin) and `--silent` proceed without asking.
- The command performs rebuild, not incremental build
- Package names are split by comma before execution
- When one package compilation fails, the command exits with code 1
- A failed build does not load the new code: the environment keeps serving the
previous build until the error is fixed and the package is compiled again
- With `--wait`, a failure answer to the build request ends the build at once: it
carries the verdict and the diagnostics. A success answer, or a dropped connection
without an answer, is followed by waiting until compilation history has been quiet
for 45 seconds (longer after a slower project has been seen), because some hosts
answer while projects are still building. The quiet is counted only once at least
one compilation-history row has been written: a success answer followed by no
history is not taken as a finished build. If the request stays open without an
answer, 5 minutes of quiet end the wait. When `--wait-timeout` elapses first, the
command exits with code 1 and the build may still be running on the environment
- Progress monitoring tolerates an environment that briefly stops answering: while
the application tier is unreachable each failed poll round is reported as a warning
and the next round is delayed 1 s, 2 s, then 5 s. Only after 90 seconds in which no
round succeeded does the command stop monitoring and report `Package compilation
could not be monitored`. The server keeps compiling either way - the message says
monitoring stopped, not that the build failed.
- The 90 seconds are measured from the moment the FIRST failed round of the current
run is detected, and the check happens when a round fails - so with the last 5 s
backoff step the give-up is observed at about 93 seconds, and a failing read that
takes time to time out adds its own duration on top of that.


## Return Values

    0       Package compilation completed successfully
    1       Package compilation failed (including a C# compile error), --wait
            timed out, or an error occurred
    2       Compilation postponed by the user (declined the interactive confirmation)

## See Also

compile-configuration   Compile all configuration
download-configuration  Download configuration to local workspace
push-pkg                Install package to environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#compile-package)
