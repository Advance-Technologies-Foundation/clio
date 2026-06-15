using System;
using System.IO;

namespace Clio.Common.Telemetry;

/// <summary>
/// Resolves the local telemetry storage layout shared by the measurement store and the flusher.
/// </summary>
internal static class TelemetryStoragePaths
{
	/// <summary>
	/// Environment variable that redirects the local telemetry storage root. Supported at runtime
	/// (e.g. to relocate or inspect the spool for data-residency) and used by tests for isolation.
	/// </summary>
	internal const string TelemetryHomeEnvironmentVariable = "CLIO_TELEMETRY_HOME";

	/// <summary>
	/// Resolves the telemetry storage root: explicit override first, then the
	/// <c>CLIO_TELEMETRY_HOME</c> environment variable, then <c>&lt;clio-home&gt;/telemetry</c>
	/// under clio's consolidated home (<see cref="ClioRuntimePaths.Home"/>, which honors
	/// <c>CLIO_HOME</c>). Keeping telemetry under the single clio home means relocating clio via
	/// <c>CLIO_HOME</c> and clio cleanup/uninstall both cover the telemetry spool.
	/// </summary>
	/// <param name="overrideRoot">Optional explicit root that wins over the environment variable.</param>
	/// <returns>The absolute telemetry storage root path.</returns>
	internal static string ResolveRoot(string overrideRoot = null)
	{
		if (!string.IsNullOrWhiteSpace(overrideRoot)) {
			return overrideRoot;
		}
		string environmentOverride = Environment.GetEnvironmentVariable(TelemetryHomeEnvironmentVariable);
		return string.IsNullOrWhiteSpace(environmentOverride)
			? Path.Combine(ClioRuntimePaths.Home, "telemetry")
			: environmentOverride;
	}

	/// <summary>
	/// Gets the spool directory holding stored event files under the telemetry root.
	/// </summary>
	/// <param name="root">The telemetry storage root.</param>
	/// <returns>The events spool directory path.</returns>
	internal static string EventsDirectory(string root) => Path.Combine(root, "events");

	/// <summary>
	/// Gets the directory holding per-session duration-inference state under the telemetry root.
	/// </summary>
	/// <param name="root">The telemetry storage root.</param>
	/// <returns>The sessions directory path.</returns>
	internal static string SessionsDirectory(string root) => Path.Combine(root, "sessions");
}
