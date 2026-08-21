namespace Clio.Command;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

/// <summary>Options for the <c>export-component-registry</c> command.</summary>
[Verb("export-component-registry",
	Aliases = ["export-registry"],
	HelpText = "Write the full Freedom UI component registry for a resolved platform version to a file " +
		"(byte-faithful to the source, no per-component round-trips, no documentation bodies fetched).")]
public sealed class ExportComponentRegistryOptions : EnvironmentOptions {

	/// <summary>Explicit catalog version to export; mutually exclusive with an environment/uri.</summary>
	[Option("version", Required = false,
		HelpText = "Explicit catalog version to export (3-part semver, e.g. 8.3.4). Mutually exclusive with " +
			"--environment/--uri. Default: latest.")]
	public string Version { get; set; }

	/// <summary>Which registry flavor to export: <c>web</c> (default) or <c>mobile</c>.</summary>
	[Option("schema-type", Required = false,
		HelpText = "Component registry to export: 'web' (default) or 'mobile'.")]
	public string SchemaType { get; set; }

	/// <summary>Optional destination path; when omitted the file is anchored under the workspace root.</summary>
	[Option("output-file", Required = false,
		HelpText = "Destination file path. Must resolve inside the workspace or the OS temp directory and must " +
			"not already exist. Default: <workspace-root>/.clio-migration/component-registry/<version>.json " +
			"(that default path IS overwritten on a repeat run).")]
	public string OutputFile { get; set; }
}

/// <summary>
/// Summary envelope returned by <c>export-component-registry</c>. Carries the absolute output-file path,
/// version-resolution fields, and structural counters — never the registry content itself (that lives only
/// in the written file).
/// </summary>
public sealed record ExportComponentRegistryResponse {

	/// <summary>Whether the registry was resolved, fetched, and written.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>Absolute path of the file written to disk.</summary>
	[JsonPropertyName("outputFile")]
	public string OutputFile { get; init; }

	/// <summary>The platform version whose registry was actually written.</summary>
	[JsonPropertyName("resolvedTargetVersion")]
	public string ResolvedTargetVersion { get; init; }

	/// <summary>One of <c>environment</c>, <c>environment-superset</c>, or <c>latest-fallback</c>.</summary>
	[JsonPropertyName("resolvedFrom")]
	public string ResolvedFrom { get; init; }

	/// <summary>Stable kebab-case reason token, present only on the <c>latest-fallback</c> tier.</summary>
	[JsonPropertyName("resolvedFromReason")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedFromReason { get; init; }

	/// <summary>
	/// <c>true</c> only on the <c>latest-fallback</c> tier: the caller must not silently assume the exported
	/// component set matches the target environment and must request confirmation before relying on it.
	/// </summary>
	[JsonPropertyName("requiresVersionConfirmation")]
	public bool RequiresVersionConfirmation { get; init; }

	/// <summary>
	/// Prose caveat for the resolved tier: the hard stop on <c>latest-fallback</c> and the soft
	/// "catalog is a superset of the target environment" caveat on <c>environment-superset</c>; <c>null</c>
	/// on an exact <c>environment</c> match. Without it an approximate export is indistinguishable from an
	/// exact one unless the caller itself diffs <see cref="ResolvedTargetVersion"/> against what it requested.
	/// </summary>
	[JsonPropertyName("versionWarning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string VersionWarning { get; init; }

	/// <summary>Non-null only when <c>schema-type</c> was an unrecognized value (fell back to web).</summary>
	[JsonPropertyName("schemaTypeWarning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SchemaTypeWarning { get; init; }

	/// <summary>Number of component entries written.</summary>
	[JsonPropertyName("componentCount")]
	public int ComponentCount { get; init; }

	/// <summary>Number of composite entries written (0 when the registry carries none).</summary>
	[JsonPropertyName("compositeCount")]
	public int CompositeCount { get; init; }

	/// <summary>Total number of per-component input/property definitions written, summed across all components.</summary>
	[JsonPropertyName("inputCount")]
	public int InputCount { get; init; }

	/// <summary>Failure reason when <see cref="Success"/> is <c>false</c>; <c>null</c> otherwise.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }
}

/// <summary>
/// CLI entry point for the <c>export-component-registry</c> verb. Writes the FULL Freedom UI component
/// registry for a resolved platform version to a file, byte-faithful to what <see cref="IComponentRegistryClient"/>
/// fetched — no re-serialization through a typed model, which would silently drop fields such as
/// <c>deprecated</c>/<c>deprecationReason</c> that exist only as raw JSON on the wire (verified against
/// <c>ComponentRegistry.live-snapshot.json</c>). Documentation bodies (<c>references.docs[]</c> paths) are
/// never fetched — only the registry payload itself. Version resolution mirrors <c>get-component-info</c>
/// (<see cref="ComponentInfoResolution"/>) and is deliberately LAZY: an <see cref="EnvironmentSettings"/>
/// probe only happens when the caller actually supplied <c>environment-name</c>/<c>uri</c>, so an explicit
/// <c>--version</c> call (or a no-flags call) never forces an environment resolution that could fail or bind
/// to an unrelated default environment.
/// </summary>
public sealed class ExportComponentRegistryCommand {

	private const string ClioMigrationDirectoryName = ".clio-migration";
	private const string RegistrySubdirectoryName = "component-registry";

	private readonly IComponentRegistryClient _webRegistryClient;
	private readonly IMobileComponentRegistryClient _mobileRegistryClient;
	private readonly IPlatformVersionResolverFactory _resolverFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IoFileSystem _ioFileSystem;
	private readonly ILogger _logger;

	public ExportComponentRegistryCommand(
		IComponentRegistryClient webRegistryClient,
		IMobileComponentRegistryClient mobileRegistryClient,
		IPlatformVersionResolverFactory resolverFactory,
		ISettingsRepository settingsRepository,
		IoFileSystem ioFileSystem,
		ILogger logger) {
		_webRegistryClient = webRegistryClient ?? throw new ArgumentNullException(nameof(webRegistryClient));
		_mobileRegistryClient = mobileRegistryClient ?? throw new ArgumentNullException(nameof(mobileRegistryClient));
		_resolverFactory = resolverFactory ?? throw new ArgumentNullException(nameof(resolverFactory));
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_ioFileSystem = ioFileSystem ?? throw new ArgumentNullException(nameof(ioFileSystem));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public int Execute(ExportComponentRegistryOptions options) =>
		ExecuteAsync(options, CancellationToken.None).GetAwaiter().GetResult();

	internal async Task<int> ExecuteAsync(ExportComponentRegistryOptions options, CancellationToken cancellationToken) {
		ExportComponentRegistryResponse response = await TryExportAsync(options, cancellationToken).ConfigureAwait(false);
		_logger.WriteInfo(JsonSerializer.Serialize(response));
		return response.Success ? 0 : 1;
	}

	/// <summary>
	/// CLI-verb entry point: validates the mutual-exclusivity/format of <paramref name="options"/>, resolves
	/// the version LAZILY via <see cref="ISettingsRepository"/> (only when <c>environment</c>/<c>uri</c> was
	/// actually supplied), then delegates to <see cref="ExportAsync"/> for the fetch/write/count that both
	/// surfaces share. The MCP tool (<c>ExportComponentRegistryTool</c>) does NOT call this method — it needs
	/// the credential-passthrough-aware <c>IToolCommandResolver</c> settings resolution instead, so it repeats
	/// the same validation and calls <see cref="ExportAsync"/> directly (mirrors how
	/// <c>ComponentInfoCommand</c>/<c>ComponentInfoTool</c> each own their own version-resolution branch).
	/// </summary>
	internal async Task<ExportComponentRegistryResponse> TryExportAsync(
		ExportComponentRegistryOptions options, CancellationToken cancellationToken) {
		SchemaTypeResolution schemaType = ComponentInfoResolution.ResolveSchemaType(options.SchemaType);
		try {
			bool hasExplicitVersion = !string.IsNullOrWhiteSpace(options.Version);
			bool hasEnvironment = !string.IsNullOrWhiteSpace(options.Environment) || !string.IsNullOrWhiteSpace(options.Uri);
			(string mutualExclusivityError, string formatError) = ValidateVersionArguments(options, hasExplicitVersion, hasEnvironment);
			if (mutualExclusivityError != null) {
				return Fail(mutualExclusivityError, schemaType.Warning);
			}
			if (formatError != null) {
				return Fail(formatError, schemaType.Warning);
			}

			PlatformVersionResolution versionResolution;
			if (hasExplicitVersion) {
				versionResolution = new PlatformVersionResolution(options.Version.Trim(), VersionResolutionSource.Environment);
			}
			else if (hasEnvironment) {
				EnvironmentSettings settings = ResolveEnvironmentSettings(options);
				IPlatformVersionResolver resolver = _resolverFactory.Create(settings);
				versionResolution = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
			}
			else {
				versionResolution = ComponentInfoResolution.CreateNoActiveEnvironmentFallback();
			}

			return await ExportAsync(schemaType, versionResolution, options.OutputFile, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) {
			return Fail(ex.Message, schemaType.Warning);
		}
	}

	/// <summary>
	/// Validates <c>version</c>'s mutual exclusivity with <c>environment</c>/<c>uri</c> and its semver shape.
	/// Shared by the CLI verb and the MCP tool so both surfaces reject malformed input identically.
	/// </summary>
	internal static (string mutualExclusivityError, string formatError) ValidateVersionArguments(
		ExportComponentRegistryOptions options, bool hasExplicitVersion, bool hasEnvironment) {
		if (hasExplicitVersion && hasEnvironment) {
			return ("'version' and 'environment-name'/'uri' are mutually exclusive. Pass one or neither.", null);
		}
		if (hasExplicitVersion && !PlatformVersionResolver.TryNormaliseToThreePartSemver(options.Version, out _)) {
			return (null,
				$"'version' value '{options.Version}' is not a valid platform version. Use a 3-part semver, for example '8.3.3'.");
		}
		return (null, null);
	}

	/// <summary>
	/// Fetches the registry (web or mobile, per <paramref name="schemaType"/>) for the already-resolved
	/// <paramref name="versionResolution"/>, writes it verbatim to the resolved output path, and returns the
	/// summary response. Never returns the registry content itself. Shared by the CLI verb's
	/// <see cref="TryExportAsync"/> and the <c>export-component-registry</c> MCP tool — both surfaces resolve
	/// the version and the environment settings differently (see the ADR D4 note above), but fetch/write/count
	/// identically from this single method.
	/// </summary>
	internal async Task<ExportComponentRegistryResponse> ExportAsync(
		SchemaTypeResolution schemaType,
		PlatformVersionResolution versionResolution,
		string outputFile,
		CancellationToken cancellationToken) {
		try {
			IComponentRegistryClient registryClient = schemaType.IsMobile ? _mobileRegistryClient : _webRegistryClient;

			// An explicit version reaches both surfaces raw, so normalise it to the 3-part catalog key BEFORE
			// the fetch: '8.3.4.5678' (the exact form Creatio reports as CoreVersion, and what a user or agent
			// pastes) and '8.3' both pass ValidateVersionArguments, but requesting them verbatim asks for a
			// catalog file that does not exist, silently falls back to 'latest', and reports the approximation
			// as environment-superset with requiresVersionConfirmation=false. Values that are not a version at
			// all (the 'latest' sentinel of the fallback tier) are passed through untouched.
			string requestedVersion =
				PlatformVersionResolver.TryNormaliseToThreePartSemver(versionResolution.ResolvedVersion, out string normalisedVersion)
					? normalisedVersion
					: versionResolution.ResolvedVersion;
			ComponentRegistryFetchResult fetch =
				await registryClient.GetAsync(requestedVersion, cancellationToken).ConfigureAwait(false);
			// No outer `using (fetch.Content)`: the StreamReader takes ownership of the stream (it is not
			// constructed with leaveOpen) and disposes it when this scope ends, so an outer using would
			// double-dispose it and contradict the ownership contract.
			string content;
			using (var reader = new StreamReader(fetch.Content)) {
				content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
			}

			string resolvedFrom = ComponentInfoResolution.MapResolvedFrom(
				versionResolution.Source, requestedVersion, fetch.ResolvedVersion);
			string resolvedFromReason = ComponentInfoResolution.GetFallbackReason(resolvedFrom, versionResolution.Reason);
			bool requiresVersionConfirmation = ComponentInfoResolution.RequiresVersionConfirmation(resolvedFrom);
			string versionWarning = ComponentInfoResolution.GetVersionWarning(resolvedFrom);

			// Count BEFORE writing: CountEntries parses the payload, and the registry client returns any
			// 2xx body verbatim (no JSON validation, ComponentRegistryClient.TryFetchOnceAsync), so a proxy
			// or CDN error page served with status 200 makes the parse throw. Writing first would leave that
			// junk file on disk under a reported failure — and an explicit output-file is refuse-if-exists,
			// so every retry to the same path would then fail with "already exists".
			(int componentCount, int compositeCount, int inputCount) = CountEntries(content);

			(string outputPath, string writeError) = await WriteRegistryAsync(
				outputFile, fetch.ResolvedVersion, content, cancellationToken).ConfigureAwait(false);
			if (writeError != null) {
				return Fail(writeError, schemaType.Warning);
			}

			return new ExportComponentRegistryResponse {
				Success = true,
				OutputFile = outputPath,
				ResolvedTargetVersion = fetch.ResolvedVersion,
				ResolvedFrom = resolvedFrom,
				ResolvedFromReason = resolvedFromReason,
				RequiresVersionConfirmation = requiresVersionConfirmation,
				VersionWarning = versionWarning,
				SchemaTypeWarning = schemaType.Warning,
				ComponentCount = componentCount,
				CompositeCount = compositeCount,
				InputCount = inputCount
			};
		}
		catch (Exception ex) {
			return Fail(ex.Message, schemaType.Warning);
		}
	}

	// Writes the registry content exactly as fetched (no re-serialization) either to the explicit,
	// confinement-checked output-file (refuses an existing target, additive-only) or to the tool-owned
	// default path (re-runnable, overwrites its own prior output) — the same two-contract split
	// GetClassicPageSourcesCommand.WriteManifest/ResolveOutputPath uses for the manifest file. The default
	// path is written temp-then-move so a process killed mid-write cannot leave a truncated file behind:
	// the next run's CountEntries would throw JsonException on it. OutputPathConfinement.WriteAtomic cannot
	// serve that branch — its FileMode.CreateNew gate is exactly the refuse-if-exists contract the default
	// path must NOT have — so the move carries overwrite:true instead.
	private async Task<(string path, string error)> WriteRegistryAsync(
		string explicitOutputFile, string resolvedVersion, string content, CancellationToken cancellationToken) {
		if (!string.IsNullOrWhiteSpace(explicitOutputFile)) {
			(string resolvedPath, string resolveError) = OutputPathConfinement.Resolve(_ioFileSystem, explicitOutputFile);
			if (resolveError != null) {
				return (null, resolveError);
			}
			try {
				OutputPathConfinement.WriteAtomic(_ioFileSystem, resolvedPath, content);
			}
			catch (IOException ex) {
				return (null, ex.Message);
			}
			return (resolvedPath, null);
		}

		// resolvedVersion becomes a path segment of the default path, and it arrives from the NETWORK
		// (ComponentRegistryFetchResult.ResolvedVersion, i.e. whatever the CDN reported) — not from the
		// locally normalised input. Guard the actual threat rather than whitelisting a version shape the CDN
		// is free to widen: anything that is not a single plain file-name component (a separator, a '..'
		// segment, a rooted path) must never reach Path.Combine.
		if (!IsSafePathSegment(resolvedVersion)) {
			return (null,
				$"The registry reported version '{resolvedVersion}', which is not usable as a file name. "
				+ "Pass an explicit --output-file to choose the destination yourself.");
		}

		string defaultPath;
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			string anchor = PageOutputDirectoryResolver.ResolveAnchor(
				_ioFileSystem,
				_ioFileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				null);
			defaultPath = Path.Combine(anchor, ClioMigrationDirectoryName, RegistrySubdirectoryName, $"{resolvedVersion}.json");
		}
		string directory = _ioFileSystem.Path.GetDirectoryName(defaultPath);
		if (!string.IsNullOrWhiteSpace(directory)) {
			_ioFileSystem.Directory.CreateDirectory(directory);
		}
		// Unique temp name in the TARGET directory: same volume (so Move is a rename, not copy+delete) and no
		// collision between two concurrent exports of the same version inside the long-running MCP server.
		string temporaryPath = $"{defaultPath}.{Guid.NewGuid():N}.tmp";
		try {
			await _ioFileSystem.File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
			_ioFileSystem.File.Move(temporaryPath, defaultPath, true);
		}
		catch (Exception) {
			TryDeleteTemporary(temporaryPath);
			throw;
		}
		return (defaultPath, null);
	}

	// A resolved version is usable as a path segment only when it is a plain file name: no directory
	// separator, no '..' segment, no volume root, no invalid file-name character.
	private static bool IsSafePathSegment(string value) =>
		!string.IsNullOrWhiteSpace(value)
			&& value != "." && value != ".."
			&& value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
			&& value.IndexOf(Path.DirectorySeparatorChar) < 0
			&& value.IndexOf(Path.AltDirectorySeparatorChar) < 0;

	// Best-effort: the caller is already failing, so a leftover temp file must not mask the real error.
	private void TryDeleteTemporary(string temporaryPath) {
		try {
			if (_ioFileSystem.File.Exists(temporaryPath)) {
				_ioFileSystem.File.Delete(temporaryPath);
			}
		}
		catch (IOException) {
			// Nothing further to do — the export failure is reported by the caller.
		}
	}

	// Counts components/composites/inputs directly off the fetched JSON (the same bytes written to disk)
	// rather than through the typed ComponentCatalogState model, so a producer field the typed model does
	// not map (e.g. deprecated/deprecationReason) still counts correctly and the counters can never disagree
	// with what the file actually contains. Handles both registry shapes: legacy top-level array and the
	// wrapped { components, composites, references } envelope. A body that is parseable JSON but carries
	// neither shape is a hard failure, NOT an empty registry: a proxy/CDN JSON error body served with status
	// 200 (or a future envelope rename) would otherwise be written to disk and reported as success with every
	// counter at zero, and those counters are the downstream migration engine's only verification signal.
	private static (int componentCount, int compositeCount, int inputCount) CountEntries(string content) {
		using JsonDocument document = JsonDocument.Parse(content);
		JsonElement root = document.RootElement;
		// Kept as statements, not a nested ternary (Sonar S3358): the two registry shapes are two distinct
		// lookups, and the 'neither' case falls through to the hard failure below.
		JsonElement components = default;
		if (root.ValueKind == JsonValueKind.Array) {
			components = root;
		}
		else if (root.TryGetProperty("components", out JsonElement componentsProperty)) {
			components = componentsProperty;
		}
		if (components.ValueKind != JsonValueKind.Array) {
			throw new InvalidOperationException(
				"The fetched payload is not a component registry: expected a top-level array or an object with a "
				+ "'components' array. Nothing was written.");
		}
		int componentCount = components.GetArrayLength();
		int compositeCount = root.ValueKind == JsonValueKind.Object
			&& root.TryGetProperty("composites", out JsonElement composites)
			&& composites.ValueKind == JsonValueKind.Array
				? composites.GetArrayLength()
				: 0;
		int inputCount = 0;
		foreach (JsonElement component in components.EnumerateArray()) {
			// "inputs" (current wrapped-schema generation) and "properties" (legacy) describe the SAME
			// component surface under two generations, so they are alternatives, never addends: a
			// transitional entry that carries both would otherwise double-count its field set, and
			// inputCount is the downstream migration engine's only verification signal. "inputs" wins
			// when present; "properties" is consulted only for an entry that has no inputs at all.
			int inputs = CountObjectProperties(component, "inputs");
			inputCount += inputs > 0 ? inputs : CountObjectProperties(component, "properties");
		}
		return (componentCount, compositeCount, inputCount);
	}

	private static int CountObjectProperties(JsonElement component, string propertyName) =>
		component.ValueKind == JsonValueKind.Object
			&& component.TryGetProperty(propertyName, out JsonElement property)
			&& property.ValueKind == JsonValueKind.Object
				? property.EnumerateObject().Count()
				: 0;

	private EnvironmentSettings ResolveEnvironmentSettings(ExportComponentRegistryOptions options) =>
		_settingsRepository.GetEnvironment(options);

	private static ExportComponentRegistryResponse Fail(string error, string schemaTypeWarning) =>
		new() { Success = false, Error = error, SchemaTypeWarning = schemaTypeWarning };
}
