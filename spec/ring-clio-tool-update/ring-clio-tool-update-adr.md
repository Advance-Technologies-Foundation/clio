# ADR: User-mediated clio tool updates from ClioRing

Status: accepted

## Context

The installed global clio tool is commonly a long-lived MCP server owned by Claude, Codex, or Ring itself.
The dotnet tool updater replaces the global shim/package files and can fail while those processes are alive.
A naive background `dotnet tool update clio` would either fail without useful context or require unsafe broad
process termination.

## Decision

Add a Ring-owned update coordinator behind interfaces. It obtains the current version from the trusted Release
shim and discovers the latest stable listed version through the NuGet V3 service/search contract. The coordinator
checks on startup and at most every eight hours, but installation always requires a button click.

The updater launches `dotnet` directly with an argument list and pins the version that was presented to the user.
Ring first stops only its own MCP child. On a lock failure, a Windows process-inspection service enumerates the
exact Release-shim processes and their parent applications. Restart Manager may be used to corroborate file
holders, but executable path plus process start time are the termination authority. The confirmation view contains
an immutable snapshot; kill-and-retry revalidates that snapshot to prevent PID-reuse and path-substitution races.

Tray attention has three layers: an update-badged tray icon, an update action/tooltip, and one transient desktop
notification beside the tray per version. Notification failure is best-effort and never affects update state. Main UI state remains
the durable source of truth.

Update state is application-owned and contains only timestamps/versions/notification acknowledgement. No process
command line, path, PID, or parent identity is persisted. The feature remains absent from MCP.

## Consequences

- Agent-hosted clio processes are visible before the user decides whether to interrupt them.
- Claude and Codex remain running; only their exact clio children may be terminated.
- Agent hosts may respawn clio and race the retry; Ring reports the refreshed blocker instead of looping.
- Windows process inspection and native notification code stay at the desktop/interop edge.
- The Ring application remains NativeAOT-compatible and clio core gains no Avalonia dependency.
