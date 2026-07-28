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
/// MCP tool surface for curated Freedom UI request metadata (<c>crt.*Request</c> types
/// wired through <c>RequestBindingConfig</c> outputs such as a button's <c>clicked</c>).
/// OOTB button-action requests initiative (ENG-93187).
/// </summary>
/// <remarks>
/// Version resolution mirrors <see cref="ComponentInfoTool"/> 1:1: the target catalog
/// version is driven entirely by the per-call arguments — never by ambient server state.
/// An explicit <c>version</c> wins; otherwise an <c>environment-name</c>/<c>uri</c>
/// triggers the platform-version probe; with neither, the tool honestly reports
/// <c>latest-fallback</c> and surfaces <see cref="RequestInfoResponse.VersionWarning"/>.
/// The academy CDN serves <c>RequestRegistry.json</c>; for offline iteration point the registry
/// at a local file via <c>CLIO_REQUEST_REGISTRY_LOCAL_FILE</c> (the Tier-0 override read before cache/CDN).
/// </remarks>
[McpServerToolType]
public sealed class RequestInfoTool(
	IRequestInfoCatalog catalog,
	IMobileRequestInfoCatalog mobileCatalog,
	IComponentRegistryDocsClient docsClient,
	IPlatformVersionResolverFactory resolverFactory,
	IToolCommandResolver commandResolver) {

	internal const string ToolName = "get-request-info";

	/// <summary>
	/// Canonical kebab-case name of the request selector parameter — the JSON property bound
	/// to <see cref="RequestInfoArgs.RequestType"/>. Every <see cref="LegacyAliases"/> entry
	/// that redirects a mis-spelled selector points at this single value.
	/// </summary>
	internal const string RequestTypeParameterName = "request-type";

	/// <summary>
	/// Cap on the "did you mean" shortlist a not-found response echoes, so it never returns
	/// the full catalog as "suggestions". Mirrors the component-info cap.
	/// </summary>
	private const int MaxNotFoundSuggestions = 8;

	/// <summary>
	/// Synthetic binding used to seed the type-definition closure with the wiring contract.
	/// Every request — parameters or not — is dispatched through a <c>RequestBindingConfig</c>
	/// binding on a view-element output, so the detail response always inlines that schema
	/// when the registry publishes it, keeping the response self-contained on wiring.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, JsonElement> WiringContractSeed =
		new Dictionary<string, JsonElement>(StringComparer.Ordinal) {
			["binding"] = JsonDocument.Parse("""{"type":"RequestBindingConfig"}""").RootElement.Clone()
		};

	/// <summary>
	/// Canonical kebab-case parameter names paired with the camelCase / snake_case / wrong-word
	/// spellings an LLM is most likely to emit. Mirrors <see cref="ComponentInfoTool"/>'s alias
	/// handling: rejection with a precise rename hint instead of silently dropping an unbound
	/// value and degrading a detail request into a full catalog dump.
	/// </summary>
	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["requestType"] = RequestTypeParameterName,
		["request_type"] = RequestTypeParameterName,
		["request-name"] = RequestTypeParameterName,
		["requestName"] = RequestTypeParameterName,
		["request_name"] = RequestTypeParameterName,
		["schemaType"] = "schema-type",
		["schema_type"] = "schema-type",
		["environmentName"] = "environment-name",
		["environment_name"] = "environment-name"
	};

	/// <summary>
	/// Returns the request catalog list or full metadata for a specific request type.
	/// </summary>
	/// <param name="args">Tool arguments that select either list or detail mode.</param>
	/// <param name="cancellationToken">Cancellation token propagated by the MCP host.</param>
	/// <returns>A structured response with a request list or a full request definition.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Get curated Freedom UI request metadata (crt.*Request types wired through request bindings such as a button's clicked) by request type, or list all cataloged requests. " +
		"PROACTIVELY list the catalog (omit request-type, or pass 'list') before wiring a button/menu action to a platform request, " +
		"so the request name and its params come from the catalog instead of memory. " +
		"Detail responses carry 'parameters' — the ONLY keys a page schema may pass via the binding's params block; an EMPTY parameters map means the request accepts NO parameters, do not invent any. " +
		"'baseParameters' are fields every request inherits from BaseRequest ($context, scopes, type) — they are platform-injected at dispatch time and must NEVER be passed via params. " +
		"'documentation' carries the authoring recipe (canonical wiring, pitfalls, checklist) when the producer published one. " +
		"IMPORTANT: pass environment-name to scope the catalog to the target environment's actual platform version — " +
		"otherwise results come from the 'latest' catalog, a SUPERSET of every GA version, and may list requests that do NOT exist in that environment. " +
		"When resolvedFrom is 'latest-fallback' the version is unknown and the response sets requiresVersionConfirmation: true — tell the user the version is unknown and request confirmation before proceeding (resolvedFromReason says whether a retry might help). " +
		"If schema-type is omitted, defaults to the web request catalog. " +
		"Use schema-type: 'mobile' when wiring a request on a MOBILE page — the mobile request registry is separate and scoped to only the requests available on Freedom UI mobile (their parameters can also differ from desktop). " +
		"Read get-guidance name=when-to-use-requests FIRST for the request-selection decision rules and the wiring discipline.")]
	public async Task<RequestInfoResponse> GetRequestInfo(
		[Description("request-type (optional; omit or 'list' for the catalog of requests), search (optional, filters the list and not-found suggestions). schema-type 'web' (default) or 'mobile'. environment-name preferred (mutually exclusive with version). uri/login/password fallback only.")]
		[Required] RequestInfoArgs args,
		CancellationToken cancellationToken = default) {
		string? legacyAliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: request-type, search, schema-type, environment-name, version, uri, login, password.");
		if (!string.IsNullOrWhiteSpace(legacyAliasError)) {
			return CreateErrorResponse(legacyAliasError);
		}
		return await ComponentInfoResolution.RunWithSchemaTypeWarningAsync(
			args.SchemaType,
			isMobile => BuildResponseAsync(args, isMobile, cancellationToken),
			CreateErrorResponse,
			(response, warning) => response.SchemaTypeWarning = warning).ConfigureAwait(false);
	}

	/// <summary>
	/// Single async pipeline behind the tool: argument guards → version resolution →
	/// catalog load → list / detail / not-found. Mirrors
	/// <see cref="ComponentInfoTool"/>'s pipeline so the response markers
	/// (<c>resolvedTargetVersion</c> / <c>resolvedFrom</c> / <c>versionWarning</c> /
	/// <c>requiresVersionConfirmation</c>) behave identically across both catalogs.
	/// </summary>
	private async Task<RequestInfoResponse> BuildResponseAsync(RequestInfoArgs args, bool isMobile, CancellationToken cancellationToken) {
		bool hasExplicitVersion = !string.IsNullOrWhiteSpace(args.Version);
		bool hasEnvironment = !string.IsNullOrWhiteSpace(args.EnvironmentName) || !string.IsNullOrWhiteSpace(args.Uri);
		if (hasExplicitVersion && hasEnvironment) {
			return CreateErrorResponse("'version' and 'environment-name'/'uri' are mutually exclusive. Pass one or neither.");
		}
		if (hasExplicitVersion && !PlatformVersionResolver.TryNormaliseToThreePartSemver(args.Version!, out _)) {
			return CreateErrorResponse(
				$"'version' value '{args.Version}' is not a valid platform version. Use a 3-part semver, for example '8.3.3'.");
		}

		PlatformVersionResolution versionResolution = await ResolveVersionAsync(args, hasExplicitVersion, hasEnvironment, cancellationToken)
			.ConfigureAwait(false);
		// The isMobile branch only picks the catalog source; version resolution, envelope parse,
		// documentation lazy-load, and the resolver markers are identical on both flavors — the same
		// symmetry get-component-info relies on to keep the response shape stable across schema-type.
		RequestCatalogState state = isMobile
			? await mobileCatalog.LoadAsync(versionResolution.ResolvedVersion, cancellationToken).ConfigureAwait(false)
			: await catalog.LoadAsync(versionResolution.ResolvedVersion, cancellationToken).ConfigureAwait(false);
		string resolvedFrom = ComponentInfoResolution.MapResolvedFrom(
			versionResolution.Source, versionResolution.ResolvedVersion, state.ResolvedVersion);
		string? resolvedFromReason = ComponentInfoResolution.GetFallbackReason(resolvedFrom, versionResolution.Reason);

		if (string.IsNullOrWhiteSpace(args.RequestType)
			|| string.Equals(args.RequestType, "list", StringComparison.OrdinalIgnoreCase)) {
			IReadOnlyList<RequestRegistryEntry> filtered = FilterEntries(state.Entries, args.Search);
			return new RequestInfoResponse {
				Success = true,
				Mode = "list",
				Count = filtered.Count,
				Items = CreateItems(filtered),
				ResolvedTargetVersion = state.ResolvedVersion,
				ResolvedFrom = resolvedFrom,
				ResolvedFromReason = resolvedFromReason
			};
		}

		string requestedType = args.RequestType.Trim();
		if (state.Lookup.TryGetValue(requestedType, out RequestRegistryEntry? entry)) {
			string? documentation = await ComponentDocumentationLoader
				.LoadAsync(docsClient, entry.References?.Docs, state.ResolvedVersion, cancellationToken).ConfigureAwait(false);
			return CreateDetailResponse(entry, state.ResolvedVersion, resolvedFrom, documentation, state.GlobalReferences, resolvedFromReason);
		}

		return CreateNotFoundResponse(state.Entries, requestedType, args.Search, state.ResolvedVersion, resolvedFrom, resolvedFromReason);
	}

	/// <summary>
	/// Selects the target catalog version from the per-call arguments, in the same
	/// resolution order as <see cref="ComponentInfoTool"/>: explicit <c>version</c> →
	/// environment probe → <c>latest</c> with the honest <c>latest-fallback</c> marker.
	/// </summary>
	private Task<PlatformVersionResolution> ResolveVersionAsync(
		RequestInfoArgs args,
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

		return Task.FromResult(ComponentInfoResolution.CreateNoActiveEnvironmentFallback());
	}

	/// <summary>
	/// Builds the <see cref="EnvironmentSettings"/> for the cliogate probe from the per-call
	/// arguments. Delegates to <see cref="IToolCommandResolver.Resolve{TCommand}(EnvironmentOptions)"/>
	/// so this (the only <c>hasEnvironment</c>-supplied) branch shares the same ENG-93208
	/// credential-passthrough seam every other resolver-routed tool uses — mirroring
	/// <see cref="ComponentInfoTool"/>: on an authorized HTTP passthrough request the header tenant
	/// wins and an explicit <c>environment-name</c>/<c>uri</c> is rejected before any
	/// named-registered-tenant lookup, instead of the root
	/// <see cref="ISettingsRepository.GetEnvironment(EnvironmentOptions)"/> probing the named
	/// environment's stored credentials directly. Stdio and registered-environment <c>mcp-http</c>
	/// keep resolving exactly as before — the resolver falls through to the same
	/// registered-environment lookup/fill when no credential context is active.
	/// </summary>
	private EnvironmentSettings ResolveEnvironmentSettings(RequestInfoArgs args) {
		EnvironmentOptions options = new() {
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return commandResolver.Resolve<EnvironmentSettings>(options);
	}

	/// <summary>
	/// Builds the detail response for a catalog hit. Unlike the component catalog's
	/// <c>baseInputs</c> — which are authorable and therefore merged flat into
	/// <c>inputs</c> — the request catalog's <c>baseParameters</c> are platform-injected
	/// (<c>$context</c>, <c>scopes</c>, <c>type</c>) and are surfaced as a SEPARATE field,
	/// so an AI consumer never learns to author them through the binding's <c>params</c>.
	/// </summary>
	internal static RequestInfoResponse CreateDetailResponse(
		RequestRegistryEntry entry,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? documentation,
		RequestGlobalReferences? globalReferences,
		string? resolvedFromReason = null) {
		IReadOnlyDictionary<string, JsonElement>? typeDefinitions = TypeReferenceClosure.Resolve(
			entry.Parameters,
			WiringContractSeed,
			entry.References?.TypeDefinitions,
			globalReferences?.TypeDefinitions);
		bool declaresDocs = entry.References?.Docs is { Count: > 0 };
		bool documentationMissing = string.IsNullOrEmpty(documentation);
		return new RequestInfoResponse {
			Success = true,
			Mode = "detail",
			Count = 1,
			RequestType = entry.RequestType,
			Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description,
			Parameters = entry.Parameters,
			BaseParameters = globalReferences?.BaseParameters is { Count: > 0 } baseParameters ? baseParameters : null,
			References = typeDefinitions is null ? null : new RequestReferencesResponse { TypeDefinitions = typeDefinitions },
			Documentation = documentationMissing ? null : documentation,
			DocumentationUnavailable = declaresDocs && documentationMissing ? true : null,
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};
	}

	/// <summary>
	/// Builds the not-found response for an unknown <c>request-type</c>: name/description
	/// matches first (the agent typically reaches for a human label), falling back to the
	/// closest-by-distance shortlist for typo tolerance. Both paths are capped so the
	/// response never echoes the full catalog as "suggestions".
	/// </summary>
	private static RequestInfoResponse CreateNotFoundResponse(
		IReadOnlyList<RequestRegistryEntry> entries,
		string requestedType,
		string? search,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? resolvedFromReason) {
		IReadOnlyList<RequestRegistryEntry> nameMatches = FilterEntries(entries, requestedType);
		bool hasNameMatch = nameMatches.Count > 0;
		IReadOnlyList<RequestRegistryEntry> suggestions = hasNameMatch
			? nameMatches.Take(MaxNotFoundSuggestions).ToArray()
			: SuggestForUnknown(entries, requestedType, search, MaxNotFoundSuggestions);

		string error = hasNameMatch
			? $"'{requestedType}' is not a request type. "
				+ $"Showing {suggestions.Count} request(s) matching '{requestedType}' by name/description — pass the correct requestType, "
				+ "or omit 'request-type' to list the full catalog."
			: $"'{requestedType}' is not a request type. "
				+ $"Showing the {suggestions.Count} closest known type(s) — pass the correct requestType, "
				+ "or omit 'request-type' to list the full catalog.";

		return new RequestInfoResponse {
			Success = false,
			Mode = "list",
			Error = error,
			Count = suggestions.Count,
			Items = CreateItems(suggestions),
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};
	}

	private static RequestInfoResponse CreateErrorResponse(string error) =>
		new() {
			Success = false,
			Mode = "list",
			Error = error,
			Count = 0,
			Items = []
		};

	/// <summary>
	/// Case-insensitive keyword filter over the positive selection signals: request type,
	/// description, parameter keys, and the well-known string fields inside each parameter
	/// schema (<c>type</c>, <c>description</c>, enum <c>values</c>). Mirrors the component
	/// catalog's search semantics on the surface the request registry actually has.
	/// </summary>
	internal static IReadOnlyList<RequestRegistryEntry> FilterEntries(
		IReadOnlyList<RequestRegistryEntry> entries, string? search) {
		if (string.IsNullOrWhiteSpace(search)) {
			return entries;
		}
		string query = search.Trim();
		return entries.Where(entry => Matches(entry, query)).ToArray();
	}

	/// <summary>
	/// Returns a bounded closest-by-distance shortlist for an unknown request type
	/// (case-insensitive Levenshtein, ties broken alphabetically), optionally narrowed by
	/// the same keyword filter as list mode.
	/// </summary>
	internal static IReadOnlyList<RequestRegistryEntry> SuggestForUnknown(
		IReadOnlyList<RequestRegistryEntry> entries, string? requestType, string? search, int max) {
		IReadOnlyList<RequestRegistryEntry> pool = string.IsNullOrWhiteSpace(search)
			? entries
			: FilterEntries(entries, search);
		string target = (requestType ?? string.Empty).Trim();
		return pool
			.OrderBy(entry => McpToolArgumentSupport.LevenshteinDistance(entry.RequestType, target))
			.ThenBy(entry => entry.RequestType, StringComparer.OrdinalIgnoreCase)
			.Take(Math.Max(0, max))
			.ToArray();
	}

	/// <summary>
	/// Projects entries to compact list items ordered alphabetically by request type.
	/// </summary>
	internal static IReadOnlyList<RequestInfoListItem> CreateItems(IReadOnlyList<RequestRegistryEntry> entries) {
		return entries
			.OrderBy(entry => entry.RequestType, StringComparer.OrdinalIgnoreCase)
			.Select(entry => new RequestInfoListItem {
				RequestType = entry.RequestType,
				Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description
			})
			.ToArray();
	}

	private static bool Matches(RequestRegistryEntry entry, string query) {
		return ContainsCi(entry.RequestType, query)
			|| ContainsCi(entry.Description, query)
			|| ParametersMatch(entry.Parameters, query);
	}

	/// <summary>
	/// Searches the <c>parameters</c> dictionary for a query match. The values are
	/// <see cref="JsonElement"/> blobs whose schema is owned by the producer, so the
	/// matcher only looks at well-known string fields (<c>type</c>, <c>description</c>,
	/// <c>values</c>) — predictable search that survives producer-side schema additions.
	/// </summary>
	private static bool ParametersMatch(IReadOnlyDictionary<string, JsonElement>? parameters, string query) {
		if (parameters is not { Count: > 0 }) {
			return false;
		}
		foreach (KeyValuePair<string, JsonElement> parameter in parameters) {
			if (ContainsCi(parameter.Key, query)) {
				return true;
			}
			if (parameter.Value.ValueKind != JsonValueKind.Object) {
				continue;
			}
			if (TryGetStringProperty(parameter.Value, "type", out string? type) && ContainsCi(type, query)) {
				return true;
			}
			if (TryGetStringProperty(parameter.Value, "description", out string? description) && ContainsCi(description, query)) {
				return true;
			}
			if (EnumValuesMatch(parameter.Value, query)) {
				return true;
			}
		}
		return false;
	}

	private static bool EnumValuesMatch(JsonElement value, string query) {
		if (!value.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array) {
			return false;
		}
		foreach (JsonElement enumValue in values.EnumerateArray()) {
			if (enumValue.ValueKind == JsonValueKind.String && ContainsCi(enumValue.GetString(), query)) {
				return true;
			}
		}
		return false;
	}

	private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value) {
		if (element.TryGetProperty(propertyName, out JsonElement property)
			&& property.ValueKind == JsonValueKind.String) {
			value = property.GetString();
			return true;
		}
		value = null;
		return false;
	}

	private static bool ContainsCi(string? value, string query) {
		return !string.IsNullOrWhiteSpace(value)
			&& value.Contains(query, StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
/// Arguments for the <c>get-request-info</c> MCP tool.
/// </summary>
public sealed record RequestInfoArgs(
	[property: JsonPropertyName("request-type")]
	[property: Description("Freedom UI request type, for example 'crt.ClosePageRequest'. Omit or use 'list' to return the catalog.")]
	string? RequestType = null,

	[property: JsonPropertyName("search")]
	[property: Description("Optional keyword filter applied in list mode and in not-found suggestions, for example 'close'.")]
	string? Search = null,

	// Declared after `search` (not next to `request-type`) so the record's positional order
	// stays backward-compatible: existing callers pass `request-type` positionally and reach
	// every other field by name. `schema-type` is reached by name.
	[property: JsonPropertyName("schema-type")]
	[property: Description("Request registry: 'web' (default) or 'mobile'. The mobile registry is separate and scoped to the requests available on Freedom UI mobile.")]
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
	/// Overflow bag for any request field that does not bind to a declared kebab-case
	/// parameter — most importantly the camelCase / snake_case spellings an LLM tends to
	/// emit. The tool inspects this and rejects mis-spelled fields with a rename hint
	/// instead of letting an unbound <c>request-type</c> silently degrade a detail request
	/// into a full catalog dump.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured response from the <c>get-request-info</c> MCP tool.
/// </summary>
public sealed class RequestInfoResponse {
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
	/// Gets or sets the number of returned requests.
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
	/// Gets or sets the request type for detail responses.
	/// </summary>
	[JsonPropertyName("requestType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? RequestType { get; init; }

	/// <summary>
	/// Gets or sets the request description for detail responses.
	/// </summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; init; }

	/// <summary>
	/// Gets or sets the request's authorable parameters — the only keys a page schema may
	/// pass through the binding's <c>params</c> block. Surfaced verbatim as
	/// forward-compatible <see cref="JsonElement"/> blobs. An EMPTY map is meaningful and
	/// deliberately kept on the wire: it tells the consumer the request accepts NO
	/// parameters, as opposed to "parameters unknown" (field absent on list items).
	/// </summary>
	[JsonPropertyName("parameters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }

	/// <summary>
	/// Gets or sets the fields every request inherits from <c>BaseRequest</c>
	/// (<c>type</c>, <c>$context</c>, <c>scopes</c>, …). Platform-injected at dispatch
	/// time — NEVER authored through the binding's <c>params</c>, which is why they are
	/// NOT merged into <see cref="Parameters"/> (the component catalog merges its
	/// authorable <c>baseInputs</c>; this catalog deliberately does not).
	/// </summary>
	[JsonPropertyName("baseParameters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? BaseParameters { get; init; }

	/// <summary>
	/// Gets or sets the named type schemas referenced by this request's surface — the
	/// transitive closure over <see cref="Parameters"/> type tokens plus the
	/// <c>RequestBindingConfig</c> wiring contract, resolved from the per-request and
	/// global type-definition bags. Omitted when the registry publishes no resolvable
	/// definitions.
	/// </summary>
	[JsonPropertyName("references")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RequestReferencesResponse? References { get; init; }

	/// <summary>
	/// Gets or sets the request list for list responses.
	/// </summary>
	[JsonPropertyName("items")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<RequestInfoListItem>? Items { get; init; }

	/// <summary>
	/// Gets or sets the long-form documentation associated with the request. Populated only
	/// on detail responses for requests whose registry entry lists files under
	/// <c>references.docs[]</c>; each file is fetched lazily through the shared docs
	/// CDN/cache pipeline and concatenated with <c>"\n\n---\n\n"</c> separators in registry
	/// order. Omitted when the request has no documentation or when every referenced file
	/// fails to fetch (see <see cref="DocumentationUnavailable"/>).
	/// </summary>
	[JsonPropertyName("documentation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Documentation { get; init; }

	/// <summary>
	/// Gets or sets the disambiguation flag emitted as <c>true</c> only when the request
	/// DECLARES docs but none could be loaded (a transient docs CDN/cache failure), so
	/// <see cref="Documentation"/> is omitted. Lets the agent tell a fetch failure (retry
	/// may help) from a request that genuinely ships no docs (where both fields are absent).
	/// </summary>
	[JsonPropertyName("documentationUnavailable")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? DocumentationUnavailable { get; init; }

	/// <summary>
	/// Gets or sets the platform version the catalog was filtered against. Same semantics
	/// as the component catalog's marker — see <see cref="ResolvedFrom"/>.
	/// </summary>
	[JsonPropertyName("resolvedTargetVersion")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ResolvedTargetVersion { get; init; }

	/// <summary>
	/// Gets or sets the resolver tier that produced <see cref="ResolvedTargetVersion"/>:
	/// <c>"environment"</c> (authoritative), <c>"environment-superset"</c> (version known,
	/// catalog approximate — soft caveat), or <c>"latest-fallback"</c> (version unknown —
	/// hard stop; see <see cref="RequiresVersionConfirmation"/>).
	/// </summary>
	[JsonPropertyName("resolvedFrom")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ResolvedFrom { get; init; }

	/// <summary>
	/// Gets the human-readable caveat derived from <see cref="ResolvedFrom"/> — identical
	/// derivation to the component catalog so both tools warn under exactly the same
	/// conditions. Omitted on the authoritative <c>environment</c> tier.
	/// </summary>
	[JsonPropertyName("versionWarning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? VersionWarning => ComponentInfoResolution.GetVersionWarning(ResolvedFrom);

	/// <summary>
	/// Gets or sets the caveat for an unrecognized <c>schema-type</c> value (the call falls back to the
	/// web request catalog and names the offending value); see
	/// <see cref="ComponentInfoResolution.ResolveSchemaType"/> for the exact semantics. Omitted for a valid
	/// selection (omitted / <c>web</c> / <c>mobile</c>).
	/// </summary>
	[JsonPropertyName("schemaTypeWarning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SchemaTypeWarning { get; set; }

	/// <summary>
	/// Gets the machine-readable hard-stop flag, emitted as <c>true</c> only on the
	/// <c>latest-fallback</c> tier: the target platform version could not be determined,
	/// so the agent must tell the user and request explicit confirmation before
	/// proceeding. Derived from <see cref="ResolvedFrom"/>, same single source as
	/// <see cref="VersionWarning"/>.
	/// </summary>
	[JsonPropertyName("requiresVersionConfirmation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? RequiresVersionConfirmation =>
		ComponentInfoResolution.RequiresVersionConfirmation(ResolvedFrom) ? true : null;

	/// <summary>
	/// Gets or sets the kebab-case reason the version fell back to <c>latest</c>, present
	/// only on the <c>latest-fallback</c> tier. Same wire tokens as the component catalog
	/// (<c>probe-error</c>, <c>no-active-environment</c>, …).
	/// </summary>
	[JsonPropertyName("resolvedFromReason")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ResolvedFromReason { get; init; }
}

/// <summary>
/// Nested <c>references</c> block on a <see cref="RequestInfoResponse"/> detail. Mirrors
/// the producer's wire shape 1:1.
/// </summary>
public sealed class RequestReferencesResponse {
	/// <summary>
	/// Gets or sets the named type schemas referenced by the request's surface. Each key
	/// is a TypeScript-like type name (e.g. <c>"RequestBindingConfig"</c>); each value is
	/// a forward-compatible <see cref="JsonElement"/> blob carrying the producer's schema
	/// for that type.
	/// </summary>
	[JsonPropertyName("typeDefinitions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonElement>? TypeDefinitions { get; init; }
}

/// <summary>
/// Compact list item for the <c>get-request-info</c> list response.
/// </summary>
public sealed class RequestInfoListItem {
	/// <summary>
	/// Gets or sets the Freedom UI request type.
	/// </summary>
	[JsonPropertyName("requestType")]
	public string RequestType { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the one-line request description. Omitted from JSON when the source
	/// payload does not carry one.
	/// </summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; init; }
}
