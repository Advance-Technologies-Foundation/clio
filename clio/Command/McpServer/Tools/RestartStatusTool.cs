using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for polling a previously started restart readiness wait, used after
/// <c>restart-by-environment-name</c> / <c>restart-by-credentials</c> returns an in-progress notice past the
/// MCP response deadline (ENG-91315). Mirrors <c>compile-status</c>: the restart request runs under the
/// per-tenant execution lock, but the read-only readiness wait is detached and lock-free, so its progress is
/// read here instead of holding the response open or blocking other same-tenant calls.
/// </summary>
[McpServerToolType]
public sealed class RestartStatusTool(IRestartOperationRegistry registry, IToolCommandResolver commandResolver) {

	/// <summary>
	/// Stable MCP tool name for restart-status.
	/// </summary>
	internal const string RestartStatusToolName = "restart-status";

	/// <summary>
	/// Returns the tracked status of a restart readiness wait.
	/// </summary>
	[McpServerTool(Name = RestartStatusToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns the readiness status of the most recent restart tracked for an environment, or of a specific operation-id from a restart-by-environment-name/restart-by-credentials in-progress response. Use this after a restart tool returns an in-progress note to check whether the instance finished warming up; do not re-run the restart just to check.")]
	public RestartStatusResponse GetStatus(
		[Description("Status query parameters")] [Required] RestartStatusArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return new RestartStatusResponse(false, "invalid-request",
				Note: "environment-name is required and cannot be empty.");
		}

		string callerTenantKey = commandResolver.GetTenantKey(new EnvironmentOptions { Environment = args.EnvironmentName });
		RestartOperationRecord record = string.IsNullOrWhiteSpace(args.OperationId)
			? registry.GetLatest(callerTenantKey)
			: registry.GetById(args.OperationId.Trim());

		// Scope operation-id lookups to the caller's tenant: on a shared MCP HTTP server a caller who obtains
		// (or guesses) another session's global operation id must not read its environment/exit-code. GetLatest
		// is already tenant-keyed, so this only tightens the GetById path.
		if (record is not null && !string.Equals(record.TenantKey, callerTenantKey, StringComparison.Ordinal)) {
			record = null;
		}

		if (record is null) {
			return new RestartStatusResponse(true, "not-found", EnvironmentName: args.EnvironmentName,
				Note: "No restart operation has been recorded for this environment in the current MCP server session.");
		}

		return new RestartStatusResponse(
			true,
			record.Status.ToString().ToLowerInvariant(),
			record.OperationId,
			record.EnvironmentName ?? args.EnvironmentName,
			record.StartedUtc,
			record.FinishedUtc,
			record.ExitCode);
	}

}

/// <summary>
/// MCP arguments for the restart-status tool.
/// </summary>
public sealed record RestartStatusArgs(

	[property: JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("operation-id")]
	[Description("Optional operation id from a restart in-progress response. When omitted, returns the most recently started restart for this environment.")]
	string? OperationId);

/// <summary>
/// Response payload for the restart-status tool.
/// </summary>
public sealed record RestartStatusResponse(

	[property: JsonPropertyName("success")]
	[Description("False only for an invalid request (e.g. empty environment-name); true whenever the lookup itself completed, including a not-found result.")]
	bool Success,

	[property: JsonPropertyName("status")]
	[Description("One of: running, ready, timedout, not-found, invalid-request.")]
	string Status,

	[property: JsonPropertyName("operation-id")]
	string OperationId = null,

	[property: JsonPropertyName("environment-name")]
	string EnvironmentName = null,

	[property: JsonPropertyName("started-utc")]
	DateTime? StartedUtc = null,

	[property: JsonPropertyName("finished-utc")]
	DateTime? FinishedUtc = null,

	[property: JsonPropertyName("exit-code")]
	[Description("0 once the instance answered its health-check (ready); non-zero when the readiness wait timed out; null while still running.")]
	int? ExitCode = null,

	[property: JsonPropertyName("note")]
	string Note = null);
