using System;
using System.Collections.Generic;
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
/// Guards the emitted MCP input schemas for every surface that accepts a column identity, so the contract's
/// "send either <c>column-name</c> or <c>name</c>" promise is what a strict client actually validates against.
/// </summary>
/// <remarks>
/// The MCP SDK derives the emitted <c>required</c> array from the bound record's non-nullable, non-defaulted
/// positional parameters — and the bound record is the DERIVED one, not
/// <see cref="ColumnModificationArgsBase" />. Relaxing only the base left <c>column-name</c> in <c>required</c>
/// on all three surfaces below while the curated <c>get-tool-contract</c> text advertised it as optional
/// (PR #984 review). These assertions read the schema through the real registry, on the production serializer
/// options, so they fail if the relaxation is ever half-landed again.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ColumnIdentityEmittedSchemaTests {

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

	/// <summary>Collects every <c>required</c> entry from every nested object schema.</summary>
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

	private static JsonElement ColumnItemSchema(JsonDocument schema) {
		return schema.RootElement
			.GetProperty("properties").GetProperty("args")
			.GetProperty("properties").GetProperty("columns")
			.GetProperty("items");
	}

	[Test]
	[Category("Unit")]
	[Description("create-entity-schema does not list either column-identity or column-type spelling as required in its emitted columns item schema, so a strict client can send the canonical 'column-name'/'data-value-type' pair (issue #947).")]
	public void CreateEntitySchema_Should_NotRequireEitherColumnSpelling_InEmittedColumnSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(CreateEntitySchemaTool.CreateEntitySchemaToolName);
		JsonElement columnSchema = ColumnItemSchema(schema);

		// Assert — navigate the `required` array rather than substring-matching the serialized schema: an exact
		// substring test passes vacuously the moment element order, whitespace, or the required set changes,
		// which is precisely when the relaxation would have silently regressed (PR #984 review).
		columnSchema.GetProperty("properties").TryGetProperty("column-name", out _).Should().BeTrue(
			because: "the canonical column identity field must still be advertised on the create surface");
		RequiredNames(columnSchema).Should().NotContain("name",
			because: "'name' is one of two accepted identity spellings, so requiring it would make a payload " +
				"that sends only the canonical 'column-name' fail client-side schema validation");
		RequiredNames(columnSchema).Should().NotContain("type",
			because: "'type' has the equally valid 'data-value-type' alias, so it must not be demanded either");
		RequiredNames(columnSchema).Should().NotContain("column-name",
			because: "relaxing one spelling into the other's place would reproduce the same defect mirrored");
		RequiredNames(columnSchema).Should().NotContain("data-value-type",
			because: "neither type spelling may be mandatory while the other one is advertised as valid");

		// Anti-vacuity note: every parameter on CreateEntitySchemaColumnArgs is nullable-with-default by
		// design, so the emitted column item currently carries NO `required` array at all — the negatives
		// above pass over an empty set. That is the intended state, and they are still the right guard:
		// verified empirically by reverting `Name` to non-nullable-non-defaulted, which puts "name" back in
		// `required` and makes both create tests fail. Asserted here so the empty set is a stated fact rather
		// than something a reader has to infer (PR #984 review).
		RequiredNames(columnSchema).Should().BeEmpty(
			because: "a column identifies itself through either spelling of both its code and its type, so " +
				"nothing on the column item is unconditionally mandatory; a non-empty set here means some " +
				"single spelling became required and the either-or contract broke");
	}

	[Test]
	[Category("Unit")]
	[Description("create-lookup binds the same column record, so its emitted columns item schema must not require either identity or type spelling either — relaxing only create-entity-schema would leave this sibling surface broken (issue #947).")]
	public void CreateLookup_Should_NotRequireEitherColumnSpelling_InEmittedColumnSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(CreateLookupTool.CreateLookupToolName);
		JsonElement columnSchema = ColumnItemSchema(schema);

		// Assert
		RequiredNames(columnSchema).Should().NotContain("name",
			because: "create-lookup binds the same CreateEntitySchemaColumnArgs record, so it inherits the same " +
				"either-spelling contract and must not demand the alias");
		RequiredNames(columnSchema).Should().NotContain("type",
			because: "the column type has the same two accepted spellings on this surface");

		// And nothing else nested in this schema may demand a single spelling either.
		CollectRequiredNames(schema.RootElement).Should().NotContain("column-name",
			because: "no nested `required` array anywhere in this surface may demand the column identity");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-entity-schema-column does not list 'column-name' as required in its emitted input schema, so a strict client can send the advertised 'name' alias instead (PR #984 review).")]
	public void ModifyEntitySchemaColumn_Should_NotRequireColumnName_InEmittedInputSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName);
		JsonElement argsSchema = schema.RootElement.GetProperty("properties").GetProperty("args");

		// Assert
		RequiredNames(argsSchema).Should().NotContain("column-name",
			because: "the contract advertises 'name' as an equally valid spelling, so requiring 'column-name' " +
				"would make a contract-following payload fail client-side schema validation");
		RequiredNames(argsSchema).Should().Contain("action",
			because: "the surrounding required set must stay intact — only the column identity was relaxed");
	}

	[Test]
	[Category("Unit")]
	[Description("get-entity-schema-column-properties keeps package-name optional in the emitted MCP schema so strict clients can request merged discovery.")]
	public void GetEntitySchemaColumnProperties_Should_NotRequirePackageName_InEmittedInputSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(
			GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName);
		JsonElement argsSchema = schema.RootElement.GetProperty("properties").GetProperty("args");

		// Assert
		RequiredNames(argsSchema).Should().NotContain("package-name",
			because: "omitting package-name is the public signal for merged runtime discovery");
		RequiredNames(argsSchema).Should().Contain(["environment-name", "schema-name", "column-name"],
			because: "only package scope became optional; the target environment, schema, and column remain required");
	}

	[Test]
	[Category("Unit")]
	[Description("update-entity-schema does not list 'column-name' as required inside its operations items, so the get-app-info read shape round-trips (PR #984 review).")]
	public void UpdateEntitySchema_Should_NotRequireColumnName_InEmittedOperationSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(UpdateEntitySchemaTool.UpdateEntitySchemaToolName);
		JsonElement operationSchema = schema.RootElement
			.GetProperty("properties").GetProperty("args")
			.GetProperty("properties").GetProperty("operations")
			.GetProperty("items");

		// Assert
		RequiredNames(operationSchema).Should().NotContain("column-name",
			because: "an operation may identify its column through the 'name' alias, which is exactly the " +
				"shape get-app-info reports");
		RequiredNames(operationSchema).Should().Contain("action",
			because: "the action verb is genuinely mandatory and must remain required");
	}

	[Test]
	[Category("Unit")]
	[Description("sync-schemas embeds the same operation record, so its update-operations items must not require 'column-name' either — fixing only modify/update would leave this third surface broken (PR #984 review).")]
	public void SyncSchemas_Should_NotRequireColumnName_InEmittedUpdateOperationSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedInputSchema(SchemaSyncTool.ToolName);
		JsonElement updateOperationSchema = schema.RootElement
			.GetProperty("properties").GetProperty("args")
			.GetProperty("properties").GetProperty("operations")
			.GetProperty("items")
			.GetProperty("properties").GetProperty("update-operations")
			.GetProperty("items");

		// Assert
		updateOperationSchema.GetProperty("properties").TryGetProperty("column-name", out _).Should().BeTrue(
			because: "the column identity field must still be advertised by the embedded operation record");
		RequiredNames(updateOperationSchema).Should().NotContain("column-name",
			because: "this is the third surface the same record backs, and fixing only modify/update would " +
				"leave it demanding an identity field the contract calls optional");
		RequiredNames(updateOperationSchema).Should().Contain("action",
			because: "the action verb stays mandatory, so an empty required set would not prove anything");

		// And nothing else nested in this schema may demand it either.
		CollectRequiredNames(schema.RootElement).Should().NotContain("column-name",
			because: "no nested `required` array anywhere in this surface may demand the column identity");
	}
}
