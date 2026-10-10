# push-workspace

## Name

push-workspace - Push workspace to selected environment

## Description

Packs the current workspace and installs it into the target environment.

## Prerequisites

cliogate package version 2.0.0.0 or higher must be installed on the Creatio
instance. Install using:

clio install-gate -e <ENVIRONMENT_NAME>

## Synopsis

```bash
clio push-workspace [OPTIONS]
```

## Options

```bash
-e, --environment <ENVIRONMENT_NAME>
Target environment name

--skip-backup <true|false>
Skip backup creation only when explicitly set to true

--unlock
Unlock workspace packages after installing the workspace

--use-application-installer
Use ApplicationInstaller instead of PackageInstaller for installation
```

## Examples

```bash
clio push-workspace -e dev
Push the current workspace to the dev environment

clio push-workspace -e dev --skip-backup true
Push the workspace without creating a backup package first

clio push-workspace -e dev --use-application-installer
Push the workspace using ApplicationInstaller
```

## Notes

Before installing, `push-workspace` checks every Freedom UI page schema (web and
mobile) in the workspace packages for user-visible text (`caption`, `label`, `title`, `tooltip`,
`placeholder`) written as an inline literal. The MCP `update-page` tool rejects
such text; `push-workspace` still installs it, but prints one warning per page
schema with the schema name and the offending `<node>.<property>` elements, for example:

```text
[WAR] - Page schema 'UsrApp_FormPage' (package 'UsrApp') sets user-visible text as inline literals: UsrLabel.caption. ...
```

Bind the text via `$Resources.Strings.<Key>` or `#ResourceString(<Key>)#` and
register the key in the schema resources to clear the warning.

A second warning names literal-only properties (for example
`crt.ImageInput.tooltip`) bound to a localizable resource. `update-page` rejects
them too, because such text renders empty at runtime; set those values as plain
literals.

### Folders without descriptor.json

`push-workspace` packs every workspace package before it installs anything. It
stops when a folder under `Schemas/` or `Data/` has no `descriptor.json`: Creatio
rejects the whole installation for such a folder with `Invalid descriptor` and
names only the last segment of its path. The error lists the full local path of
every such folder in all packages (the first 20, then a count), and the command
exits with `1`:

```text
[ERR] - This package folder has no descriptor.json: /repo/packages/UsrApp/Data/Lookup_Status (28 files, e.g. Localization/data.de-DE.json, Localization/data.en-US.json, Localization/data.es-ES.json; only Localization files, delete the folder). Creatio rejects the whole installation ...
```

Each folder carries a verdict:

- `only Localization files, delete the folder` — git leaves such a folder behind
  when a data binding is deleted: it removes the tracked files and keeps the
  ignored per-culture `Localization/data.<culture>.json` files.
- `only operating system files, delete the folder` — the folder holds nothing but
  files such as `.DS_Store` or `Thumbs.db`, so there is no element to restore.
- `element files, restore descriptor.json` — the element is still there and only
  its descriptor is missing. Restore it, for example from git; deleting the folder
  can remove the element from the environment on the next install.

Empty folders and files excluded by `clioignore` are not packed, so they are not
reported. Folders under `Assemblies/`, `Files/`, `Resources/` and `SqlScripts/`
are not checked.

## See Also

create-workspace - Create a workspace
restore-workspace - Restore a workspace from an environment

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#push-workspace)
