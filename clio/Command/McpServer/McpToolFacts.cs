using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Clio.Command.McpServer;

/// <summary>
/// What a call does to a Creatio environment, to this machine and to clio's settings. Ordered from the narrowest to
/// the widest, so a host can compare two values.
/// </summary>
/// <remarks>
/// A write to a file or folder the call names in an argument is not an effect: a <c>get-...-to-file</c> tool that only
/// reads the environment is <see cref="Read"/>. Neither are clio's own caches and working files (page baselines, the
/// browser-session cache, logs, the telemetry outbox).
/// </remarks>
public enum McpToolEffect {
	/// <summary>No value declared. Never a valid declaration: the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>Changes nothing in any environment, on the machine or in clio's settings.</summary>
	Read = 1,

	/// <summary>
	/// Creates something, or changes only what the call names: a schema, a page, a record, an app, a setting's value.
	/// Replacing the content of the one thing the call names is still a change.
	/// </summary>
	Change = 2,

	/// <summary>
	/// Removes something; overwrites more than the one thing the call names, or data the caller did not name one by
	/// one (a whole package, a workspace, a database); or runs code, SQL or a business process whose effect clio
	/// cannot bound.
	/// </summary>
	Destroy = 3
}

/// <summary>
/// Whether the call builds or restarts the environment, which every user of it feels while it runs.
/// </summary>
public enum McpToolEnvironmentImpact {
	/// <summary>No value declared. Never a valid declaration: the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>The environment keeps serving its users as before.</summary>
	None = 1,

	/// <summary>
	/// Compiles the configuration, restarts or stops the application, installs packages or the process builder,
	/// flushes its cache, or switches the file system mode.
	/// </summary>
	Rebuild = 2
}

/// <summary>The widest place the call reads or changes. Ordered from the narrowest to the widest.</summary>
public enum McpToolScope {
	/// <summary>No value declared. Never a valid declaration: the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>clio itself and the files the call names: catalogs, guidance, offline checks, generated files.</summary>
	Local = 1,

	/// <summary>The Creatio environment the call names.</summary>
	Environment = 2,

	/// <summary>
	/// This computer beyond the files the call names: installed software and services, local Creatio instances,
	/// local databases, clio itself, the agents' configuration, knowledge sources, links between folders.
	/// </summary>
	Machine = 3
}

/// <summary>
/// Whether the call shows or changes clio's own connections: the environments registered in clio's settings and the
/// credentials clio holds or creates for them. Ordered from the narrowest to the widest.
/// </summary>
/// <remarks>Resolving an environment name to sign in with it is not touching the connections.</remarks>
public enum McpToolConnections {
	/// <summary>No value declared. Never a valid declaration: the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>Neither shows nor changes a connection.</summary>
	None = 1,

	/// <summary>
	/// Returns registered environments or their details (address, login, settings) to the caller, or hands back a
	/// credential derived from one (a signed-in browser session, a token) without storing anything.
	/// </summary>
	Read = 2,

	/// <summary>
	/// Registers, removes or edits a registered environment, stores credentials, or creates a credential meant for
	/// signing in (an OAuth client, a technical user whose secret is handed back).
	/// </summary>
	Change = 3
}

/// <summary>Whether a host path names a file, a folder, or either.</summary>
public enum McpHostPathKind {
	/// <summary>No value declared. Never a valid declaration: the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>A file.</summary>
	File = 1,

	/// <summary>A folder, with everything below it.</summary>
	Folder = 2,

	/// <summary>A file or a folder, depending on the value.</summary>
	FileOrFolder = 3
}

/// <summary>What clio does at a host path, at most. Ordered from the narrowest to the widest.</summary>
public enum McpHostPathAccess {
	/// <summary>No value declared. Never a valid declaration: the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>clio only reads there.</summary>
	Read = 1,

	/// <summary>clio creates or replaces what the path names, and reads nothing that was there.</summary>
	Write = 2,

	/// <summary>
	/// clio reads and writes there, or deletes there; also a folder clio only stores, for later calls to read and
	/// write in.
	/// </summary>
	ReadWrite = 3
}

/// <summary>
/// One argument whose value names a file or folder on the machine clio runs on, which clio reads or writes as the
/// person running it: a full path, or a path relative to clio's working folder.
/// </summary>
/// <param name="Argument">The argument's name as a caller writes it: inside <c>args</c> for an args-record tool.</param>
/// <param name="Kind">Whether it names a file, a folder, or either.</param>
/// <param name="Access">What clio does there, at most.</param>
/// <param name="List">
/// Whether the value holds several paths: a JSON array of them, or one string with the paths separated by commas.
/// </param>
/// <param name="Or">
/// What the value may be instead of a path, which clio tells from a path by the value's look: <c>name</c> (a name
/// clio looks up, such as a registered environment) or <c>url</c>. <see langword="null"/> when it is always a path.
/// </param>
public sealed record McpToolHostPath(string Argument, McpHostPathKind Kind, McpHostPathAccess Access, bool List, string Or) {

	/// <summary>The value may instead be a name clio looks up.</summary>
	public const string OrName = "name";

	/// <summary>The value may instead be a URL.</summary>
	public const string OrUrl = "url";
}

/// <summary>An argument whose name reads like a path but whose value is not a path on this machine.</summary>
/// <param name="Argument">The argument's name as a caller writes it.</param>
/// <param name="Why">What the value is instead: a URL, a path inside a package on the environment, a flag.</param>
public sealed record McpToolNotHostPath(string Argument, string Why);

/// <summary>
/// The facts for one value of one argument, with every field resolved against the tool's own facts: a field the
/// catalog entry leaves out takes the tool's value.
/// </summary>
/// <param name="Argument">The argument's name as a caller writes it.</param>
/// <param name="Value">The value these facts apply to.</param>
/// <param name="WhenAbsent">Whether they also apply when the call does not send the argument.</param>
/// <param name="Effect">What the call does with this value.</param>
/// <param name="EnvironmentImpact">Whether it builds or restarts the environment.</param>
/// <param name="Scope">The widest place it reaches.</param>
/// <param name="Connections">Whether it shows or changes connections.</param>
public sealed record McpToolArgumentFacts(
	string Argument,
	string Value,
	bool WhenAbsent,
	McpToolEffect Effect,
	McpToolEnvironmentImpact EnvironmentImpact,
	McpToolScope Scope,
	McpToolConnections Connections);

/// <summary>
/// The reviewed facts of one MCP tool: the widest it does, plus the argument values that narrow them. Declared in
/// one catalog, <c>Command/McpServer/Data/mcp-tool-facts.json</c>, read by <see cref="McpToolFactsReader"/> and
/// published in <c>tools/list</c> (<c>_meta</c>) and by <c>get-tool-contract</c> <c>detail=facts</c>.
/// </summary>
/// <remarks>
/// <para>
/// The facts are the WIDEST the tool can do, over every argument it takes. <see cref="ByArgument"/> narrows them for
/// single argument values (a composite tool's <c>action: list</c>, a <c>dry-run</c>); an entry never widens them.
/// </para>
/// <para>
/// One catalog rather than an attribute on each tool method: about 250 near-identical declarations spread over the
/// tool files read as duplicated code to the quality gate, and one file is also one place to review them. A field
/// the catalog leaves out reads back as <c>Unspecified</c>, which the coverage test fails on, as it fails on a
/// served tool with no entry and on an entry for a tool clio does not serve.
/// </para>
/// </remarks>
/// <param name="Effect">What the call does, at most.</param>
/// <param name="EnvironmentImpact">Whether it builds or restarts the environment.</param>
/// <param name="Scope">The widest place it reads or changes.</param>
/// <param name="Connections">Whether it shows or changes clio's connections.</param>
/// <param name="ByArgument">The argument values that narrow these facts, ordered by argument and value.</param>
/// <param name="HostPaths">The arguments that name a file or folder on this machine, ordered by argument.</param>
/// <param name="NotHostPaths">
/// The arguments whose name reads like a path but that are not one on this machine, ordered by argument.
/// </param>
public sealed record McpToolFacts(
	McpToolEffect Effect,
	McpToolEnvironmentImpact EnvironmentImpact,
	McpToolScope Scope,
	McpToolConnections Connections,
	IReadOnlyList<McpToolArgumentFacts> ByArgument,
	IReadOnlyList<McpToolHostPath> HostPaths,
	IReadOnlyList<McpToolNotHostPath> NotHostPaths) {

	/// <summary>The key the facts sit under in a tool's <c>_meta</c> in <c>tools/list</c>.</summary>
	public const string MetaKey = "clio/facts";

	/// <summary>
	/// The names of the tool-level fields left <c>Unspecified</c>, plus each argument entry that leaves its
	/// <c>Effect</c> unspecified and each host path that leaves its kind or access unspecified. Empty when the
	/// declaration is complete.
	/// </summary>
	public IReadOnlyList<string> UnspecifiedFieldNames {
		get {
			List<string> missing = [];
			if (Effect == McpToolEffect.Unspecified) {
				missing.Add(nameof(Effect));
			}
			if (EnvironmentImpact == McpToolEnvironmentImpact.Unspecified) {
				missing.Add(nameof(EnvironmentImpact));
			}
			if (Scope == McpToolScope.Unspecified) {
				missing.Add(nameof(Scope));
			}
			if (Connections == McpToolConnections.Unspecified) {
				missing.Add(nameof(Connections));
			}
			missing.AddRange(ByArgument
				.Where(entry => entry.Effect == McpToolEffect.Unspecified)
				.Select(entry => $"{entry.Argument}={entry.Value}: {nameof(Effect)}"));
			foreach (McpToolHostPath path in HostPaths) {
				if (path.Kind == McpHostPathKind.Unspecified) {
					missing.Add($"host path {path.Argument}: {nameof(McpToolHostPath.Kind)}");
				}
				if (path.Access == McpHostPathAccess.Unspecified) {
					missing.Add($"host path {path.Argument}: {nameof(McpToolHostPath.Access)}");
				}
			}
			return missing;
		}
	}

	/// <summary>
	/// The facts as published: <c>effect</c>, <c>rebuilds-environment</c>, <c>scope</c>, <c>connections</c>; when
	/// some argument values narrow them, <c>by-argument</c> with every field resolved; when some arguments name a
	/// file or folder on this machine, <c>host-paths</c>; and when some only read like one, <c>not-host-paths</c>.
	/// </summary>
	public JsonObject ToJson() {
		JsonObject json = WriteFields(new JsonObject(), Effect, EnvironmentImpact, Scope, Connections);
		if (ByArgument.Count > 0) {
			json["by-argument"] = new JsonArray([.. ByArgument.Select(WriteArgumentEntry)]);
		}
		if (HostPaths.Count > 0) {
			json["host-paths"] = new JsonArray([.. HostPaths.Select(WriteHostPath)]);
		}
		if (NotHostPaths.Count > 0) {
			json["not-host-paths"] = new JsonArray([.. NotHostPaths.Select(notPath => new JsonObject {
				["argument"] = notPath.Argument,
				["why"] = notPath.Why
			})]);
		}
		return json;
	}

	private const string UnspecifiedName = "unspecified";

	private static JsonNode WriteArgumentEntry(McpToolArgumentFacts entry) {
		JsonObject item = new() {
			["argument"] = entry.Argument,
			["value"] = entry.Value
		};
		if (entry.WhenAbsent) {
			item["when-absent"] = true;
		}
		return WriteFields(item, entry.Effect, entry.EnvironmentImpact, entry.Scope, entry.Connections);
	}

	private static JsonNode WriteHostPath(McpToolHostPath path) {
		JsonObject item = new() {
			["argument"] = path.Argument,
			["kind"] = Name(path.Kind),
			["access"] = Name(path.Access)
		};
		if (path.List) {
			item["list"] = true;
		}
		if (!string.IsNullOrEmpty(path.Or)) {
			item["or"] = path.Or;
		}
		return item;
	}

	/// <summary>The published name of an effect: <c>read</c>, <c>change</c> or <c>destroy</c>.</summary>
	public static string Name(McpToolEffect value) => value switch {
		McpToolEffect.Read => "read",
		McpToolEffect.Change => "change",
		McpToolEffect.Destroy => "destroy",
		_ => UnspecifiedName
	};

	/// <summary>The published name of a scope: <c>local</c>, <c>environment</c> or <c>machine</c>.</summary>
	public static string Name(McpToolScope value) => value switch {
		McpToolScope.Local => "local",
		McpToolScope.Environment => "environment",
		McpToolScope.Machine => "machine",
		_ => UnspecifiedName
	};

	/// <summary>The published name of a connections fact: <c>none</c>, <c>read</c> or <c>change</c>.</summary>
	public static string Name(McpToolConnections value) => value switch {
		McpToolConnections.None => "none",
		McpToolConnections.Read => "read",
		McpToolConnections.Change => "change",
		_ => UnspecifiedName
	};

	/// <summary>The published name of a host-path kind: <c>file</c>, <c>folder</c> or <c>file-or-folder</c>.</summary>
	public static string Name(McpHostPathKind value) => value switch {
		McpHostPathKind.File => "file",
		McpHostPathKind.Folder => "folder",
		McpHostPathKind.FileOrFolder => "file-or-folder",
		_ => UnspecifiedName
	};

	/// <summary>The published name of a host-path access: <c>read</c>, <c>write</c> or <c>read-write</c>.</summary>
	public static string Name(McpHostPathAccess value) => value switch {
		McpHostPathAccess.Read => "read",
		McpHostPathAccess.Write => "write",
		McpHostPathAccess.ReadWrite => "read-write",
		_ => UnspecifiedName
	};

	private static JsonObject WriteFields(
		JsonObject json,
		McpToolEffect effect,
		McpToolEnvironmentImpact impact,
		McpToolScope scope,
		McpToolConnections connections) {
		json["effect"] = Name(effect);
		json["rebuilds-environment"] = impact == McpToolEnvironmentImpact.Rebuild;
		json["scope"] = Name(scope);
		json["connections"] = Name(connections);
		return json;
	}
}
