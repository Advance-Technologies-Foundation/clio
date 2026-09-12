using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Clio.UserEnvironment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common;

/// <summary>
/// One resident clio MCP host, as recorded on disk by that host while it runs.
/// </summary>
/// <param name="ProcessId">The host's operating-system process identifier.</param>
/// <param name="ClioVersion">The clio version the host is running.</param>
/// <param name="StartedAtUtc">When the host wrote the marker.</param>
/// <param name="MarkerFilePath">The absolute path of the marker file.</param>
public sealed record McpHostPresenceMarker(
	int ProcessId,
	string ClioVersion,
	DateTimeOffset StartedAtUtc,
	string MarkerFilePath
);

/// <summary>
/// Answers whether a recorded process identifier still belongs to a running process.
/// </summary>
/// <remarks>
/// A seam, not an abstraction for its own sake: the presence scan has to distinguish a live MCP host
/// from a marker left by a process that was killed, and a unit test can neither create nor kill a real
/// process to prove it.
/// </remarks>
public interface IProcessLivenessProbe {

	/// <summary>Determines whether a process with the given identifier is currently running.</summary>
	/// <param name="processId">The process identifier to test.</param>
	/// <param name="startedNoLaterThanUtc">
	/// When supplied, a running process whose own start time is LATER than this is reported as not
	/// alive: the recorded process has ended and an unrelated one inherited its identifier. A pid is
	/// not an identity.
	/// </param>
	/// <returns><see langword="true"/> when the process is running.</returns>
	bool IsAlive(int processId, DateTimeOffset? startedNoLaterThanUtc = null);
}

/// <inheritdoc />
public sealed class ProcessLivenessProbe : IProcessLivenessProbe {

	/// <inheritdoc />
	public bool IsAlive(int processId, DateTimeOffset? startedNoLaterThanUtc = null) {
		if (processId <= 0) {
			return false;
		}
		// CLIO004 asks for IProcessExecutor, which LAUNCHES processes. This asks whether a foreign
		// process that clio never started is still alive, and the executor has no lookup-by-id at all.
#pragma warning disable CLIO004
		Process process = null;
		try {
			process = Process.GetProcessById(processId);
			if (process.HasExited) {
				return false;
			}
			// A margin, not an exact comparison: the marker is written a moment AFTER the process
			// started, and the two clocks are read through different APIs. Only a process that started
			// well after the marker was written can be a stranger holding a recycled identifier.
			return startedNoLaterThanUtc is null
				|| process.StartTime.ToUniversalTime() <= startedNoLaterThanUtc.Value.UtcDateTime.AddMinutes(1);
		}
		catch (ArgumentException) {
			// No process with that identifier exists.
			return false;
		}
		catch (InvalidOperationException) {
			// The process ended between lookup and inspection.
			return false;
		}
		finally {
			process?.Dispose();
		}
#pragma warning restore CLIO004
	}
}

/// <summary>
/// Records that an MCP host is resident in this clio home, and reports whether one still is.
/// </summary>
/// <remarks>
/// The marker exists so that a SEPARATE clio CLI process can see the resident host before it replaces
/// clio's binaries and appsettings.json underneath it (issue #1462). Nothing in the host's own memory
/// can answer that question, because the process that has to ask is a different one.
/// </remarks>
public interface IMcpHostPresenceRegistry {

	/// <summary>Writes the marker for the current process.</summary>
	/// <returns>The marker path, or <see langword="null"/> when the marker could not be written.</returns>
	string Register();

	/// <summary>Removes a marker written by <see cref="Register"/>.</summary>
	/// <param name="markerFilePath">The marker path, as returned by <see cref="Register"/>.</param>
	void Unregister(string markerFilePath);

	/// <summary>
	/// Finds a marker whose process is still running, deleting any marker whose process is gone.
	/// </summary>
	/// <returns>The first live host found, or <see langword="null"/> when none is resident.</returns>
	McpHostPresenceMarker FindLiveHost();
}

/// <inheritdoc />
// The file-system parameter is spelled out in full: this file lives in Clio.Common, which has an
// IFileSystem of its own, and an alias cannot win against a type in the enclosing namespace.
public sealed class McpHostPresenceRegistry(
	System.IO.Abstractions.IFileSystem fileSystem,
	IProcessLivenessProbe processLivenessProbe)
	: IMcpHostPresenceRegistry {
	internal const string MarkerPrefix = "mcp-server.";
	internal const string MarkerSuffix = ".lock";

	/// <inheritdoc />
	public string Register() {
		int processId = Environment.ProcessId;
		string markerFilePath = BuildMarkerPath(processId);
		try {
			string folder = SettingsRepository.AppSettingsFolderPath;
			if (!fileSystem.Directory.Exists(folder)) {
				fileSystem.Directory.CreateDirectory(folder);
			}
			JObject content = new() {
				["pid"] = processId,
				["clio-version"] = CurrentClioVersion,
				["started-at-utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
			};
			fileSystem.File.WriteAllText(markerFilePath, content.ToString(Formatting.Indented));
			return markerFilePath;
		}
		catch (Exception exception) when (IsMarkerIoFailure(exception)) {
			// A host that cannot announce itself must still serve. The cost is one missed deferral.
			return null;
		}
	}

	/// <inheritdoc />
	public void Unregister(string markerFilePath) {
		if (string.IsNullOrWhiteSpace(markerFilePath)) {
			return;
		}
		try {
			if (fileSystem.File.Exists(markerFilePath)) {
				fileSystem.File.Delete(markerFilePath);
			}
		}
		catch (Exception exception) when (IsMarkerIoFailure(exception)) {
			// A marker left behind is harmless: the next scan finds its process gone and deletes it.
		}
	}

	/// <inheritdoc />
	public McpHostPresenceMarker FindLiveHost() {
		string folder = SettingsRepository.AppSettingsFolderPath;
		IEnumerable<string> markerFiles;
		try {
			if (!fileSystem.Directory.Exists(folder)) {
				return null;
			}
			markerFiles = fileSystem.Directory.GetFiles(folder, $"{MarkerPrefix}*{MarkerSuffix}");
		}
		catch (Exception exception) when (IsMarkerIoFailure(exception)) {
			return null;
		}
		foreach (string markerFile in markerFiles.OrderBy(path => path, StringComparer.Ordinal)) {
			McpHostPresenceMarker marker = TryReadMarker(markerFile);
			if (marker is null) {
				continue;
			}
			if (processLivenessProbe.IsAlive(marker.ProcessId,
				marker.StartedAtUtc == DateTimeOffset.MinValue ? null : marker.StartedAtUtc)) {
				return marker;
			}
			// The recorded host is gone: drop the marker so a killed host cannot defer updates forever.
			Unregister(markerFile);
		}
		return null;
	}

	private McpHostPresenceMarker TryReadMarker(string markerFilePath) {
		int processId = ParseProcessIdFromFileName(markerFilePath);
		if (processId <= 0) {
			return null;
		}
		string version = "unknown";
		DateTimeOffset startedAtUtc = DateTimeOffset.MinValue;
		try {
			JObject content = JObject.Parse(fileSystem.File.ReadAllText(markerFilePath));
			version = content.Value<string>("clio-version") ?? version;
			if (DateTimeOffset.TryParse(content.Value<string>("started-at-utc"),
				CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)) {
				startedAtUtc = parsed;
			}
		}
		catch (JsonException) {
			// The file name carries the identity that matters; a damaged body only costs the detail.
		}
		catch (Exception exception) when (IsMarkerIoFailure(exception)) {
			return null;
		}
		return new McpHostPresenceMarker(processId, version, startedAtUtc, markerFilePath);
	}

	internal static int ParseProcessIdFromFileName(string markerFilePath) {
		string fileName = System.IO.Path.GetFileName(markerFilePath) ?? string.Empty;
		if (!fileName.StartsWith(MarkerPrefix, StringComparison.Ordinal)
			|| !fileName.EndsWith(MarkerSuffix, StringComparison.Ordinal)) {
			return 0;
		}
		string processIdText = fileName.Substring(MarkerPrefix.Length,
			fileName.Length - MarkerPrefix.Length - MarkerSuffix.Length);
		return int.TryParse(processIdText, NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
			? processId
			: 0;
	}

	private static string BuildMarkerPath(int processId) =>
		System.IO.Path.Combine(SettingsRepository.AppSettingsFolderPath,
			$"{MarkerPrefix}{processId.ToString(CultureInfo.InvariantCulture)}{MarkerSuffix}");

	private static bool IsMarkerIoFailure(Exception exception) =>
		exception is System.IO.IOException or UnauthorizedAccessException or NotSupportedException
			or ArgumentException;

	private static string CurrentClioVersion =>
		typeof(McpHostPresenceRegistry).Assembly.GetName().Version?.ToString() ?? "unknown";
}
