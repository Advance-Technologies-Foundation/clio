# create-workspace

`create-workspace` (`createw`) creates a local clio workspace.

## Empty workspace mode

Use `--empty` to create a new workspace folder without connecting to any Creatio environment.

```bash
clio create-workspace my-workspace --empty
```

In empty mode, clio resolves the base directory in this order:

1. `--directory`
2. appsettings.json property `workspaces-root`

If neither source is available, or the resolved base directory does not exist, the command fails.

`workspace-name` must stay a relative folder name. `--directory` must be an absolute path.

On success, clio reports the final workspace directory using `Workspace created at: <full-path>`.

Create the workspace under an explicit directory:

```bash
clio create-workspace my-workspace --empty --directory C:\Workspaces
```

Allow creation when the destination folder already exists and is not empty:

```bash
clio create-workspace my-workspace --empty --directory C:\Workspaces --force
```

## Environment-backed mode

Without `--empty`, `create-workspace` keeps the existing behavior and works in the current directory.

Create a workspace and download editable packages from a configured environment:

```bash
clio create-workspace -e dev
```

Create a workspace with package loading based on application code:

```bash
clio create-workspace --AppCode <APP_CODE>
```

In environment-backed mode, the reported path is the current working directory where the workspace was created.

## Settings

Global empty-workspace root can be stored in appsettings.json:

```json
{
  "workspaces-root": "C:\\Workspaces"
}
```

This setting is global to clio and is separate from per-environment `WorkspacePathes`.
