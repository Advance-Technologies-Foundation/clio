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
			"not already exist. Default: <workspace-root>/.clio-migration/component-registry/[mobile/]<version>.json " +
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
				using IOwnedPlatformVersionResolver resolver = _resolverFactory.CreateOwned(settings);
				versionResolution = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
			}
			else {
				versionResolution = ComponentInfoResolution.CreateNoActiveEnvironmentFallback();
			}

			return await ExportAsync(schemaType, versionResolution, options.OutputFile, cancellationToken).ConfigureAwait(false);
		}
		// A caller-requested cancellation is NOT a failure of the export — it is the caller withdrawing the
		// request. Converting it to a Fail() envelope would hand the MCP dispatcher (or a Ctrl-C'd CLI) a tool
		// failure where the protocol expects a cooperative cancel, and would report "The operation was
		// canceled." as if the CDN or the filesystem had refused. Guarded on the token so an OperationCanceled
		// raised for any OTHER reason (an internal budget, a library that reuses the type) still degrades into
		// the normal failure envelope rather than escaping as an unhandled exception.
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw;
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
			// Bytes, not a decoded string: "byte-faithful" has to mean the wire bytes, and a
			// StreamReader/StreamWriter round-trip normalises the encoding on the way through — a UTF-8 BOM on
			// the source is silently dropped and an invalid sequence becomes U+FFFD. JsonDocument parses UTF-8
			// bytes directly, so the counters read the same bytes that reach disk with no intermediate copy.
			byte[] content;
			using (Stream payload = fetch.Content)
			using (var buffer = new MemoryStream()) {
				await payload.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
				content = buffer.ToArray();
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
				outputFile, fetch.ResolvedVersion, schemaType, content, cancellationToken).ConfigureAwait(false);
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
		// Cancellation propagates rather than becoming a Fail() envelope — see TryExportAsync. This catch is the
		// one that would otherwise swallow the `throw;` WriteRegistryAsync re-raises after its temp-file cleanup.
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			throw;
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
		string explicitOutputFile, string resolvedVersion, SchemaTypeResolution schemaType, byte[] content,
		CancellationToken cancellationToken) {
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

		// The FLAVOR is part of the default path, mirroring RegistryFlavor.CacheSubdirectoryName (empty for web,
		// "mobile" for mobile). Without it a web export and a mobile export of the SAME version resolve to the
		// same file, and since the default path deliberately overwrites its own prior output, exporting web then
		// mobile leaves one file, reports the identical `outputFile` for both, and hands the consumer plausible
		// counters for the wrong registry — with no signal at all. The cache layout already treats this exact
		// pairing as a hazard for the same reason; the output layout stays in lockstep with it.
		string flavorSubdirectory = schemaType.IsMobile
			? RegistryFlavor.Mobile.CacheSubdirectoryName
			: RegistryFlavor.Web.CacheSubdirectoryName;
		string defaultPath;
		string defaultAnchor;
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			defaultAnchor = PageOutputDirectoryResolver.ResolveAnchor(
				_ioFileSystem,
				_ioFileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				null);
			defaultPath = Path.Combine(
				defaultAnchor, ClioMigrationDirectoryName, RegistrySubdirectoryName, flavorSubdirectory, $"{resolvedVersion}.json");
		}
		// Post-assembly containment, the second layer of the same defence ComponentRegistryDocsCacheStore
		// .TryGetPaths uses: the segment guard above validates the input, this validates the RESULT. A guard on
		// the input alone trusts that Path.Combine did what was expected; comparing the resolved absolute path
		// against the resolved anchor is what actually proves the write cannot land outside it, whatever the
		// segment turns out to mean to the platform.
		string registryRoot = _ioFileSystem.Path.GetFullPath(
			_ioFileSystem.Path.Combine(defaultAnchor, ClioMigrationDirectoryName, RegistrySubdirectoryName, flavorSubdirectory))
			+ Path.DirectorySeparatorChar;
		if (!_ioFileSystem.Path.GetFullPath(defaultPath).StartsWith(registryRoot, StringComparison.Ordinal)) {
			return (null,
				$"The registry reported version '{resolvedVersion}', which resolves outside the tool-owned "
				+ "output directory. Pass an explicit --output-file to choose the destination yourself.");
		}

		string directory = _ioFileSystem.Path.GetDirectoryName(defaultPath);
		if (!string.IsNullOrWhiteSpace(directory)) {
			_ioFileSystem.Directory.CreateDirectory(directory);
		}
		// Unique temp name in the TARGET directory: same volume (so Move is a rename, not copy+delete) and no
		// collision between two concurrent exports of the same version inside the long-running MCP server.
		string temporaryPath = $"{defaultPath}.{Guid.NewGuid():N}.tmp";
		try {
			await _ioFileSystem.File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
			_ioFileSystem.File.Move(temporaryPath, defaultPath, true);
		}
		catch (Exception) {
			TryDeleteTemporary(temporaryPath);
			throw;
		}
		return (defaultPath, null);
	}

	// A resolved version is usable as a path segment only when every character is one a version may contain.
	// An ALLOW-list, not a deny-list: Path.GetInvalidFileNameChars() is only { '\0', '/' } on Linux, so a
	// deny-list would let a CDN-supplied control character (LF, CR, ESC) through and make the guard behave
	// differently per platform for the same input. Same character class as
	// ComponentRegistryDocsCacheStore.SanitizeVersion, which guards this exact input class — except that this
	// REFUSES rather than strips: the resolved path is reported back to the caller and consumed by the migration
	// engine, so silently renaming the output file would be worse than declining to write it. '.' and '..' are
	// all-allowed characters yet never file names, so they are rejected explicitly.
	// Written as All() rather than a foreach (Sonar S3267): this runs ONCE per export over a short version
	// string, so the LINQ iterator allocation is irrelevant here — unlike CountObjectProperties above, which
	// runs up to twice per component over a registry with hundreds of them and therefore keeps its foreach.
	private static bool IsSafePathSegment(string value) =>
		!string.IsNullOrWhiteSpace(value)
			&& value != "." && value != ".."
			&& value.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_');

	// Best-effort: the caller is already failing, so a leftover temp file must not mask the real error.
	// Catches EVERY exception, not just IOException: this runs from the write path's catch-and-rethrow, and a
	// failed write can leave the OS raising UnauthorizedAccessException (or any other non-IOException) on the
	// delete — on Windows typically because the failed write still holds a handle. Letting that escape would
	// replace the causal write exception with a cleanup one, making the real failure undiagnosable.
	private void TryDeleteTemporary(string temporaryPath) {
		try {
			if (_ioFileSystem.File.Exists(temporaryPath)) {
				_ioFileSystem.File.Delete(temporaryPath);
			}
		}
		// Narrowed away from OutOfMemoryException: swallowing a process-fatal condition to preserve a message is
		// the wrong trade, and it is not a failure this cleanup can be shadowing anyway.
		catch (Exception ex) when (ex is not OutOfMemoryException) {
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
	private static (int componentCount, int compositeCount, int inputCount) CountEntries(byte[] content) {
		// Skip a UTF-8 BOM before parsing. JsonDocument does NOT tolerate one (RFC 8259 forbids a BOM in
		// exchanged JSON, so 0xEF is an invalid start of a value to it) — and the payload is now handed over as
		// raw wire bytes rather than a decoded string, which is what makes this explicit: the decoder used to
		// swallow the BOM on the way in. Only the counters skip it; the bytes WRITTEN keep it, because the file
		// is advertised as a byte-faithful copy of what the CDN served.
		ReadOnlyMemory<byte> json = content.AsMemory();
		if (json.Length >= 3 && json.Span[0] == 0xEF && json.Span[1] == 0xBB && json.Span[2] == 0xBF) {
			json = json[3..];
		}
		using JsonDocument document = JsonDocument.Parse(json);
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
				$"The fetched payload is not a component registry: its root is {root.ValueKind} and no "
				+ "'components' array was found, but a top-level array or an object with a 'components' array was "
				+ "expected. Nothing was written.");
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

	// foreach over the struct enumerator instead of LINQ Count(): the LINQ call resolves through
	// IEnumerable<JsonProperty> and boxes JsonElement.ObjectEnumerator, and this runs up to twice per
	// component over a registry that carries hundreds of them.
	private static int CountObjectProperties(JsonElement component, string propertyName) {
		if (component.ValueKind != JsonValueKind.Object
			|| !component.TryGetProperty(propertyName, out JsonElement property)
			|| property.ValueKind != JsonValueKind.Object) {
			return 0;
		}
		int count = 0;
		foreach (JsonProperty unused in property.EnumerateObject()) {
			count++;
		}
		return count;
	}

	private EnvironmentSettings ResolveEnvironmentSettings(ExportComponentRegistryOptions options) =>
		_settingsRepository.GetEnvironment(options);

	private static ExportComponentRegistryResponse Fail(string error, string schemaTypeWarning) =>
		new() { Success = false, Error = error, SchemaTypeWarning = schemaTypeWarning };
}
