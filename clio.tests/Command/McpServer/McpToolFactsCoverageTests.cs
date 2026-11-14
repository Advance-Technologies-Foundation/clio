using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Keeps every MCP tool saying what it does (<see cref="McpToolFactsAttribute"/>), so a host can treat a call by
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

	// Every enabled tool with its declared facts (null when it declares none), discovered by the production readers.
	private static IReadOnlyList<(string Name, McpServerTool Tool, McpToolFacts Facts)> ReadCatalog() {
		McpToolInvokerRegistry registry = BuildRegistryOverFullCatalog();
		IReadOnlyDictionary<string, McpToolFacts> declared = McpToolFactsReader.ReadDeclaredFactsOrNull(
			McpFeatureToggleFilter.GetAttributedTypes(ProductionAssembly, typeof(McpServerToolTypeAttribute)));
		return [.. registry.ToolNames
			.OrderBy(name => name, StringComparer.Ordinal)
			.Select(name => {
				registry.TryGetTool(name, out McpServerTool tool);
				return (name, tool, declared.TryGetValue(name, out McpToolFacts facts) ? facts : null);
			})];
	}

	// The same triples for synthetic tool types: static methods, so the SDK builds them without a target.
	private static IReadOnlyList<(string Name, McpServerTool Tool, McpToolFacts Facts)> ReadSynthetic(params Type[] types) {
		return [.. types
			.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
			.Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
			.Select(method => {
				McpServerTool tool = McpServerTool.Create(method, target: null);
				return (tool.ProtocolTool.Name, tool, McpToolFactsReader.Read(method));
			})];
	}

	#region Checking routine (runs against the real catalog AND against synthetic tools)

	/// <summary>One readable failure per problem; empty when every tool's facts are complete and consistent.</summary>
	internal static IReadOnlyList<string> FindFactsFailures(
		IEnumerable<(string Name, McpServerTool Tool, McpToolFacts Facts)> tools) {
		List<string> failures = [];
		foreach ((string name, McpServerTool tool, McpToolFacts facts) in tools) {
			if (facts is null) {
				failures.Add($"Tool '{name}' declares no [McpToolFacts]: a host cannot tell what a call to it does.");
				continue;
			}
			if (facts.UnspecifiedFieldNames.Count > 0) {
				failures.Add($"Tool '{name}' leaves {string.Join(", ", facts.UnspecifiedFieldNames)} unspecified.");
				continue;
			}
			failures.AddRange(FindHintFailures(name, tool, facts));
			failures.AddRange(FindConsistencyFailures(name, facts.Effect, facts.EnvironmentImpact, facts.Connections, string.Empty));
			failures.AddRange(FindArgumentFailures(name, tool, facts));
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
		"and narrows its facts only by its own arguments and only downwards. A new tool without [McpToolFacts] fails here.")]
	public void EveryEnabledTool_ShouldDeclareCompleteAndConsistentFacts() {
		// Arrange
		IReadOnlyList<(string Name, McpServerTool Tool, McpToolFacts Facts)> catalog = ReadCatalog();

		// Act
		IReadOnlyList<string> failures = FindFactsFailures(catalog);

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
			because: "a tool with no [McpToolFacts] must be reported");
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
	private static class DeclaredFixtureTool {
		internal const string ToolName = "zz-facts-declared-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true)]
		[System.ComponentModel.Description("Correctly declared synthetic tool.")]
		[McpToolFacts(Effect = McpToolEffect.Destroy, EnvironmentImpact = McpToolEnvironmentImpact.None,
			Scope = McpToolScope.Environment, Connections = McpToolConnections.None)]
		[McpToolArgumentFacts("action", "list", Effect = McpToolEffect.Read)]
		public static string Run(string action) => action;
	}

	[McpServerToolType]
	private static class UndeclaredFixtureTool {
		internal const string ToolName = "zz-facts-undeclared-tool";

		[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool with no [McpToolFacts].")]
		public static string Run() => "undeclared";
	}

	[McpServerToolType]
	private static class PartialFixtureTool {
		internal const string ToolName = "zz-facts-partial-tool";

		[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool that leaves Connections out.")]
		[McpToolFacts(Effect = McpToolEffect.Read, EnvironmentImpact = McpToolEnvironmentImpact.None, Scope = McpToolScope.Local)]
		public static string Run() => "partial";
	}

	[McpServerToolType]
	private static class HintContradictionFixtureTool {
		internal const string ToolName = "zz-facts-hint-contradiction-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic destroy whose hint says it is not destructive.")]
		[McpToolFacts(Effect = McpToolEffect.Destroy, EnvironmentImpact = McpToolEnvironmentImpact.None,
			Scope = McpToolScope.Environment, Connections = McpToolConnections.None)]
		public static string Run() => "contradiction";
	}

	[McpServerToolType]
	private static class WideArgumentFixtureTool {
		internal const string ToolName = "zz-facts-wide-argument-tool";

		[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false)]
		[System.ComponentModel.Description("Synthetic tool whose argument entries are unknown or wider than the tool.")]
		[McpToolFacts(Effect = McpToolEffect.Change, EnvironmentImpact = McpToolEnvironmentImpact.None,
			Scope = McpToolScope.Environment, Connections = McpToolConnections.None)]
		[McpToolArgumentFacts("mode", "wipe", Effect = McpToolEffect.Destroy)]
		[McpToolArgumentFacts("missing", "x", Effect = McpToolEffect.Read)]
		public static string Run(string mode) => mode;
	}
}
