using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for curated Freedom UI component metadata.
/// </summary>
[McpServerToolType]
public sealed class ComponentInfoTool(IComponentInfoCatalog catalog) {
	private static readonly string[] CategoryOrder = ["containers", "fields", "interactive", "display"];

	internal const string ToolName = "component-info";

	/// <summary>
	/// Returns grouped component summaries or full metadata for a specific component type.
	/// </summary>
	/// <param name="args">Tool arguments that select either list or detail mode.</param>
	/// <returns>A structured response with grouped summaries or a full component definition.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Get curated Freedom UI component metadata by component type or list all known types")]
	public ComponentInfoResponse GetComponentInfo(
		[Description("Parameters: component-type (optional; omit or use 'list' to list all), search (optional keyword filter)")]
		[Required]
		ComponentInfoArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.ComponentType)
				|| string.Equals(args.ComponentType, "list", StringComparison.OrdinalIgnoreCase)) {
				return CreateListResponse(catalog.Search(args.Search));
			}

			ComponentRegistryEntry? entry = catalog.Find(args.ComponentType);
			if (entry is null) {
				return new ComponentInfoResponse {
					Success = false,
					Mode = "list",
					Error = $"Component type '{args.ComponentType}' was not found.",
					Count = catalog.Search(args.Search).Count,
					Groups = CreateGroups(catalog.Search(args.Search))
				};
			}

			return new ComponentInfoResponse {
				Success = true,
				Mode = "detail",
				Count = 1,
				ComponentType = entry.ComponentType,
				Category = entry.Category,
				Description = entry.Description,
				Container = entry.Container,
				ParentTypes = entry.ParentTypes,
				Properties = entry.Properties,
				TypicalChildren = entry.TypicalChildren,
				Example = entry.Example
			};
		}
		catch (Exception ex) {
			return new ComponentInfoResponse {
				Success = false,
				Mode = "list",
				Error = ex.Message,
				Count = 0,
				Groups = []
			};
		}
	}

	private static ComponentInfoResponse CreateListResponse(IReadOnlyList<ComponentRegistryEntry> entries) {
		return new ComponentInfoResponse {
			Success = true,
			Mode = "list",
			Count = entries.Count,
			Groups = CreateGroups(entries)
		};
	}

	private static IReadOnlyList<ComponentInfoGroup> CreateGroups(IReadOnlyList<ComponentRegistryEntry> entries) {
		return entries
			.GroupBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
			.OrderBy(group => GetCategorySortKey(group.Key))
			.ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Select(group => new ComponentInfoGroup {
				Category = group.Key,
				Items = group
					.OrderBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
					.Select(entry => new ComponentInfoListItem {
						ComponentType = entry.ComponentType,
						Description = entry.Description
					})
					.ToArray()
			})
			.ToArray();
	}

	private static int GetCategorySortKey(string? category) {
		int index = Array.FindIndex(
			CategoryOrder,
			item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase));
		return index >= 0 ? index : CategoryOrder.Length;
	}
}

/// <summary>
/// Arguments for the <c>component-info</c> MCP tool.
/// </summary>
public sealed record ComponentInfoArgs(
	[property: JsonPropertyName("component-type")]
	[property: Description("Freedom UI component type, for example 'crt.TabContainer'. Omit or use 'list' to return the grouped catalog.")]
	string? ComponentType = null,

	[property: JsonPropertyName("search")]
	[property: Description("Optional keyword filter applied in list mode and in not-found suggestions, for example 'tab'.")]
	string? Search = null
);

/// <summary>
/// Structured response from the <c>component-info</c> MCP tool.
/// </summary>
public sealed class ComponentInfoResponse {
	/// <summary>
	/// Gets or sets whether the request completed successfully.
	/// </summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>
	/// Gets or sets the response mode: <c>detail</c> or <c>list</c>.
	/// </summary>
	[JsonPropertyName("mode")]
	public string Mode { get; init; } = "list";

	/// <summary>
	/// Gets or sets the number of returned components.
	/// </summary>
	[JsonPropertyName("count")]
	public int Count { get; init; }

	/// <summary>
	/// Gets or sets the error message when the request fails.
	/// </summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }

	/// <summary>
	/// Gets or sets the component type for detail responses.
	/// </summary>
	[JsonPropertyName("componentType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ComponentType { get; init; }

	/// <summary>
	/// Gets or sets the component category for detail responses.
	/// </summary>
	[JsonPropertyName("category")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Category { get; init; }

	/// <summary>
	/// Gets or sets the component description for detail responses.
	/// </summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; init; }

	/// <summary>
	/// Gets or sets whether the component is a container.
	/// </summary>
	[JsonPropertyName("container")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Container { get; init; }

	/// <summary>
	/// Gets or sets the supported parent component types.
	/// </summary>
	[JsonPropertyName("parentTypes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? ParentTypes { get; init; }

	/// <summary>
	/// Gets or sets the curated property catalog for the component.
	/// </summary>
	[JsonPropertyName("properties")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, ComponentPropertyDefinition>? Properties { get; init; }

	/// <summary>
	/// Gets or sets typical child component types for container components.
	/// </summary>
	[JsonPropertyName("typicalChildren")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? TypicalChildren { get; init; }

	/// <summary>
	/// Gets or sets an example insert payload for the component.
	/// </summary>
	[JsonPropertyName("example")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? Example { get; init; }

	/// <summary>
	/// Gets or sets grouped component summaries for list responses.
	/// </summary>
	[JsonPropertyName("groups")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<ComponentInfoGroup>? Groups { get; init; }
}

/// <summary>
/// Grouped list response entry for the <c>component-info</c> tool.
/// </summary>
public sealed class ComponentInfoGroup {
	/// <summary>
	/// Gets or sets the component category name.
	/// </summary>
	[JsonPropertyName("category")]
	public string Category { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the component summaries that belong to the category.
	/// </summary>
	[JsonPropertyName("items")]
	public IReadOnlyList<ComponentInfoListItem> Items { get; init; } = [];
}

/// <summary>
/// Compact list item for grouped component summaries.
/// </summary>
public sealed class ComponentInfoListItem {
	/// <summary>
	/// Gets or sets the Freedom UI component type.
	/// </summary>
	[JsonPropertyName("componentType")]
	public string ComponentType { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the one-line component description.
	/// </summary>
	[JsonPropertyName("description")]
	public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Curated Freedom UI component definition stored in the shipped registry.
/// </summary>
public sealed class ComponentRegistryEntry {
	/// <summary>
	/// Gets or sets the Freedom UI component type.
	/// </summary>
	[JsonPropertyName("componentType")]
	public string ComponentType { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the high-level component category.
	/// </summary>
	[JsonPropertyName("category")]
	public string Category { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the component description.
	/// </summary>
	[JsonPropertyName("description")]
	public string Description { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets whether the component is a container.
	/// </summary>
	[JsonPropertyName("container")]
	public bool Container { get; init; }

	/// <summary>
	/// Gets or sets the supported parent component types.
	/// </summary>
	[JsonPropertyName("parentTypes")]
	public IReadOnlyList<string> ParentTypes { get; init; } = [];

	/// <summary>
	/// Gets or sets the curated property metadata.
	/// </summary>
	[JsonPropertyName("properties")]
	public IReadOnlyDictionary<string, ComponentPropertyDefinition> Properties { get; init; }
		= new Dictionary<string, ComponentPropertyDefinition>();

	/// <summary>
	/// Gets or sets typical child component types.
	/// </summary>
	[JsonPropertyName("typicalChildren")]
	public IReadOnlyList<string> TypicalChildren { get; init; } = [];

	/// <summary>
	/// Gets or sets a representative insert payload for the component.
	/// </summary>
	[JsonPropertyName("example")]
	public JsonElement? Example { get; init; }
}

/// <summary>
/// Curated Freedom UI component property metadata.
/// </summary>
public sealed class ComponentPropertyDefinition {
	/// <summary>
	/// Gets or sets the expected property type.
	/// </summary>
	[JsonPropertyName("type")]
	public string Type { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the property description.
	/// </summary>
	[JsonPropertyName("description")]
	public string Description { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets whether the property is required for a valid config.
	/// </summary>
	[JsonPropertyName("required")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Required { get; init; }

	/// <summary>
	/// Gets or sets the documented default value when one exists.
	/// </summary>
	[JsonPropertyName("default")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? Default { get; init; }

	/// <summary>
	/// Gets or sets the documented allowed values when the property is constrained.
	/// </summary>
	[JsonPropertyName("values")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? Values { get; init; }
}
