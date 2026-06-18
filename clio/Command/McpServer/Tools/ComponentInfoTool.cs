using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
	/// Upper bound on the "did you mean" entries returned when a requested <c>component-type</c>
	/// is unknown. Keeps the not-found envelope a small, actionable shortlist instead of echoing
	/// the full ~199-item catalog as "suggestions".
	/// </summary>
	private const int MaxNotFoundSuggestions = 8;

	/// <summary>
	/// Canonical contract text returned for every data-source-bound field component type
	/// (members of <see cref="SchemaValidationService.StandardFieldComponentTypes"/>).
	/// Surfaced as <c>dataSourceBindingContract</c> in the tool response so agents see the
	/// inserted-field contract next to the component's <c>example</c>. The wording
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
		"PROACTIVELY list the catalog (omit component-type, or pass 'list') at the start of any page work to discover the full component set " +
		"— including non-obvious components such as crt.Gallery — instead of authoring types from memory or waiting for the user to ask you to search. " +
		"Detail responses include selection-metadata when the producer publishes it: whenToUse / whenNotToUse (one-line 'pick this when…' / 'do NOT pick this when…' guidance) plus synonyms / useCases — " +
		"use whenToUse / whenNotToUse to choose between visually similar components (e.g. crt.Gallery vs crt.DataGrid vs crt.List) instead of guessing. " +
		"IMPORTANT: pass environment-name to scope the catalog to the target environment's actual platform version — " +
		"otherwise results come from the 'latest' catalog, a SUPERSET of every GA version, and may list components " +
		"(e.g. a freshly shipped crt.Switch) that do NOT exist in that environment and will fail to render at runtime. " +
		"When resolvedFrom is 'latest-fallback' the version is unknown and the response sets requiresVersionConfirmation: true — do not silently assume the component set: tell the user the version is unknown and request confirmation before proceeding (resolvedFromReason says whether a retry might help). " +
		"When you target a page-editing environment, pass the same environment-name here. " +
		"If schema-type is omitted, defaults to the web component catalog (excludes mobile-only components such as crt.Toggle and crt.BarcodeScanner). " +
		"Use schema-type: 'mobile' to retrieve mobile-specific components — the mobile registry is separate and excludes web-only types.")]
	public async Task<ComponentInfoResponse> GetComponentInfo(
		[Description("Parameters: component-type (optional; omit or use 'list' to return the catalog), search (optional keyword filter). " +
			"schema-type: 'web' (default) or 'mobile'. environment-name: PREFERRED — scopes the catalog to the target platform version (mutually exclusive with version). " +
			"version: explicit 3-part semver. uri/login/password: emergency fallback only.")]
		[Required] ComponentInfoArgs args,
		CancellationToken cancellationToken = default) {
		string? legacyAliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: component-type, search, schema-type, environment-name, version, uri, login, password.");
		if (!string.IsNullOrWhiteSpace(legacyAliasError)) {
			return new ComponentInfoResponse {
				Success = false,
				Mode = "list",
				Error = legacyAliasError,
				Count = 0,
				Items = []
			};
		}
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
	/// Canonical kebab-case parameter names paired with the camelCase / snake_case spellings an
	/// LLM is most likely to emit. Mirrors <see cref="PageListTool"/>'s alias handling so the
	/// reality (the bound <see cref="ComponentInfoArgs"/> shape) and the advertised
	/// <c>get-tool-contract</c> aliases stay in lockstep — the rejection here is exactly what the
	/// contract's <c>componentType -&gt; component-type</c> alias promises, instead of silently
	/// dropping an unbound camelCase value and degrading a detail request to a 199-item list.
	/// </summary>
	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["componentType"] = "component-type",
		["component_type"] = "component-type",
		["schemaType"] = "schema-type",
		["schema_type"] = "schema-type",
		["environmentName"] = "environment-name",
		["environment_name"] = "environment-name"
	};

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
		if (hasExplicitVersion && !PlatformVersionResolver.TryNormaliseToThreePartSemver(args.Version!, out _)) {
			return new ComponentInfoResponse {
				Success = false,
				Mode = "list",
				Error = $"'version' value '{args.Version}' is not a valid platform version. Use a 3-part semver, for example '8.3.3'.",
				Count = 0,
				Items = []
			};
		}

		PlatformVersionResolution versionResolution = await ResolveVersionAsync(args, hasExplicitVersion, hasEnvironment, cancellationToken)
			.ConfigureAwait(false);
		ComponentCatalogState state = isMobile
			? await mobileCatalog.LoadAsync(versionResolution.ResolvedVersion, cancellationToken).ConfigureAwait(false)
			: await catalog.LoadAsync(versionResolution.ResolvedVersion, cancellationToken).ConfigureAwait(false);
		string resolvedFrom = ComponentInfoResolution.MapResolvedFrom(
			versionResolution.Source, versionResolution.ResolvedVersion, state.ResolvedVersion);
		string? resolvedFromReason = ComponentInfoResolution.GetFallbackReason(resolvedFrom, versionResolution.Reason);

		if (string.IsNullOrWhiteSpace(args.ComponentType)
			|| string.Equals(args.ComponentType, "list", StringComparison.OrdinalIgnoreCase)) {
			IReadOnlyList<ComponentRegistryEntry> filtered = ComponentInfoGrouping.FilterEntries(state.Entries, args.Search);
			return CreateListResponse(filtered, state.ResolvedVersion, resolvedFrom, resolvedFromReason);
		}

		if (state.Lookup.TryGetValue(args.ComponentType.Trim(), out ComponentRegistryEntry? entry)) {
			string? documentation = await LoadDocumentationAsync(entry, state.ResolvedVersion, cancellationToken).ConfigureAwait(false);
			return CreateDetailResponse(entry, state.ResolvedVersion, resolvedFrom, documentation, state.GlobalReferences, resolvedFromReason);
		}

		string requestedType = args.ComponentType.Trim();
		IReadOnlyList<ComponentRegistryEntry> suggestions =
			ComponentInfoGrouping.SuggestForUnknown(state.Entries, requestedType, args.Search, MaxNotFoundSuggestions);
		return new ComponentInfoResponse {
			Success = false,
			Mode = "list",
			Error = $"Component type '{requestedType}' was not found. "
				+ $"Showing the {suggestions.Count} closest known type(s) — pass one of these as 'component-type', "
				+ "or omit 'component-type' to list the full catalog.",
			Count = suggestions.Count,
			Items = ComponentInfoGrouping.CreateItems(suggestions),
			ResolvedTargetVersion = state.ResolvedVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};
	}

	/// <summary>
	/// Selects the target catalog version from the per-call arguments, mirroring
	/// <see cref="ComponentInfoCommand"/>'s resolution order so the MCP tool and the CLI verb
	/// stay in lockstep:
	/// <list type="number">
	/// <item>explicit <c>version</c> — authoritative; if the CDN has no catalog for that version
	/// <see cref="ComponentInfoResolution.MapResolvedFrom"/> maps to <c>environment-superset</c>
	/// (known version, approximate catalog) rather than <c>latest-fallback</c>;</item>
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

		// Neither an explicit version nor an environment was supplied, so there is nothing to probe:
		// a clear input gap (no-active-environment), not a probe error. Built via the shared factory so
		// the CLI verb and this MCP tool stay byte-identical on the no-flags fallback.
		return Task.FromResult(ComponentInfoResolution.CreateNoActiveEnvironmentFallback());
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
		string? resolvedFrom,
		string? resolvedFromReason = null) {
		return new ComponentInfoResponse {
			Success = true,
			Mode = "list",
			Count = entries.Count,
			Items = ComponentInfoGrouping.CreateItems(entries),
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};
	}

	internal static ComponentInfoResponse CreateDetailResponse(
		ComponentRegistryEntry entry,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? documentation,
		RegistryGlobalReferences? globalReferences,
		string? resolvedFromReason = null) {
		IReadOnlyDictionary<string, JsonElement>? mergedInputs = MergeBindings(globalReferences?.BaseInputs, entry.Inputs);
		ComponentReferencesResponse? references = BuildReferencesResponse(entry, globalReferences);
		return new ComponentInfoResponse {
			Success = true,
			Mode = "detail",
			Count = 1,
			ComponentType = entry.ComponentType,
			Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description,
			Synonyms = entry.Synonyms.Count == 0 ? null : entry.Synonyms,
			UseCases = entry.UseCases.Count == 0 ? null : entry.UseCases,
			WhenToUse = string.IsNullOrWhiteSpace(entry.WhenToUse) ? null : entry.WhenToUse,
			WhenNotToUse = string.IsNullOrWhiteSpace(entry.WhenNotToUse) ? null : entry.WhenNotToUse,
			AppliesToCustomEntities = entry.AppliesToCustomEntities,
			EntityCouplingNote = string.IsNullOrWhiteSpace(entry.EntityCouplingNote) ? null : entry.EntityCouplingNote,
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
			ResolvedFromReason = resolvedFromReason,
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
	[property: Description("Registered environment name to scope the catalog to its real platform version (probed via cliogate GetSysInfo). PREFER this — pass the same environment you edit pages on. Mutually exclusive with 'version'.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("version")]
	[property: Description("Explicit catalog version (3-part semver, e.g. '8.3.3') when the platform version is already known. Mutually exclusive with 'environment-name'.")]
	string? Version = null,

	[property: JsonPropertyName("uri")]
	[property: Description("Emergency fallback only: direct application URI when no environment is registered. Prefer 'environment-name'.")]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description("Emergency fallback only: login paired with 'uri'. Prefer 'environment-name'.")]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description("Emergency fallback only: password paired with 'uri'. Prefer 'environment-name'.")]
	string? Password = null
) {
	/// <summary>
	/// Overflow bag for any request field that does not bind to a declared kebab-case parameter —
	/// most importantly the camelCase / snake_case spellings (<c>componentType</c>,
	/// <c>schemaType</c>, <c>environmentName</c>) an LLM tends to emit. The tool inspects this in
	/// <c>GetComponentInfo</c> and rejects mis-spelled fields with a rename hint instead of letting
	/// an unbound <c>component-type</c> silently degrade a detail request into a full catalog dump.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

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
	/// Gets or sets the alternate names a user might use for this component (Solution A,
	/// ENG-91571). Surfaced on detail responses so the agent can confirm a match by an
	/// informal name; also folded into the list-mode keyword search. Omitted when empty.
	/// </summary>
	[JsonPropertyName("synonyms")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? Synonyms { get; init; }

	/// <summary>
	/// Gets or sets the concrete scenarios this component fits (Solution A, ENG-91571).
	/// Omitted when the producer published none.
	/// </summary>
	[JsonPropertyName("useCases")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? UseCases { get; init; }

	/// <summary>
	/// Gets or sets the one-line "pick this when…" selection guidance (Solution A,
	/// ENG-91571). The primary signal for choosing between visually similar components
	/// (e.g. <c>crt.Gallery</c> vs <c>crt.DataGrid</c> vs <c>crt.List</c>). Omitted when
	/// the producer published none.
	/// </summary>
	[JsonPropertyName("whenToUse")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? WhenToUse { get; init; }

	/// <summary>
	/// Gets or sets the one-line "do NOT pick this when…" anti-pattern guidance
	/// (Solution A, ENG-91571), typically naming the component to use instead. Omitted
	/// when the producer published none.
	/// </summary>
	[JsonPropertyName("whenNotToUse")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? WhenNotToUse { get; init; }

	/// <summary>
	/// Gets or sets the applicability constraint (Solution A, ENG-91571). <c>false</c>
	/// flags an entity-coupled component that cannot be built on a custom entity (see
	/// <see cref="EntityCouplingNote"/> for why); omitted when the producer stated no
	/// constraint (treat as unconstrained).
	/// </summary>
	[JsonPropertyName("appliesToCustomEntities")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? AppliesToCustomEntities { get; init; }

	/// <summary>
	/// Gets or sets the human-readable reason a restrictive
	/// <see cref="AppliesToCustomEntities"/> applies (Solution A, ENG-91571). Omitted
	/// when the producer published none.
	/// </summary>
	[JsonPropertyName("entityCouplingNote")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? EntityCouplingNote { get; init; }

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
	/// (text/number/checkbox/lookup/etc. inputs). Tells the agent what any <c>operation:"insert"</c> of
	/// this component type requires: a viewConfigDiff entry (carrying the label), a matching
	/// viewModelConfigDiff attribute declaration, and a label that is auto-provided (its key equals the
	/// DS-bound binding attribute) or registered.
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
	/// Permitted values:
	/// <list type="bullet">
	/// <item><c>"environment"</c> — version resolved from cliogate GetSysInfo and the catalog
	/// matched that exact version; treat as authoritative.</item>
	/// <item><c>"environment-superset"</c> — version resolved from the environment (known), but
	/// the CDN had no catalog for that version so <c>latest</c> was served; a soft caveat is
	/// emitted in <see cref="VersionWarning"/>. Verify that critical component types exist
	/// before proceeding.</item>
	/// <item><c>"latest-fallback"</c> — environment unknown, probe failed, or version
	/// unparseable; <c>latest</c> is a superset of the true environment. Hard stop: confirm
	/// version with the user before any modification.</item>
	/// </list>
	/// </summary>
	[JsonPropertyName("resolvedFrom")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ResolvedFrom { get; init; }

	/// <summary>
	/// Gets the human-readable caveat emitted whenever <see cref="ResolvedFrom"/> is
	/// <c>"environment-superset"</c> (soft caveat: version known, catalog approximate) or
	/// <c>"latest-fallback"</c> (hard stop: version unknown). Derived from
	/// <see cref="ResolvedFrom"/> so every response shape (list / detail / not-found, web
	/// only) carries it without each branch having to set it, and so the MCP tool and CLI
	/// verb stay in lockstep. Omitted from the wire shape when <see cref="ResolvedFrom"/>
	/// is <c>"environment"</c> (exact catalog match) or when the flavor reports no version
	/// markers (mobile). See <see cref="ComponentInfoResolution.LatestFallbackWarning"/> and
	/// <see cref="ComponentInfoResolution.EnvironmentSupersetWarning"/> for the warning text.
	/// </summary>
	[JsonPropertyName("versionWarning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? VersionWarning => ComponentInfoResolution.GetVersionWarning(ResolvedFrom);

	/// <summary>
	/// Gets the machine-readable hard-stop flag, emitted as <c>true</c> only when <see cref="ResolvedFrom"/>
	/// is <c>"latest-fallback"</c> (the target platform version could not be determined). Unlike the prose
	/// <see cref="VersionWarning"/> — which an agent can skip — this flag exists so the client can branch on it
	/// programmatically: it MUST tell the user the version is unknown and request explicit confirmation before
	/// generating an implementation plan, instead of silently assuming the <c>latest</c> superset. Derived from
	/// <see cref="ResolvedFrom"/> (the same single source as <see cref="VersionWarning"/>) so every response
	/// shape and both the MCP tool and CLI verb stay in lockstep. Omitted from the wire shape on every other
	/// tier (<c>environment</c> / <c>environment-superset</c>), where the version is known and no gate applies.
	/// </summary>
	[JsonPropertyName("requiresVersionConfirmation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? RequiresVersionConfirmation =>
		ComponentInfoResolution.RequiresVersionConfirmation(ResolvedFrom) ? true : null;

	/// <summary>
	/// Gets the kebab-case reason the version fell back to <c>latest</c>, present only alongside
	/// <see cref="RequiresVersionConfirmation"/> on the <c>latest-fallback</c> tier. Lets the client
	/// distinguish a transient <c>"probe-error"</c> (a retry or a reachable environment may resolve the
	/// version) from a genuinely undeterminable one (<c>"no-active-environment"</c>,
	/// <c>"core-version-missing"</c>, <c>"core-version-unparseable"</c>) and tailor what it asks the user.
	/// Set by the response factories from <see cref="PlatformVersionResolution.Reason"/>; omitted on every
	/// non-fallback tier.
	/// </summary>
	[JsonPropertyName("resolvedFromReason")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ResolvedFromReason { get; init; }

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
	/// Gets or sets alternate names a user might use for this component (Solution A,
	/// ENG-91571) — produced from repeatable <c>@synonym</c> JSDoc tags on the component
	/// class. Feeds the list-mode keyword search so a prompt like "table" finds
	/// <c>crt.DataGrid</c>. Empty when the producer published no synonyms.
	/// </summary>
	[JsonPropertyName("synonyms")]
	public IReadOnlyList<string> Synonyms { get; init; } = [];

	/// <summary>
	/// Gets or sets concrete scenarios the component fits (Solution A, ENG-91571) —
	/// produced from repeatable <c>@useCase</c> JSDoc tags. Surfaced on the detail
	/// response and folded into the list-mode keyword search. Empty when none published.
	/// </summary>
	[JsonPropertyName("useCases")]
	public IReadOnlyList<string> UseCases { get; init; } = [];

	/// <summary>
	/// Gets or sets the one-line "pick this when…" selection guidance (Solution A,
	/// ENG-91571) — the <c>@whenToUse</c> JSDoc tag. Helps the agent choose the right
	/// component (e.g. <c>crt.Gallery</c> vs <c>crt.DataGrid</c>). Null when not published.
	/// </summary>
	[JsonPropertyName("whenToUse")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? WhenToUse { get; init; }

	/// <summary>
	/// Gets or sets the one-line "do NOT pick this when…" anti-pattern guidance
	/// (Solution A, ENG-91571) — the <c>@whenNotToUse</c> JSDoc tag, typically naming the
	/// component to use instead. Null when not published.
	/// </summary>
	[JsonPropertyName("whenNotToUse")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? WhenNotToUse { get; init; }

	/// <summary>
	/// Gets or sets the applicability constraint (Solution A, ENG-91571) — the
	/// <c>@appliesToCustomEntities</c> JSDoc tag. <c>false</c> marks an entity-coupled
	/// component that cannot be built on a custom entity; <c>null</c> when the producer
	/// stated no constraint (treat as unconstrained).
	/// </summary>
	[JsonPropertyName("appliesToCustomEntities")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? AppliesToCustomEntities { get; init; }

	/// <summary>
	/// Gets or sets the human-readable reason for a restrictive
	/// <see cref="AppliesToCustomEntities"/> (Solution A, ENG-91571) — the
	/// <c>@entityCouplingNote</c> JSDoc tag. Null when not published.
	/// </summary>
	[JsonPropertyName("entityCouplingNote")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? EntityCouplingNote { get; init; }

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
