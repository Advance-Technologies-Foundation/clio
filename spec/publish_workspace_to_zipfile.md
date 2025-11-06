### publish-workspace — new requirements

- The command must support the format `clio publish-workspace {Path_to_workspace} --file {path_to_zip_file_name.zip} --app-version {semver}` without requiring `--app-hub` or `--app-name`.
- The workspace path is taken either from the positional argument or from `--repo-path`; when both are provided, `--repo-path` wins.
- Before packaging, the version supplied via `--app-version` must be:
  - written to the application `app-descriptor.json`;
  - applied to every package `descriptor.json` (the `PackageVersion` field).
- The resulting zip file must be created exactly at the path passed via `--file`; overwrite the file if it already exists.
- The previous app-hub publishing mode must remain available with `--app-hub`, `--app-name`, `--app-version`, and optional `--branch`.
- Add an automated test for the new scenario to confirm that a workspace can be packaged into an arbitrary zip file and that all descriptors receive the requested version.

### Implementation Notes

- Extend `PublishWorkspaceCommand` options with a positional workspace argument and a new `--file/-f` parameter; make `--app-hub`/`--app-name` optional and perform runtime validation depending on the chosen mode.
- Teach `IWorkspace`/`Workspace` a `PublishToFile` method that sets the version on both `app-descriptor.json` and every package `descriptor.json`, then reuses `WorkspaceInstaller.PublishToFolder` to produce the archive before renaming it to the requested target path.
- Keep the existing hub flow untouched by routing to the old `PublishToFolder` implementation when `--file` is not provided.
- Update docs (`clio/docs/commands/PublishWorkspaceCommand.md`) to describe both modes and their parameters, and add a dedicated unit test (e.g., in `clio.tests/WorkspaceTest`) covering the direct-file scenario.

### Architecture Overview

- **CLI Layer (`PublishWorkspaceCommand`)**
  - Parses either positional or `--repo-path` workspace inputs.
  - Switches between *App Hub mode* (requires `--app-hub`, `--app-name`) and *Direct File mode* (requires `--file`).
  - Delegates all publishing work to `IWorkspace` implementation; CLI remains thin and focused on validation.

- **Workspace Facade (`Workspace` implementing `IWorkspace`)**
  - Coordinates higher-level operations: version stamping, packaging, and handoff to infrastructure services.
  - Exposes `PublishToFolder` (existing hub flow) and the new `PublishToFile`.
  - Uses `IWorkspacePathBuilder` to resolve package directories and `IComposableApplicationManager` to update the app descriptor.
  - Updates package descriptors via `IJsonConverter` to ensure `PackageVersion` parity across the workspace.

- **Packaging Infrastructure (`IWorkspaceInstaller`)**
  - Encapsulates ZIP creation logic: iterates packages, packs each via `IPackageArchiver`, zips the bundle, and writes it to the destination directory.
  - Reused for both modes to avoid duplicating build logic; `PublishToFile` simply renames the output to the requested path.

- **Supporting Services**
  - `IWorkspacePathBuilder`: centralizes knowledge of workspace structure (packages folder, settings paths).
  - `IComposableApplicationManager`: handles application-level metadata updates (version, code lookups).
  - `IWorkingDirectoriesProvider`: supplies temporary folders for packaging without leaking resources.
  - `IFileSystem`: abstracted file operations, enabling reliable unit testing.

- **Testing Strategy**
  - Unit tests target the `Workspace` façade (e.g., `WorkspaceTest.PublishWorkspaceToFileTest`) to verify correct descriptor updates and final ZIP placement.
  - CLI-level behavior remains implicitly tested by invoking the command with mocked `IWorkspace` dependencies if needed.
