# ClioRing clio-tool update test plan

## Design checkpoint

- Render Release mode with a newer clio version available and an Update action.
- Render Development mode while a Release update remains available.
- Render the lock-holder confirmation with clio PID/path and Claude/Codex parent identity.
- Verify Cancel and kill-and-retry are visually distinct, keyboard accessible, and do not obscure the target list.
- Stop for requester approval before backend implementation, comprehensive review, or final NativeAOT validation.

## Automated

- Compare four-part clio versions and ignore prerelease/unlisted candidates.
- Bound automatic checks and persist one notification acknowledgement per version.
- Keep Development runtime target untouched while updating Release.
- Stop Ring's owned IPC child before updater execution.
- Parse update success, ordinary failure, and lock failure without relying on localized prose alone where possible.
- Enumerate only exact trusted-shim processes and display accessible parent identity.
- Revalidate PID, path, and start time before kill; reject PID reuse, path mismatch, access denial, and exited holders.
- Cancel performs no kill or retry.
- Kill-and-retry performs one retry and reports any respawned/refreshed blockers.
- Successful update verifies the installed version and clears update attention.
- Native notification failure does not change durable main-UI state.
- Existing runtime-selection, deployment, and uninstall tests remain green.

## Manual Windows harness

- Use a harmless fake updater and disposable helper process that opens a temporary file; do not alter the installed
  clio tool during automated validation.
- Separately inspect the live trusted clio process read-only to confirm parent attribution (currently Claude).
- A real clio update is allowed only by an explicit user click when a newer version actually exists.

## Required validation after approval

- `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`
- `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true`
