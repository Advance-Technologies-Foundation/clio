# Application-pool profile cleanup PRD

Issue: [#881](https://github.com/Advance-Technologies-Foundation/clio/issues/881)

## Problem

`uninstall-creatio` removes IIS, database, files, and registration but leaves the registered Windows profile for the IIS application-pool virtual account. Deleting only `C:\Users\<name>` is unsafe and incomplete.

## Requirements

- Resolve the actual application-pool name from IIS before deleting IIS configuration.
- On Windows, resolve only `IIS APPPOOL\<name>` identities whose SID is in the `S-1-5-82-*` namespace and whose profile is registered under Windows ProfileList.
- Delete the profile through the Windows profile API, including registration and files.
- Retry deletion three times with bounded delays.
- Treat missing profiles and non-Windows execution as not applicable.
- Treat locked, denied, and native deletion failures as one warning after retry exhaustion while continuing unregistration and returning exit code 0.
- Emit typed stage `warning` and terminal `success-with-warnings` through MCP with `IsError=false`.
- Render warning and completed-with-warnings states in ClioRing and refresh environments normally.
- Keep user-facing detail friendly and stack-trace free; preserve `APPPOOL_PROFILE_DELETE_FAILED` for diagnosis.

## Acceptance criteria

The acceptance criteria are those in issue #881. The implementation must cover successful, absent, locked/access-denied, retry-exhausted, and non-Windows paths plus CLI, MCP, mirrored event contract, ordered replay, and ClioRing rendering.

## Exclusions

- Deleting arbitrary paths or non-IIS user profiles.
- Changing ordinary uninstall failures into warnings.
- Adding a new CLI option or protocol version.
