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

/// <summary>
/// Declares what an MCP tool does, so a host can decide how to treat a call to it without keeping its own list of
/// tool names. Read by <see cref="McpToolFactsReader"/>, published in <c>tools/list</c> (<c>_meta</c>) and in
/// <c>get-tool-contract</c>.
/// </summary>
/// <remarks>
/// <para>
/// The facts are the WIDEST the tool can do, over every argument it takes. When some argument values narrow them
/// (a composite tool's <c>action: list</c>, a <c>dry-run</c>), declare <see cref="McpToolArgumentFactsAttribute"/>
/// for those values.
/// </para>
/// <para>
/// Every field is an enum whose zero value is <c>Unspecified</c>, so a field left out is a coverage-test failure
/// rather than a plausible default. Declared, not derived: the tool's name and its <c>ReadOnly</c> /
/// <c>Destructive</c> hints are too coarse to derive these from, and the coverage test checks the hints agree.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false)]
/// [McpToolFacts(
///     Effect = McpToolEffect.Read,
///     EnvironmentImpact = McpToolEnvironmentImpact.None,
///     Scope = McpToolScope.Environment,
///     Connections = McpToolConnections.None)]
/// public MyResponse Run(MyArgs args) => ...;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolFactsAttribute : Attribute {

	/// <summary>What the call does, at most.</summary>
	public McpToolEffect Effect { get; set; }

	/// <summary>Whether the call builds or restarts the environment.</summary>
	public McpToolEnvironmentImpact EnvironmentImpact { get; set; }

	/// <summary>The widest place the call reads or changes.</summary>
	public McpToolScope Scope { get; set; }

	/// <summary>Whether the call shows or changes clio's connections.</summary>
	public McpToolConnections Connections { get; set; }
}

/// <summary>
/// Narrows a tool's <see cref="McpToolFactsAttribute"/> for one value of one argument: <c>manage-role</c> with
/// <c>action: list</c> only reads.
/// </summary>
/// <remarks>
/// <see cref="Effect"/> must be declared. A field left <c>Unspecified</c> takes the tool's own value. The coverage
/// test checks that no field is wider than the tool's, so a host that ignores these entries is never less careful
/// than one that reads them.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class McpToolArgumentFactsAttribute : Attribute {

	/// <summary>Declares the facts for <paramref name="argument"/> = <paramref name="value"/>.</summary>
	/// <param name="argument">The argument's name as a caller writes it: inside <c>args</c> for an args-record tool.</param>
	/// <param name="value">The value, as text; a boolean as <c>true</c> or <c>false</c>.</param>
	public McpToolArgumentFactsAttribute(string argument, string value) {
		Argument = argument;
		Value = value;
	}

	/// <summary>The argument's name as a caller writes it.</summary>
	public string Argument { get; }

	/// <summary>The value these facts apply to.</summary>
	public string Value { get; }

	/// <summary>Whether these facts also apply when the call does not send the argument (the value is its default).</summary>
	public bool WhenAbsent { get; set; }

	/// <summary>What the call does with this value. Must be declared.</summary>
	public McpToolEffect Effect { get; set; }

	/// <summary>Whether the call builds or restarts the environment with this value; <c>Unspecified</c> takes the tool's.</summary>
	public McpToolEnvironmentImpact EnvironmentImpact { get; set; }

	/// <summary>The widest place the call reaches with this value; <c>Unspecified</c> takes the tool's.</summary>
	public McpToolScope Scope { get; set; }

	/// <summary>Whether the call shows or changes connections with this value; <c>Unspecified</c> takes the tool's.</summary>
	public McpToolConnections Connections { get; set; }
}

/// <summary>The facts for one value of one argument, with every field resolved against the tool's own facts.</summary>
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
/// The declared facts of one MCP tool: the widest it does, plus the argument values that narrow them.
/// </summary>
/// <param name="Effect">What the call does, at most.</param>
/// <param name="EnvironmentImpact">Whether it builds or restarts the environment.</param>
/// <param name="Scope">The widest place it reads or changes.</param>
/// <param name="Connections">Whether it shows or changes clio's connections.</param>
/// <param name="ByArgument">The argument values that narrow these facts, ordered by argument and value.</param>
public sealed record McpToolFacts(
	McpToolEffect Effect,
	McpToolEnvironmentImpact EnvironmentImpact,
	McpToolScope Scope,
	McpToolConnections Connections,
	IReadOnlyList<McpToolArgumentFacts> ByArgument) {

	/// <summary>The key the facts sit under in a tool's <c>_meta</c> in <c>tools/list</c>.</summary>
	public const string MetaKey = "clio/facts";

	/// <summary>
	/// The names of the tool-level fields left <c>Unspecified</c>, plus each argument entry that leaves its
	/// <c>Effect</c> unspecified. Empty when the declaration is complete.
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
			return missing;
		}
	}

	/// <summary>
	/// The facts as published: <c>effect</c>, <c>rebuilds-environment</c>, <c>scope</c>, <c>connections</c> and, when
	/// some argument values narrow them, <c>by-argument</c> with every field resolved.
	/// </summary>
	public JsonObject ToJson() {
		JsonObject json = WriteFields(new JsonObject(), Effect, EnvironmentImpact, Scope, Connections);
		if (ByArgument.Count > 0) {
			JsonArray entries = [];
			foreach (McpToolArgumentFacts entry in ByArgument) {
				JsonObject item = new() {
					["argument"] = entry.Argument,
					["value"] = entry.Value
				};
				if (entry.WhenAbsent) {
					item["when-absent"] = true;
				}
				entries.Add(WriteFields(item, entry.Effect, entry.EnvironmentImpact, entry.Scope, entry.Connections));
			}
			json["by-argument"] = entries;
		}
		return json;
	}

	/// <summary>The published name of an effect: <c>read</c>, <c>change</c> or <c>destroy</c>.</summary>
	public static string Name(McpToolEffect value) => value switch {
		McpToolEffect.Read => "read",
		McpToolEffect.Change => "change",
		McpToolEffect.Destroy => "destroy",
		_ => "unspecified"
	};

	/// <summary>The published name of a scope: <c>local</c>, <c>environment</c> or <c>machine</c>.</summary>
	public static string Name(McpToolScope value) => value switch {
		McpToolScope.Local => "local",
		McpToolScope.Environment => "environment",
		McpToolScope.Machine => "machine",
		_ => "unspecified"
	};

	/// <summary>The published name of a connections fact: <c>none</c>, <c>read</c> or <c>change</c>.</summary>
	public static string Name(McpToolConnections value) => value switch {
		McpToolConnections.None => "none",
		McpToolConnections.Read => "read",
		McpToolConnections.Change => "change",
		_ => "unspecified"
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
