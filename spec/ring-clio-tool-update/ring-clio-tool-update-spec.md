# ClioRing clio-tool update specification

Issue: #905

## Goal

Let ClioRing detect and install a newer stable global clio dotnet tool without silently disrupting
Claude, Codex, or another host that is using clio as a local MCP server.

## Requirements

- Compare the trusted Release clio executable's installed version with the latest stable listed clio
  package on NuGet.org. Development targets are informational only and are never updated.
- Check once after Ring starts and at a bounded eight-hour interval while it remains resident. Network or
  feed failures stay non-blocking and do not claim that Release is current.
- When an update exists, show it on the main Ring surface, change the tray state/tooltip, add a tray update
  action, and emit one transient desktop notification beside the tray per available version.
- Never install in the background. The user must choose Update.
- Gracefully stop Ring's owned MCP child after the Update gesture and before launching the updater.
- Run `dotnet tool update --global clio --version <detected-version>` without a shell and capture its result.
- If the updater reports a lock failure, enumerate processes using the exact trusted Release clio shim.
  Show PID, executable path, process start identity, and parent application details when accessible.
- Present Cancel and `Kill listed clio processes and retry`. Killing requires its own explicit gesture.
- Immediately before termination, re-open every PID and revalidate executable path and start time. Never
  terminate a mismatched, inaccessible, or newly appeared process implicitly. Never terminate parent agent
  applications.
- After a successful retry, verify the trusted shim version and let Ring reconnect its owned MCP child lazily.
- Persist only non-sensitive update-check/notification state. Do not log command output that may expose
  unrelated machine configuration.

## Acceptance criteria

- Release-current, update-available, checking, updating, lock-blocked, cancelled, failed, and updated states
  are represented honestly.
- Live trusted `clio.exe` MCP processes are attributed to their current Claude, Codex, or other immediate parent,
  while the parent itself is never a termination target.
- Selecting Cancel leaves every process and installed file untouched.
- Selecting kill-and-retry affects only revalidated processes whose executable path equals the trusted Release
  shim and then retries exactly once per gesture.
- Notification state does not repeatedly alert for the same available version.
- The approved runtime selector remains visible and coherent in both Release and Development modes.
- Focused Ring tests and Windows x64 NativeAOT publish pass.

## Exclusions

- Ring does not update a configured Development DLL or executable.
- Ring does not terminate Claude, Codex, IDEs, terminals, or arbitrary processes named `clio` from another path.
- Ring does not expose clio update or process termination through MCP.
- Ring does not auto-update on startup or retry indefinitely when an agent immediately respawns clio.
