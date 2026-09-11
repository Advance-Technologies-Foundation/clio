# new-test-project

Create package unit-test projects from a clio workspace root. Registers each test project and package project in `tests/UnitTests.slnx`, and the test project in `MainSolution.slnx`.


## Usage

```bash
clio new-test-project [options]
```

## Description

Create package unit-test projects from a clio workspace root. Registers each test project and package project in `tests/UnitTests.slnx`, and the test project in `MainSolution.slnx`.

## Aliases

`create-test-project`, `unit-test`

## Examples

```bash
clio new-test-project --package UsrOrders
```

## Options

```bash
--package <VALUE>
Required package name or comma-separated package names
```

## Environment Options

```bash
-u, --uri <VALUE>
Application uri
-p, --Password <VALUE>
User password
-l, --Login <VALUE>
User login (administrator permission required)
-i, --IsNetCore
Use NetCore application
-e, --Environment <VALUE>
Environment name
-m, --Maintainer <VALUE>
Maintainer name
-c, --dev <VALUE>
Developer mode state for environment
--WorkspacePathes <VALUE>
Workspace path
-s, --Safe <VALUE>
Safe action in this environment
--clientId <VALUE>
OAuth client id
--clientSecret <VALUE>
OAuth client secret
--authAppUri <VALUE>
OAuth app URI
--silent
Use default behavior without user interaction
--restart-environment
Restart environment after execute command
--db-server-uri <VALUE>
Db server uri
--db-user <VALUE>
Database user
--db-password <VALUE>
Database password
--backup-file <VALUE>
Full path to backup file
--db-working-folder <VALUE>
Folder visible to db server
--db-name <VALUE>
Desired database name
--force
Force restore
--callback-process <VALUE>
Callback process name
--ep <VALUE>
Path to the application root folder
```

- [Clio Command Reference](../../Commands.md#new-test-project)

Existing project and fixture files are preserved. Rerun to repair missing solution entries. Legacy .sln files are left untouched. Solution-write failures return a nonzero exit code.

Use only the Clio scaffold for package test projects, then write test cases inside the generated project. MCP callers use clio-run with command new-test-project and package-name, absolute workspace-path, and environment-name. Scaffolding needs no ClioGate installation.


The package project packages/<NAME>/Files/<NAME>.csproj must exist before unit-test scaffolding. A missing package project fails before any files are written.
