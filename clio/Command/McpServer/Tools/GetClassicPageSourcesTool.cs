using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetClassicPageSourcesTool(
	GetClassicPageSourcesCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetClassicPageSourcesOptions>(command, logger, commandResolver) {

	// Canonical MCP tool name. The prior name (get-classic-migration-bundle) resolves via the
	// McpToolCompatibilityCatalog DeprecatedAlias entry, so shipped agents keep working (ENG-94218).
	internal const string ToolName = "get-classic-page-sources";

	// ReadOnly=false: the tool's whole purpose is a local file write (the manifest). Destructive stays false
	// because every write is additive and confined: the DEFAULT path is anchored under the workspace with a format-validated
	// schema-name (re-runs overwrite only its own prior manifest), and an explicit output-file is accepted ONLY
	// when it resolves (symlinks followed) inside a trusted workspace anchor or the OS temp directory AND does not
	// already exist (OutputPathConfinement) — a `..` traversal, absolute system path, symlink escape, or existing
	// target is rejected before any write, so this MCP-callable tool cannot be steered into overwriting a file.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Collect the Classic page sources for a classic page schema and WRITE them to disk as a manifest " +
		"the migration engine (migrate.mjs) folds: the whole replacing-schema layer chain (base->top) + the " +
		"parent-template seed + resolution inputs (entityColumns/columnTitles/resources). The response returns the " +
		"ABSOLUTE manifest file path and a small summary — the layer bodies are written to the file, NOT returned, " +
		"so they never enter the caller's context. ALWAYS read `warnings` before planning from the manifest: it is " +
		"present only when the collected sources are incomplete (e.g. no section resolved, so the plan's List-page " +
		"side would be empty; or a detail whose bound entity could not be determined, so its child pages were never " +
		"looked up) — that is NOT the same as 'nothing to migrate'. " +
		"`childPageSchemas` carries the pages each detail's entity registers in SysModuleEdit — its edit card AND its " +
		"add mini page — so `childPageCount` can exceed `detailCount`; an empty one with no warning means the details " +
		"genuinely register no child page. Each detail entry carries the resolved `entity` and `editPage` (or " +
		"`editPage: false` = verified no edit card), which is how the engine keys those nested manifests. " +
		"`enumVocabulary` carries the TARGET stand's own ViewItemType/ContentType/DataValueType enum member->value " +
		"tables, read live from that stand's sysenums.js — never a copy of the engine's pinned tables — so the " +
		"engine's enum-drift guard can catch a stand on a different platform version; an enum whose value could not " +
		"be measured on this run is simply omitted from the block, and `enumVocabularyCount` reports how many of the " +
		"three were resolved. " +
		"The unit is collected WHOLE - no limit on details, child edit pages, or parent-template depth - so a very " +
		"wide page costs one round-trip per detail and can take minutes. If the call exceeds your client's request " +
		"timeout, run the CLI verb for that page rather than reading the timeout as 'the page is too big'. " +
		"Prefer `environment-name`; keep direct connection args for fallback only.")]
	public GetClassicPageSourcesResponse GetPageSources(
		[Description("Parameters: schema-name (required, the classic page); entity (optional); output-file (optional); environment-name preferred.")]
		[Required]
		GetClassicPageSourcesArgs args) {
		if (args is null) {
			return new GetClassicPageSourcesResponse { Success = false, Error = "args is required" };
		}
		GetClassicPageSourcesOptions options = new() {
			SchemaName = args.SchemaName,
			Entity = args.Entity,
			OutputFile = args.OutputFile,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		// This tool is environment-sensitive (environment-name/uri/login/password), so it must run under the
		// PER-TENANT execution lock. ExecuteResolved keys the lock on the resolved tenant and marks the session
		// container in-use for the whole multi-round-trip page-sources collection (ENG-93208), instead of the
		// environment-less ExecuteWithCleanLog overload which keys on the shared fallback — that would serialize
		// independent tenants and leave the resolved IApplicationClient/HttpClient evictable mid-call.
		// ExecuteResolved also centralizes the resolution-failure redaction that used to be hand-rolled here.
		return ExecuteResolved<GetClassicPageSourcesCommand, GetClassicPageSourcesResponse>(
			options,
			resolvedCommand => {
				resolvedCommand.TryAssemblePageSources(options, out GetClassicPageSourcesResponse response);
				if (!string.IsNullOrEmpty(response?.Error)) {
					// The command's inner error can carry an HTTP/DataService message with the environment
					// URI/host; redact before it lands in the MCP transcript (parity with ExecuteResolved's
					// resolution-failure redaction).
					response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
				}
				if (response?.Warnings is { Count: > 0 }) {
					// Warnings are a second error channel: the section-metadata warning interpolates the
					// DataService/transport failure text verbatim, so it can carry the same host/URI detail the
					// Error path redacts. Redact here — at the MCP boundary — rather than in the command, so the
					// CLI (where the full message is useful and goes nowhere but the operator's terminal) keeps it.
					response.Warnings = SensitiveErrorTextRedactor.RedactAll(response.Warnings);
				}
				return response;
			},
			error => new GetClassicPageSourcesResponse { Success = false, Error = error });
	}
}

/// <summary>
/// Arguments of the <c>get-classic-page-sources</c> MCP tool. Derives from
/// <see cref="ConnectionArgsBase"/> for the shared connection surface (environment-name / uri / login /
/// password) but deliberately NOT from <see cref="SchemaGetBaseArgs"/>: that base describes
/// <c>output-file</c> as an optional schema-body sink, while here it is the manifest destination and the
/// manifest is always written, so <c>output-file</c> is declared locally with its own semantics.
/// </summary>
public sealed record GetClassicPageSourcesArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Classic client-unit (page) schema name to collect the page sources for, e.g. 'ContactPageV2'")]
	[property: Required]
	string SchemaName,
	[property: JsonPropertyName("entity")]
	[property: Description("Entity schema name (optional; inferred from the page body when omitted). Drives entityColumns/columnTitles.")]
	string Entity = null
) : ConnectionArgsBase {

	[JsonPropertyName("output-file")]
	[Description("Manifest output path (absolute path recommended). Must resolve inside the workspace or the OS " +
		"temp directory — a path outside both (e.g. a `..` traversal or a system path) is rejected. Default: " +
		"<workspace-root>/.clio-migration/<schema>/manifest.json. The manifest is always written; the response " +
		"reports the absolute path.")]
	public string OutputFile { get; init; }
}
