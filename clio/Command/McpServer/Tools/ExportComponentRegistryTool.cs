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

	// ReadOnly=false: the tool's whole purpose is a local file write (the registry payload). Destructive
	// stays false because every write is additive and confined exactly like get-classic-page-sources: the
	// DEFAULT path is anchored under the workspace (re-runs overwrite only their own prior output), and an
	// explicit output-file is accepted only when it resolves inside a trusted workspace anchor or the OS
	// temp directory AND does not already exist (OutputPathConfinement).
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
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
		"or 'mobile'.")]
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
				versionResolution = await resolverFactory.Create(settings).ResolveAsync(cancellationToken).ConfigureAwait(false);
			}
			else {
				versionResolution = ComponentInfoResolution.CreateNoActiveEnvironmentFallback();
			}

			ExportComponentRegistryResponse response =
				await command.ExportAsync(schemaType, versionResolution, args.OutputFile, cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrEmpty(response.Error)) {
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			}
			return response;
		}
		catch (Exception ex) {
			return new ExportComponentRegistryResponse {
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message),
				SchemaTypeWarning = schemaType.Warning
			};
		}
	}
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
