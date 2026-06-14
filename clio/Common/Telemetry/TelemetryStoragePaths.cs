using System;
using System.IO;

namespace Clio.Common.Telemetry;

/// <summary>
/// Resolves the local telemetry storage layout shared by the measurement store and the flusher.
/// </summary>
internal static class TelemetryStoragePaths
{
	/// <summary>
	/// Environment variable that redirects the local telemetry storage root (used by tests).
	/// </summary>
	internal const string TelemetryHomeEnvironmentVariable = "CLIO_TELEMETRY_HOME";

	/// <summary>
	/// Resolves the telemetry storage root: explicit override first, then the
	/// <c>CLIO_TELEMETRY_HOME</c> environment variable, then the default user-profile location.
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
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				".creatio-ai-app-development-toolkit", "telemetry")
			: environmentOverride;
	}

	/// <summary>
	/// Gets the spool directory holding stored event files under the telemetry root.
	/// </summary>
	/// <param name="root">The telemetry storage root.</param>
	/// <returns>The events spool directory path.</returns>
	internal static string EventsDirectory(string root) => Path.Combine(root, "events");
}
