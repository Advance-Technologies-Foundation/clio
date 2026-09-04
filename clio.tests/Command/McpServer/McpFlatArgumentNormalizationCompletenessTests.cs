using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95885 completeness gate for flat-argument normalization. Instead of asserting behavior for a
/// hand-picked handful of tools (which drifts the moment the resident profile changes), these cases
/// derive the tool set at runtime from <see cref="McpCoreToolProfile"/> and assert the classifier's two
/// load-bearing invariants across EVERY resident tool that takes a single composite <c>args</c> record.
/// </summary>
/// <remarks>
/// The dangerous invariant is the second one. Most resident args records carry no
/// <c>[JsonExtensionData]</c> overflow bag, so wrapping an unknown-only payload would let the serializer
/// drop the unknown key, materialize the record with defaults, and let the tool answer a validation
/// mistake with a plausible-but-wrong list/default SUCCESS. A per-tool test would cover today's tools;
/// this one also covers the tools added after this change.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpFlatArgumentNormalizationCompletenessTests
{
	// Mirrors McpCoreToolProfile.BuildResidentToolNames so residency here never diverges from what the
	// SDK's own WithTools(types) scan registers.
#pragma warning disable S3011
	private const BindingFlags ToolMethodFlags = BindingFlags.Public | BindingFlags.NonPublic |
		BindingFlags.Instance | BindingFlags.Static;
#pragma warning restore S3011

	[Test]
	[Category("Unit")]
	[Description("The resident tool set actually yields single-composite-args tools to normalize, so a profile change that empties this set fails loudly instead of silently making the completeness cases vacuous.")]
	public void ResidentSingleCompositeArgsTools_ShouldNotBeEmpty() {
		// Act
		IReadOnlyList<ResidentToolContract> contracts = EnumerateResidentSingleCompositeArgsTools();

		// Assert
		contracts.Should().NotBeEmpty(
			because: "normalization targets resident single-composite-args tools; an empty set would make "
				+ "every assertion below pass without testing anything");
		contracts.Select(contract => contract.ToolName).Should().Contain("list-apps",
			because: "list-apps is the canonical example from the ENG-95885 field-test data and must be in scope");
	}

	[Test]
	[Category("Unit")]
	[Description("A canonical flat payload is normalized into the wrapped shape for EVERY resident single-composite-args tool — the core contract ENG-95885 exists to deliver (R1).")]
	public void CanonicalFlatPayload_ShouldBeNormalized_ForEveryResidentSingleCompositeArgsTool() {
		// Arrange
		List<string> failures = [];

		// Act
		foreach (ResidentToolContract contract in EnumerateResidentSingleCompositeArgsTools()) {
			string canonicalName = contract.CanonicalPropertyNames[0];
			CallToolRequestParams parameters = new() {
				Name = contract.ToolName,
				Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
					[canonicalName] = JsonSerializer.SerializeToElement("probe-value")
				}
			};

			bool refused = McpToolErrorFilter.TryRefuseOrRewriteArguments(
				parameters, contract.Method, out CallToolResult? result);

			if (refused || result is not null) {
				failures.Add($"{contract.ToolName}: a canonical flat '{canonicalName}' payload was refused");
				continue;
			}
			if (parameters.Arguments is not { Count: 1 }
				|| !parameters.Arguments.TryGetValue(contract.WrapperName, out JsonElement wrapped)
				|| wrapped.ValueKind != JsonValueKind.Object
				|| !wrapped.TryGetProperty(canonicalName, out JsonElement moved)
				|| moved.GetString() != "probe-value") {
				failures.Add(
					$"{contract.ToolName}: a canonical flat '{canonicalName}' payload was not moved into "
					+ $"the '{contract.WrapperName}' wrapper");
			}
		}

		// Assert
		failures.Should().BeEmpty(
			because: "a fresh-context agent's canonical flat call must succeed on the first attempt for every "
				+ $"resident single-composite-args tool. Failures:{Environment.NewLine}"
				+ string.Join(Environment.NewLine, failures));
	}

	[Test]
	[Category("Unit")]
	[Description("An unknown-only payload is REFUSED for every resident single-composite-args tool whose args record has no [JsonExtensionData] overflow bag, so a validation mistake can never be answered with a defaulted record (ENG-95885 R2, RISK2).")]
	public void UnknownOnlyPayload_ShouldBeRefused_ForEveryArgsRecordWithoutOverflowBucket() {
		// Arrange
		List<string> failures = [];
		int covered = 0;

		// Act
		foreach (ResidentToolContract contract in EnumerateResidentSingleCompositeArgsTools()) {
			if (HasJsonExtensionDataBucket(contract.ArgsType)) {
				// The tool can SEE the unknown key; whether it is forwarded is then an explicit, declared
				// per-tool decision (McpRecoversUnknownArguments), covered by McpToolErrorFilterTests.
				continue;
			}
			covered++;
			CallToolRequestParams parameters = new() {
				Name = contract.ToolName,
				Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
					["definitely-not-a-real-argument"] = JsonSerializer.SerializeToElement("x")
				}
			};

			bool refused = McpToolErrorFilter.TryRefuseOrRewriteArguments(
				parameters, contract.Method, out CallToolResult? result);

			if (!refused || result is null || result.IsError != true) {
				failures.Add(
					$"{contract.ToolName} (args {contract.ArgsType.Name}, no overflow bag): an unknown-only "
					+ "payload was not refused, so the record can materialize with defaults");
			}
		}

		// Assert
		covered.Should().BeGreaterThan(0,
			because: "the no-overflow-bag population is the whole point of this gate; if it is empty the "
				+ "reflection filter is wrong, not the codebase");
		failures.Should().BeEmpty(
			because: "a plausible-but-wrong success is worse for an agent than a hard failure. Failures:"
				+ $"{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
	}

	[Test]
	[Category("Unit")]
	[Description("A PARTIAL-unknown payload — one canonical field beside a typo — is REFUSED for every resident single-composite-args tool whose args record has no [JsonExtensionData] overflow bag, so the good field never makes the typo safe by letting the serializer drop it into a plausible success (ENG-95885 R2, partial-unknown hole).")]
	public void PartialUnknownPayload_ShouldBeRefused_ForEveryArgsRecordWithoutOverflowBucket() {
		// Arrange
		List<string> failures = [];
		int covered = 0;

		// Act
		foreach (ResidentToolContract contract in EnumerateResidentSingleCompositeArgsTools()) {
			if (HasJsonExtensionDataBucket(contract.ArgsType)) {
				// The record can SEE the unknown key, so forwarding is a declared per-tool decision
				// (McpRecoversUnknownArguments) — not this gate's concern.
				continue;
			}
			covered++;
			CallToolRequestParams parameters = new() {
				Name = contract.ToolName,
				Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
					[contract.CanonicalPropertyNames[0]] = JsonSerializer.SerializeToElement("real"),
					["definitely-not-a-real-argument"] = JsonSerializer.SerializeToElement("typo")
				}
			};

			bool refused = McpToolErrorFilter.TryRefuseOrRewriteArguments(
				parameters, contract.Method, out CallToolResult? result);

			if (!refused || result is null || result.IsError != true) {
				failures.Add(
					$"{contract.ToolName} (args {contract.ArgsType.Name}, no overflow bag): a canonical field "
					+ "next to a typo was not refused, so the serializer can silently drop the typo");
			}
		}

		// Assert
		covered.Should().BeGreaterThan(0,
			because: "the no-overflow-bag population is the whole point of this gate; an empty set means the "
				+ "reflection filter is wrong");
		failures.Should().BeEmpty(
			because: "a real field beside a typo must not be rescued into a defaulted success. Failures:"
				+ $"{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
	}

	[Test]
	[Category("Unit")]
	[Description("No resident single-composite-args tool exposes a wire field whose name equals its own args-wrapper parameter name, so the already-wrapped classification (keyed on the wrapper name) can never be fooled into treating a flat field as the SDK wrapper (ENG-95885 latent-collision guard).")]
	public void NoResidentToolHasWireFieldNamedLikeItsWrapper() {
		// Arrange
		List<string> collisions = [];

		// Act
		foreach (ResidentToolContract contract in EnumerateResidentSingleCompositeArgsTools()) {
			if (contract.CanonicalPropertyNames.Contains(contract.WrapperName, StringComparer.Ordinal)) {
				collisions.Add($"{contract.ToolName}: wire field named '{contract.WrapperName}'");
			}
		}

		// Assert
		collisions.Should().BeEmpty(
			because: "a wire field whose name equals the wrapper parameter name would make a legitimate flat "
				+ "call read as an already-wrapped pass-through (or be misrefused as ambiguous). Collisions:"
				+ $"{Environment.NewLine}{string.Join(Environment.NewLine, collisions)}");
	}

	[Test]
	[Category("Unit")]
	[Description("An empty payload is refused (left to today's missing-parameter behavior) for every resident tool that has NOT declared no-arguments capability — the capability is fail-closed and never inferred from the schema (ENG-95885 R3).")]
	public void EmptyPayload_ShouldStayUntouched_ForEveryToolWithoutDeclaredCapability() {
		// Arrange
		List<string> failures = [];

		// Act
		foreach (ResidentToolContract contract in EnumerateResidentSingleCompositeArgsTools()) {
			if (contract.Method.GetCustomAttribute<McpAcceptsEmptyArgumentsAttribute>() is not null) {
				continue;
			}
			Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal);
			CallToolRequestParams parameters = new() { Name = contract.ToolName, Arguments = arguments };

			McpToolErrorFilter.TryRefuseOrRewriteArguments(parameters, contract.Method, out CallToolResult? _);

			if (!ReferenceEquals(parameters.Arguments, arguments)) {
				failures.Add(
					$"{contract.ToolName}: an empty payload was rewritten without the tool declaring "
					+ $"[{nameof(McpAcceptsEmptyArgumentsAttribute)}]");
			}
		}

		// Assert
		failures.Should().BeEmpty(
			because: "no-arguments capability must be an explicit declaration. Failures:"
				+ $"{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
	}

	[Test]
	[Category("Unit")]
	[Description("Every tool that declares it recovers unknown arguments itself really does bind a [JsonExtensionData] overflow bag — otherwise the forwarded unknown key would be dropped by the serializer and the declaration would reintroduce the default-success failure mode it is meant to avoid (ENG-95885).")]
	public void UnknownRecoveryDeclarations_ShouldOnlyExistOnArgsRecordsWithOverflowBucket() {
		// Arrange
		List<string> failures = [];
		int declarations = 0;

		// Act
		foreach (ResidentToolContract contract in EnumerateResidentSingleCompositeArgsTools()) {
			if (contract.Method.GetCustomAttribute<McpRecoversUnknownArgumentsAttribute>() is null) {
				continue;
			}
			declarations++;
			if (!HasJsonExtensionDataBucket(contract.ArgsType)) {
				failures.Add(
					$"{contract.ToolName} declares [{nameof(McpRecoversUnknownArgumentsAttribute)}] but its "
					+ $"args record {contract.ArgsType.Name} has no [JsonExtensionData] property, so a "
					+ "forwarded unknown key would be silently dropped");
			}
		}

		// Assert
		declarations.Should().BeGreaterThan(0,
			because: "get-tool-contract carries this declaration; losing it would silently re-open the "
				+ "flat-name-only defect where the full tool index came back as a success");
		failures.Should().BeEmpty(
			because: "the declaration is only safe where the tool can actually see the unknown key. Failures:"
				+ $"{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
	}

	[Test]
	[Category("Unit")]
	[Description("The tools declared as accepting an empty payload are exactly the ones ENG-95885 scoped, so widening that set stays a deliberate, reviewed decision rather than a drive-by attribute.")]
	public void NoArgumentsDeclarations_ShouldCoverExactlyTheScopedTools() {
		// Act
		IEnumerable<string> declared = EnumerateResidentSingleCompositeArgsTools()
			.Where(contract => contract.Method.GetCustomAttribute<McpAcceptsEmptyArgumentsAttribute>() is not null)
			.Select(contract => contract.ToolName);

		// Assert
		declared.Should().BeEquivalentTo(
			["list-apps", "get-request-info"],
			because: "an empty payload must only be accepted where calling with no arguments has a documented, "
				+ "useful meaning — every other tool keeps its missing-parameter error");
	}

	private static bool HasJsonExtensionDataBucket(Type argsType) =>
		argsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Any(property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null);

	/// <summary>
	/// Every resident MCP tool method whose single bindable parameter is a composite args record with at
	/// least one wire-contract property — the exact population the normalizer targets.
	/// </summary>
	private static IReadOnlyList<ResidentToolContract> EnumerateResidentSingleCompositeArgsTools() {
		List<ResidentToolContract> contracts = [];
		// The resident set is CoreToolTypes ∪ AlwaysOnLazyToolTypes — exactly the union that backs
		// McpCoreToolProfile.ResidentToolNames, so this population tracks the profile instead of a
		// hardcoded list or count that would silently stop covering newly-resident tools.
		HashSet<Type> residentToolTypes = new(McpCoreToolProfile.CoreToolTypes);
		residentToolTypes.UnionWith(McpCoreToolProfile.AlwaysOnLazyToolTypes);
		foreach (Type toolType in residentToolTypes) {
			foreach (MethodInfo method in toolType.GetMethods(ToolMethodFlags)) {
				string? toolName = method.GetCustomAttribute<McpServerToolAttribute>()?.Name;
				if (string.IsNullOrWhiteSpace(toolName)) {
					continue;
				}
				if (!McpToolArgumentSupport.TryGetSingleCompositeParameter(method, out ParameterInfo? parameter)) {
					continue;
				}
				List<string> canonicalNames = GetWireContractPropertyNames(parameter.ParameterType);
				if (canonicalNames.Count == 0) {
					continue;
				}
				contracts.Add(new ResidentToolContract(
					toolName!,
					method,
					parameter.ParameterType,
					parameter.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? parameter.Name!,
					canonicalNames));
			}
		}
		return contracts;
	}

	// Mirrors McpToolErrorFilter's own reflection of the wire contract: [JsonExtensionData] buckets and
	// always-ignored properties are not caller-supplied arguments.
	private static List<string> GetWireContractPropertyNames(Type argsType) =>
		argsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is null
				&& property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
			.Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name)
			.ToList();

	private sealed record ResidentToolContract(
		string ToolName,
		MethodInfo Method,
		Type ArgsType,
		string WrapperName,
		IReadOnlyList<string> CanonicalPropertyNames);
}
