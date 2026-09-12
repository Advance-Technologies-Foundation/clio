using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Abstractions;
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
	/// <remarks>
	/// FAILS SAFE: anything that stops this from reaching an answer reports the process as ALIVE. Reading
	/// another user's process throws Win32Exception on both Windows and macOS, and a probe that reported
	/// "not running" there would let a CLI replace the binaries of a host that is very much running - the
	/// failure this whole marker exists to prevent. The cost of the opposite mistake is one deferred
	/// update, announced on the console.
	/// </remarks>
	// CLIO004 asks for IProcessExecutor, which LAUNCHES processes. This asks whether a foreign process
	// that clio never started is still alive, and the executor has no lookup-by-id at all.
#pragma warning disable CLIO004
	public bool IsAlive(int processId, DateTimeOffset? startedNoLaterThanUtc = null) {
		if (processId <= 0) {
			return false;
		}
		Process process = null;
		try {
			process = Process.GetProcessById(processId);
		}
		catch (ArgumentException) {
			// The ONLY definite negative: the operating system reports no such process.
			return false;
		}
		catch (Exception) {
			// Could not even look it up. Assume it is there.
			return true;
		}
		try {
			if (process.HasExited) {
				return false;
			}
			if (startedNoLaterThanUtc is null) {
				return true;
			}
			// A margin, not an exact comparison: the marker is written a moment AFTER the process started,
			// and the two clocks are read through different APIs. Only a process that started well after
			// the marker was written can be a stranger holding a recycled identifier.
			return process.StartTime.ToUniversalTime()
				<= startedNoLaterThanUtc.Value.UtcDateTime.AddMinutes(1);
		}
		catch (Exception) {
			// A live handle we are not allowed to inspect. Treat it as the host it claims to be.
			return true;
		}
		finally {
			process?.Dispose();
		}
	}
#pragma warning restore CLIO004
}

/// <summary>
/// Records that an MCP host is resident in this clio home, and reports whether one still is.
/// </summary>
/// <remarks>
/// The marker exists so that a SEPARATE clio CLI process can see the resident host before it replaces
/// clio's binaries and appsettings.json underneath it (issue #1462). Nothing in the host's own memory
/// can answer that question, because the process that has to ask is a different one.
/// <para>
/// ACCEPTED RESIDUAL: a host that starts while a detached <c>dotnet tool update</c> is ALREADY
/// downloading still has its binaries replaced under it. The marker is read before the update is
/// launched, so it cannot see an update that is already in flight, and closing that window means
/// coordinating with the lifetime of a process clio deliberately does not wait for. The exposure is
/// one host start inside one update window; restarting that session is the remedy, and it is the same
/// remedy the whole issue ends in.
/// </para>
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
	IProcessLivenessProbe processLivenessProbe,
	Clio.Common.Skills.IUserHomeProvider userHomeProvider)
	: IMcpHostPresenceRegistry {
	/// <summary>Folder name under the per-user clio directory that holds the presence markers.</summary>
	internal const string MarkerFolderName = "mcp-hosts";

	internal const string MarkerPrefix = "mcp-server.";
	internal const string MarkerSuffix = ".lock";

	/// <summary>Refuse to read anything larger than this; a marker is three short fields.</summary>
	internal const int MaxMarkerBytes = 4096;

	/// <summary>
	/// Where markers live: <c>&lt;user home&gt;/.clio/mcp-hosts</c>, and deliberately NOT under the clio
	/// home.
	/// </summary>
	/// <remarks>
	/// The thing being protected is the <c>dotnet tool update clio -g</c> installation, and there is ONE
	/// of those per user however many clio homes exist. A marker kept under a <c>CLIO_HOME</c>-relative
	/// path would be invisible to a clio started without that variable - which would then replace the
	/// binaries of the very host that wrote it. The scope of the marker has to match the scope of the
	/// thing it guards.
	/// </remarks>
	private string MarkerFolder =>
		System.IO.Path.Combine(userHomeProvider.GetClioDir(), MarkerFolderName);

	/// <inheritdoc />
	public string Register() {
		int processId = Environment.ProcessId;
		string markerFilePath = BuildMarkerPath(processId);
		try {
			string folder = MarkerFolder;
			if (!fileSystem.Directory.Exists(folder)) {
				fileSystem.Directory.CreateDirectory(folder);
			}
			JObject content = new() {
				["pid"] = processId,
				["clio-version"] = ClioAssemblyVersion.Current,
				["started-at-utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
			};
			// Written to a temporary name and moved into place: a CLI process scans this directory at any
			// moment, and a half-written body would be read as an unparsable marker - which this class
			// treats as stale and DELETES. A torn write would therefore erase the very marker it is
			// creating, and the resulting missed deferral is silent.
			string temporaryPath = markerFilePath + ".tmp";
			fileSystem.File.WriteAllText(temporaryPath, content.ToString(Formatting.Indented));
			fileSystem.File.Move(temporaryPath, markerFilePath, overwrite: true);
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
		string folder = MarkerFolder;
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
			// Per marker, because ONE unreadable file must not abort the scan: the scan's answer decides
			// whether a self-update runs under a live host, and an exception escaping here is read by the
			// caller as "no host resident".
			try {
				McpHostPresenceMarker marker = TryReadMarker(markerFile);
				if (marker is null) {
					// Unusable, by whatever route - and therefore stale. An unparsable body is not a
					// reason to keep a file that can never again name a live process: `touch
					// mcp-server.1.lock` would otherwise defer every clio update on the machine forever.
					Unregister(markerFile);
					continue;
				}
				if (processLivenessProbe.IsAlive(marker.ProcessId, marker.StartedAtUtc)) {
					return marker;
				}
				// The recorded host is gone: drop the marker so a killed host cannot defer updates forever.
				Unregister(markerFile);
			}
			catch (Exception exception) when (IsMarkerIoFailure(exception)) {
				// Skip this marker; the next one may still name a live host.
			}
		}
		return null;
	}

	/// <summary>
	/// Reads one marker, or returns <see langword="null"/> when it is not a usable marker.
	/// </summary>
	/// <remarks>
	/// Everything is checked BEFORE the body is read, because reading is the dangerous part: this file
	/// lives in a directory the user can write, and a symbolic link to an endless device would hang every
	/// clio command on the machine at startup. A link and an oversized file are therefore refused
	/// unread, and a marker without a usable start time is refused too - without it the recycled-pid
	/// guard silently turns itself off.
	/// </remarks>
	private McpHostPresenceMarker TryReadMarker(string markerFilePath) {
		int processId = ParseProcessIdFromFileName(markerFilePath);
		if (processId <= 0) {
			return null;
		}
		IFileInfo markerFile = fileSystem.FileInfo.New(markerFilePath);
		if (!markerFile.Exists || markerFile.LinkTarget is not null || markerFile.Length > MaxMarkerBytes) {
			return null;
		}
		string version;
		DateTimeOffset startedAtUtc;
		try {
			// Parsed WITHOUT date coercion: Json.NET's default turns an ISO timestamp into a DateTime
			// while parsing, and the started-at-utc field would then not be a JSON string any more - the
			// type check below would refuse every marker clio itself wrote.
			JObject content = ParseWithoutDateCoercion(fileSystem.File.ReadAllText(markerFilePath));
			// Read through the token TYPE, never through Value<string>(): a field holding an object or an
			// array makes that throw InvalidCastException, which is not a JsonException and would escape
			// the whole scan - so one hand-mangled file would hide every OTHER live host and let the
			// update run under it. A wrong-typed field means this marker is not one.
			version = ReadStringField(content, "clio-version") ?? "unknown";
			string startedAtText = ReadStringField(content, "started-at-utc");
			if (startedAtText is null
				|| !DateTimeOffset.TryParse(startedAtText, CultureInfo.InvariantCulture,
					DateTimeStyles.RoundtripKind, out startedAtUtc)) {
				return null;
			}
		}
		catch (JsonException) {
			return null;
		}
		return new McpHostPresenceMarker(processId, version, startedAtUtc, markerFilePath);
	}

	private static JObject ParseWithoutDateCoercion(string content) {
		using System.IO.StringReader stringReader = new(content);
		using JsonTextReader jsonReader = new(stringReader) { DateParseHandling = DateParseHandling.None };
		return JObject.Load(jsonReader);
	}

	/// <summary>Returns a field's value only when it really is a JSON string.</summary>
	private static string ReadStringField(JObject content, string fieldName) =>
		content[fieldName] is JValue { Type: JTokenType.String } value
			? value.Value<string>()
			: null;

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

	private string BuildMarkerPath(int processId) =>
		System.IO.Path.Combine(MarkerFolder,
			$"{MarkerPrefix}{processId.ToString(CultureInfo.InvariantCulture)}{MarkerSuffix}");

	private static bool IsMarkerIoFailure(Exception exception) =>
		exception is System.IO.IOException or UnauthorizedAccessException or NotSupportedException
			or ArgumentException;

}
