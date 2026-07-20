using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for polling a previously started <c>compile-creatio</c> operation, used after that
/// tool returns an in-progress notice past the MCP response deadline (ENG-91315).
/// </summary>
[McpServerToolType]
public sealed class CompileStatusTool(ICompileOperationRegistry registry, IToolCommandResolver commandResolver) {

	/// <summary>
	/// Stable MCP tool name for compile-status.
	/// </summary>
	internal const string CompileStatusToolName = "compile-status";

	/// <summary>
	/// Returns the tracked status of a compile-creatio operation.
	/// </summary>
	[McpServerTool(Name = CompileStatusToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns the status of the most recent compile-creatio operation tracked for an environment, or of a specific operation-id from a compile-creatio in-progress response. Use this after compile-creatio returns an in-progress note to check whether the compile finished; do not re-run compile-creatio just to check.")]
	public CompileStatusResponse GetStatus(
		[Description("Status query parameters")] [Required] CompileStatusArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return new CompileStatusResponse(false, "invalid-request",
				Note: "environment-name is required and cannot be empty.");
		}

		string callerTenantKey = commandResolver.GetTenantKey(new EnvironmentOptions { Environment = args.EnvironmentName });
		CompileOperationRecord record = string.IsNullOrWhiteSpace(args.OperationId)
			? registry.GetLatest(callerTenantKey)
			: registry.GetById(args.OperationId.Trim());

		// Scope operation-id lookups to the caller's tenant: on a shared MCP HTTP server a caller who obtains
		// (or guesses) another session's global operation id must not read its environment/package/exit-code/
		// message-tail. GetLatest is already tenant-keyed, so this only tightens the GetById path.
		if (record is not null && !string.Equals(record.TenantKey, callerTenantKey, StringComparison.Ordinal)) {
			record = null;
		}

		if (record is null) {
			return new CompileStatusResponse(true, "not-found", EnvironmentName: args.EnvironmentName,
				Note: "No compile-creatio operation has been recorded for this environment in the current MCP server session.");
		}

		return new CompileStatusResponse(
			true,
			record.Status.ToString().ToLowerInvariant(),
			record.OperationId,
			record.EnvironmentName,
			record.PackageName,
			record.StartedUtc,
			record.FinishedUtc,
			record.ExitCode,
			record.MessageTail);
	}

}

/// <summary>
/// MCP arguments for the compile-status tool.
/// </summary>
public sealed record CompileStatusArgs(

	[property: JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("operation-id")]
	[Description("Optional operation id from a compile-creatio in-progress response. When omitted, returns the most recently started operation for this environment.")]
	string? OperationId);

/// <summary>
/// Response payload for the compile-status tool.
/// </summary>
public sealed record CompileStatusResponse(

	[property: JsonPropertyName("success")]
	[Description("False only for an invalid request (e.g. empty environment-name); true whenever the lookup itself completed, including a not-found result.")]
	bool Success,

	[property: JsonPropertyName("status")]
	[Description("One of: running, succeeded, failed, not-found, invalid-request.")]
	string Status,

	[property: JsonPropertyName("operation-id")]
	string OperationId = null,

	[property: JsonPropertyName("environment-name")]
	string EnvironmentName = null,

	[property: JsonPropertyName("package-name")]
	[Description("The single package compiled, or null for a full compilation.")]
	string PackageName = null,

	[property: JsonPropertyName("started-utc")]
	DateTime? StartedUtc = null,

	[property: JsonPropertyName("finished-utc")]
	DateTime? FinishedUtc = null,

	[property: JsonPropertyName("exit-code")]
	int? ExitCode = null,

	[property: JsonPropertyName("message-tail")]
	[Description("The trailing lines of compile output captured when the operation finished; empty while running.")]
	IReadOnlyList<string> MessageTail = null,

	[property: JsonPropertyName("note")]
	string Note = null);
