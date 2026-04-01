# link-package-store

Link PackageStore packages into an environment.


## Usage

```bash
clio link-package-store [options]
```

## Description

link-package-store

Link packages from PackageStore to environment packages with version control.

Syntax:
clio link-package-store --packageStorePath <path> --envPkgPath <path>
clio lps --packageStorePath <path> --envPkgPath <path>

Description:
This command links packages from a PackageStore directory to environment packages.
Only packages that exist in BOTH the PackageStore and the environment will be linked.
Package versions are determined from descriptor.json in each environment package.
If a package version doesn't match between store and environment, it will be skipped.

Options:
--packageStorePath <path>
[Required] Path to PackageStore directory.
Expected structure: {Package_name}/{branches}/{version}/{content}
Example: /store/packages or C:\PackageStore

--envPkgPath <path>
[Optional] Path to environment package folder.
Example: /path/to/Creatio/Terrasoft.Configuration/Pkg
Alternative: use --Environment to specify environment name (Windows only)

-e, --Environment <name>
[Optional, Windows only] Environment name registered in clio settings.
Cannot be used together with --envPkgPath

Examples:

macOS/Linux:
clio lps --packageStorePath /store/packages \
--envPkgPath /path/to/Creatio/Terrasoft.Configuration/Pkg

Windows with direct path:
clio link-package-store --packageStorePath "C:\PackageStore" \
--envPkgPath "C:\Creatio\Terrasoft.Configuration\Pkg"

Windows with environment name:
clio link-package-store --packageStorePath "C:\PackageStore" -e MyEnvironment

Algorithm:
1. Reads all packages, branches, and versions from PackageStore
- Scans PackageStore for package directories
- For each package, scans all branch directories
- For each branch, scans all version directories
- Aggregates all versions from all branches per package

2. Reads all packages from environment (with versions from descriptor.json)

3. For each package in environment:
a. Checks if package exists in PackageStore
b. Checks if package version matches in any branch
c. If both conditions are met, searches through branches to find the version
d. Creates or updates symbolic link from environment to store location
e. If package or version not found, logs warning and skips

4. Returns appropriate exit code

Return codes:
0 - linking completed successfully (all packages linked)
1 - errors occurred during linking or validation failed

Notes:
- Works on Windows, macOS, and Linux
- Package version is determined from descriptor.json
- Creates symbolic links (not copies) for efficient disk usage
- Searches all branches to find target version
- If a link already exists, it will be removed and recreated
- Missing packages in store are skipped (not added to environment)
- Missing packages in environment are not modified
- Versions are found in first matching branch (search is sequential)

## Aliases

`lps`

## Examples

```bash
clio link-package-store -e dev
```

## Options

```bash
--packageStorePath <VALUE>
Path to PackageStore folder with structure:
{Package_name}/{branches}/{version}/{content}. Required.
--envPkgPath <VALUE>
Path to environment package folder
({LOCAL_CREATIO_PATH}Terrasoft.WebApp\Terrasoft.Configuration\Pkg)
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
--restartEnvironment
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

- [Clio Command Reference](../../Commands.md#link-package-store)
