using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

[Verb("get-component-info",
	Aliases = ["component-info"],
	HelpText = "Get curated Freedom UI component metadata by component type or list the catalog.")]
public sealed class ComponentInfoCommandOptions : EnvironmentOptions {
	[Value(0, MetaName = "component-type", Required = false,
		HelpText = "Freedom UI component type (e.g. crt.TabContainer). Omit or pass 'list' to list the catalog.")]
	public string ComponentType { get; set; }

	[Option("search", Required = false,
		HelpText = "Keyword filter applied in list mode and in not-found suggestions.")]
	public string Search { get; set; }

	[Option("version", Required = false,
		HelpText = "Explicit catalog version to load (3-part semver, e.g. 8.3.4). Mutually exclusive with --environment. Default: latest.")]
	public string Version { get; set; }

	[Option("pretty", Required = false, Default = false,
		HelpText = "Render a human-readable text block on stdout instead of JSON.")]
	public bool Pretty { get; set; }

	[Option("schema-type", Required = false,
		HelpText = "Component registry to query: 'web' (default) or 'mobile'. Both flavors honor --version/--environment and share the CDN → cache fallback chain.")]
	public string SchemaType { get; set; }
}

/// <summary>
/// CLI entry point for the <c>get-component-info</c> verb. Wraps the same
/// <see cref="IComponentInfoCatalog"/> the MCP tool uses, then prints either the JSON shape
/// of <see cref="ComponentInfoResponse"/> (default — pipe-friendly, identical to MCP) or a
/// human-readable rendering (--pretty).
/// </summary>
public sealed class ComponentInfoCommand {
	private const string SchemaTypeMobile = "mobile";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		// Pin LF so the verb produces identical bytes on Windows and Unix runners —
		// AI clients diff output across hosts, and the CRLF default of Utf8JsonWriter
		// on Windows would otherwise change every line. Pipe-friendly for `jq`.
		NewLine = "\n",
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new Clio.Command.McpServer.Tools.CompactPrimitiveArrayJsonElementConverter() }
	};

	private readonly IComponentInfoCatalog _catalog;
	private readonly IMobileComponentInfoCatalog _mobileCatalog;
	private readonly IComponentRegistryDocsClient _docsClient;
	private readonly IPlatformVersionResolverFactory _resolverFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;

	public ComponentInfoCommand(
		IComponentInfoCatalog catalog,
		IMobileComponentInfoCatalog mobileCatalog,
		IComponentRegistryDocsClient docsClient,
		IPlatformVersionResolverFactory resolverFactory,
		ISettingsRepository settingsRepository,
		ILogger logger) {
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		_mobileCatalog = mobileCatalog ?? throw new ArgumentNullException(nameof(mobileCatalog));
		_docsClient = docsClient ?? throw new ArgumentNullException(nameof(docsClient));
		_resolverFactory = resolverFactory ?? throw new ArgumentNullException(nameof(resolverFactory));
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public int Execute(ComponentInfoCommandOptions options) {
		return ExecuteAsync(options, CancellationToken.None).GetAwaiter().GetResult();
	}

	internal async Task<int> ExecuteAsync(ComponentInfoCommandOptions options, CancellationToken cancellationToken) {
		bool isMobile = IsMobile(options.SchemaType);

		bool hasExplicitVersion = !string.IsNullOrWhiteSpace(options.Version);
		bool hasEnvironment = !string.IsNullOrWhiteSpace(options.Environment) || !string.IsNullOrWhiteSpace(options.Uri);
		if (hasExplicitVersion && hasEnvironment) {
			_logger.WriteError("get-component-info: --version and --environment/--uri are mutually exclusive. Pass one or neither.");
			return 1;
		}

		PlatformVersionResolution resolution = await ResolveVersionAsync(options, hasExplicitVersion, hasEnvironment, cancellationToken)
			.ConfigureAwait(false);

		ComponentInfoResponse response;
		try {
			ComponentCatalogState state = isMobile
				? await _mobileCatalog.LoadAsync(resolution.ResolvedVersion, cancellationToken).ConfigureAwait(false)
				: await _catalog.LoadAsync(resolution.ResolvedVersion, cancellationToken).ConfigureAwait(false);
			response = await BuildResponseAsync(options, state, resolution, cancellationToken).ConfigureAwait(false);
		} catch (Exception ex) {
			string flavor = isMobile ? "mobile " : string.Empty;
			_logger.WriteError($"get-component-info: {flavor}catalog load failed: {ex.Message}");
			return 1;
		}

		Emit(response, options.Pretty);
		return response.Success ? 0 : 1;
	}

	private static bool IsMobile(string? schemaType) =>
		string.Equals(schemaType, SchemaTypeMobile, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Builds the detail response for the CLI verb. Delegates to the MCP tool's
	/// merger so both surfaces produce identical envelope-aware payloads —
	/// baseInputs ∪ per-component inputs, global typeDefinitions ∪ per-component
	/// typeDefinitions, per-component winning on key collision. The
	/// <paramref name="documentation"/> argument carries the concatenated
	/// <c>references.docs[]</c> markdown fetched through <see cref="ComponentDocumentationLoader"/>
	/// — same payload the MCP tool produces.
	/// </summary>
	private static ComponentInfoResponse BuildDetail(
		ComponentRegistryEntry entry,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? documentation,
		RegistryGlobalReferences globalReferences) =>
		ComponentInfoTool.CreateDetailResponse(
			entry, resolvedTargetVersion, resolvedFrom, documentation, globalReferences);

	private void Emit(ComponentInfoResponse response, bool pretty) {
		string payload = pretty
			? ComponentInfoPrettyRenderer.Render(response).TrimEnd('\n')
			: JsonSerializer.Serialize(response, JsonOptions);
		_logger.WriteLine(payload);
	}

	private Task<PlatformVersionResolution> ResolveVersionAsync(
		ComponentInfoCommandOptions options,
		bool hasExplicitVersion,
		bool hasEnvironment,
		CancellationToken cancellationToken) {
		if (hasExplicitVersion) {
			// Explicit user choice — treat as authoritative. MapResolvedFrom maps to
			// "environment-superset" (soft caveat) if the CDN has no catalog for this version
			// and falls back to latest; it does NOT downgrade to "latest-fallback".
			return Task.FromResult(new PlatformVersionResolution(options.Version.Trim(), VersionResolutionSource.Environment));
		}

		if (hasEnvironment) {
			EnvironmentSettings settings = ResolveEnvironmentSettings(options);
			IPlatformVersionResolver resolver = _resolverFactory.Create(settings);
			return resolver.ResolveAsync(cancellationToken);
		}

		// No flags — default to latest with a non-authoritative source so the response carries
		// "latest-fallback" regardless of what the catalog returns.
		return Task.FromResult(new PlatformVersionResolution(
			PlatformVersionResolver.LatestVersion,
			VersionResolutionSource.LatestFallback));
	}

	private EnvironmentSettings ResolveEnvironmentSettings(ComponentInfoCommandOptions options) {
		if (string.IsNullOrWhiteSpace(options.Environment) && string.IsNullOrWhiteSpace(options.Uri)) {
			string activeEnvName = _settingsRepository.GetDefaultEnvironmentName();
			if (!string.IsNullOrWhiteSpace(activeEnvName) && _settingsRepository.IsEnvironmentExists(activeEnvName)) {
				options.Environment = activeEnvName;
			}
		}
		return _settingsRepository.GetEnvironment(options);
	}

	private async Task<ComponentInfoResponse> BuildResponseAsync(
		ComponentInfoCommandOptions options,
		ComponentCatalogState state,
		PlatformVersionResolution resolution,
		CancellationToken cancellationToken) {
		string resolvedFrom = ComponentInfoResolution.MapResolvedFrom(
			resolution.Source, resolution.ResolvedVersion, state.ResolvedVersion);

		string componentType = options.ComponentType?.Trim();
		bool listMode = string.IsNullOrWhiteSpace(componentType)
			|| string.Equals(componentType, "list", StringComparison.OrdinalIgnoreCase);

		if (listMode) {
			IReadOnlyList<ComponentRegistryEntry> ranked = ComponentInfoGrouping.RankEntries(state.Entries, options.Search);
			return new ComponentInfoResponse {
				Success = true,
				Mode = "list",
				Count = ranked.Count,
				Items = ComponentInfoGrouping.CreateItems(ranked),
				ResolvedTargetVersion = state.ResolvedVersion,
				ResolvedFrom = resolvedFrom
			};
		}

		if (state.Lookup.TryGetValue(componentType!, out ComponentRegistryEntry entry)) {
			string? documentation = await ComponentDocumentationLoader
				.LoadAsync(_docsClient, entry, state.ResolvedVersion, cancellationToken)
				.ConfigureAwait(false);
			return BuildDetail(entry, state.ResolvedVersion, resolvedFrom, documentation, state.GlobalReferences);
		}

		// Unified with the MCP tool's not-found path (ADR Decision 2): both surfaces return the same
		// bounded closest-match shortlist via SuggestForUnknown (Levenshtein), instead of the CLI's
		// previous full keyword filter — so the suggestion path has CLI/MCP parity, not just list mode.
		IReadOnlyList<ComponentRegistryEntry> suggestions = ComponentInfoGrouping.SuggestForUnknown(
			state.Entries, componentType, options.Search, ComponentInfoGrouping.MaxNotFoundSuggestions);
		return new ComponentInfoResponse {
			Success = false,
			Mode = "list",
			Error = $"Component type '{componentType}' was not found. "
				+ $"Showing the {suggestions.Count} closest known type(s) — pass one of these as the "
				+ "component-type argument, or omit it to list the full catalog.",
			Count = suggestions.Count,
			Items = ComponentInfoGrouping.CreateItems(suggestions),
			ResolvedTargetVersion = state.ResolvedVersion,
			ResolvedFrom = resolvedFrom
		};
	}

}
