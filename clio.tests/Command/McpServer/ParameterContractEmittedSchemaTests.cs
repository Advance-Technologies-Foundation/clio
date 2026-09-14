using System;
using System.Text.Json;
using Clio;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Guards the EMITTED MCP input schemas for the parameter-contract relaxations of issues #1297 and #1305, so a
/// schema-validating client can actually send the calls the curated <c>get-tool-contract</c> text advertises.
/// </summary>
/// <remarks>
/// The MCP SDK derives the emitted <c>required</c> array from the bound record's positional parameters that carry
/// NO default — nullability alone does not remove a field from it. So `string? Body` without `= null` still shipped
/// `required: ["body"]` on a resident tool, and a strict client refused the `body-file`-only call client-side, with
/// nothing in the server log (PR #1352 review). Same class of half-landed relaxation as the PR #984 incident that
/// <see cref="ColumnIdentityEmittedSchemaTests" /> exists for: the curated contract said one thing and the emitted
/// schema another. These read the schema through the real registry on the production serializer options, so a
/// revert fails here rather than in a client nobody is watching.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ParameterContractEmittedSchemaTests {

	private static McpToolInvokerRegistry BuildProductionRegistry() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
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

	private static string[] RequiredNames(JsonElement objectSchema) {
		if (!objectSchema.TryGetProperty("required", out JsonElement required)) {
			return [];
		}
		string[] names = new string[required.GetArrayLength()];
		int index = 0;
		foreach (JsonElement item in required.EnumerateArray()) {
			names[index++] = item.GetString() ?? string.Empty;
		}
		return names;
	}

	private static JsonElement ArgsSchema(JsonDocument schema) =>
		schema.RootElement.GetProperty("properties").GetProperty("args");

	[Test]
	[Category("Unit")]
	[Description("validate-page does not list 'body' as required in its emitted input schema, so a schema-validating client can send the body-file-only call the curated contract advertises (issue #1297).")]
	public void ValidatePage_Should_NotRequireBody_InEmittedInputSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(PageValidateTool.ToolName);
		JsonElement argsSchema = ArgsSchema(schema);

		// Assert — navigate `required` rather than substring-matching the serialized schema, so the assertion
		// cannot pass vacuously on a reordering.
		argsSchema.GetProperty("properties").TryGetProperty("body", out _).Should().BeTrue(
			because: "'body' must still be advertised — the relaxation makes it optional, not absent");
		argsSchema.GetProperty("properties").TryGetProperty("body-file", out _).Should().BeTrue(
			because: "'body-file' is the alternative the relaxation exists to let a client send");
		RequiredNames(argsSchema).Should().NotContain("body",
			because: "the contract says 'pass either body or body-file', so demanding 'body' makes a " +
				"contract-following payload fail client-side schema validation with nothing in the server log");
		RequiredNames(argsSchema).Should().NotContain("body-file",
			because: "relaxing one spelling into the other's place would reproduce the same defect mirrored");

		// Anti-vacuity: every remaining parameter on PageValidateArgs is nullable-with-default by design, so the
		// emitted args schema carries no `required` array at all. Stated rather than left for a reader to infer —
		// verified by reverting `Body` to no-default, which puts "body" back and fails the negatives above.
		RequiredNames(argsSchema).Should().BeEmpty(
			because: "either input alone is a complete validate-page call, so nothing on the args is " +
				"unconditionally mandatory; a non-empty set means one of the two became required again");
	}

	[TestCase(CreatePageBusinessRuleTool.BusinessRuleCreateToolName)]
	[TestCase(ReadPageBusinessRuleTool.ToolName)]
	[TestCase(UpdatePageBusinessRuleTool.ToolName)]
	[TestCase(DeletePageBusinessRuleTool.ToolName)]
	[Category("Unit")]
	[Description("Every page business-rule tool leaves 'page-schema-name' out of its emitted required array, so the advertised 'schema-name' alias is a call a strict client can actually make (issue #1305).")]
	public void PageBusinessRuleTools_Should_NotRequirePageSchemaName_InEmittedInputSchema(string toolName) {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(toolName);
		JsonElement argsSchema = ArgsSchema(schema);

		// Assert
		argsSchema.GetProperty("properties").TryGetProperty("page-schema-name", out _).Should().BeTrue(
			because: "the canonical page identity field must still be advertised on every page tool");
		argsSchema.GetProperty("properties").TryGetProperty("schema-name", out _).Should().BeTrue(
			because: "the alias is part of the contract, so it has to appear in the emitted schema too");
		RequiredNames(argsSchema).Should().NotContain("page-schema-name",
			because: "'schema-name' is advertised as an equally valid spelling, so requiring the canonical one " +
				"would refuse an alias-only payload client-side");
		RequiredNames(argsSchema).Should().NotContain("schema-name",
			because: "neither spelling may be mandatory while the other is advertised as valid");

		// The surrounding required set must stay intact — an empty set here would prove nothing.
		RequiredNames(argsSchema).Should().Contain("environment-name",
			because: "only the page identity was relaxed; the target environment is still genuinely mandatory");
		RequiredNames(argsSchema).Should().Contain("package-name",
			because: "the package layer a rule is written to cannot be inferred, so it stays required");
	}
}
