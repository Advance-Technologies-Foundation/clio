using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClioRing.Ipc;

/// <summary>
/// Configuration for launching the clio MCP child process. Bound from
/// <c>app-settings.json</c> (<c>ClioIpc</c> section) with a sensible default when absent.
/// Pure data carrier.
/// </summary>
public sealed record ClioIpcSettings {
	/// <summary>Executable to launch (for example <c>dotnet</c> or <c>clio</c>).</summary>
	public required string Command { get; init; }

	/// <summary>Arguments passed to <see cref="Command"/> (for example the clio dll path + <c>mcp-server</c>).</summary>
	public required IReadOnlyList<string> Args { get; init; }

	/// <summary>Optional working directory for the child; null uses the launcher's directory.</summary>
	public string? WorkingDirectory { get; init; }

	/// <summary>
	/// The released clio dotnet tool installed in the current user's standard global-tool directory.
	/// Using the absolute shim path prevents an unrelated executable earlier on <c>PATH</c> from being launched.
	/// </summary>
	public static ClioIpcSettings Default { get; } = new() {
		Command = ResolveReleaseToolPath(Environment.GetEnvironmentVariable("DOTNET_CLI_HOME"),
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
		Args = new[] { "mcp-server" }
	};

	/// <summary>
	/// Resolves a trusted clio dotnet-tool shim from DOTNET_CLI_HOME or the normal user profile.
	/// Custom tool paths remain explicit Development targets rather than being trusted implicitly from PATH.
	/// </summary>
	/// <param name="dotnetCliHome">Optional DOTNET_CLI_HOME value.</param>
	/// <param name="userProfile">Current user profile directory.</param>
	/// <returns>An absolute trusted shim path, or the standard user shim path before installation.</returns>
	public static string ResolveReleaseToolPath(string? dotnetCliHome, string userProfile) {
		string shimName = OperatingSystem.IsWindows() ? "clio.exe" : "clio";
		IEnumerable<string> globalHomes = new[] { dotnetCliHome, userProfile }
			.Where(home => !string.IsNullOrWhiteSpace(home))
			.Select(home => Path.Combine(home!, ".dotnet", "tools"));
		foreach (string directory in globalHomes.Distinct(StringComparer.OrdinalIgnoreCase)) {
			string candidate = Path.GetFullPath(Path.Combine(directory, shimName));
			if (File.Exists(candidate) && Directory.Exists(Path.Combine(directory, ".store", "clio"))) {
				return candidate;
			}
		}
		return Path.GetFullPath(Path.Combine(userProfile, ".dotnet", "tools", shimName));
	}
}

/// <summary>
/// Immutable record of the MCP <c>initialize</c> handshake: proves which clio server version and
/// protocol surface the ring negotiated with. Enables a separate version-pinned release cycle.
/// </summary>
public sealed record ClioServerHandshake {
	/// <summary>Server implementation name (for example <c>clio</c>).</summary>
	public required string ServerName { get; init; }

	/// <summary>Server implementation version (for example <c>8.1.0.64</c>).</summary>
	public required string ServerVersion { get; init; }

	/// <summary>Negotiated MCP protocol version string, when the SDK exposes it.</summary>
	public string? ProtocolVersion { get; init; }

	/// <summary>Advertised server capability names (for example <c>tools</c>, <c>resources</c>, <c>prompts</c>, <c>logging</c>).</summary>
	public required IReadOnlyList<string> Capabilities { get; init; }

	/// <summary>Server-provided usage instructions (may be long); null when not advertised.</summary>
	public string? Instructions { get; init; }
}

/// <summary>
/// One entry of the full clio command catalog returned by <c>get-tool-contract</c> with empty
/// arguments. This is the complete surface (~140 tools), not the resident <c>tools/list</c> subset.
/// </summary>
public sealed record ClioCatalogEntry {
	/// <summary>Flat tool name (for example <c>list-environments</c>).</summary>
	public required string Name { get; init; }

	/// <summary>One-line purpose/summary of the tool.</summary>
	public string Purpose { get; init; } = string.Empty;

	/// <summary>True when a full contract/schema is retrievable via <c>get-tool-contract {name}</c>.</summary>
	public bool ContractAvailable { get; init; }

	/// <summary>True when the tool is resident (surfaced in <c>tools/list</c>) rather than long-tail.</summary>
	public bool Resident { get; init; }

	/// <summary>True when the tool performs a destructive/irreversible action.</summary>
	public bool Destructive { get; init; }
}

/// <summary>
/// Parsed outcome of an MCP tool call. clio returns its payload as a JSON string inside a text
/// content block (not <c>structuredContent</c>), so <see cref="RawText"/> is the concatenated text
/// and <see cref="Json"/> is that text parsed as JSON when possible.
/// </summary>
public sealed record ClioToolCallResult {
	/// <summary>The concatenated text content blocks returned by the tool (the primary payload).</summary>
	public string RawText { get; init; } = string.Empty;

	/// <summary>The parsed JSON form of <see cref="RawText"/> as a compact string, or null when it was not valid JSON.</summary>
	public string? Json { get; init; }

	/// <summary>True when the server flagged the result as an error.</summary>
	public bool IsError { get; init; }

	/// <summary>True when the server also populated the optional <c>structuredContent</c> field.</summary>
	public bool HasStructuredContent { get; init; }
}
