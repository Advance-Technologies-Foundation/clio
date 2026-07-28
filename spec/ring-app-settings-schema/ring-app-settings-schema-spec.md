# ClioRing app-settings schema specification

Issue: #890

## Goal

Provide an editor-discoverable JSON Schema for `clio-ring/ClioRing.Desktop/app-settings.json` and clear guidance for selecting a development clio MCP child without confusing that choice with the Ring `Channel` badge.

## Requirements

- The shipped sample references `./app-settings.schema.json` through `$schema`.
- The schema covers every property in `AppSettings`, `ExperimentSettings`, and `ClioIpcSettingsDto`.
- `Channel` is documented as a display/deployment label with a `dev` default; it does not select a clio executable.
- Development clio selection documents the actual precedence: valid `DevClioPath`, explicit `ClioIpc`, then the machine default.
- `Experiments.ClioIpc` remains off by default.
- The schema is copied into build and NativeAOT publish output.
- Refreshing `C:\Tools\clio-ring` preserves the existing mutable `app-settings.json` and log/measurement directories.

## Acceptance criteria

- The default settings file and schema parse as JSON.
- Tests prove the schema and settings files are present in Ring test output and that descriptions cover `Channel`, `DevClioPath`, and `ClioIpc`.
- Ring Release tests pass.
- Windows x64 NativeAOT publish succeeds without trim/AOT warnings.

