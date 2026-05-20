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
		HelpText = "Component registry to query: 'web' (default) or 'mobile'. Mobile uses the bundled mobile registry and ignores --version/--environment.")]
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
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly IComponentInfoCatalog _catalog;
	private readonly IMobileComponentInfoCatalog _mobileCatalog;
	private readonly IPlatformVersionResolverFactory _resolverFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;

	public ComponentInfoCommand(
		IComponentInfoCatalog catalog,
		IMobileComponentInfoCatalog mobileCatalog,
		IPlatformVersionResolverFactory resolverFactory,
		ISettingsRepository settingsRepository,
		ILogger logger) {
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		_mobileCatalog = mobileCatalog ?? throw new ArgumentNullException(nameof(mobileCatalog));
		_resolverFactory = resolverFactory ?? throw new ArgumentNullException(nameof(resolverFactory));
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public int Execute(ComponentInfoCommandOptions options) {
		return ExecuteAsync(options, CancellationToken.None).GetAwaiter().GetResult();
	}

	internal async Task<int> ExecuteAsync(ComponentInfoCommandOptions options, CancellationToken cancellationToken) {
		if (IsMobile(options.SchemaType)) {
			ComponentInfoResponse mobileResponse;
			try {
				mobileResponse = BuildMobileResponse(options);
			} catch (Exception ex) {
				_logger.WriteError($"get-component-info: mobile catalog load failed: {ex.Message}");
				return 1;
			}
			Emit(mobileResponse, options.Pretty);
			return mobileResponse.Success ? 0 : 1;
		}

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
			ComponentCatalogState state = await _catalog.LoadAsync(resolution.ResolvedVersion, cancellationToken).ConfigureAwait(false);
			response = BuildResponse(options, state, resolution);
		} catch (Exception ex) {
			_logger.WriteError($"get-component-info: catalog load failed: {ex.Message}");
			return 1;
		}

		Emit(response, options.Pretty);
		return response.Success ? 0 : 1;
	}

	private static bool IsMobile(string? schemaType) =>
		string.Equals(schemaType, SchemaTypeMobile, StringComparison.OrdinalIgnoreCase);

	private ComponentInfoResponse BuildMobileResponse(ComponentInfoCommandOptions options) {
		string? componentType = options.ComponentType?.Trim();
		bool listMode = string.IsNullOrWhiteSpace(componentType)
			|| string.Equals(componentType, "list", StringComparison.OrdinalIgnoreCase);

		if (listMode) {
			IReadOnlyList<ComponentRegistryEntry> entries = _mobileCatalog.Search(options.Search);
			return new ComponentInfoResponse {
				Success = true,
				Mode = "list",
				Count = entries.Count,
				Items = ComponentInfoGrouping.CreateItems(entries)
			};
		}

		ComponentRegistryEntry? entry = _mobileCatalog.Find(componentType!);
		if (entry is not null) {
			return BuildDetail(entry, resolvedTargetVersion: null, resolvedFrom: null);
		}

		IReadOnlyList<ComponentRegistryEntry> suggestions = _mobileCatalog.Search(options.Search);
		return new ComponentInfoResponse {
			Success = false,
			Mode = "list",
			Error = $"Component type '{componentType}' was not found.",
			Count = suggestions.Count,
			Items = ComponentInfoGrouping.CreateItems(suggestions)
		};
	}

	private static ComponentInfoResponse BuildDetail(
		ComponentRegistryEntry entry,
		string? resolvedTargetVersion,
		string? resolvedFrom) {
		ComponentContentResponse contentResponse =
			entry.Content?.TypeDefinitions is { Count: > 0 } typeDefinitions
				? new ComponentContentResponse { TypeDefinitions = typeDefinitions }
				: null;
		return new ComponentInfoResponse {
			Success = true,
			Mode = "detail",
			Count = 1,
			ComponentType = entry.ComponentType,
			Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description,
			Container = entry.Container ? true : null,
			ParentTypes = entry.ParentTypes.Count == 0 ? null : entry.ParentTypes,
			Properties = entry.Properties.Count == 0 ? null : entry.Properties,
			Inputs = entry.Inputs is { Count: > 0 } ? entry.Inputs : null,
			Outputs = entry.Outputs is { Count: > 0 } ? entry.Outputs : null,
			TypicalChildren = entry.TypicalChildren.Count == 0 ? null : entry.TypicalChildren,
			Example = entry.Example,
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			Content = contentResponse
		};
	}

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
			// Explicit user choice — treat as authoritative. MapResolvedFrom will downgrade
			// to "latest-fallback" automatically if the catalog ends up loading a different
			// version (CDN 404 → latest, etc.).
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

	private static ComponentInfoResponse BuildResponse(
		ComponentInfoCommandOptions options,
		ComponentCatalogState state,
		PlatformVersionResolution resolution) {
		string resolvedFrom = ComponentInfoResolution.MapResolvedFrom(
			resolution.Source, resolution.ResolvedVersion, state.ResolvedVersion);

		string componentType = options.ComponentType?.Trim();
		bool listMode = string.IsNullOrWhiteSpace(componentType)
			|| string.Equals(componentType, "list", StringComparison.OrdinalIgnoreCase);

		if (listMode) {
			IReadOnlyList<ComponentRegistryEntry> filtered = ComponentInfoGrouping.FilterEntries(state.Entries, options.Search);
			return new ComponentInfoResponse {
				Success = true,
				Mode = "list",
				Count = filtered.Count,
				Items = ComponentInfoGrouping.CreateItems(filtered),
				ResolvedTargetVersion = state.ResolvedVersion,
				ResolvedFrom = resolvedFrom
			};
		}

		if (state.Lookup.TryGetValue(componentType!, out ComponentRegistryEntry entry)) {
			return BuildDetail(entry, state.ResolvedVersion, resolvedFrom);
		}

		IReadOnlyList<ComponentRegistryEntry> suggestions = ComponentInfoGrouping.FilterEntries(state.Entries, options.Search);
		return new ComponentInfoResponse {
			Success = false,
			Mode = "list",
			Error = $"Component type '{componentType}' was not found.",
			Count = suggestions.Count,
			Items = ComponentInfoGrouping.CreateItems(suggestions),
			ResolvedTargetVersion = state.ResolvedVersion,
			ResolvedFrom = resolvedFrom
		};
	}

}
