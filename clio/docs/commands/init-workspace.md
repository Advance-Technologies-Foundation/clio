# init-workspace

`init-workspace` (`initw`) initializes the current directory as a local clio workspace.

Use this command when the directory already contains files that must be preserved. clio adds missing workspace files and metadata, but does not overwrite files that already exist.

## Local initialization

Initialize the current directory without connecting to a Creatio environment:

```bash
clio init-workspace
```

On success, clio reports the initialized directory using `Workspace initialized at: <full-path>`.

## Environment-backed initialization

You can initialize the current directory and then restore editable packages from an environment:

```bash
clio init-workspace -e dev
```

If you need package enrollment based on an installed application code, use:

```bash
clio init-workspace -e dev --AppCode <APP_CODE>
```

In environment-backed mode, `init-workspace` keeps existing local files intact, creates missing workspace metadata, and then runs the same restore flow used by workspace restore/create commands.
