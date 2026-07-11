using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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
	IToolCommandResolver commandResolver) {

	internal const string ToolName = "get-component-info";

	/// <summary>
	/// Canonical kebab-case name of the component selector parameter — the JSON property bound to
	/// <see cref="ComponentInfoArgs.ComponentType"/>. Every <see cref="LegacyAliases"/> entry that
	/// redirects a mis-spelled selector points at this single value, so the alias target lives in
	/// one place instead of being repeated as a literal per entry.
	/// </summary>
	internal const string ComponentTypeParameterName = "component-type";
	internal const string ResolvedFromEnvironment = ComponentInfoResolution.ResolvedFromEnvironment;
	internal const string ResolvedFromLatestFallback = ComponentInfoResolution.ResolvedFromLatestFallback;
	internal const string SchemaTypeMobile = "mobile";
	internal const string DocumentationSeparator = ComponentDocumentationLoader.DocumentationSeparator;

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
	/// Actionable hint surfaced next to <c>compositeOnly: true</c> on a component detail
	/// response. The component has no standalone Designer toolbar presence, so it should
	/// preferentially be built via a composite that assembles it. Composites have no
	/// <c>componentType</c> and carry no machine-readable list of their member components
	/// (a <see cref="CompositeDefinition"/> is only <c>caption</c>/<c>description</c>/<c>docs</c>),
	/// so clio cannot resolve the owning composite for the agent — the hint instead encodes the
	/// decision rule: discover composites in list mode, confirm membership by reading each
	/// candidate's recipe (<c>composite="&lt;caption&gt;"</c>), build the composite when one
	/// assembles this component, and otherwise fall back to building the component directly —
	/// only when its own applicability (<c>appliesToCustomEntities</c> / <c>entityCouplingNote</c>) allows.
	/// </summary>
	internal const string CompositeOnlyHintText =
		"This component has no standalone Designer toolbar presence. First look for a composite that assembles it: "
		+ "call get-component-info in list mode, scan the 'composites' array, and fetch each plausible candidate's "
		+ "recipe with get-component-info composite=\"<caption>\" to confirm it uses this component. "
		+ "If a composite assembles this component, build that composite and follow its recipe. "
		+ "If no composite assembles it, build this component directly as a fallback — but only if this "
		+ "component's own applicability allows it: check appliesToCustomEntities / entityCouplingNote on this "
		+ "response first, and do not build it standalone on an entity those fields exclude.";

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
		"use whenToUse / whenNotToUse to choose between visually similar components instead of guessing. " +
		"The list response also returns 'composites' — pre-built combinations of several components that have NO componentType of their own " +
		"— read the returned 'composites' array for the available captions. When a user wants one of these, do NOT hand-build it from raw types: pass composite='<caption>' " +
		"to get its assembly recipe. A component flagged compositeOnly:true has no standalone toolbar presence — prefer the composite that assembles it and build that; build the component directly only as a fallback when no composite assembles it AND its own applicability (appliesToCustomEntities / entityCouplingNote) allows. " +
		"IMPORTANT: pass environment-name to scope the catalog to the target environment's actual platform version — " +
		"otherwise results come from the 'latest' catalog, a SUPERSET of every GA version, and may list components " +
		"(e.g. a freshly shipped crt.Switch) that do NOT exist in that environment and will fail to render at runtime. " +
		"When resolvedFrom is 'latest-fallback' the version is unknown and the response sets requiresVersionConfirmation: true — do not silently assume the component set: tell the user the version is unknown and request confirmation before proceeding (resolvedFromReason says whether a retry might help). " +
		"When you target a page-editing environment, pass the same environment-name here. " +
		"If schema-type is omitted, defaults to the web component catalog (excludes mobile-only components such as crt.Toggle and crt.BarcodeScanner). " +
		"Use schema-type: 'mobile' to retrieve mobile-specific components — the mobile registry is separate and excludes web-only types.")]
	public async Task<ComponentInfoResponse> GetComponentInfo(
		[Description("component-type (optional; omit or 'list' for the catalog of components AND composites), composite (optional; a composite Designer-element caption such as 'Expanded list' — returns its assembly docs, mutually exclusive with component-type), search (optional, filters both). schema-type 'web' (default) or 'mobile'. environment-name preferred (mutually exclusive with version). uri/login/password fallback only.")]
		[Required] ComponentInfoArgs args,
		CancellationToken cancellationToken = default) {
		string? legacyAliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: component-type, composite, search, schema-type, environment-name, version, uri, login, password.");
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
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
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
		["componentType"] = ComponentTypeParameterName,
		["component_type"] = ComponentTypeParameterName,
		// 'component-name' (plus its camelCase/snake_case spellings) is the wrong-WORD mistake an LLM
		// reaches for when it expects the selector to be named after the component "name" rather than
		// its "type" (observed in the field). Reject it with the same precise rename hint as the casing
		// variants above instead of letting it fall through to the generic "Unknown args" message — the
		// contract advertises the identical aliases (see ToolContractGetTool.BuildComponentInfo).
		["component-name"] = ComponentTypeParameterName,
		["componentName"] = ComponentTypeParameterName,
		["component_name"] = ComponentTypeParameterName,
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

		bool hasComposite = !string.IsNullOrWhiteSpace(args.Composite);
		bool hasComponentType = !string.IsNullOrWhiteSpace(args.ComponentType)
			&& !string.Equals(args.ComponentType, "list", StringComparison.OrdinalIgnoreCase);
		if (hasComposite && hasComponentType) {
			// Mode "list" mirrors the sibling version/environment mutual-exclusivity guard
			// (and every other argument-validation error) so callers branch on one shape.
			return ComponentInfoResponseFactory.CreateMutualExclusivityError(
				"'composite' and 'component-type' are mutually exclusive. Pass 'composite' for a composite "
					+ "Designer element, or 'component-type' for a single component.",
				state.ResolvedVersion, resolvedFrom, resolvedFromReason);
		}
		if (hasComposite) {
			return await BuildCompositeDetailAsync(
				args.Composite!.Trim(), state, isMobile, resolvedFrom, resolvedFromReason, cancellationToken).ConfigureAwait(false);
		}

		if (string.IsNullOrWhiteSpace(args.ComponentType)
			|| string.Equals(args.ComponentType, "list", StringComparison.OrdinalIgnoreCase)) {
			IReadOnlyList<ComponentRegistryEntry> filtered = ComponentInfoGrouping.FilterEntries(state.Entries, args.Search);
			IReadOnlyList<CompositeDefinition> filteredComposites =
				ComponentInfoGrouping.FilterComposites(state.Composites, args.Search);
			return CreateListResponse(filtered, filteredComposites, state.ResolvedVersion, resolvedFrom, resolvedFromReason);
		}

		if (state.Lookup.TryGetValue(args.ComponentType.Trim(), out ComponentRegistryEntry? entry)) {
			string? documentation = await LoadDocumentationAsync(entry, state.ResolvedVersion, cancellationToken).ConfigureAwait(false);
			return CreateDetailResponse(entry, state.ResolvedVersion, resolvedFrom, documentation, state.GlobalReferences, resolvedFromReason);
		}

		string requestedType = args.ComponentType.Trim();
		// Name/description-first resolution: when the exact type id misses, the requested label may
		// be a composite ("Expanded list") the agent reached for as if it were a component. The shared
		// factory matches components by name/description first, then routes to composite="<caption>"
		// when the label names a composite. Shared with the CLI verb so both resolve identically.
		return ComponentInfoResponseFactory.CreateComponentNotFoundResponse(
			state.Entries, state.Composites, requestedType, args.Search,
			state.ResolvedVersion, resolvedFrom, resolvedFromReason);
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
	/// arguments. Delegates to <see cref="IToolCommandResolver.Resolve{TCommand}(EnvironmentOptions)"/>
	/// so this (the only <c>hasEnvironment</c>-supplied) branch shares the same ENG-93208
	/// credential-passthrough seam every other resolver-routed tool uses: on an authorized HTTP
	/// passthrough request the header tenant wins and an explicit <c>environment-name</c>/<c>uri</c>
	/// is rejected before any named-registered-tenant lookup, instead of the root
	/// <see cref="ISettingsRepository.GetEnvironment(EnvironmentOptions)"/> probing the named
	/// environment's stored credentials directly. Stdio and registered-environment <c>mcp-http</c>
	/// keep resolving exactly as before — the resolver falls through to the same
	/// registered-environment lookup/fill when no credential context is active.
	/// </summary>
	private EnvironmentSettings ResolveEnvironmentSettings(ComponentInfoArgs args) {
		EnvironmentOptions options = new() {
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return commandResolver.Resolve<EnvironmentSettings>(options);
	}

	private static bool IsMobile(string? schemaType) =>
		string.Equals(schemaType, SchemaTypeMobile, StringComparison.OrdinalIgnoreCase);

	private static ComponentInfoResponse CreateListResponse(
		IReadOnlyList<ComponentRegistryEntry> entries,
		IReadOnlyList<CompositeDefinition> composites,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? resolvedFromReason = null) {
		IReadOnlyList<CompositeSummary> compositeItems = ComponentInfoGrouping.CreateCompositeItems(composites);
		return new ComponentInfoResponse {
			Success = true,
			Mode = "list",
			Count = entries.Count,
			Items = ComponentInfoGrouping.CreateItems(entries),
			Composites = compositeItems.Count == 0 ? null : compositeItems,
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};
	}

	/// <summary>
	/// Builds a <c>mode: "composite"</c> detail response for a composite Designer element
	/// looked up by caption (case-insensitive). On a hit the composite's markdown docs are
	/// fetched through the same docs pipeline as component documentation and concatenated
	/// into <see cref="ComponentInfoResponse.Documentation"/>. On a miss the response lists
	/// the known captions so the agent can correct the lookup or fall back to list mode.
	/// </summary>
	private async Task<ComponentInfoResponse> BuildCompositeDetailAsync(
		string caption,
		ComponentCatalogState state,
		bool isMobile,
		string? resolvedFrom,
		string? resolvedFromReason,
		CancellationToken cancellationToken) {
		CompositeDefinition? composite = ComponentInfoResponseFactory.FindComposite(state.Composites, caption);
		if (composite is null) {
			return ComponentInfoResponseFactory.CreateCompositeNotFoundResponse(
				state.Composites, caption, isMobile, state.ResolvedVersion, resolvedFrom, resolvedFromReason);
		}
		string? documentation = await ComponentDocumentationLoader
			.LoadAsync(docsClient, composite.Docs, state.ResolvedVersion, cancellationToken).ConfigureAwait(false);
		return ComponentInfoResponseFactory.CreateCompositeDetailResponse(
			composite, documentation, state.ResolvedVersion, resolvedFrom, resolvedFromReason);
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
			CompositeOnly = entry.CompositeOnly == true ? true : null,
			CompositeOnlyHint = entry.CompositeOnly == true ? CompositeOnlyHintText : null,
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
	[property: Description("Freedom UI component type, for example 'crt.TabContainer'. Omit or use 'list' to return the catalog (components AND composites).")]
	string? ComponentType = null,

	[property: JsonPropertyName("search")]
	[property: Description("Optional keyword filter applied in list mode (filters BOTH components and composites) and in not-found suggestions, for example 'tab'.")]
	string? Search = null,

	// Declared after `search` (not next to `component-type`) on purpose: the record's
	// positional order is part of its contract, and existing callers use the
	// `(component-type, search)` positional pair. `composite` is reached by name.
	[property: JsonPropertyName("composite")]
	[property: Description("Composite Designer element caption, for example 'Expanded list' or 'Next steps'. Returns the composite's assembly docs — a composite is a pre-built combination of several components, not a single component. Discover available captions via list mode. Mutually exclusive with 'component-type'.")]
	string? Composite = null,

	[property: JsonPropertyName("schema-type")]
	[property: Description("Component registry: 'web' (default) or 'mobile'.")]
	string? SchemaType = null,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered environment name; scopes the catalog to its real platform version. Preferred. Mutually exclusive with version.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("version")]
	[property: Description("Explicit catalog version (3-part semver). Mutually exclusive with environment-name.")]
	string? Version = null,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
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
/// Shared Solution A (ENG-91571) selection-metadata fields surfaced flat on both the public
/// <see cref="ComponentInfoResponse"/> detail payload and the registry-side
/// <see cref="ComponentRegistryEntry"/>. Declared once here so the identical
/// <c>whenToUse</c>/<c>whenNotToUse</c>/<c>appliesToCustomEntities</c>/<c>entityCouplingNote</c>
/// JSON fields are not copy-pasted across both types — System.Text.Json keeps them flat on every
/// derived type, so the JSON shape and the snapshot-guard binding behaviour are unchanged.
/// </summary>
public abstract class ComponentSelectionMetadata {
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
}

/// <summary>
/// Structured response from the <c>get-component-info</c> MCP tool.
/// </summary>
public sealed class ComponentInfoResponse : ComponentSelectionMetadata {
	/// <summary>
	/// Gets or sets whether the request completed successfully.
	/// </summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>
	/// Gets or sets the response mode: <c>detail</c>, <c>list</c>, or <c>composite</c>.
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
	/// Gets or sets the composite caption for <c>mode: "composite"</c> detail responses
	/// (the human-readable Designer label the composite was looked up by). Omitted on
	/// component detail/list responses.
	/// </summary>
	[JsonPropertyName("caption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Caption { get; init; }

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

	// WhenToUse / WhenNotToUse / AppliesToCustomEntities / EntityCouplingNote are inherited
	// from ComponentSelectionMetadata (Solution A, ENG-91571).

	/// <summary>
	/// Gets or sets the composite-only flag on a component detail response. <c>true</c>
	/// means the component has no standalone Designer toolbar presence and should preferentially
	/// be built via a composite that assembles it, falling back to building the component directly
	/// when none does (see <see cref="CompositeOnlyHint"/> for the decision rule); omitted otherwise.
	/// </summary>
	[JsonPropertyName("compositeOnly")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? CompositeOnly { get; init; }

	/// <summary>
	/// Gets or sets the actionable hint paired with <see cref="CompositeOnly"/> on a
	/// component detail response — tells the agent not to insert the component standalone
	/// and how to find the composite to assemble instead. Omitted unless the component is
	/// composite-only.
	/// </summary>
	[JsonPropertyName("compositeOnlyHint")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? CompositeOnlyHint { get; init; }

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
	/// Gets or sets the composite Designer elements for list responses — pre-built
	/// combinations of components (e.g. "Expanded list", "Next steps") that have no
	/// <c>componentType</c> of their own. Surfaced alongside <see cref="Items"/> so the
	/// proactive catalog list reveals them in one call; fetch a composite's assembly docs
	/// with <c>get-component-info composite="&lt;caption&gt;"</c>. Omitted when the
	/// registry declares no composites.
	/// </summary>
	[JsonPropertyName("composites")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<CompositeSummary>? Composites { get; init; }

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
	/// Gets the disambiguation flag for a <c>mode: "composite"</c> detail response: emitted
	/// as <c>true</c> only when the composite DECLARES docs but none could be loaded (a
	/// transient docs CDN/cache failure), so <see cref="Documentation"/> is null. Lets the
	/// agent tell a fetch failure (retry may help) from a composite that genuinely ships no
	/// docs (where the field is omitted). Never set on component detail or list responses.
	/// </summary>
	[JsonPropertyName("documentationUnavailable")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? DocumentationUnavailable { get; init; }

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

	/// <summary>
	/// Gets or sets the composite-only marker, emitted as <c>true</c> only for components
	/// that must be assembled via a composite rather than placed standalone (see
	/// <see cref="ComponentRegistryEntry.CompositeOnly"/>). Lets the agent spot
	/// non-standalone components directly in the catalog list. Omitted otherwise.
	/// </summary>
	[JsonPropertyName("compositeOnly")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? CompositeOnly { get; init; }
}

/// <summary>
/// Compact list item for the composites section of the <c>get-component-info</c> list
/// response. Carries just enough to discover and pick a composite; fetch its assembly
/// docs with <c>get-component-info composite="&lt;caption&gt;"</c>.
/// </summary>
public sealed class CompositeSummary {
	/// <summary>Gets or sets the composite's Designer caption (its lookup key).</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; } = string.Empty;

	/// <summary>Gets or sets the one-line composite description. Omitted when absent.</summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; init; }
}

/// <summary>
/// Curated Freedom UI component definition stored in the shipped registry.
/// </summary>
public sealed class ComponentRegistryEntry : ComponentSelectionMetadata {
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
	/// Gets or sets whether this component has NO standalone Designer toolbar presence —
	/// its <c>@CrtInterfaceDesignerItem</c> declares no <c>toolbarConfig</c>, so it is only
	/// ever used as part of a composite (see <see cref="ComponentRegistryEnvelope.Composites"/>),
	/// never dropped on its own. The producer emits <c>true</c> only for such components and
	/// omits the key otherwise, so <see langword="null"/> means "normal standalone component".
	/// Surfaced verbatim on the detail/list response so AI never inserts it standalone.
	/// </summary>
	[JsonPropertyName("compositeOnly")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? CompositeOnly { get; init; }

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

	// WhenToUse / WhenNotToUse / AppliesToCustomEntities / EntityCouplingNote are inherited
	// from ComponentSelectionMetadata (Solution A, ENG-91571) — produced from the
	// @whenToUse / @whenNotToUse / @appliesToCustomEntities / @entityCouplingNote JSDoc tags.

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

	/// <summary>
	/// Composite Designer elements (e.g. "Expanded list", "Next steps") — pre-built
	/// combinations of several real components that have no <c>@CrtViewElement</c> class
	/// of their own, so they are NOT in <see cref="Components"/>. Each carries a
	/// human-readable <c>caption</c>, an optional <c>description</c>, and the markdown
	/// <c>docs</c> describing how to assemble it. Omitted by producers that predate the
	/// feature, so older registries deserialise with <see langword="null"/> here.
	/// </summary>
	[JsonPropertyName("composites")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CompositeDefinition[]? Composites { get; init; }

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
/// A composite Designer element from the registry's top-level <c>composites</c> array —
/// a pre-built combination of several components (e.g. "Expanded list" = an expansion
/// panel hosting a data grid plus a toolbar). Composites have no <c>componentType</c>;
/// they are keyed by <see cref="Caption"/> and described entirely by their <see cref="Docs"/>.
/// </summary>
public sealed class CompositeDefinition {
	/// <summary>Human-readable Designer label, e.g. "Approval list". The composite's key.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; init; } = string.Empty;

	/// <summary>One-line "what this is / when to use it" guidance. Omitted when the producer supplies none.</summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; init; }

	/// <summary>
	/// Markdown doc paths (relative to <c>/api/mcp/{version}/</c>, e.g.
	/// <c>"docs/expansion-panel-approval-list.component.md"</c>) describing how to assemble
	/// the composite. Fetched lazily on a composite-detail request, exactly like a
	/// component's <see cref="ComponentReferences.Docs"/>.
	/// </summary>
	[JsonPropertyName("docs")]
	public IReadOnlyList<string> Docs { get; init; } = [];

	/// <summary>
	/// Strict-mode catch-all for any new per-composite producer key. The snapshot guard
	/// test fails when this is non-empty, mirroring the component-entry contract.
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
