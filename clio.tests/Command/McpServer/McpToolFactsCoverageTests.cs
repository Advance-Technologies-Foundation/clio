using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Keeps every MCP tool saying what it does (the catalog <c>Command/McpServer/Data/mcp-tool-facts.json</c>), so a host can treat a call by
/// clio's own facts instead of a list of tool names. A new tool that ships without its facts, or with facts its
/// standard MCP hints contradict, fails here.
/// </summary>
/// <remarks>
/// Every check is a routine that runs over the real catalog AND over synthetic tools, so a green result over the
/// catalog is shown not to be vacuous.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolFactsCoverageTests {

	/// <summary>
	/// A deprecated name registered as its own tool method that delegates to the canonical one. Both run the same
	/// code, so their facts must be identical.
	/// </summary>
	private static readonly (string Alias, string Canonical)[] MethodLevelAliasPairs = [
		("StopAllCreatio", "stop-all-creatio")
	];

	private static Assembly ProductionAssembly => typeof(McpFeatureToggleFilter).Assembly;

	private static McpToolInvokerRegistry BuildRegistryOverFullCatalog() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		return new McpToolInvokerRegistry(provider, ProductionAssembly, featureToggle, JsonSerializerOptions.Default);
	}

	// Every tool clio serves (every feature on) with its catalog facts, null when the catalog has no entry.
	private static IReadOnlyList<(string Name, McpServerTool Tool, McpToolFacts Facts)> ReadCatalog() {
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();
		IReadOnlyDictionary<string, McpToolFacts> declared = McpToolFactsReader.Declared;
		return [.. registry.ToolNames
			.OrderBy(name => name, StringComparer.Ordinal)
			.Select(name => {
				registry.TryGetTool(name, out McpServerTool tool);
				return (name, tool, declared.TryGetValue(name, out McpToolFacts facts) ? facts : null);
			})];
	}

	// A catalog for the synthetic tools below, parsed by the production parser, so the defects it holds are the kind a
	// real catalog edit can make.
	private const string SyntheticCatalog = """
		{
		  "zz-facts-declared-tool": {"effect": "destroy", "rebuilds-environment": false, "scope": "environment", "connections": "none",
		    "by-argument": [{"argument": "action", "value": "list", "effect": "read"}]},
		  "zz-facts-partial-tool": {"effect": "read", "rebuilds-environment": false, "scope": "local"},
		  "zz-facts-hint-contradiction-tool": {"effect": "destroy", "rebuilds-environment": false, "scope": "environment", "connections": "none"},
		  "zz-facts-wide-argument-tool": {"effect": "change", "rebuilds-environment": false, "scope": "environment", "connections": "none",
		    "by-argument": [{"argument": "mode", "value": "wipe", "effect": "destroy"}, {"argument": "missing", "value": "x", "effect": "read"}]},
		  "zz-facts-host-path-declared-tool": {"effect": "read", "rebuilds-environment": false, "scope": "environment", "connections": "none",
		    "host-paths": [{"argument": "outputFile", "kind": "file", "access": "write"}, {"argument": "source", "kind": "folder", "access": "read", "list": true, "or": "name"}],
		    "not-host-paths": [{"argument": "filePath", "why": "a path inside a package on the environment"}]},
		  "zz-facts-host-path-defect-tool": {"effect": "read", "rebuilds-environment": false, "scope": "environment", "connections": "none",
		    "host-paths": [{"argument": "missing", "kind": "file", "access": "read"}, {"argument": "both", "kind": "folder", "access": "write"},
		      {"argument": "twice", "kind": "file", "access": "read"}, {"argument": "twice", "kind": "file", "access": "write", "or": "anything"}],
		    "not-host-paths": [{"argument": "both", "why": "declared both ways"}, {"argument": "zipFile"}, {"argument": "absent", "why": "not an argument"}]},
		  "zz-facts-host-path-partial-tool": {"effect": "read", "rebuilds-environment": false, "scope": "environment", "connections": "none",
		    "host-paths": [{"argument": "outputFile", "kind": "file"}, {"argument": "source", "kind": "archive", "access": "read"}]},
		  "zz-facts-stale-entry": {"effect": "read", "rebuilds-environment": false, "scope": "local", "connections": "none"}
		}
		""";

	// The same triples for synthetic tool types (static methods, so the SDK builds them without a target), with their
	// facts from SyntheticCatalog.
	private static IReadOnlyList<(string Name, McpServerTool Tool, McpToolFacts Facts)> ReadSynthetic(params Type[] types) {
		IReadOnlyDictionary<string, McpToolFacts> catalog = McpToolFactsReader.Parse(SyntheticCatalog);
		return [.. types
			.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
			.Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
			.Select(method => {
				McpServerTool tool = McpServerTool.Create(method, target: null);
				string name = tool.ProtocolTool.Name;
				return (name, tool, catalog.TryGetValue(name, out McpToolFacts facts) ? facts : null);
			})];
	}

	/// <summary>Catalog entries for a tool clio does not serve: a renamed or removed tool must not leave one behind.</summary>
	internal static IReadOnlyList<string> FindStaleEntries(IEnumerable<string> catalogNames, IEnumerable<string> servedNames) {
		HashSet<string> served = new(servedNames, StringComparer.OrdinalIgnoreCase);
		return [.. catalogNames
			.Where(name => !served.Contains(name))
			.OrderBy(name => name, StringComparer.Ordinal)
			.Select(name => $"mcp-tool-facts.json has an entry for '{name}', which clio does not serve.")];
	}

	#region Checking routine (runs against the real catalog AND against synthetic tools)

	/// <summary>One readable failure per problem; empty when every tool's facts are complete and consistent.</summary>
	internal static IReadOnlyList<string> FindFactsFailures(
		IEnumerable<(string Name, McpServerTool Tool, McpToolFacts Facts)> tools) {
		List<string> failures = [];
		foreach ((string name, McpServerTool tool, McpToolFacts facts) in tools) {
			if (facts is null) {
				failures.Add($"Tool '{name}' has no entry in Command/McpServer/Data/mcp-tool-facts.json: a host cannot tell what a call to it does.");
				continue;
			}
			if (facts.UnspecifiedFieldNames.Count > 0) {
				failures.Add($"Tool '{name}' leaves {string.Join(", ", facts.UnspecifiedFieldNames)} unspecified.");
				continue;
			}
			failures.AddRange(FindHintFailures(name, tool, facts));
			failures.AddRange(FindConsistencyFailures(name, facts.Effect, facts.EnvironmentImpact, facts.Connections, string.Empty));
			failures.AddRange(FindArgumentFailures(name, tool, facts));
			failures.AddRange(FindHostPathFailures(name, tool, facts));
		}
		return failures;
	}

	private static IEnumerable<string> FindHintFailures(string name, McpServerTool tool, McpToolFacts facts) {
		bool? readOnlyHint = tool.ProtocolTool.Annotations?.ReadOnlyHint;
		bool? destructiveHint = tool.ProtocolTool.Annotations?.DestructiveHint;
		if (readOnlyHint == true && facts.Effect != McpToolEffect.Read) {
			yield return $"Tool '{name}' is readOnlyHint=true but its effect is {McpToolFacts.Name(facts.Effect)}.";
		}
		// An absent destructiveHint means true in MCP, so only an explicit false contradicts a destroy.
		if (facts.Effect == McpToolEffect.Destroy && destructiveHint == false) {
			yield return $"Tool '{name}' is destroy but destructiveHint=false: the (D) marker would under-report it.";
		}
	}

	private static IEnumerable<string> FindConsistencyFailures(
		string name,
		McpToolEffect effect,
		McpToolEnvironmentImpact impact,
		McpToolConnections connections,
		string where) {
		if (effect == McpToolEffect.Read && impact == McpToolEnvironmentImpact.Rebuild) {
			yield return $"Tool '{name}'{where} is a read that rebuilds the environment.";
		}
		if (effect == McpToolEffect.Read && connections == McpToolConnections.Change) {
			yield return $"Tool '{name}'{where} is a read that changes connections.";
		}
	}

	private static IEnumerable<string> FindArgumentFailures(string name, McpServerTool tool, McpToolFacts facts) {
		HashSet<string> arguments = ReadArgumentNames(tool.ProtocolTool.InputSchema);
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> absentDefaults = new(StringComparer.Ordinal);
		foreach (McpToolArgumentFacts entry in facts.ByArgument) {
			string where = $" with {entry.Argument}={entry.Value}";
			if (!arguments.Contains(entry.Argument)) {
				yield return $"Tool '{name}' narrows its facts by '{entry.Argument}', which is not one of its arguments.";
			}
			if (!seen.Add($"{entry.Argument}\u0000{entry.Value}")) {
				yield return $"Tool '{name}' declares{where} twice.";
			}
			if (entry.WhenAbsent && !absentDefaults.Add(entry.Argument)) {
				yield return $"Tool '{name}' marks more than one value of '{entry.Argument}' as its default (when-absent).";
			}
			if (entry.Effect > facts.Effect || entry.EnvironmentImpact > facts.EnvironmentImpact
				|| entry.Scope > facts.Scope || entry.Connections > facts.Connections) {
				yield return $"Tool '{name}'{where} is wider than the tool's own facts, which must be the widest.";
			}
			foreach (string failure in FindConsistencyFailures(name, entry.Effect, entry.EnvironmentImpact, entry.Connections, where)) {
				yield return failure;
			}
		}
	}

	// A name a host would read as a path. An argument with such a name is declared a host path or, with what it is
	// instead, not one; so a host that guesses by name has clio's answer for every argument it would guess on.
	private static readonly Regex PathLikeArgumentName = new(
		"(dir|directory|folder|destination|location|zip)s?$|path|file",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static IEnumerable<string> FindHostPathFailures(string name, McpServerTool tool, McpToolFacts facts) {
		HashSet<string> arguments = ReadArgumentNames(tool.ProtocolTool.InputSchema);
		HashSet<string> hostPaths = new(StringComparer.Ordinal);
		HashSet<string> notHostPaths = new(StringComparer.Ordinal);
		foreach (McpToolHostPath path in facts.HostPaths) {
			if (!arguments.Contains(path.Argument)) {
				yield return $"Tool '{name}' declares host path '{path.Argument}', which is not one of its arguments.";
			}
			if (!hostPaths.Add(path.Argument)) {
				yield return $"Tool '{name}' declares host path '{path.Argument}' twice.";
			}
			if (path.Or is not (null or McpToolHostPath.OrName or McpToolHostPath.OrUrl)) {
				yield return $"Tool '{name}' says host path '{path.Argument}' may instead be '{path.Or}', which is neither '{McpToolHostPath.OrName}' nor '{McpToolHostPath.OrUrl}'.";
			}
		}
		foreach (McpToolNotHostPath notPath in facts.NotHostPaths) {
			if (!arguments.Contains(notPath.Argument)) {
				yield return $"Tool '{name}' declares '{notPath.Argument}' not a host path, but it is not one of its arguments.";
			}
			if (!notHostPaths.Add(notPath.Argument)) {
				yield return $"Tool '{name}' declares '{notPath.Argument}' not a host path twice.";
			}
			if (hostPaths.Contains(notPath.Argument)) {
				yield return $"Tool '{name}' declares '{notPath.Argument}' both a host path and not one.";
			}
			if (string.IsNullOrWhiteSpace(notPath.Why)) {
				yield return $"Tool '{name}' declares '{notPath.Argument}' not a host path without saying what it is instead (why).";
			}
		}
		foreach (string argument in arguments
			.Where(argument => PathLikeArgumentName.IsMatch(argument)
				&& !hostPaths.Contains(argument) && !notHostPaths.Contains(argument))
			.OrderBy(argument => argument, StringComparer.Ordinal)) {
			yield return $"Tool '{name}' has the argument '{argument}', whose name reads like a path: declare it under host-paths or, with what it is instead, under not-host-paths.";
		}
	}

	// The names a caller writes: the top-level arguments and, for an args-record tool, the record's members.
	private static HashSet<string> ReadArgumentNames(JsonElement schema) {
		HashSet<string> names = new(StringComparer.Ordinal);
		if (!schema.TryGetProperty("properties", out JsonElement properties) || properties.ValueKind != JsonValueKind.Object) {
			return names;
		}
		foreach (JsonProperty property in properties.EnumerateObject()) {
			names.Add(property.Name);
			if (property.Value.ValueKind == JsonValueKind.Object
				&& property.Value.TryGetProperty("properties", out JsonElement members)
				&& members.ValueKind == JsonValueKind.Object) {
				foreach (JsonProperty member in members.EnumerateObject()) {
					names.Add(member.Name);
				}
			}
		}
		return names;
	}

	#endregion

	[Test]
	[Category("Unit")]
	[Description("Every enabled MCP tool declares all four facts, agrees with its standard readOnlyHint/destructiveHint, " +
		"narrows its facts only by its own arguments and only downwards, and the catalog names no tool clio does not serve. " +
		"A new tool without a catalog entry fails here.")]
	public void EveryEnabledTool_ShouldDeclareCompleteAndConsistentFacts() {
		// Arrange
		IReadOnlyList<(string Name, McpServerTool Tool, McpToolFacts Facts)> catalog = ReadCatalog();

		// Act
		IReadOnlyList<string> failures = [
			.. FindFactsFailures(catalog),
			.. FindStaleEntries(McpToolFactsReader.Declared.Keys, catalog.Select(entry => entry.Name))
		];

		// Assert
		catalog.Count.Should().BeGreaterThan(200,
			because: "clio serves about 250 tools; a collapse would mean discovery broke, which would make this check vacuous");
		failures.Should().BeEmpty(
			because: "a host decides how to treat a call from these facts, so every tool must declare them and they must not contradict the standard hints");
	}

	[Test]
	[Category("Unit")]
	[Description("The checking routine has teeth: over synthetic tools it reports a missing declaration, a partial one, a " +
		"destroy marked non-destructive, a read-only hint on a change, and argument entries that are unknown or wider than " +
		"the tool, and stays silent about a correct tool.")]
	public void FactsRoutine_ShouldReportEachKindOfDefect_WhenRunOverSyntheticTools() {
		// Arrange
		IReadOnlyList<(string Name, McpServerTool Tool, McpToolFacts Facts)> synthetic = ReadSynthetic(
			typeof(DeclaredFixtureTool),
			typeof(UndeclaredFixtureTool),
			typeof(PartialFixtureTool),
			typeof(HintContradictionFixtureTool),
			typeof(WideArgumentFixtureTool));

		// Act
		IReadOnlyList<string> failures = FindFactsFailures(synthetic);

		// Assert
		synthetic.Should().HaveCount(5, because: "every synthetic tool must be discovered, or this proof is itself vacuous");
		failures.Should().ContainSingle(failure => failure.Contains(UndeclaredFixtureTool.ToolName, StringComparison.Ordinal),
			because: "a tool with no catalog entry must be reported");
		failures.Should().ContainSingle(failure => failure.Contains(PartialFixtureTool.ToolName, StringComparison.Ordinal)
				&& failure.Contains(nameof(McpToolFacts.Connections), StringComparison.Ordinal),
			because: "a declaration that leaves a field out must be reported, naming the field");
		failures.Should().Contain(failure => failure.Contains(HintContradictionFixtureTool.ToolName, StringComparison.Ordinal)
				&& failure.Contains("destructiveHint=false", StringComparison.Ordinal),
			because: "a destroy whose hint says non-destructive would hide it from the (D) marker");
		failures.Should().Contain(failure => failure.Contains(WideArgumentFixtureTool.ToolName, StringComparison.Ordinal)
				&& failure.Contains("not one of its arguments", StringComparison.Ordinal),
			because: "an entry on an argument the tool does not take can never match a call");
		failures.Should().Contain(failure => failure.Contains(WideArgumentFixtureTool.ToolName, StringComparison.Ordinal)
				&& failure.Contains("wider than the tool", StringComparison.Ordinal),
			because: "an entry must only narrow, so a host that ignores entries is never less careful than one that reads them");
		failures.Should().NotContain(failure => failure.Contains(DeclaredFixtureTool.ToolName, StringComparison.Ordinal),
			because: "a correct declaration is not a failure, otherwise the routine would prove nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("The host-path check has teeth: over synthetic tools it reports an argument whose name reads like a " +
		"path and is declared neither way, a declaration on an argument the tool does not take, one made twice or both " +
		"ways, an unknown alternative to a path, a not-host-path without its reason, and a host path that leaves out or misspells its kind or access; " +
		"and stays silent about a tool that declares its paths and its look-alike.")]
	public void FactsRoutine_ShouldReportHostPathDefects_WhenRunOverSyntheticTools() {
		// Arrange
		IReadOnlyList<(string Name, McpServerTool Tool, McpToolFacts Facts)> synthetic = ReadSynthetic(
			typeof(HostPathDeclaredFixtureTool),
			typeof(HostPathDefectFixtureTool),
			typeof(HostPathPartialFixtureTool));

		// Act
		IReadOnlyList<string> failures = FindFactsFailures(synthetic);
		string[] defects = [.. failures.Where(failure => failure.Contains(HostPathDefectFixtureTool.ToolName, StringComparison.Ordinal))];

		// Assert
		synthetic.Should().HaveCount(3, because: "every synthetic tool must be discovered, or this proof is itself vacuous");
		defects.Should().Contain(failure => failure.Contains("'outputFile', whose name reads like a path", StringComparison.Ordinal),
			because: "an argument a host would read as a path must be declared one way or the other, so a new tool cannot hide one");
		defects.Should().Contain(failure => failure.Contains("host path 'missing', which is not one of its arguments", StringComparison.Ordinal),
			because: "a host path on an argument the tool does not take can never match a call");
		defects.Should().Contain(failure => failure.Contains("'absent' not a host path, but it is not one of its arguments", StringComparison.Ordinal),
			because: "a not-host-path on an argument the tool does not take is a leftover of a rename");
		defects.Should().Contain(failure => failure.Contains("host path 'twice' twice", StringComparison.Ordinal),
			because: "two declarations of one argument can disagree, and a host would read either");
		defects.Should().Contain(failure => failure.Contains("may instead be 'anything'", StringComparison.Ordinal),
			because: "a host acts on the alternative by its name, so an unknown one tells it nothing");
		defects.Should().Contain(failure => failure.Contains("'both' both a host path and not one", StringComparison.Ordinal),
			because: "an argument is a host path or it is not");
		defects.Should().Contain(failure => failure.Contains("'zipFile' not a host path without saying what it is instead", StringComparison.Ordinal),
			because: "the reason is what a reviewer checks the exemption against");
		failures.Should().ContainSingle(failure => failure.Contains(HostPathPartialFixtureTool.ToolName, StringComparison.Ordinal))
			.Which.Should().Contain("host path outputFile: Access", because: "a host path that leaves its access out says nothing a host can act on")
			.And.Contain("host path source: Kind", because: "a misspelled kind reads back as unspecified instead of a plausible default");
		failures.Should().NotContain(failure => failure.Contains(HostPathDeclaredFixtureTool.ToolName, StringComparison.Ordinal),
			because: "a tool that declares its host paths and its look-alike is correct");
	}

	[Test]
	[Category("Unit")]
	[Description("A catalog entry for a tool clio does not serve is reported, so a renamed or removed tool cannot leave a stale entry.")]
	public void StaleEntryRoutine_ShouldReportAnEntryForAToolClioDoesNotServe() {
		// Arrange
		IReadOnlyDictionary<string, McpToolFacts> catalog = McpToolFactsReader.Parse(SyntheticCatalog);

		// Act
		IReadOnlyList<string> failures = FindStaleEntries(catalog.Keys, [DeclaredFixtureTool.ToolName, PartialFixtureTool.ToolName,
			HintContradictionFixtureTool.ToolName, WideArgumentFixtureTool.ToolName, HostPathDeclaredFixtureTool.ToolName,
			HostPathDefectFixtureTool.ToolName, HostPathPartialFixtureTool.ToolName]);

		// Assert
		failures.Should().ContainSingle(because: "only the stale entry names a tool that is not served")
			.Which.Should().Contain("zz-facts-stale-entry", because: "the failure must name the entry to remove");
	}

	[Test]
	[Category("Unit")]
	[Description("A deprecated tool name registered as its own method declares exactly the facts of the tool it delegates to.")]
	public void MethodLevelAliases_ShouldDeclareTheFactsOfTheirCanonicalTool() {
		// Arrange
		IReadOnlyDictionary<string, McpToolFacts> declared = McpToolFactsReader.Declared;

		// Act & Assert
		foreach ((string alias, string canonical) in MethodLevelAliasPairs) {
			declared.Should().ContainKey(alias, because: $"the alias '{alias}' is still served and needs facts");
			declared.Should().ContainKey(canonical, because: $"the canonical tool '{canonical}' needs facts");
			declared[alias].ToJson().ToJsonString().Should().Be(declared[canonical].ToJson().ToJsonString(),
				because: $"'{alias}' runs the same code as '{canonical}', so a host must treat both the same");
		}
	}

	[McpServerToolType]
	private static class HostPathDeclaredFixtureTool {
		internal const string ToolName = "zz-facts-host-path-declared-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool that declares its host paths, one under a name that does not read like a path, and its look-alike.")]
		public static string Run(string outputFile, string source, string filePath) => outputFile + source + filePath;
	}

	[McpServerToolType]
	private static class HostPathDefectFixtureTool {
		internal const string ToolName = "zz-facts-host-path-defect-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool with an undeclared path-like argument and host-path declarations that are unknown, doubled, contradictory or unexplained.")]
		public static string Run(string outputFile, string both, string twice, string zipFile) => outputFile + both + twice + zipFile;
	}

	[McpServerToolType]
	private static class HostPathPartialFixtureTool {
		internal const string ToolName = "zz-facts-host-path-partial-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool whose host paths leave out an access and misspell a kind.")]
		public static string Run(string outputFile, string source) => outputFile + source;
	}

	[McpServerToolType]
	private static class DeclaredFixtureTool {
		internal const string ToolName = "zz-facts-declared-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Correctly declared synthetic tool.")]
		public static string Run(string action) => action;
	}

	[McpServerToolType]
	private static class UndeclaredFixtureTool {
		internal const string ToolName = "zz-facts-undeclared-tool";

		[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool with no catalog entry.")]
		public static string Run() => "undeclared";
	}

	[McpServerToolType]
	private static class PartialFixtureTool {
		internal const string ToolName = "zz-facts-partial-tool";

		[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool that leaves Connections out.")]
		public static string Run() => "partial";
	}

	[McpServerToolType]
	private static class HintContradictionFixtureTool {
		internal const string ToolName = "zz-facts-hint-contradiction-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic destroy whose hint says it is not destructive.")]
		public static string Run() => "contradiction";
	}

	[McpServerToolType]
	private static class WideArgumentFixtureTool {
		internal const string ToolName = "zz-facts-wide-argument-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool whose argument entries are unknown or wider than the tool.")]
		public static string Run(string mode) => mode;
	}
}
