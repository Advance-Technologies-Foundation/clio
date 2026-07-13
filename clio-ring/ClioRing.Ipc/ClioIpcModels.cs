using System.Collections.Generic;

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
	/// A conventional default for this machine: the framework-dependent clio dll driven by <c>dotnet</c>.
	/// Used only when <c>app-settings.json</c> supplies no <c>ClioIpc</c> section. The Debug build is
	/// preferred here because on this machine it is newer (8.1.0.77) and matches the documented
	/// <c>get-tool-contract</c> compact-index contract, whereas the Release dll (8.1.0.64) predates it.
	/// Override freely in app-settings.
	/// </summary>
	public static ClioIpcSettings Default { get; } = new() {
		Command = "dotnet",
		Args = new[] { @"C:\Projects\clio\clio\bin\Debug\net10.0\clio.dll", "mcp-server" }
	};
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
