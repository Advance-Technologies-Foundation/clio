using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that checks record-triggered processes end to end: create the record, wait for what it starts, report
/// what those runs created and how they ended, and clean up.
/// </summary>
[McpServerToolType]
public sealed class TriggerSmokeTool(
	IToolCommandResolver commandResolver,
	IOperationCorrelationIdProvider correlationIds) {

	internal const string ToolName = "trigger-smoke";

	internal const string ValidArgsHint = "Valid: environment-name, entity, values, watch, process, timeout, cleanup.";

	/// <summary>Runs one trigger smoke check.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Verify a record-triggered (signal-start) process in ONE call - the check run-process cannot do, because a " +
		"signal start cannot be launched directly. Creates the triggering record from `values`, waits up to `timeout` " +
		"seconds for the process runs it starts (from SysProcessLog) to finish or park, and returns `processes` " +
		"[{processId, name, status, error, waitingOn, stateBeforeCancel}] - only runs attributable to this call: the " +
		"caller's own, and with `process` only that process's - `unrelated` [{processId, name, reason}] for the other " +
		"runs of the window, which never count, and `created` [{entity, id, title}] - the records that appeared in the " +
		"`watch` entities. Then, unless cleanup=false, it deletes only what it can tie to this call: the trigger, and " +
		"the caller's new records whose lookup points at the trigger (or at another such record), children first; it " +
		"cancels a still-running attributable run only when that run holds one of those records. A record the process " +
		"created with no lookup to the trigger is NOT deleted: it stays in cleanup.skipped, so the stand is not " +
		"necessarily clean afterwards. success=false when the trigger could not be created, no attributable run " +
		"started, one ended in error, or the state of one could not be read. " +
		McpToolDescriptions.CorrelationIdOnEveryResponse)]
	public TriggerSmokeResponse Run(
		[Description("Parameters: environment-name, entity, values, watch, process, timeout, cleanup.")]
		[Required]
		TriggerSmokeArgs args) {
		string correlationId = correlationIds.New();
		TriggerSmokeResponse response = RunCore(args);
		response.CorrelationId = correlationId;
		return response;
	}

	private TriggerSmokeResponse RunCore(TriggerSmokeArgs args) {
		if (args is null) {
			return TriggerSmokeResponse.Failure("args is required. " + ValidArgsHint);
		}
		string argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(args.ExtensionData, ValidArgsHint);
		if (string.IsNullOrWhiteSpace(argumentError)) {
			argumentError = Validate(args);
		}
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return TriggerSmokeResponse.Failure(argumentError);
		}
		try {
			ITriggerSmokeRunner runner = commandResolver.Resolve<ITriggerSmokeRunner>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
			TriggerSmokeResult result = runner.Run(new TriggerSmokeRequest(
				args.Entity.Trim(),
				args.Values ?? [],
				args.Watch ?? [],
				args.Timeout ?? TriggerSmokeRunner.DefaultTimeoutSeconds,
				args.Cleanup ?? true,
				string.IsNullOrWhiteSpace(args.Process) ? null : args.Process.Trim()));
			return TriggerSmokeResponse.From(result);
		}
		catch (Exception exception) {
			return TriggerSmokeResponse.Failure(SensitiveErrorTextRedactor.Redact(exception.Message));
		}
	}

	internal static string Validate(TriggerSmokeArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return "environment-name is required.";
		}
		if (!CreatedRecordWatcher.IsValidEntityName(args.Entity?.Trim())) {
			return "entity must be an entity schema name (letters, digits, underscore).";
		}
		if (args.Values is null || args.Values.Count == 0) {
			return "values is required: the column values of the triggering record, as OData property names "
				+ "(a lookup as <Column>Id with the record GUID), for example {\"Name\": \"Smoke\", \"AccountId\": \"…\"}.";
		}
		string badColumn = args.Values.Keys.FirstOrDefault(key => !ODataKeyFormatter.IsSimpleIdentifier(key));
		if (badColumn is not null) {
			return $"values keys must be plain property names; '{badColumn}' is not one.";
		}
		IReadOnlyList<string> watch = args.Watch ?? [];
		if (watch.Count == 0) {
			return "watch is required: list the entities the started process writes, for example [\"Activity\"]. The "
				+ "platform keeps no list of what a run created, so clio reports only what appears in watched entities.";
		}
		if (watch.Count > TriggerSmokeRunner.MaxWatchedEntities || watch.Any(name => !CreatedRecordWatcher.IsValidEntityName(name))) {
			return $"watch takes 1-{TriggerSmokeRunner.MaxWatchedEntities} entity schema names.";
		}
		if (args.Timeout is < 1 or > TriggerSmokeRunner.MaxTimeoutSeconds) {
			return $"timeout must be between 1 and {TriggerSmokeRunner.MaxTimeoutSeconds} seconds.";
		}
		return null;
	}
}

/// <summary>Arguments for <see cref="TriggerSmokeTool"/>.</summary>
public sealed record TriggerSmokeArgs {

	/// <summary>Unknown top-level keys, named back to the caller.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Entity of the triggering record.</summary>
	[JsonPropertyName("entity")]
	[Description("Entity whose 'record added' signal starts the process under test, e.g. UsrRequest.")]
	[Required]
	public required string Entity { get; init; }

	/// <summary>Column values of the triggering record.</summary>
	[JsonPropertyName("values")]
	[Description("Column values of the record to create, as OData property names; a lookup is <Column>Id with the "
		+ "record GUID. Fill what the signal's filter checks.")]
	[Required]
	public Dictionary<string, JsonElement>? Values { get; init; }

	/// <summary>Entities to diff.</summary>
	[JsonPropertyName("watch")]
	[Description("1-5 entities the started process writes, e.g. [\"Activity\"]; `created` lists what appeared in them.")]
	[Required]
	public string[]? Watch { get; init; }

	/// <summary>The process under test.</summary>
	[JsonPropertyName("process")]
	[Description("Code or caption of the process under test. Only its runs (and their sub-processes) decide success; "
		+ "every other run of the window goes to `unrelated`. Without it, every run the calling login started in the "
		+ "window counts, a scheduled one of the same login included.")]
	public string? Process { get; init; }

	/// <summary>Wait, in seconds.</summary>
	[JsonPropertyName("timeout")]
	[Description("Seconds to wait for runs to start and finish or park, 1-30, default 15. The wait ends early once "
		+ "every started run has finished or parked and nothing changed for 3 s.")]
	public int? Timeout { get; init; }

	/// <summary>Whether to clean up.</summary>
	[JsonPropertyName("cleanup")]
	[Description("Default true: delete the trigger and the caller's new records that point at it through a lookup, "
		+ "children first, cancelling first a still-running run of this call that holds one of them. Nothing else is "
		+ "cancelled or deleted - a created record with no lookup to the trigger stays and is listed in cleanup.skipped. "
		+ "false keeps everything for inspection.")]
	public bool? Cleanup { get; init; }
}

/// <summary>Response of <see cref="TriggerSmokeTool"/>.</summary>
public sealed record TriggerSmokeResponse {

	/// <summary>See <see cref="TriggerSmokeResult.Success"/>.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>See <see cref="TriggerSmokeResult.Error"/>.</summary>
	[JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }

	/// <summary>See <see cref="TriggerSmokeResult.Trigger"/>.</summary>
	[JsonPropertyName("trigger"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CreatedRecord? Trigger { get; init; }

	/// <summary>See <see cref="TriggerSmokeResult.Processes"/>.</summary>
	[JsonPropertyName("processes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<SmokeProcessRun>? Processes { get; init; }

	/// <summary>See <see cref="TriggerSmokeResult.Unrelated"/>.</summary>
	[JsonPropertyName("unrelated"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<UnrelatedRun>? Unrelated { get; init; }

	/// <summary>See <see cref="TriggerSmokeResult.Created"/>.</summary>
	[JsonPropertyName("created"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<CreatedRecord>? Created { get; init; }

	/// <summary>See <see cref="TriggerSmokeResult.Cleanup"/>.</summary>
	[JsonPropertyName("cleanup"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CleanupReport? Cleanup { get; init; }

	/// <summary>See <see cref="TriggerSmokeResult.Cancelled"/>.</summary>
	[JsonPropertyName("cancelled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? Cancelled { get; init; }

	/// <summary>See <see cref="TriggerSmokeResult.Warnings"/>.</summary>
	[JsonPropertyName("warnings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? Warnings { get; init; }

	/// <summary>Identifier of this call.</summary>
	[JsonPropertyName("correlation-id")]
	public string? CorrelationId { get; set; }

	internal static TriggerSmokeResponse Failure(string error) => new() { Success = false, Error = error };

	internal static TriggerSmokeResponse From(TriggerSmokeResult result) => new() {
		Success = result.Success,
		Error = result.Error,
		Trigger = result.Trigger,
		Processes = result.Processes,
		Unrelated = result.Unrelated.Count > 0 ? result.Unrelated : null,
		Created = result.Created,
		Cleanup = result.Cleanup,
		Cancelled = result.Cancelled,
		Warnings = result.Warnings.Count > 0 ? result.Warnings : null
	};
}
