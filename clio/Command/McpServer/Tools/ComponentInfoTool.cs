using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for curated Freedom UI component metadata.
/// </summary>
[McpServerToolType]
public sealed class ComponentInfoTool(
	IComponentInfoCatalog catalog,
	IMobileComponentInfoCatalog mobileCatalog,
	IPlatformVersionResolver versionResolver,
	IComponentRegistryDocsClient docsClient) {

	internal const string ToolName = "get-component-info";
	internal const string ResolvedFromEnvironment = ComponentInfoResolution.ResolvedFromEnvironment;
	internal const string ResolvedFromLatestFallback = ComponentInfoResolution.ResolvedFromLatestFallback;
	internal const string SchemaTypeMobile = "mobile";
	internal const string DocumentationSeparator = "\n\n---\n\n";

	/// <summary>
	/// Returns the component catalog list or full metadata for a specific component type.
	/// </summary>
	/// <param name="args">Tool arguments that select either list or detail mode.</param>
	/// <param name="cancellationToken">Cancellation token propagated by the MCP host.</param>
	/// <returns>A structured response with a component list or a full component definition.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Get curated Freedom UI component metadata by component type or list all known types. " +
		"If schema-type is omitted, defaults to the web component catalog (excludes mobile-only components such as crt.Toggle and crt.BarcodeScanner). " +
		"Use schema-type: 'mobile' to retrieve mobile-specific components — the mobile registry is separate and excludes web-only types.")]
	public async Task<ComponentInfoResponse> GetComponentInfo(
		[Description("Freedom UI component type, for example 'crt.TabContainer'. Omit or use 'list' to return the catalog.")]
		string? componentType = null,
		[Description("Optional keyword filter applied in list mode and in not-found suggestions, for example 'tab'.")]
		string? search = null,
		[Description("Component registry to query: 'web' (default) for standard Freedom UI pages, or 'mobile' for mobile page components (crt.Toggle, crt.BarcodeScanner, crt.Sort, etc.).")]
		string? schemaType = null,
		CancellationToken cancellationToken = default) {
		ComponentInfoArgs args = new(componentType, search, schemaType);
		try {
			if (IsMobile(args.SchemaType)) {
				return BuildMobileResponse(args);
			}
			return await BuildWebResponseAsync(args, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) {
			return new ComponentInfoResponse {
				Success = false,
				Mode = "list",
				Error = ex.Message,
				Count = 0,
				Items = []
			};
		}
	}

	private async Task<ComponentInfoResponse> BuildWebResponseAsync(ComponentInfoArgs args, CancellationToken cancellationToken) {
		PlatformVersionResolution version = await versionResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
		ComponentCatalogState state = await catalog.LoadAsync(version.ResolvedVersion, cancellationToken).ConfigureAwait(false);
		string resolvedFrom = ComponentInfoResolution.MapResolvedFrom(
			version.Source, version.ResolvedVersion, state.ResolvedVersion);

		if (string.IsNullOrWhiteSpace(args.ComponentType)
			|| string.Equals(args.ComponentType, "list", StringComparison.OrdinalIgnoreCase)) {
			IReadOnlyList<ComponentRegistryEntry> filtered = ComponentInfoGrouping.FilterEntries(state.Entries, args.Search);
			return CreateListResponse(filtered, state.ResolvedVersion, resolvedFrom);
		}

		if (state.Lookup.TryGetValue(args.ComponentType.Trim(), out ComponentRegistryEntry? entry)) {
			string? documentation = await LoadDocumentationAsync(entry, state.ResolvedVersion, cancellationToken).ConfigureAwait(false);
			return CreateDetailResponse(entry, state.ResolvedVersion, resolvedFrom, documentation);
		}

		IReadOnlyList<ComponentRegistryEntry> suggestions = ComponentInfoGrouping.FilterEntries(state.Entries, args.Search);
		return new ComponentInfoResponse {
			Success = false,
			Mode = "list",
			Error = $"Component type '{args.ComponentType}' was not found.",
			Count = suggestions.Count,
			Items = ComponentInfoGrouping.CreateItems(suggestions),
			ResolvedTargetVersion = state.ResolvedVersion,
			ResolvedFrom = resolvedFrom
		};
	}

	/// <summary>
	/// Builds the mobile-schema response branch. The mobile catalog ships as static data
	/// inside the deployed Data folder, has no CDN tier, and is not version-pinned, so
	/// the response intentionally omits the <c>resolvedTargetVersion</c> and
	/// <c>resolvedFrom</c> markers — they would be meaningless here.
	/// </summary>
	private ComponentInfoResponse BuildMobileResponse(ComponentInfoArgs args) {
		if (string.IsNullOrWhiteSpace(args.ComponentType)
			|| string.Equals(args.ComponentType, "list", StringComparison.OrdinalIgnoreCase)) {
			return CreateListResponse(mobileCatalog.Search(args.Search), resolvedTargetVersion: null, resolvedFrom: null);
		}

		ComponentRegistryEntry? entry = mobileCatalog.Find(args.ComponentType);
		if (entry is not null) {
			// Mobile catalog ships as static data without a CDN tier, so the docs
			// pipeline is not consulted here even if a future mobile entry lists
			// content.docs[]. Documentation is intentionally null for mobile.
			return CreateDetailResponse(entry, resolvedTargetVersion: null, resolvedFrom: null, documentation: null);
		}

		IReadOnlyList<ComponentRegistryEntry> suggestions = mobileCatalog.Search(args.Search);
		return new ComponentInfoResponse {
			Success = false,
			Mode = "list",
			Error = $"Component type '{args.ComponentType}' was not found.",
			Count = suggestions.Count,
			Items = ComponentInfoGrouping.CreateItems(suggestions)
		};
	}

	private static bool IsMobile(string? schemaType) =>
		string.Equals(schemaType, SchemaTypeMobile, StringComparison.OrdinalIgnoreCase);

	private static ComponentInfoResponse CreateListResponse(
		IReadOnlyList<ComponentRegistryEntry> entries,
		string? resolvedTargetVersion,
		string? resolvedFrom) {
		return new ComponentInfoResponse {
			Success = true,
			Mode = "list",
			Count = entries.Count,
			Items = ComponentInfoGrouping.CreateItems(entries),
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom
		};
	}

	private static ComponentInfoResponse CreateDetailResponse(
		ComponentRegistryEntry entry,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? documentation) {
		return new ComponentInfoResponse {
			Success = true,
			Mode = "detail",
			Count = 1,
			ComponentType = entry.ComponentType,
			Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description,
			Container = entry.Container ? true : null,
			ParentTypes = entry.ParentTypes.Count == 0 ? null : entry.ParentTypes,
			Properties = entry.Properties.Count == 0 ? null : entry.Properties,
			TypicalChildren = entry.TypicalChildren.Count == 0 ? null : entry.TypicalChildren,
			Example = entry.Example,
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			Documentation = string.IsNullOrEmpty(documentation) ? null : documentation
		};
	}

	/// <summary>
	/// Fetches every documentation file referenced by the entry through the docs
	/// pipeline (cache → CDN, no embedded tier) and concatenates them in registry order
	/// with <see cref="DocumentationSeparator"/>. Partial-failure mode: a single missed
	/// fetch is skipped and the remaining files are still concatenated, matching the
	/// graceful-degradation posture of the registry chain itself.
	/// </summary>
	private async Task<string?> LoadDocumentationAsync(
		ComponentRegistryEntry entry,
		string resolvedVersion,
		CancellationToken cancellationToken) {
		IReadOnlyList<string>? docs = entry.Content?.Docs;
		if (docs is null || docs.Count == 0) {
			return null;
		}

		List<string> blocks = new(capacity: docs.Count);
		foreach (string docPath in docs) {
			string? block = await docsClient.GetDocAsync(resolvedVersion, docPath, cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrEmpty(block)) {
				blocks.Add(block);
			}
		}

		return blocks.Count == 0 ? null : string.Join(DocumentationSeparator, blocks);
	}
}

/// <summary>
/// Arguments for the <c>get-component-info</c> MCP tool.
/// </summary>
public sealed record ComponentInfoArgs(
	[property: JsonPropertyName("component-type")]
	[property: Description("Freedom UI component type, for example 'crt.TabContainer'. Omit or use 'list' to return the catalog.")]
	string? ComponentType = null,

	[property: JsonPropertyName("search")]
	[property: Description("Optional keyword filter applied in list mode and in not-found suggestions, for example 'tab'.")]
	string? Search = null,

	[property: JsonPropertyName("schema-type")]
	[property: Description("Component registry to query: 'web' (default) for standard Freedom UI pages, or 'mobile' for mobile page components (crt.Toggle, crt.BarcodeScanner, crt.Sort, etc.).")]
	string? SchemaType = null
);

/// <summary>
/// Structured response from the <c>get-component-info</c> MCP tool.
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
	/// Gets or sets the component list for list responses.
	/// </summary>
	[JsonPropertyName("items")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<ComponentInfoListItem>? Items { get; init; }

	/// <summary>
	/// Gets or sets the platform version the catalog was filtered against. In v1 this is
	/// always <c>"latest"</c> (the catalog is loaded from CDN <c>latest.json</c> / cache / embedded);
	/// once <c>IPlatformVersionResolver</c> lands this will carry the GA-tag-shaped semver
	/// resolved from the active environment's cliogate <c>GetSysInfo</c> probe.
	/// </summary>
	[JsonPropertyName("resolvedTargetVersion")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ResolvedTargetVersion { get; init; }

	/// <summary>
	/// Gets or sets the resolver tier that produced <see cref="ResolvedTargetVersion"/>.
	/// Permitted values: <c>"environment"</c> (resolved from cliogate GetSysInfo),
	/// <c>"latest-fallback"</c> (env unknown, probe failed, or version unparseable). AI should
	/// treat <c>"latest-fallback"</c> as a superset of the true target environment.
	/// </summary>
	[JsonPropertyName("resolvedFrom")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ResolvedFrom { get; init; }

	/// <summary>
	/// Gets or sets the long-form documentation associated with the component. Populated
	/// only on detail responses for components whose registry entry lists files under
	/// <c>content.docs[]</c>; each file is fetched lazily through the docs CDN/cache
	/// pipeline and the resulting markdown blocks are concatenated with
	/// <c>"\n\n---\n\n"</c> separators in registry order. Omitted entirely when the
	/// component has no documentation, when the schema is mobile, or when every
	/// referenced file fails to fetch (the partial-failure mode skips failed files and
	/// keeps the rest — see <c>clio/Command/McpServer/AGENTS.md</c>).
	/// </summary>
	[JsonPropertyName("documentation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Documentation { get; init; }
}

/// <summary>
/// Compact list item for the <c>get-component-info</c> list response.
/// </summary>
public sealed class ComponentInfoListItem {
	/// <summary>
	/// Gets or sets the Freedom UI component type.
	/// </summary>
	[JsonPropertyName("componentType")]
	public string ComponentType { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the one-line component description. Omitted from JSON when the source
	/// payload does not carry one (new wrapped registry shape).
	/// </summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; init; }
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

	/// <summary>
	/// Gets or sets the per-component <c>content</c> block from the registry payload.
	/// In the wrapped registry shape this carries the long-form documentation references
	/// (<see cref="ComponentContent.Docs"/>) and per-component type definitions.
	/// </summary>
	[JsonPropertyName("content")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ComponentContent? Content { get; init; }
}

/// <summary>
/// The optional <c>content</c> block inside a <see cref="ComponentRegistryEntry"/>.
/// Only the fields clio actually consumes are surfaced here; unknown keys are ignored
/// by the deserialiser so the producer can evolve the block without a coordinated
/// schema bump.
/// </summary>
public sealed class ComponentContent {
	/// <summary>
	/// Gets or sets the list of long-form documentation files for the component.
	/// Each entry is a path relative to <c>/api/mcp/{version}/</c> (e.g.
	/// <c>"docs/data-grid.component.md"</c>); clio fetches the bytes lazily on detail
	/// requests, caches them with the same 5-minute TTL as the registry payload, and
	/// concatenates them into the response <c>documentation</c> field.
	/// </summary>
	[JsonPropertyName("docs")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? Docs { get; init; }
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
