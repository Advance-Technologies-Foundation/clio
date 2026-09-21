# create-user-task-page

Creates an editable Classic parameter page for an existing workspace user task and links the task to its new page UId. It inherits `ProcessFlowElementPropertiesPage`, uses native MAPPING editors for In/Variable parameters, and excludes Out/Internal parameters. Missing L12 metadata means Variable. Task execution code, parameter identities and directions are preserved.

```shell
clio create-user-task-page --workspace-path ./MyWorkspace --package-name UsrExample --user-task-uid 90810b59-c2aa-4133-8b6b-61f57b12143c --page-name UsrTaskPropertiesPage --caption "Task parameters"
```

| Option | Required | Meaning |
|---|---|---|
| `--workspace-path` | Yes | Explicit clio workspace root |
| `--package-name` | Yes | Owning package listed in workspace settings |
| `--user-task-uid` | Yes | Existing native user-task schema UId |
| `--page-name` | Yes | Unique Classic page schema name |
| `--caption` | Yes | Page display caption |
| `--culture` | No | Native caption/resource culture, default `en-US` |
| `--small-icon-path` | No | Local SVG for `SmallSvgImage` |
| `--large-icon-path` | No | Local SVG for `LargeSvgImage` |
| `--title-icon-path` | No | Local SVG for `TitleSvgImage` |

The command works offline and expects native JSON user-task metadata. Input names `UserTaskContainer` and `EditorsContainer` are reserved by the page layout. Existing page names, resource directories and nonempty task page associations are refused. This is a create operation: edit the generated JavaScript to customize layout, labels and behavior afterward.

Optional icon slots are independent and replace only the requested slots in the selected task resource culture. Other resource entries are preserved. Without icon arguments task resource files are not rewritten. SVGs must be at most 1 MiB and contain static basic shapes (`svg`, `g`, `path`, `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `title`, `desc`, `defs`, `use`), without styles, animation, event handlers or external references.

Ensure the package depends on `CrtProcessDesigner`. Deploy through the normal workspace/package workflow; the command does not deploy or register toolbox elements. Validate the panel, icons, mapping persistence after process save/reopen, and downstream output selection against your target Creatio version.

For MCP, discover `create-user-task-page` using `get-tool-contract`, then use `clio-run` with `command: "create-user-task-page"` and an `args` object containing the options above. This is a Classic process properties page, not a Freedom UI page.

[Command index](../../Commands.md#create-user-task-page)

