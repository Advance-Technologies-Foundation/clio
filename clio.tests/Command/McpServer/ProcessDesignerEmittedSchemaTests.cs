using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Guards the emitted MCP input schemas of the process-designer tools, so that an argument the contract
/// advertises as one-of-several or as optional is not advertised to a strict client as mandatory.
/// </summary>
/// <remarks>
/// Same mechanism as <see cref="ColumnIdentityEmittedSchemaTests" />: the SDK derives the emitted
/// <c>required</c> array from the bound record's non-nullable, NON-DEFAULTED positional parameters, so the
/// reliable lever is the parameter DEFAULT, not the <c>?</c> annotation alone (clio compiles with the nullable
/// context off, so annotations carry no metadata the generator can rely on) and not
/// <see cref="System.ComponentModel.DataAnnotations.RequiredAttribute" />, which does not feed the array.
/// <para>
/// This has regressed once already: <c>modify-business-process</c> shipped <c>process-name</c> and
/// <c>process-uid</c> as non-nullable positional parameters while its own tool body mapped them with
/// <c>?? string.Empty</c> and refused a payload carrying both — so a client that obeyed the contract and sent
/// exactly one was rejected client-side, before clio ever saw the call. These assertions read the schema
/// through the real registry on the production serializer options, so a half-landed relaxation fails here.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ProcessDesignerEmittedSchemaTests {

	private static McpToolInvokerRegistry BuildProductionRegistry() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		// All toggles report enabled so the registry mirrors the full production catalog. The
		// process-designer tools themselves ship gate-free since go-live (ENG-96132); the blanket
		// substitute just keeps unrelated gated tools from perturbing the scan.
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		return new McpToolInvokerRegistry(
			provider,
			typeof(SchemaSyncTool).Assembly,
			featureToggle,
			BindingsModule.CreateMcpSerializerOptions());
	}

	private static JsonDocument EmittedInputSchema(string toolName) {
		McpToolInvokerRegistry registry = BuildProductionRegistry();
		registry.TryGetTool(toolName, out McpServerTool tool).Should().BeTrue(
			because: $"'{toolName}' must be a registered tool for its emitted schema to be assertable");
		return JsonDocument.Parse(JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));
	}

	/// <summary>The <c>args</c> envelope schema, which is where every tool argument is declared.</summary>
	private static JsonElement ArgsSchema(JsonDocument schema) {
		return schema.RootElement.GetProperty("properties").GetProperty("args");
	}

	private static List<string> RequiredNames(JsonElement objectSchema) {
		List<string> names = [];
		if (!objectSchema.TryGetProperty("required", out JsonElement required)
			|| required.ValueKind != JsonValueKind.Array) {
			return names;
		}
		foreach (JsonElement item in required.EnumerateArray()) {
			names.Add(item.GetString() ?? string.Empty);
		}
		return names;
	}

	private static void ShouldAdvertise(JsonElement argsSchema, string wireName) {
		argsSchema.GetProperty("properties").TryGetProperty(wireName, out _).Should().BeTrue(
			because: $"'{wireName}' must stay advertised - a field dropped from the schema is as unusable to a " +
				"strict client as one wrongly demanded");
	}

	/// <summary>Collects every <c>required</c> entry from a schema and every schema nested inside it.</summary>
	private static List<string> CollectRequiredNames(JsonElement node) {
		List<string> names = [];
		switch (node.ValueKind) {
			case JsonValueKind.Object:
				foreach (JsonProperty property in node.EnumerateObject()) {
					if (property.NameEquals("required") && property.Value.ValueKind == JsonValueKind.Array) {
						names.AddRange(RequiredNames(node));
						continue;
					}
					names.AddRange(CollectRequiredNames(property.Value));
				}
				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in node.EnumerateArray()) {
					names.AddRange(CollectRequiredNames(item));
				}
				break;
		}
		return names;
	}

	[Test]
	[Category("Unit")]
	[Description("validate-process-graph's NESTED node and edge item schemas demand nothing, so the tool can report a graph's own missing fields as findings instead of having the payload rejected client-side.")]
	public void ValidateProcessGraph_Should_NotRequireAnythingOnNestedGraphItems_InEmittedSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(ValidateProcessGraphTool.ToolName);
		JsonElement args = ArgsSchema(schema);

		// Assert - the sibling tools' arguments live on the args object, but a graph arrives as arrays of
		// objects, so the required arrays that matter here are NESTED under nodes/items and edges/items and are
		// invisible to an args-level check (the same blind spot ColumnIdentityEmittedSchemaTests exists for).
		foreach (string wireName in new[] { "name", "type", "source", "target", "flow-kind" }) {
			CollectRequiredNames(args).Should().NotContain(wireName,
				because: $"'{wireName}' is a field of the graph UNDER VALIDATION - the tool's whole job is to " +
					"report it missing as a finding, which it cannot do if a client refuses to send the call");
		}

		// Anti-vacuity: the args object itself must still demand the environment, which proves this recursive
		// collection reaches real required arrays rather than silently finding none.
		RequiredNames(args).Should().Contain("environment-name",
			because: "validate-process-graph resolves element types server-side, so it needs an environment");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process must not advertise either process-name or process-uid as required: they are mutually exclusive alternatives, and the tool itself refuses a payload that carries both.")]
	public void ModifyBusinessProcess_Should_NotRequireEitherProcessIdentity_InEmittedSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(ModifyBusinessProcessTool.ModifyBusinessProcessToolName);
		JsonElement args = ArgsSchema(schema);
		List<string> required = RequiredNames(args);

		// Assert - navigate the `required` array rather than substring-matching the serialized schema, which
		// would pass vacuously as soon as ordering or whitespace changed.
		ShouldAdvertise(args, "process-name");
		ShouldAdvertise(args, "process-uid");
		required.Should().NotContain("process-name",
			because: "the contract is 'exactly one of process-name or process-uid', so demanding this one makes " +
				"a payload that legitimately sends only process-uid fail client-side schema validation");
		required.Should().NotContain("process-uid",
			because: "requiring the other identity instead would reproduce the same defect mirrored - and " +
				"requiring both makes every contract-following payload unsendable");

		// Anti-vacuity: the set must not be empty, or the negatives above would hold for a schema that
		// advertises nothing as required and the guard would prove nothing.
		required.Should().Contain("environment-name",
			because: "environment-name is unconditionally mandatory, which proves the required array is " +
				"populated and actually being read by these assertions");
		required.Should().Contain("operations",
			because: "operations is the payload the tool cannot run without, so it stays mandatory");
	}

	[Test]
	[Category("Unit")]
	[Description("describe-business-process must not advertise any of its three alternative identities, nor the optional culture, as required in its emitted schema.")]
	public void DescribeBusinessProcess_Should_NotRequireAnyProcessIdentityOrCulture_InEmittedSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(DescribeProcessTool.ToolName);
		JsonElement args = ArgsSchema(schema);
		List<string> required = RequiredNames(args);

		// Assert
		foreach (string identity in new[] { "process-name", "process-uid", "process-caption" }) {
			ShouldAdvertise(args, identity);
			required.Should().NotContain(identity,
				because: $"'{identity}' is one of three accepted identities, so a client sending exactly one of " +
					"the other two must not be blocked before the call reaches clio");
		}

		ShouldAdvertise(args, "culture");
		required.Should().NotContain("culture",
			because: "the tool defaults culture to en-US, and its own description calls it optional");

		// Anti-vacuity, as above.
		required.Should().Contain("environment-name",
			because: "environment-name remains unconditionally mandatory on this surface");
	}

	[Test]
	[Category("Unit")]
	[Description("create-business-process must not advertise package-name as required: its own description calls it an optional override of the descriptor's packageName.")]
	public void CreateBusinessProcess_Should_NotRequirePackageName_InEmittedSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(CreateBusinessProcessTool.CreateBusinessProcessToolName);
		JsonElement args = ArgsSchema(schema);
		List<string> required = RequiredNames(args);

		// Assert
		ShouldAdvertise(args, "package-name");
		required.Should().NotContain("package-name",
			because: "package-name only OVERRIDES the descriptor's packageName, so a descriptor that already " +
				"names its package is a complete payload without it");

		// Anti-vacuity, as above.
		required.Should().Contain("environment-name",
			because: "environment-name remains unconditionally mandatory on this surface");
		required.Should().Contain("descriptor",
			because: "the descriptor is the process definition itself, so it stays mandatory");
	}

	[Test]
	[Category("Unit")]
	[Description("get-process-signature keeps process-name required, because it is that tool's single identity argument rather than one of several alternatives - relaxing it would be a different defect.")]
	public void GetProcessSignature_Should_KeepItsSoleIdentityRequired_InEmittedSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(GetProcessSignatureTool.ToolName);
		JsonElement args = ArgsSchema(schema);

		// Assert
		RequiredNames(args).Should().Contain("process-name",
			because: "process-name is the ONLY way to identify a process on this surface, so it is genuinely " +
				"mandatory; this pins the boundary of the relaxation applied to the sibling tools");
	}

	[Test]
	[Category("Unit")]
	[Description("run-process demands only process-name: parameters, result-parameters, timeout and environment-name are all legitimately absent from a valid payload, so a strict client must not be forced to send them.")]
	public void RunProcess_Should_RequireOnlyTheProcessName_InEmittedSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(RunProcessTool.ToolName);
		JsonElement args = ArgsSchema(schema);
		List<string> required = RequiredNames(args);

		// Assert
		required.Should().Contain("process-name",
			because: "process-name is the only way to identify the process to launch");
		foreach (string optional in new[] { "parameters", "result-parameters", "timeout", "environment-name" }) {
			ShouldAdvertise(args, optional);
			required.Should().NotContain(optional,
				because: $"a process can legitimately be launched without '{optional}' - a parameterless " +
					"process, a process with no outputs, an unbounded request, and the direct-connection " +
					"fallback are each a complete payload without it");
		}
	}

	[TestCase(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
	[TestCase(ModifyBusinessProcessTool.ModifyBusinessProcessToolName)]
	[TestCase(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName)]
	[Category("Unit")]
	[Description("Every process WRITE tool's emitted description names the multi-instance member by its exact path, subProcess.multiInstanceOptions, and never by the one-word-short subProcess.multiInstance. A live agent reached for subProcess.multiInstance on a stand because the contract named no member, and an older server silently DISCARDS an unknown member while answering success - so the wrong name builds a plain sub-process and nothing says so. The payload budget in ToolContractPayloadBudgetTests pushes the next author toward deleting exactly this text, which is why it is pinned rather than trusted.")]
	public void ProcessWriteTools_Should_NameTheMultiInstanceOptionsMember_InEmittedDescription(string toolName) {
		// Arrange
		McpToolInvokerRegistry registry = BuildProductionRegistry();
		registry.TryGetTool(toolName, out McpServerTool tool).Should().BeTrue(
			because: $"'{toolName}' must be a registered tool for its emitted description to be assertable");

		// Act
		string description = tool.ProtocolTool.Description ?? string.Empty;

		// Assert
		description.Should().Contain("subProcess.multiInstanceOptions",
			because: "the member has to be named by its exact path in the contract the agent actually receives - "
				+ "the guidance that also names it ships on a separate release train");
		Regex.IsMatch(description, @"subProcess\.multiInstance(?!Options)", RegexOptions.None,
				TimeSpan.FromSeconds(1))
			.Should().BeFalse(
				because: "subProcess.multiInstance is the READ-side flag describe reports, not a write member - "
					+ "naming it on a write tool is the exact mistake a live agent made, and an older server "
					+ "discards an unknown member silently");
	}

	[TestCase(CreateBusinessProcessTool.CreateBusinessProcessToolName, "elementParameter.column")]
	[TestCase(ModifyBusinessProcessTool.ModifyBusinessProcessToolName, "elementParameter {elementName, parameter, column?}")]
	[Category("Unit")]
	[Description("The create and modify tools' emitted descriptions name the record-column source fields - sourceColumn beside the element source, and the filter's elementParameter column. The description is the agent's only contract for them, and ToolContractPayloadBudgetTests bounds only its size, so a trim for budget could remove the names with the suite green.")]
	public void ProcessWriteTools_Should_NameTheRecordColumnSourceFields_InEmittedDescription(string toolName,
			string filterColumnForm) {
		// Arrange
		McpToolInvokerRegistry registry = BuildProductionRegistry();
		registry.TryGetTool(toolName, out McpServerTool tool).Should().BeTrue(
			because: $"'{toolName}' must be a registered tool for its emitted description to be assertable");

		// Act
		string description = tool.ProtocolTool.Description ?? string.Empty;

		// Assert
		description.Should().Contain("sourceColumn",
			because: "the mapping / value / recordId column source is named only here for the agent");
		description.Should().Contain(filterColumnForm,
			because: "the filter's column source has to be named too, in the form this tool documents");
	}

	[TestCase(CreateBusinessProcessTool.CreateBusinessProcessToolName, "descriptor", true)]
	[TestCase(ModifyBusinessProcessTool.ModifyBusinessProcessToolName, "operations", true)]
	[TestCase(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName, "operations", false)]
	[Category("Unit")]
	[Description("The JSON-document argument of each process write tool is emitted with its description intact, keeps its required status, and is no longer typed as a string (ENG-100153). The SDK drops the description of an optional positional JsonElement? = null, and for these non-resident tools that description is the only one an agent reads.")]
	public void ProcessWriteTools_Should_AdvertiseTheirJsonDocumentArgument_AsAValueWithADescription(
			string toolName, string wireName, bool required) {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(toolName);
		JsonElement args = ArgsSchema(schema);
		JsonElement property = args.GetProperty("properties").GetProperty(wireName);

		// Assert
		property.TryGetProperty("description", out JsonElement description).Should().BeTrue(
			because: $"'{wireName}' must carry its description - it says both accepted forms, and a derived "
				+ "contract has nothing else to show the agent");
		description.GetString().Should().Contain("string holding the same JSON is also accepted",
			because: "the description must say that the long-standing string form still works");
		property.TryGetProperty("type", out JsonElement type).Should().BeFalse(
			because: $"a 'type' on '{wireName}' would restrict it to one JSON kind, and it accepts two: the value and a "
				+ $"string holding it (emitted: {type})");
		RequiredNames(args).Contains(wireName).Should().Be(required,
			because: required
				? $"'{wireName}' is the edit itself on this tool, so it stays mandatory"
				: "absent operations is the documented snapshot form, so the argument must stay optional");
	}

	[TestCase(CreateBusinessProcessTool.CreateBusinessProcessToolName, "descriptor")]
	[TestCase(ModifyBusinessProcessTool.ModifyBusinessProcessToolName, "operations")]
	[TestCase(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName, "operations")]
	[Category("Unit")]
	[Description("The contract get-tool-contract derives for a process write tool types its JSON-document argument as 'any', not 'object': the operations argument is an ARRAY, and the typeless schema used to be reported as an object (ENG-100153).")]
	public void DerivedContract_Should_TypeTheJsonDocumentArgument_AsAny(string toolName, string wireName) {
		// Arrange
		ToolContractGetTool tool = new(BuildProductionRegistry());

		// Act - detail "full": the contract under test is the complete one. Today detail is ignored for a named
		// lookup; once ENG-100154 lands, a default named lookup is fitted to one inline reply and shortens long
		// field descriptions, and "full" keeps this test reading the whole field.
		ToolContractDefinition contract = tool.GetToolContracts(
			new ToolContractGetArgs([toolName], "full")).Tools!.Single();
		ToolContractField field = contract.InputSchema.Properties.Single(property => property.Name == wireName);

		// Assert
		field.Type.Should().Be(McpToolRegistrySchemaContract.AnyType,
			because: $"'{wireName}' accepts the JSON value or a string holding it; 'object' would tell the caller the "
				+ "operations array is an object, and 'string' would restate the pre-ENG-100153 contract");
		field.Description.Should().Contain("string holding the same JSON is also accepted",
			because: "the derived contract carries the emitted description, which names both accepted forms");
	}

	[TestCase(CreateBusinessProcessTool.CreateBusinessProcessToolName, "descriptor")]
	[TestCase(ModifyBusinessProcessTool.ModifyBusinessProcessToolName, "operations")]
	[TestCase(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName, "operations")]
	[Category("Unit")]
	[Description("With no invoker registry, get-tool-contract falls back to the contract reflected from the args record, and that fallback types the JSON-document argument as 'any' too - the answer the registry-derived contract gives - rather than 'object' (ENG-100153).")]
	public void ReflectedFallbackContract_Should_TypeTheJsonDocumentArgument_AsAny(string toolName, string wireName) {
		// Arrange - no registry: the degraded path that resolves an uncurated tool through McpToolSchemaCatalog.
		ToolContractGetTool tool = new();

		// Act
		ToolContractDefinition contract = tool.GetToolContracts(
			new ToolContractGetArgs([toolName], "full")).Tools!.Single();
		ToolContractField field = contract.InputSchema.Properties.Single(property => property.Name == wireName);

		// Assert
		contract.Description.Should().Contain("Auto-generated from the tool input schema",
			because: "the contract under test must come from the reflection fallback, not from a registry");
		field.Type.Should().Be(McpToolRegistrySchemaContract.AnyType,
			because: $"both contract builders must type '{wireName}' the same way, and it accepts the JSON value or a "
				+ "string holding it - 'object' would tell the caller the operations array is an object");
		field.Description.Should().Contain("string holding the same JSON is also accepted",
			because: "the fallback reads the same member description, which names both accepted forms");
	}
}
