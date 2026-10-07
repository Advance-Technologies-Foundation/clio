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
	/// The note a <c>not-found</c> answer carries.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>It must not read as "nothing ran" (ENG-102333).</b> The record lives only in this MCP server session,
	/// and only for a while after its compile ends, while the environment itself still holds the verdict of its
	/// latest build. An agent told only that nothing was recorded either guesses or compiles again, and a
	/// second compile is a second runtime reload for every user; <c>last-compilation-log</c> reads that verdict
	/// without compiling. The note names no list of reasons on purpose: any list is one more place to fall
	/// out of date.
	/// </para>
	/// <para>
	/// <b>That verdict carries no time.</b> It belongs to the latest FINISHED build (see
	/// <see cref="Clio.Common.ICompilationResultReader"/>), so read while a compile is still running it is the
	/// previous build's — which is why the note says to wait before relying on it and never to restart on it.
	/// </para>
	/// </remarks>
	internal const string NotFoundNote =
		"This MCP server session holds no record of a compile-creatio operation for this environment. That does "
		+ "not mean no compile ran: a record lives only in this session, and only for a while after its compile "
		+ "ends. Do not compile again to find out. " + LastCompilationLogTool.ToolName + " (through clio-run) "
		+ "reads the environment's latest FINISHED compile and carries no time: while a compile may still be "
		+ "running (a process-name compile takes minutes, a full one up to about 20) it can return an earlier "
		+ "compile's verdict, so wait that long before relying on it, and never restart the environment on that "
		+ "answer alone.";

	/// <summary>
	/// Returns the tracked status of a compile-creatio operation.
	/// </summary>
	[McpServerTool(Name = CompileStatusToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.Sticky,
		OperationFamily = McpToolOperationFamily.ConfigurationBuild,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.ConfigurationBuild)]
	[Description("Returns the status of the most recent compile-creatio operation tracked for an environment, or of a specific operation-id from a compile-creatio in-progress response. Use this after compile-creatio returns an in-progress note, AND after your MCP client stopped waiting for compile-creatio (for example 'Request timed out'): the compile keeps running and is tracked here. Do not re-run compile-creatio just to check. A not-found answer means this MCP server session holds no record, not that nothing ran: then last-compilation-log (through clio-run) reads the environment's latest FINISHED compile - it carries no time, so while a compile may still be running it can be an earlier compile's verdict, and it is never a reason to restart.")]
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
				Note: NotFoundNote);
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
			record.MessageTail,
			ProcessName: record.ProcessName);
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
	string? OperationId = null);

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
	[Description("The single package compiled, or null for a full compilation - or for a process-name compile, which sets process-name instead.")]
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
	string Note = null,

	[property: JsonPropertyName("process-name")]
	[Description("The business process whose package a process-name compile compiled; null otherwise.")]
	string ProcessName = null);
