using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for <c>export-component-registry</c>. Deliberately NOT derived from
/// <see cref="BaseTool{T}"/>: that base's <c>ResolveCommand&lt;TCommand&gt;</c> eagerly builds a
/// per-environment container, which would force an environment resolution even for an explicit-<c>version</c>-only
/// or no-flags call — exactly the shape <see cref="ComponentInfoTool"/> already had to solve the same way, for
/// the same reason (see <c>adr-export-component-registry.md</c> D4). This tool mirrors <see cref="ComponentInfoTool"/>:
/// version resolution stays entirely per-call and goes through <see cref="IToolCommandResolver"/> (the
/// credential-passthrough-aware seam, ENG-93208) rather than <see cref="ISettingsRepository"/> directly, so an
/// authorized passthrough request's header tenant is honored instead of a named <c>environment-name</c> reading
/// that tenant's stored credentials. The fetch/write/count pipeline itself is shared with the CLI verb via
/// <see cref="ExportComponentRegistryCommand.ExportAsync"/>.
/// </summary>
[McpServerToolType]
public sealed class ExportComponentRegistryTool(
	ExportComponentRegistryCommand command,
	IPlatformVersionResolverFactory resolverFactory,
	IToolCommandResolver commandResolver) {

	internal const string ToolName = "export-component-registry";

	/// <summary>
	/// Upper bound on the platform-version probe that runs for the <c>environment-name</c>/<c>uri</c> shape.
	/// The probe wraps a SYNCHRONOUS <c>IApplicationClient</c> request in <c>Task.Run</c>, so a stand that
	/// accepts the TCP connection and never answers cannot be interrupted by the cancellation token alone —
	/// and this tool is neither read-only nor destructive, so no call-tool pipeline deadline covers it.
	/// Aligned with the 30s per-attempt timeout of the CDN leg (the only other network leg of the export) so
	/// neither dominates. Elapsing is not an error: the resolver already degrades softly, so the export
	/// continues against <c>latest</c> with <c>resolvedFrom=latest-fallback</c> and
	/// <c>requiresVersionConfirmation=true</c> instead of hanging without a ceiling.
	/// </summary>
	internal static readonly TimeSpan VersionResolutionBudget = TimeSpan.FromSeconds(30);

	// ReadOnly=false: the tool's whole purpose is a local file write (the registry payload). Destructive
	// stays false because every write is additive and confined exactly like get-classic-page-sources: the
	// DEFAULT path is anchored under the workspace (re-runs overwrite only their own prior output), and an
	// explicit output-file is accepted only when it resolves inside a trusted workspace anchor or the OS
	// temp directory AND does not already exist (OutputPathConfinement).
	// Idempotent=false: only the DEFAULT-path shape repeats safely. With an explicit output-file the second
	// identical call is deliberately REFUSED (the target now exists), so advertising idempotence would tell a
	// retry/backoff layer that re-issuing after a perceived timeout is harmless when it in fact turns a
	// completed export into an "already exists" failure. One boolean cannot describe both shapes, so it
	// carries the safe value for the non-repeatable one.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Write the FULL Freedom UI component registry for a resolved platform version to a file — one call " +
		"instead of dozens of get-component-info round-trips when validating many crt.* componentTypes/propMap " +
		"keys at once (e.g. a migration engine). The response returns the ABSOLUTE output file path and small " +
		"counters (componentCount/compositeCount/inputCount) — the registry content itself is written to the " +
		"file, NOT returned, so it never enters the caller's context. The file is byte-faithful to the source " +
		"registry (no re-serialization), so per-input deprecated/deprecationReason markers survive. Documentation " +
		"bodies (references.docs[] paths) are never fetched — only their paths, already part of the registry " +
		"payload. Prefer environment-name to scope the export to the target environment's real platform version; " +
		"when resolvedFrom is 'latest-fallback' the version is unknown and requiresVersionConfirmation is true — " +
		"do not silently assume the exported set matches any specific environment. schema-type: 'web' (default) " +
		"or 'mobile'. NOT repeatable on every shape: a repeat call succeeds only for the default path (it " +
		"overwrites its own prior output), while an explicit output-file must be deleted or renamed first — a " +
		"blind retry against the same output-file is refused because the file already exists.")]
	public async Task<ExportComponentRegistryResponse> ExportComponentRegistry(
		[Description("version (optional, 3-part semver, mutually exclusive with environment-name/uri); " +
			"schema-type 'web' (default) or 'mobile'; output-file (optional, confined to the workspace or OS " +
			"temp dir, refused if it already exists); environment-name preferred over uri/login/password.")]
		[Required]
		ExportComponentRegistryArgs args,
		CancellationToken cancellationToken = default) {
		if (args is null) {
			return new ExportComponentRegistryResponse { Success = false, Error = "args is required" };
		}

		SchemaTypeResolution schemaType = ComponentInfoResolution.ResolveSchemaType(args.SchemaType);
		try {
			ExportComponentRegistryOptions options = new() {
				Version = args.Version,
				SchemaType = args.SchemaType,
				OutputFile = args.OutputFile,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			bool hasExplicitVersion = !string.IsNullOrWhiteSpace(args.Version);
			bool hasEnvironment = !string.IsNullOrWhiteSpace(args.EnvironmentName) || !string.IsNullOrWhiteSpace(args.Uri);
			(string mutualExclusivityError, string formatError) =
				ExportComponentRegistryCommand.ValidateVersionArguments(options, hasExplicitVersion, hasEnvironment);
			if (mutualExclusivityError != null) {
				return new ExportComponentRegistryResponse { Success = false, Error = mutualExclusivityError, SchemaTypeWarning = schemaType.Warning };
			}
			if (formatError != null) {
				return new ExportComponentRegistryResponse { Success = false, Error = formatError, SchemaTypeWarning = schemaType.Warning };
			}

			PlatformVersionResolution versionResolution;
			if (hasExplicitVersion) {
				versionResolution = new PlatformVersionResolution(args.Version.Trim(), VersionResolutionSource.Environment);
			}
			else if (hasEnvironment) {
				EnvironmentSettings settings = commandResolver.Resolve<EnvironmentSettings>(options);
				versionResolution = await ResolveVersionWithinBudgetAsync(settings, cancellationToken).ConfigureAwait(false);
			}
			else {
				versionResolution = ComponentInfoResolution.CreateNoActiveEnvironmentFallback();
			}

			ExportComponentRegistryResponse response =
				await command.ExportAsync(schemaType, versionResolution, args.OutputFile, cancellationToken).ConfigureAwait(false);
			// The response is an immutable record (init-only, matching every other response POCO here), so the
			// MCP-boundary redaction produces a NEW envelope instead of mutating the one the pipeline returned.
			return string.IsNullOrEmpty(response.Error)
				? response
				: response with { Error = SensitiveErrorTextRedactor.Redact(response.Error) };
		}
		catch (Exception ex) {
			return new ExportComponentRegistryResponse {
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
				SchemaTypeWarning = schemaType.Warning
			};
		}
	}

	/// <summary>
	/// Resolves the platform version under <see cref="VersionResolutionBudget"/>. The linked token gives the
	/// resolver a cooperative early exit, and <c>WaitAsync</c> abandons the wait when the underlying blocking
	/// probe ignores it. A budget expiry degrades to the same <c>latest</c> fallback the resolver itself
	/// produces for an unreachable environment (<see cref="VersionFallbackReason.ProbeError"/> — the transient
	/// class, so the response still advertises a retry as worthwhile). A genuine caller cancellation is not
	/// treated as a budget expiry — it propagates out of this helper (the calling method's own catch-all then
	/// turns it into the usual failure response). The linked token and the <c>WaitAsync</c> timeout carry the
	/// same budget, so whichever fires first is a race — a benign one: both land on the same fallback, so do
	/// not "fix" one of the two values away, they cover different halves (cooperative exit vs abandoning a
	/// blocking probe that ignores the token).
	/// </summary>
	private async Task<PlatformVersionResolution> ResolveVersionWithinBudgetAsync(
		EnvironmentSettings settings, CancellationToken cancellationToken) {
		using CancellationTokenSource budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		budgetCts.CancelAfter(VersionResolutionBudget);
		try {
			return await resolverFactory.Create(settings)
				.ResolveAsync(budgetCts.Token)
				.WaitAsync(VersionResolutionBudget, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (TimeoutException) {
			return CreateProbeTimeoutFallback();
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			return CreateProbeTimeoutFallback();
		}
	}

	/// <summary>
	/// The soft-degrade outcome of a budget expiry: <c>latest</c> on the fallback tier with the transient
	/// <see cref="VersionFallbackReason.ProbeError"/> reason, matching what the resolver itself returns when
	/// the probe throws.
	/// </summary>
	private static PlatformVersionResolution CreateProbeTimeoutFallback() =>
		new(PlatformVersionResolver.LatestVersion, VersionResolutionSource.LatestFallback) {
			Reason = VersionFallbackReason.ProbeError
		};
}

/// <summary>Arguments of the <c>export-component-registry</c> MCP tool.</summary>
public sealed record ExportComponentRegistryArgs(
	[property: JsonPropertyName("version")]
	[property: Description("Explicit catalog version to export (3-part semver, e.g. 8.3.4). Mutually exclusive with environment-name/uri.")]
	string? Version = null,
	[property: JsonPropertyName("schema-type")]
	[property: Description("Component registry to export: 'web' (default) or 'mobile'.")]
	string? SchemaType = null
) : ConnectionArgsBase {

	[JsonPropertyName("output-file")]
	[Description("Destination file path (absolute path recommended). Must resolve inside the workspace or the " +
		"OS temp directory and must not already exist — refused before any write otherwise. Default: " +
		"<workspace-root>/.clio-migration/component-registry/<version>.json (that default path IS overwritten " +
		"on a repeat run).")]
	public string? OutputFile { get; init; }
}
