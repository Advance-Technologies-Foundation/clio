namespace ClioLauncher.Models;

/// <summary>
/// Root configuration loaded from <c>app-settings.json</c> next to the executable.
/// </summary>
public class AppSettings
{
	/// <summary>Absolute path to the folder scanned for clio workspaces.</summary>
	public required string WorkspaceFolder { get; init; }

	/// <summary>Global hotkey gesture, e.g. "Ctrl+Alt+C". Optional; defaults when absent/invalid.</summary>
	public string? Hotkey { get; init; }

	/// <summary>
	/// Absolute path to the folder that receives run logs and deployment/uninstall receipts. Optional;
	/// when absent or blank the app defaults to a <c>Logs</c> folder next to the executable
	/// (e.g. <c>C:\Tools\clio-ring\Logs</c> for an installed build). See
	/// <see cref="ClioLauncher.Diagnostics.RingLog"/>.
	/// </summary>
	public string? LogsFolder { get; init; }

	/// <summary>
	/// Deployment channel label shown in the build badge (e.g. "preview", "aot", "install").
	/// Stamp this per staged copy WITHOUT a rebuild. Defaults to "dev" when unset.
	/// </summary>
	public string? Channel { get; init; }

	/// <summary>
	/// Experimental feature switches (default OFF). When an experiment is off the app behaves exactly
	/// as today; the corresponding proof UI/behaviour is never wired.
	/// </summary>
	public ExperimentSettings? Experiments { get; init; }

	/// <summary>
	/// Optional override for how the EXPERIMENTAL clio MCP child process is launched. Absent = a
	/// machine-sensible default (the clio Release dll driven by <c>dotnet</c>).
	/// </summary>
	public ClioIpcSettingsDto? ClioIpc { get; init; }

	/// <summary>
	/// Optional explicit path to an unreleased/dev clio build (a <c>clio.dll</c> or <c>clio.exe</c>) to
	/// drive the ring instead of the normal build. When set and valid it takes precedence over
	/// <see cref="ClioIpc"/> and the machine default; when absent or blank the ring connects to the normal
	/// clio. Persisted from the ring's settings panel; the change applies on the next ring launch.
	/// </summary>
	public string? DevClioPath { get; init; }
}

/// <summary>Experimental feature switches. All default to false (fail-closed).</summary>
public sealed class ExperimentSettings {
	/// <summary>
	/// Enables the clio MCP-over-stdio IPC proof: the "clio IPC (experimental)" tray entry + catalog
	/// view. Off by default — the installed build is unaffected.
	/// </summary>
	public bool ClioIpc { get; init; }
}

/// <summary>Serializable form of the clio MCP child launch configuration (see the IPC layer).</summary>
public sealed class ClioIpcSettingsDto {
	/// <summary>Executable to launch (for example <c>dotnet</c> or <c>clio</c>).</summary>
	public string? Command { get; init; }

	/// <summary>Arguments (for example the clio dll path followed by <c>mcp-server</c>).</summary>
	public string[]? Args { get; init; }

	/// <summary>Optional working directory for the child.</summary>
	public string? WorkingDirectory { get; init; }
}
