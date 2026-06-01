# new-ui-project

Create a new Freedom UI project.


## Usage

```bash
clio new-ui-project <Name> [options]
```

## Description

Create a new Freedom UI (Angular) project.

When run inside a workspace, the command also wires the generated Angular project into
`MainSolution.slnx` via an MSBuild `.esproj` wrapper, so that a single `dotnet build` produces
both the C# package assembly and the Angular client bundle. Specifically it:

- writes a version-less `projects/<name>/<name>.esproj` (Microsoft JavaScript SDK) next to
  `package.json`, with its `BuildOutputFolder` pointing at the bundle location inside the package;
- pins the JavaScript SDK version in a repo-root `global.json` (merged, never overwriting an
  existing `sdk` node);
- adds the `.esproj` to `MainSolution.slnx` with an empty `<Build />` element so it participates in
  every solution configuration (including custom ones such as `dev-n8`/`dev-nf`);
- adds a `clean` npm script so `dotnet clean` removes the generated bundle.

> Building the bundle requires **Node.js + npm** on the machine; the JavaScript SDK shells out to
> `npm` but does not install Node itself.

## Aliases

`create-ui-project`, `createup`, `new-ui`, `ui`, `uiproject`

## Examples

```bash
clio new-ui-project <Name> [options]
```

## Arguments

```bash
Name
    Project name. Required.
```

## Options

```bash
--version <VALUE>
Creatio version
--empty
Create empty package
--package <VALUE>
Package name. Required.
-v, --vendor-prefix <VALUE>
Vendor prefix. Required.
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

- [Clio Command Reference](../../Commands.md#new-ui-project)
