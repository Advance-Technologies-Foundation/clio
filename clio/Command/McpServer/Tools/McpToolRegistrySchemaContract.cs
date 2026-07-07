using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Synthesizes a <see cref="ToolContractDefinition"/> for an uncurated MCP tool directly from the
/// REAL <see cref="McpServerTool"/> input schema that <c>clio-run</c> / <c>clio-run-destructive</c>
/// dispatch against, so the contract advertised by <c>get-tool-contract</c> matches the invokable
/// argument shape exactly (Codex review #1, PR #743 / story-mcp-lazy-schema-6).
/// </summary>
/// <remarks>
/// This replaces the lossy reflection-over-options-types fallback (<see cref="McpToolSchemaCatalog"/>)
/// for any tool present in <see cref="IMcpToolInvokerRegistry"/>. The reflection catalog dropped
/// single-scalar parameters (for example the lone <c>environmentName</c> of <c>stop-creatio</c>) and
/// emitted an empty property list; the registered tool's JSON schema carries them.
/// </remarks>
internal static class McpToolRegistrySchemaContract {
	private const string ObjectType = "object";
	private const string TypePropertyName = "type";
	private const string PropertiesPropertyName = "properties";
	private const string RequiredPropertyName = "required";
	private const string DescriptionPropertyName = "description";
	private const string Note =
		"Auto-generated from the registered MCP tool input schema (the same schema clio-run dispatches against); no curated contract is available for this tool yet.";

	/// <summary>
	/// Tries to build a contract for <paramref name="toolName"/> from the tool's registered input schema.
	/// </summary>
	/// <param name="registry">The invoker registry holding the real, dispatchable tools.</param>
	/// <param name="toolName">The requested MCP tool name.</param>
	/// <param name="contract">The synthesized contract when the tool is registered; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when a registered tool matched and a contract was built; otherwise <c>false</c>.</returns>
	internal static bool TryBuild(
		IMcpToolInvokerRegistry registry,
		string toolName,
		out ToolContractDefinition contract) {
		contract = null;
		if (registry is null || !registry.TryGetTool(toolName, out McpServerTool tool) || tool is null) {
			return false;
		}

		string description = tool.ProtocolTool.Description ?? string.Empty;
		string fullDescription = string.IsNullOrWhiteSpace(description) ? Note : $"{description}\n\n{Note}";
		ToolInputSchemaContract inputSchema = BuildInputSchema(tool.ProtocolTool.InputSchema);
		bool destructive = registry.IsDestructive(toolName);

		contract = new ToolContractDefinition(
			toolName,
			fullDescription,
			inputSchema,
			new ToolOutputContract(
				"tool-native-response",
				SuccessField: null,
				FailureSignals: ["success == false"],
				Fields: []),
			new ToolErrorContract([
				new ToolErrorCodeContract("tool-not-found", "Requested tool name is not registered by clio MCP."),
				new ToolErrorCodeContract("missing-required-parameter", "A required parameter is missing."),
				new ToolErrorCodeContract("invalid-parameter-type", "A parameter value type does not match the tool contract.")
			]),
			Aliases: [],
			Defaults: [],
			Examples: [],
			PreferredFlow: new ToolFlowHint(
				[destructive ? ClioRunDestructiveTool.ToolName : ClioRunTool.ToolName, toolName],
				"Registry-derived contract for a hidden tool: dispatch by name through the matching generic clio-run executor."),
			FallbackFlow: [],
			Deprecations: []);
		return true;
	}

	/// <summary>
	/// Tries to read the tool's RAW registered description (the <c>[Description]</c> on the MCP tool method,
	/// surfaced as <c>ProtocolTool.Description</c>) WITHOUT the uncurated "Auto-generated … no curated
	/// contract yet" <see cref="Note"/> that <see cref="TryBuild"/> appends. The compact discovery index
	/// uses this so a one-line purpose reflects only what the tool DOES, never the absence of curation; the
	/// full named-contract path keeps the note via <see cref="TryBuild"/> (unchanged).
	/// </summary>
	/// <param name="registry">The invoker registry holding the real, dispatchable tools.</param>
	/// <param name="toolName">The requested MCP tool name.</param>
	/// <param name="description">
	/// The raw tool description (possibly empty when the tool declares none) when the tool is registered;
	/// otherwise empty.
	/// </param>
	/// <returns><c>true</c> when a registered tool matched (even with an empty description); otherwise <c>false</c>.</returns>
	internal static bool TryGetRawDescription(
		IMcpToolInvokerRegistry registry,
		string toolName,
		out string description) {
		description = string.Empty;
		if (registry is null || !registry.TryGetTool(toolName, out McpServerTool tool) || tool is null) {
			return false;
		}
		description = tool.ProtocolTool.Description ?? string.Empty;
		return true;
	}

	private static ToolInputSchemaContract BuildInputSchema(JsonElement schema) {
		if (schema.ValueKind != JsonValueKind.Object) {
			return new ToolInputSchemaContract([], []);
		}

		JsonElement effective = UnwrapSingleArgsWrapper(schema);

		List<string> required = ReadRequired(effective);
		List<ToolContractField> properties = [];
		if (effective.TryGetProperty(PropertiesPropertyName, out JsonElement propertiesElement) &&
			propertiesElement.ValueKind == JsonValueKind.Object) {
			foreach (JsonProperty property in propertiesElement.EnumerateObject()) {
				string name = property.Name;
				string type = ReadType(property.Value);
				string description = property.Value.ValueKind == JsonValueKind.Object &&
					property.Value.TryGetProperty(DescriptionPropertyName, out JsonElement descriptionElement) &&
					descriptionElement.ValueKind == JsonValueKind.String
						? descriptionElement.GetString() ?? string.Empty
						: string.Empty;
				properties.Add(new ToolContractField(name, type, description));
			}
		}
		return new ToolInputSchemaContract(required, properties);
	}

	// clio tools that take a single complex args record are emitted by the SDK as a single top-level
	// `args` object property (mirrors ClioRunTool.BuildChildParams' wrapper unwrap). Descend into that
	// inner object so the contract lists the real inner parameters; scalar/multi-param tools (e.g.
	// stop-creatio's lone `environmentName`) keep their top-level properties.
	private static JsonElement UnwrapSingleArgsWrapper(JsonElement schema) {
		if (!schema.TryGetProperty(PropertiesPropertyName, out JsonElement properties) ||
			properties.ValueKind != JsonValueKind.Object) {
			return schema;
		}
		List<JsonProperty> topLevel = properties.EnumerateObject().ToList();
		if (topLevel.Count != 1) {
			return schema;
		}
		JsonElement inner = topLevel[0].Value;
		if (inner.ValueKind == JsonValueKind.Object &&
			IsObjectType(inner) &&
			inner.TryGetProperty(PropertiesPropertyName, out JsonElement innerProperties) &&
			innerProperties.ValueKind == JsonValueKind.Object) {
			return inner;
		}
		return schema;
	}

	private static bool IsObjectType(JsonElement element) =>
		element.TryGetProperty(TypePropertyName, out JsonElement typeElement) &&
		NormalizeType(typeElement) == ObjectType;

	private static List<string> ReadRequired(JsonElement schema) {
		List<string> required = [];
		if (schema.TryGetProperty(RequiredPropertyName, out JsonElement requiredElement) &&
			requiredElement.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in requiredElement.EnumerateArray()) {
				if (item.ValueKind == JsonValueKind.String) {
					required.Add(item.GetString()!);
				}
			}
		}
		return required;
	}

	private static string ReadType(JsonElement propertyValue) {
		if (propertyValue.ValueKind != JsonValueKind.Object ||
			!propertyValue.TryGetProperty(TypePropertyName, out JsonElement typeElement)) {
			return ObjectType;
		}
		return NormalizeType(typeElement);
	}

	// JSON-schema `type` is either a scalar string ("string") or an array of candidates where the SDK
	// emits nullable shapes as ["string","null"]. Collapse to the first non-null concrete type.
	private static string NormalizeType(JsonElement typeElement) {
		if (typeElement.ValueKind == JsonValueKind.String) {
			return typeElement.GetString() ?? ObjectType;
		}
		if (typeElement.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement candidate in typeElement.EnumerateArray()) {
				if (candidate.ValueKind == JsonValueKind.String) {
					string? value = candidate.GetString();
					if (!string.IsNullOrEmpty(value) && value != "null") {
						return value;
					}
				}
			}
		}
		return ObjectType;
	}
}
