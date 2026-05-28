using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for curated Freedom UI component metadata.
/// </summary>
/// <remarks>
/// Version resolution mirrors the <c>clio get-component-info</c> CLI verb 1:1 (see
/// <see cref="ComponentInfoCommand"/>): the target catalog version is driven entirely by the
/// per-call arguments — never by ambient server state — so the tool returns the same catalog
/// the targeted environment actually ships. An explicit <c>version</c> wins; otherwise an
/// <c>environment-name</c>/<c>uri</c> triggers a cliogate <c>GetSysInfo</c> probe; with neither,
/// the tool honestly reports <c>latest-fallback</c> and surfaces
/// <see cref="ComponentInfoResponse.VersionWarning"/>.
/// </remarks>
[McpServerToolType]
public sealed class ComponentInfoTool(
	IComponentInfoCatalog catalog,
	IMobileComponentInfoCatalog mobileCatalog,
	IComponentRegistryDocsClient docsClient,
	IPlatformVersionResolverFactory resolverFactory,
	ISettingsRepository settingsRepository) {

	internal const string ToolName = "get-component-info";
	internal const string ResolvedFromEnvironment = ComponentInfoResolution.ResolvedFromEnvironment;
	internal const string ResolvedFromLatestFallback = ComponentInfoResolution.ResolvedFromLatestFallback;
	internal const string SchemaTypeMobile = "mobile";
	internal const string DocumentationSeparator = ComponentDocumentationLoader.DocumentationSeparator;

	/// <summary>
	/// Canonical contract text returned for every data-source-bound field component type
	/// (members of <see cref="SchemaValidationService.StandardFieldComponentTypes"/>).
	/// Surfaced as <c>dataSourceBindingContract</c> in the tool response so agents see the
	/// three-part payload requirement next to the component's <c>example</c>. The wording
	/// is intentionally identical to the <c>update-page</c> tool [Description] and the
	/// <c>page-modification</c> guidance — all three reuse
	/// <see cref="SchemaValidationService.InsertedFieldContractSummary"/>.
	/// </summary>
	internal const string DataSourceBindingContractText =
		"This is a data-source-bound field component. "
		+ SchemaValidationService.InsertedFieldContractSummary;

	/// <summary>
	/// Returns the component catalog list or full metadata for a specific component type.
	/// </summary>
	/// <param name="args">Tool arguments that select either list or detail mode.</param>
	/// <param name="cancellationToken">Cancellation token propagated by the MCP host.</param>
	/// <returns>A structured response with a component list or a full component definition.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Get curated Freedom UI component metadata by component type or list all known types. " +
		"IMPORTANT: pass environment-name to scope the catalog to the target environment's actual platform version — " +
		"otherwise results come from the 'latest' catalog, a SUPERSET of every GA version, and may list components " +
		"(e.g. a freshly shipped crt.Switch) that do NOT exist in that environment and will fail to render at runtime. " +
		"When you target a page-editing environment, pass the same environment-name here. " +
		"If schema-type is omitted, defaults to the web component catalog (excludes mobile-only components such as crt.Toggle and crt.BarcodeScanner). " +
		"Use schema-type: 'mobile' to retrieve mobile-specific components — the mobile registry is separate and excludes web-only types.")]
	public async Task<ComponentInfoResponse> GetComponentInfo(
		[Description("Freedom UI component type, for example 'crt.TabContainer'. Omit or use 'list' to return the catalog.")]
		string? componentType = null,
		[Description("Optional keyword filter applied in list mode and in not-found suggestions, for example 'tab'.")]
		string? search = null,
		[Description("Component registry to query: 'web' (default) for standard Freedom UI pages, or 'mobile' for mobile page components (crt.Toggle, crt.BarcodeScanner, crt.Sort, etc.).")]
		string? schemaType = null,
		[Description("Registered environment name to scope the catalog to its real platform version (probed via cliogate GetSysInfo). PREFER this — pass the same environment you edit pages on. Mutually exclusive with 'version'.")]
		string? environmentName = null,
		[Description("Explicit catalog version (3-part semver, e.g. '8.3.3') when the platform version is already known. Mutually exclusive with 'environment-name'.")]
		string? version = null,
		[Description("Emergency fallback only: direct application URI when no environment is registered. Prefer 'environment-name'.")]
		string? uri = null,
		[Description("Emergency fallback only: login paired with 'uri'. Prefer 'environment-name'.")]
		string? login = null,
		[Description("Emergency fallback only: password paired with 'uri'. Prefer 'environment-name'.")]
		string? password = null,
		CancellationToken cancellationToken = default) {
		ComponentInfoArgs args = new(componentType, search, schemaType, environmentName, version, uri, login, password);
		try {
			return await BuildResponseAsync(args, cancellationToken).ConfigureAwait(false);
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

	/// <summary>
	/// Single async pipeline that backs both the web and mobile flavors. The branch
	/// only picks the right catalog source up front; everything below — version
	/// resolution, envelope merge, documentation lazy-load, suggestion fallback — is
	/// identical on both surfaces. That symmetry is what makes the response shape
	/// (<c>inputs</c> / <c>outputs</c> / <c>content.typeDefinitions</c> /
	/// <c>documentation</c> / <c>resolvedTargetVersion</c> / <c>resolvedFrom</c>)
	/// stable across the <c>schema-type</c> dimension.
	/// </summary>
	private async Task<ComponentInfoResponse> BuildResponseAsync(ComponentInfoArgs args, CancellationToken cancellationToken) {
		bool isMobile = IsMobile(args.SchemaType);
		bool hasExplicitVersion = !string.IsNullOrWhiteSpace(args.Version);
		bool hasEnvironment = !string.IsNullOrWhiteSpace(args.EnvironmentName) || !string.IsNullOrWhiteSpace(args.Uri);
		if (hasExplicitVersion && hasEnvironment) {
			return new ComponentInfoResponse {
				Success = false,
				Mode = "list",
				Error = "'version' and 'environment-name'/'uri' are mutually exclusive. Pass one or neither.",
				Count = 0,
				Items = []
			};
		}

		PlatformVersionResolution version = await ResolveVersionAsync(args, hasExplicitVersion, hasEnvironment, cancellationToken)
			.ConfigureAwait(false);
		ComponentCatalogState state = isMobile
			? await mobileCatalog.LoadAsync(version.ResolvedVersion, cancellationToken).ConfigureAwait(false)
			: await catalog.LoadAsync(version.ResolvedVersion, cancellationToken).ConfigureAwait(false);
		string resolvedFrom = ComponentInfoResolution.MapResolvedFrom(
			version.Source, version.ResolvedVersion, state.ResolvedVersion);

		if (string.IsNullOrWhiteSpace(args.ComponentType)
			|| string.Equals(args.ComponentType, "list", StringComparison.OrdinalIgnoreCase)) {
			IReadOnlyList<ComponentRegistryEntry> filtered = ComponentInfoGrouping.FilterEntries(state.Entries, args.Search);
			return CreateListResponse(filtered, state.ResolvedVersion, resolvedFrom);
		}

		if (state.Lookup.TryGetValue(args.ComponentType.Trim(), out ComponentRegistryEntry? entry)) {
			string? documentation = await LoadDocumentationAsync(entry, state.ResolvedVersion, cancellationToken).ConfigureAwait(false);
			return CreateDetailResponse(entry, state.ResolvedVersion, resolvedFrom, documentation, state.GlobalReferences);
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
	/// Selects the target catalog version from the per-call arguments, mirroring
	/// <see cref="ComponentInfoCommand"/>'s resolution order so the MCP tool and the CLI verb
	/// stay in lockstep:
	/// <list type="number">
	/// <item>explicit <c>version</c> — authoritative (<see cref="ComponentInfoResolution.MapResolvedFrom"/>
	/// downgrades it to <c>latest-fallback</c> automatically if the catalog ends up loading a different version);</item>
	/// <item><c>environment-name</c>/<c>uri</c> — probe cliogate <c>GetSysInfo</c> on that environment;</item>
	/// <item>neither — <c>latest</c> with a non-authoritative source so the response carries <c>latest-fallback</c>.</item>
	/// </list>
	/// </summary>
	private Task<PlatformVersionResolution> ResolveVersionAsync(
		ComponentInfoArgs args,
		bool hasExplicitVersion,
		bool hasEnvironment,
		CancellationToken cancellationToken) {
		if (hasExplicitVersion) {
			return Task.FromResult(new PlatformVersionResolution(args.Version!.Trim(), VersionResolutionSource.Environment));
		}

		if (hasEnvironment) {
			EnvironmentSettings settings = ResolveEnvironmentSettings(args);
			IPlatformVersionResolver resolver = resolverFactory.Create(settings);
			return resolver.ResolveAsync(cancellationToken);
		}

		return Task.FromResult(new PlatformVersionResolution(
			PlatformVersionResolver.LatestVersion,
			VersionResolutionSource.LatestFallback));
	}

	/// <summary>
	/// Builds the <see cref="EnvironmentSettings"/> for the cliogate probe from the per-call
	/// arguments. Delegates to <see cref="ISettingsRepository.GetEnvironment(EnvironmentOptions)"/>
	/// so the same registered-environment lookup, active-environment fallback, and explicit
	/// uri/login/password fill the CLI verb uses also back the MCP tool.
	/// </summary>
	private EnvironmentSettings ResolveEnvironmentSettings(ComponentInfoArgs args) {
		EnvironmentOptions options = new() {
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return settingsRepository.GetEnvironment(options);
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

	internal static ComponentInfoResponse CreateDetailResponse(
		ComponentRegistryEntry entry,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? documentation,
		RegistryGlobalReferences? globalReferences) {
		IReadOnlyDictionary<string, JsonElement>? mergedInputs = MergeBindings(globalReferences?.BaseInputs, entry.Inputs);
		ComponentReferencesResponse? references = BuildReferencesResponse(entry, globalReferences);
		return new ComponentInfoResponse {
			Success = true,
			Mode = "detail",
			Count = 1,
			ComponentType = entry.ComponentType,
			Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description,
			Container = entry.Container ? true : null,
			ParentTypes = entry.ParentTypes.Count == 0 ? null : entry.ParentTypes,
			Properties = entry.Properties.Count == 0 ? null : entry.Properties,
			Inputs = mergedInputs,
			Outputs = entry.Outputs is { Count: > 0 } ? entry.Outputs : null,
			TypicalChildren = entry.TypicalChildren.Count == 0 ? null : entry.TypicalChildren,
			Example = entry.Example,
			DataSourceBindingContract = SchemaValidationService.StandardFieldComponentTypes.Contains(entry.ComponentType)
				? DataSourceBindingContractText
				: null,
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			Documentation = string.IsNullOrEmpty(documentation) ? null : documentation,
			References = references
		};
	}

	/// <summary>
	/// Lifts the registry entry's <c>references</c> block into the wire shape exposed
	/// on the detail response. The producer publishes a shared <c>root.references.typeDefinitions</c>
	/// bag (~190 keys), of which any one component only references a handful — we
	/// resolve the transitive closure starting from the component's
	/// inputs/outputs/per-component typedefs (see <see cref="TypeReferenceClosure"/>)
	/// so AI receives only the schemas it actually needs to interpret this
	/// component, not the whole global dictionary. Raw <c>docs</c> paths are
	/// intentionally not surfaced here; the docs CDN/cache pipeline consumes them
	/// and the result lands on <see cref="ComponentInfoResponse.Documentation"/>
	/// instead.
	/// </summary>
	private static ComponentReferencesResponse? BuildReferencesResponse(
		ComponentRegistryEntry entry,
		RegistryGlobalReferences? globalReferences) {
		IReadOnlyDictionary<string, JsonElement>? resolved = TypeReferenceClosure.Resolve(
			entry.Inputs,
			entry.Outputs,
			entry.References?.TypeDefinitions,
			globalReferences?.TypeDefinitions);
		return resolved is null ? null : new ComponentReferencesResponse { TypeDefinitions = resolved };
	}

	/// <summary>
	/// Merges a global binding dictionary (lower priority) with a per-component
	/// binding dictionary (higher priority). When both sides declare the same key,
	/// the per-component value wins — a component that overrides a baseInput or
	/// re-defines a global type has explicitly opted out of inheritance. Returns
	/// <c>null</c> when both inputs are null/empty so the JsonIgnore strips the
	/// field from the wire shape.
	/// </summary>
	internal static IReadOnlyDictionary<string, JsonElement>? MergeBindings(
		IReadOnlyDictionary<string, JsonElement>? global,
		IReadOnlyDictionary<string, JsonElement>? perComponent) {
		bool globalEmpty = global is null || global.Count == 0;
		bool localEmpty = perComponent is null || perComponent.Count == 0;
		if (globalEmpty && localEmpty) {
			return null;
		}
		if (globalEmpty) {
			return perComponent;
		}
		if (localEmpty) {
			return global;
		}
		Dictionary<string, JsonElement> merged = new(capacity: global!.Count + perComponent!.Count);
		foreach (KeyValuePair<string, JsonElement> binding in global) {
			merged[binding.Key] = binding.Value;
		}
		foreach (KeyValuePair<string, JsonElement> binding in perComponent) {
			merged[binding.Key] = binding.Value;
		}
		return merged;
	}

	/// <summary>
	/// Delegates to the shared <see cref="ComponentDocumentationLoader"/> so the MCP tool
	/// and the CLI verb produce identical <c>documentation</c> payloads. See the loader
	/// for the cache → CDN pipeline contract and the partial-failure semantics.
	/// </summary>
	private Task<string?> LoadDocumentationAsync(
		ComponentRegistryEntry entry,
		string resolvedVersion,
		CancellationToken cancellationToken) =>
		ComponentDocumentationLoader.LoadAsync(docsClient, entry, resolvedVersion, cancellationToken);
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
	string? SchemaType = null,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered environment name to scope the catalog to its real platform version. Mutually exclusive with 'version'.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("version")]
	[property: Description("Explicit catalog version (3-part semver). Mutually exclusive with 'environment-name'.")]
	string? Version = null,

	[property: JsonPropertyName("uri")]
	[property: Description("Emergency fallback only: direct application URI. Prefer 'environment-name'.")]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description("Emergency fallback only: login paired with 'uri'.")]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description("Emergency fallback only: password paired with 'uri'.")]
	string? Password = null
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
	/// Gets or sets the curated property catalog for the component. Populated by the
	/// legacy registry shape (top-level array with <c>properties</c>); empty in the
	/// wrapped registry shape where the producer uses <see cref="Inputs"/> and
	/// <see cref="Outputs"/> instead.
	/// </summary>
	[JsonPropertyName("properties")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, ComponentPropertyDefinition>? Properties { get; init; }

	/// <summary>
	/// Gets or sets the component's input bindings in the wrapped registry shape.
	/// Each value is surfaced as a forward-compatible <see cref="JsonElement"/> so the
	/// producer can evolve the inner schema (e.g. add <c>keyType</c>, <c>items</c>,
	/// <c>deprecated</c>) without a coordinated clio release. Omitted entirely when
	/// the underlying entry has no <c>inputs</c> block.
	/// </summary>
	[JsonPropertyName("inputs")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? Inputs { get; init; }

	/// <summary>
	/// Gets or sets the component's output bindings in the wrapped registry shape.
	/// Same forward-compatible <see cref="JsonElement"/> treatment as
	/// <see cref="Inputs"/>; omitted entirely when the entry has no <c>outputs</c>
	/// block.
	/// </summary>
	[JsonPropertyName("outputs")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? Outputs { get; init; }

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
	/// Gets or sets the data-source binding contract that surfaces only for standard field components
	/// (text/number/checkbox/lookup/etc. inputs). Tells the agent the three-part payload required for
	/// any <c>operation:"insert"</c> of this component type: viewConfigDiff entry, matching
	/// viewModelConfigDiff attribute declaration, and a registered or auto-provided label resource.
	/// </summary>
	[JsonPropertyName("dataSourceBindingContract")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DataSourceBindingContract { get; init; }

	/// <summary>
	/// Gets or sets the component list for list responses.
	/// </summary>
	[JsonPropertyName("items")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<ComponentInfoListItem>? Items { get; init; }

	/// <summary>
	/// Gets or sets the platform version the catalog was filtered against. Carries the
	/// GA-tag-shaped semver resolved from the targeted environment's cliogate <c>GetSysInfo</c>
	/// probe (or an explicit <c>version</c> argument); falls back to <c>"latest"</c> when no
	/// environment/version was supplied or the probe could not produce a usable version — see
	/// <see cref="ResolvedFrom"/> for which tier produced it.
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
	/// Gets the human-readable caveat emitted whenever <see cref="ResolvedFrom"/> is
	/// <c>latest-fallback</c>. Derived from <see cref="ResolvedFrom"/> so every response
	/// shape (list / detail / not-found, web only) carries it without each branch having
	/// to set it, and so the MCP tool and CLI verb stay in lockstep. Omitted from the wire
	/// shape when the catalog matched the target version (<c>environment</c> tier) or when
	/// the flavor reports no version markers (mobile). See
	/// <see cref="ComponentInfoResolution.LatestFallbackWarning"/> for the rationale.
	/// </summary>
	[JsonPropertyName("versionWarning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? VersionWarning => ComponentInfoResolution.GetVersionWarning(ResolvedFrom);

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

	/// <summary>
	/// Gets or sets the raw <c>content</c> block surfaced from the registry entry —
	/// today this is just <see cref="ComponentReferencesResponse.TypeDefinitions"/>. The
	/// nested shape mirrors the producer's payload 1:1 so AI can resolve the named
	/// type references that appear in <c>inputs</c>/<c>outputs</c> <c>type</c> strings
	/// (e.g. <c>"string | ButtonIcon | ButtonAnimatedIcon"</c>). The flat
	/// <see cref="Documentation"/> field is derived from <c>content.docs[]</c>; raw
	/// docs paths are intentionally not surfaced here.
	/// </summary>
	[JsonPropertyName("references")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ComponentReferencesResponse? References { get; init; }
}

/// <summary>
/// Nested <c>content</c> block on a <see cref="ComponentInfoResponse"/> detail. Mirrors
/// the producer's wire shape 1:1 — unknown sub-fields are intentionally not surfaced
/// here, but the producer can add new ones to the underlying payload without breaking
/// existing AI consumers.
/// </summary>
public sealed class ComponentReferencesResponse {
	/// <summary>
	/// Gets or sets the named type schemas referenced by the component's
	/// <c>inputs</c>/<c>outputs</c> values. Each key is a TypeScript-like type name
	/// (e.g. <c>"ButtonIcon"</c>, <c>"DataGridColumnDefinition"</c>); each value is a
	/// forward-compatible <see cref="JsonElement"/> blob carrying the producer's
	/// schema for that type (<c>fields</c>, <c>values</c>, <c>items</c>,
	/// <c>required</c>, …). Omitted entirely when the registry entry has no
	/// <c>content.typeDefinitions</c> block.
	/// </summary>
	[JsonPropertyName("typeDefinitions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? TypeDefinitions { get; init; }
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
	/// Gets or sets the curated property metadata. Populated by the legacy registry
	/// shape (top-level array). The wrapped registry shape produced by
	/// <c>static-files-mcp</c> uses <see cref="Inputs"/> and <see cref="Outputs"/>
	/// instead — both deserialise from the same JSON without a coordinated rename.
	/// </summary>
	[JsonPropertyName("properties")]
	public IReadOnlyDictionary<string, ComponentPropertyDefinition> Properties { get; init; }
		= new Dictionary<string, ComponentPropertyDefinition>();

	/// <summary>
	/// Gets or sets the component's input bindings from the wrapped registry shape.
	/// Values are kept as <see cref="JsonElement"/> so the producer can evolve the
	/// inner schema (e.g. add <c>keyType</c>, <c>items</c>, <c>deprecated</c>) without
	/// a coordinated clio release. The forward-compatible payload is surfaced
	/// verbatim through <c>ComponentInfoResponse.Inputs</c>.
	/// </summary>
	[JsonPropertyName("inputs")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? Inputs { get; init; }

	/// <summary>
	/// Gets or sets the component's output bindings from the wrapped registry shape.
	/// Same forward-compatible <see cref="JsonElement"/> treatment as
	/// <see cref="Inputs"/>; surfaced verbatim through <c>ComponentInfoResponse.Outputs</c>.
	/// </summary>
	[JsonPropertyName("outputs")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? Outputs { get; init; }

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
	/// Gets or sets the per-component <c>references</c> block from the registry payload.
	/// In the wrapped registry shape this carries the long-form documentation references
	/// (<see cref="ComponentReferences.Docs"/>) and per-component type definitions.
	/// </summary>
	[JsonPropertyName("references")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public ComponentReferences? References { get; init; }

	/// <summary>
	/// Gets or sets the producer-defined default values applied by the page designer when
	/// the user drops the component onto a Freedom UI page (e.g. <c>{ "isAddAllowed": true,
	/// "showValueAsLink": true }</c> for <c>crt.ComboBox</c>). The shape is producer-owned —
	/// each key is an input name, each value is a primitive or nested literal. Captured as
	/// a forward-compatible <see cref="JsonElement"/> so the producer can evolve the inner
	/// schema freely; the field is not surfaced on the public response today, but the data
	/// round-trips through the catalog so a future <c>get-component-info</c> revision can
	/// promote it without a coordinated producer change.
	/// </summary>
	[JsonPropertyName("designerDefaults")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? DesignerDefaults { get; init; }

	/// <summary>
	/// Captures any per-component field clio has not mapped yet (e.g. a future
	/// <c>deprecated</c>/<c>stability</c>/<c>resources</c> producer addition). Always
	/// expected to be empty against the live snapshot — the snapshot guard test
	/// fails when this dictionary is non-empty. Goal: make silent data loss a CI
	/// failure rather than a runtime mystery.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? UnmappedExtensions { get; init; }
}

/// <summary>
/// Root-level envelope of the wrapped registry shape. Top-level keys live here when
/// the payload is an object rather than the legacy top-level array. Today the
/// envelope carries:
/// <list type="bullet">
/// <item><c>components</c> — the per-component entries (the only field the legacy
/// shape uses, lifted to top-level there).</item>
/// <item><c>content</c> — global metadata shared across every component:
/// <see cref="RegistryGlobalReferences.BaseInputs"/> (the inherited input surface
/// every component carries on top of its own <c>inputs</c>) and
/// <see cref="RegistryGlobalReferences.TypeDefinitions"/> (the named-type schemas
/// referenced by every component's <c>inputs</c>/<c>outputs</c> <c>type</c>
/// strings — e.g. <c>RequestBindingConfig</c>, <c>ViewElementConfig</c>).</item>
/// </list>
/// The deserialiser is strict over this envelope (see
/// <c>ComponentInfoCatalog.SnapshotJsonOptions</c> + the snapshot guard test); any
/// new producer field forces a coordinated decision — map it, or explicitly
/// allowlist it via <see cref="UnmappedExtensions"/>.
/// </summary>
public sealed class ComponentRegistryEnvelope {
	/// <summary>Per-component entries (the only required field).</summary>
	[JsonPropertyName("components")]
	public ComponentRegistryEntry[] Components { get; init; } = [];

	/// <summary>Global content block shared across every component.</summary>
	[JsonPropertyName("references")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RegistryGlobalReferences? References { get; init; }

	/// <summary>
	/// Captures any top-level producer field clio has not mapped yet. Always
	/// expected to be empty against the live snapshot — the
	/// <c>Live_Registry_Snapshot_Should_Have_No_Unmapped_Fields</c> guard test
	/// fails when this dictionary is non-empty. The bucket exists so deserialise
	/// does NOT throw under strict mode; the test does the bookkeeping.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? UnmappedExtensions { get; init; }
}

/// <summary>
/// Global metadata block under <c>ComponentRegistry.json</c>'s <c>content</c> key.
/// Shared by every component; merged into per-component data when surfaced on the
/// detail response (per-component wins on a key collision).
/// </summary>
public sealed class RegistryGlobalReferences {
	/// <summary>
	/// Inherited input surface every component carries. Each key is an input name
	/// (e.g. <c>"classes"</c>, <c>"id"</c>, <c>"styles"</c>, <c>"tabIndex"</c>);
	/// each value is the producer's schema for that input (same shape as
	/// per-component <see cref="ComponentRegistryEntry.Inputs"/> values). Merged
	/// into the detail response's <c>inputs</c> block so AI sees a single flat
	/// surface — the per-component override wins when both sides declare the same
	/// key.
	/// </summary>
	[JsonPropertyName("baseInputs")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? BaseInputs { get; init; }

	/// <summary>
	/// Global named-type schemas (e.g. <c>"RequestBindingConfig"</c>,
	/// <c>"CrtMenuItemViewElementConfig"</c>, <c>"ViewElementConfig"</c>). Same
	/// shape as per-component <see cref="ComponentReferences.TypeDefinitions"/>;
	/// merged into the detail response's <c>content.typeDefinitions</c> block so
	/// AI does not need to dereference a second registry call to resolve a type
	/// name referenced by the per-component schema.
	/// </summary>
	[JsonPropertyName("typeDefinitions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? TypeDefinitions { get; init; }

	/// <summary>
	/// Strict-mode catch-all for any new producer key under
	/// <c>root.content.*</c>. See <see cref="ComponentRegistryEnvelope.UnmappedExtensions"/>
	/// for the same rationale.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? UnmappedExtensions { get; init; }
}

/// <summary>
/// The optional <c>content</c> block inside a <see cref="ComponentRegistryEntry"/>.
/// Only the fields clio actually consumes are surfaced here; unknown keys are ignored
/// by the deserialiser so the producer can evolve the block without a coordinated
/// schema bump.
/// </summary>
public sealed class ComponentReferences {
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

	/// <summary>
	/// Gets or sets the named type schemas referenced by the component's <c>inputs</c>
	/// and <c>outputs</c> values (e.g. <c>"ButtonIcon"</c>, <c>"DataGridColumnDefinition"</c>).
	/// Each value carries the producer's TypeScript-like schema for the named type —
	/// fields, allowed values, nested item shapes. Values are stored as
	/// <see cref="JsonElement"/> so the producer can evolve the inner schema freely
	/// (add <c>fields</c>, <c>values</c>, <c>items</c>, <c>required</c>, …) without a
	/// coordinated clio release. Surfaced verbatim through
	/// <c>ComponentInfoResponse.Content.TypeDefinitions</c>.
	/// </summary>
	[JsonPropertyName("typeDefinitions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? TypeDefinitions { get; init; }

	/// <summary>
	/// Strict-mode catch-all for any new per-component <c>content.*</c> producer
	/// key. Snapshot guard test fails when this is non-empty.
	/// </summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement>? UnmappedExtensions { get; init; }
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
