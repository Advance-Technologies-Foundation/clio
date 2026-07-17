using ClioRing.Ipc;
using System.Collections.Generic;
using System.Linq;

namespace ClioRing.Models;

/// <summary>Identifies the clio runtime selected for the current Ring process.</summary>
public enum ClioRuntimeMode {
	/// <summary>The installed clio dotnet tool.</summary>
	Release,

	/// <summary>An explicitly configured development build.</summary>
	Development
}

/// <summary>
/// Immutable startup decision shared by process launch and UI identity so the visible mode cannot diverge
/// from the child Ring is configured to start.
/// </summary>
/// <param name="Mode">The runtime mode selected for this Ring process.</param>
/// <param name="LaunchSettings">The exact child-process launch settings for that mode.</param>
/// <param name="RequestedMode">The persisted user choice when it differs from the safe resolved mode.</param>
/// <param name="ConfigurationWarning">Actionable warning when the requested runtime could not be used.</param>
public sealed record ResolvedClioRuntime(ClioRuntimeMode Mode, ClioIpcSettings LaunchSettings,
	ClioRuntimeMode? RequestedMode = null, string? ConfigurationWarning = null);

/// <summary>Shared validation rules for explicit development child-process configuration.</summary>
public static class ClioRuntimeConfiguration {
	/// <summary>Validates a complete explicit IPC target consistently across startup and settings discovery.</summary>
	/// <param name="command">Executable command.</param>
	/// <param name="args">Command arguments.</param>
	/// <returns>True only when the command and every required argument are nonblank.</returns>
	public static bool IsValidExplicitIpc(string? command, IEnumerable<string?>? args) =>
		!string.IsNullOrWhiteSpace(command) && args is not null && args.Any()
		&& args.All(argument => !string.IsNullOrWhiteSpace(argument));
}
