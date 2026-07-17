using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP probe tool surface for the <c>list-printables</c> command: lists the MS Word
/// printables (reports) registered in a Creatio environment so an agent can fill the
/// environment-dependent <c>crt.PrintablesRequest</c> parameters (<c>templateId</c>,
/// <c>printableCaption</c>) from real values instead of inventing them.
/// OOTB button-action requests initiative (ENG-93187).
/// </summary>
/// <remarks>
/// The probe is the companion of the request catalog's <c>valueSource</c> annotations.
/// It is deliberately NOT resident in <c>tools/list</c> — the per-request documentation
/// and the <c>when-to-use-requests</c> guide route agents to it, and it stays reachable
/// through <c>clio-run</c>. Gated under the <c>requests-registry</c> feature because it is a
/// feature-INTERNAL probe: MCP-only (no registered CLI verb, no <c>help</c>/<c>docs</c>), born with
/// ENG-93187, with no purpose outside <c>crt.PrintablesRequest</c> wiring — so it gates with the
/// feature it belongs to. This is NOT inconsistent with the ungated <c>get-process-signature</c>
/// probe: that one is a pre-existing GA standalone CLI verb the feature merely REUSES, so it stays
/// ungated (the differentiator is provenance, not that both are read-only DataService reads).
/// </remarks>
[McpServerToolType]
[FeatureToggle("requests-registry")]
public sealed class ListPrintablesTool(
	ListPrintablesCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ListPrintablesOptions>(command, logger, commandResolver) {

	internal const string ToolName = "list-printables";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"List the MS Word printables (reports) registered in a Creatio environment, optionally filtered " +
		"by the entity they are attached to (directly or via their section module). " +
		"Use this BEFORE authoring a direct-mode crt.PrintablesRequest: fill templateId and " +
		"printableCaption ONLY from this probe's results — NEVER invent or guess a template GUID; " +
		"an invented id prints nothing and a half-configured button is a silent no-op. " +
		"When the result is empty or ambiguous, ask the user or author the declarative menu mode " +
		"(dataSourceName only) instead. Each returned item carries templateId / printableCaption / " +
		"convertInPDF named exactly like the request parameters they fill, plus showInCard / " +
		"showInSection visibility flags. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ListPrintablesResponse ListPrintables(
		[Description("Parameters: entity-name (optional entity schema name filter, e.g. 'Contact'); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ListPrintablesArgs args) {
		ListPrintablesOptions options = new() {
			EntityName = args.EntityName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		// ExecuteResolved runs the resolve + probe under the PER-TENANT execution lock and marks the
		// session-container in-use for the call (ENG-93208), instead of the environment-less
		// ExecuteWithCleanLog overload which keys on the shared fallback — that would serialize independent
		// tenants and leave this credential-bearing session evictable mid-call. Resolution-failure
		// exceptions are caught and redacted by ExecuteResolved (onFailure).
		return ExecuteResolved<ListPrintablesCommand, ListPrintablesResponse>(
			options,
			resolvedCommand => {
				if (!resolvedCommand.TryGetPrintables(options, out ListPrintablesResponse response)) {
					// The command-produced Error is the raw transport/deserialisation exception message
					// (ListPrintablesCommand.TryGetPrintables catch), which can carry the target URI/host or
					// credential values. Redact it at the MCP boundary before it crosses into the client
					// transcript, mirroring the resolution-failure path above and ListThemesTool. The CLI
					// Execute path keeps the raw message for the operator console (different trust boundary).
					return new ListPrintablesResponse {
						Success = false,
						Error = SensitiveErrorTextRedactor.Redact(
							string.IsNullOrWhiteSpace(response?.Error) ? "Failed to list printables." : response.Error)
					};
				}
				return response;
			},
			error => new ListPrintablesResponse { Success = false, Error = error });
	}
}

/// <summary>
/// MCP arguments for the <c>list-printables</c> tool.
/// </summary>
public sealed record ListPrintablesArgs(
	[property: JsonPropertyName("entity-name")]
	[property: Description("Optional entity schema name to filter by (e.g. 'Contact'). Matches printables "
		+ "attached to the entity directly or through their section module. Omit to list every "
		+ "MS Word printable of the environment.")]
	string? EntityName,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description("Creatio base URI (emergency fallback only; prefer environment-name)")]
	string? Uri,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password
);
