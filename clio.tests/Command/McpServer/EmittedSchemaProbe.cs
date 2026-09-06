using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;
using NSubstitute;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// One object schema found somewhere inside an emitted MCP tool input schema.
/// </summary>
/// <param name="ToolName">The MCP tool the schema belongs to.</param>
/// <param name="Path">A JSON-pointer-like path of the object inside that tool's schema, for diagnostics.</param>
/// <param name="IsRoot"><c>true</c> for the tool's own top-level schema, <c>false</c> for anything nested.</param>
/// <param name="Schema">The object schema node itself.</param>
public sealed record EmittedObjectSchema(string ToolName, string Path, bool IsRoot, JsonElement Schema);

/// <summary>
/// Reads the REAL emitted MCP input schemas out of the production tool registry, so a guard can assert
/// against the exact JSON a strict client validates a payload against.
/// </summary>
/// <remarks>
/// Shared by <see cref="ColumnIdentityEmittedSchemaTests" /> and
/// <see cref="EmittedSchemaRequiredContractTests" />. The MCP SDK derives <c>required</c> from the bound
/// record's non-nullable, non-defaulted positional parameters, and nothing in-process validates it — the
/// STJ binder happily binds a missing nullable parameter to <c>null</c>. Schema and binder therefore
/// disagree silently, and only a strict client ever sees the defect, which is why these guards read the
/// emitted schema instead of exercising the binder (issue #965).
/// </remarks>
public static class EmittedSchemaProbe {

	/// <summary>Builds the registry over the real production tool assembly with every feature enabled.</summary>
	public static McpToolInvokerRegistry BuildProductionRegistry() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		return new McpToolInvokerRegistry(
			provider,
			typeof(SchemaSyncTool).Assembly,
			featureToggle,
			BindingsModule.CreateMcpSerializerOptions());
	}

	/// <summary>Serializes one registered tool's emitted input schema.</summary>
	/// <param name="toolName">The MCP tool name.</param>
	/// <returns>The parsed emitted input schema; the caller owns the document.</returns>
	/// <exception cref="InvalidOperationException">When the tool is not registered.</exception>
	public static JsonDocument EmittedInputSchema(string toolName) {
		McpToolInvokerRegistry registry = BuildProductionRegistry();
		if (!registry.TryGetTool(toolName, out McpServerTool tool)) {
			throw new InvalidOperationException($"'{toolName}' is not a registered MCP tool.");
		}
		return JsonDocument.Parse(JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));
	}

	/// <summary>Reads the <c>required</c> entries declared directly on one object schema.</summary>
	public static string[] RequiredNames(JsonElement objectSchema) {
		if (objectSchema.ValueKind != JsonValueKind.Object ||
			!objectSchema.TryGetProperty("required", out JsonElement required) ||
			required.ValueKind != JsonValueKind.Array) {
			return [];
		}
		List<string> names = [];
		foreach (JsonElement item in required.EnumerateArray()) {
			if (item.ValueKind == JsonValueKind.String) {
				names.Add(item.GetString() ?? string.Empty);
			}
		}
		return [.. names];
	}

	/// <summary>Reads one property's <c>description</c>, or an empty string when it declares none.</summary>
	public static string PropertyDescription(JsonElement objectSchema, string propertyName) {
		if (objectSchema.ValueKind != JsonValueKind.Object ||
			!objectSchema.TryGetProperty("properties", out JsonElement properties) ||
			properties.ValueKind != JsonValueKind.Object ||
			!properties.TryGetProperty(propertyName, out JsonElement property) ||
			property.ValueKind != JsonValueKind.Object ||
			!property.TryGetProperty("description", out JsonElement description) ||
			description.ValueKind != JsonValueKind.String) {
			return string.Empty;
		}
		return description.GetString() ?? string.Empty;
	}

	/// <summary>
	/// Determines whether an object schema is the SYNTHETIC single-<c>args</c> wrapper the SDK emits for a
	/// tool method that takes one complex record parameter.
	/// </summary>
	/// <remarks>
	/// The distinction matters to every registry-wide guard: on a wrapper root the sole <c>required</c>
	/// entry is the machine-generated <c>args</c> name and the real user-facing fields live one level down,
	/// whereas on a METHOD-PARAMETER tool (for example <c>link-from-repository-by-environment</c>) the SDK
	/// puts the real fields on the root itself. Skipping every root indiscriminately therefore exempts the
	/// whole method-parameter family — the shape that carried this defect in ENG-93347.
	/// </remarks>
	/// <param name="objectSchema">The candidate root schema.</param>
	public static bool IsSingleArgsWrapper(JsonElement objectSchema) {
		if (objectSchema.ValueKind != JsonValueKind.Object ||
			!objectSchema.TryGetProperty("properties", out JsonElement properties) ||
			properties.ValueKind != JsonValueKind.Object) {
			return false;
		}
		List<JsonProperty> topLevel = [.. properties.EnumerateObject()];
		return topLevel.Count == 1 && topLevel[0].NameEquals("args");
	}

	/// <summary>
	/// Returns the schema a caller's arguments are actually validated against: the inner <c>args</c> object
	/// for a single-record tool, or the root itself for a method-parameter tool.
	/// </summary>
	/// <param name="rootSchema">The tool's emitted top-level input schema.</param>
	public static JsonElement EffectiveArgumentSchema(JsonElement rootSchema) =>
		IsSingleArgsWrapper(rootSchema) &&
		rootSchema.GetProperty("properties").GetProperty("args") is { ValueKind: JsonValueKind.Object } args
			? args
			: rootSchema;

	/// <summary>Determines whether an object schema advertises a property of the given name.</summary>
	public static bool Advertises(JsonElement objectSchema, string propertyName) =>
		objectSchema.ValueKind == JsonValueKind.Object &&
		objectSchema.TryGetProperty("properties", out JsonElement properties) &&
		properties.ValueKind == JsonValueKind.Object &&
		properties.TryGetProperty(propertyName, out _);

	/// <summary>
	/// Enumerates every object schema that declares a <c>required</c> array, across every registered tool.
	/// </summary>
	public static IReadOnlyList<EmittedObjectSchema> EnumerateRequiredBearingSchemas() {
		McpToolInvokerRegistry registry = BuildProductionRegistry();
		List<EmittedObjectSchema> found = [];
		foreach (string toolName in registry.ToolNames) {
			if (!registry.TryGetTool(toolName, out McpServerTool tool)) {
				continue;
			}
			// The JsonDocument is kept alive by the JsonElement instances collected from it; the returned
			// clones survive the walk because JsonSerializer.Serialize produced a self-contained document.
			JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));
			Walk(toolName, document.RootElement.Clone(), "$", isRoot: true, found);
		}
		return found;
	}

	private static void Walk(string toolName, JsonElement node, string path, bool isRoot, List<EmittedObjectSchema> found) {
		switch (node.ValueKind) {
			case JsonValueKind.Object:
				if (node.TryGetProperty("required", out JsonElement required) &&
					required.ValueKind == JsonValueKind.Array) {
					found.Add(new EmittedObjectSchema(toolName, path, isRoot, node));
				}
				foreach (JsonProperty property in node.EnumerateObject()) {
					if (property.NameEquals("required")) {
						continue;
					}
					Walk(toolName, property.Value, $"{path}.{property.Name}", isRoot: false, found);
				}
				break;
			case JsonValueKind.Array:
				int index = 0;
				foreach (JsonElement item in node.EnumerateArray()) {
					Walk(toolName, item, $"{path}[{index++}]", isRoot: false, found);
				}
				break;
		}
	}
}
