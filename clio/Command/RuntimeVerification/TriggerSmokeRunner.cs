using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Newtonsoft.Json;

namespace Clio.Command.RuntimeVerification;

/// <summary>
/// Creates a record, waits for the processes it starts (signal starts), reports what they created in the watched
/// entities and how they ended, then cleans up.
/// </summary>
public interface ITriggerSmokeRunner {

	/// <summary>Runs one smoke check; never throws for a server-side refusal, which is reported instead.</summary>
	/// <param name="request">What to create, what to watch, how long to wait and whether to clean up.</param>
	/// <returns>The outcome.</returns>
	TriggerSmokeResult Run(TriggerSmokeRequest request);
}

/// <summary>Input of <see cref="ITriggerSmokeRunner.Run"/>.</summary>
/// <param name="Entity">Entity of the triggering record.</param>
/// <param name="Values">Column values of the triggering record, as OData property names (lookups as <c>AccountId</c>).</param>
/// <param name="Watch">Entities the started processes are expected to write.</param>
/// <param name="TimeoutSeconds">How long to wait for processes to start and settle.</param>
/// <param name="Cleanup">Whether to cancel what is still running and delete what was created, the trigger included.</param>
public sealed record TriggerSmokeRequest(
	string Entity,
	IReadOnlyDictionary<string, JsonElement> Values,
	IReadOnlyList<string> Watch,
	int TimeoutSeconds,
	bool Cleanup);

/// <summary>One process run started while the smoke check waited.</summary>
public sealed record SmokeProcessRun(
	[property: JsonProperty("processId")]
	[property: System.Text.Json.Serialization.JsonPropertyName("processId")]
	string ProcessId,
	[property: JsonProperty("name")]
	[property: System.Text.Json.Serialization.JsonPropertyName("name")]
	string Name,
	[property: JsonProperty("status")]
	[property: System.Text.Json.Serialization.JsonPropertyName("status")]
	string Status,
	[property: JsonProperty("error")]
	[property: System.Text.Json.Serialization.JsonPropertyName("error")]
	string Error,
	[property: JsonProperty("waitingOn")]
	[property: System.Text.Json.Serialization.JsonPropertyName("waitingOn")]
	IReadOnlyList<WaitingElement> WaitingOn);

/// <summary>Outcome of <see cref="ITriggerSmokeRunner.Run"/>.</summary>
public sealed class TriggerSmokeResult {

	/// <summary>False when the trigger could not be created, or a started process ended in error.</summary>
	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>Why the check failed.</summary>
	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }

	/// <summary>The triggering record.</summary>
	[JsonProperty("trigger")]
	[System.Text.Json.Serialization.JsonPropertyName("trigger")]
	public CreatedRecord Trigger { get; set; }

	/// <summary>Process runs that started while the check waited.</summary>
	[JsonProperty("processes")]
	[System.Text.Json.Serialization.JsonPropertyName("processes")]
	public List<SmokeProcessRun> Processes { get; set; } = [];

	/// <summary>Records that appeared in the watched entities, the trigger excluded.</summary>
	[JsonProperty("created")]
	[System.Text.Json.Serialization.JsonPropertyName("created")]
	public List<CreatedRecord> Created { get; set; } = [];

	/// <summary>What cleanup did; null when cleanup was off.</summary>
	[JsonProperty("cleanup")]
	[System.Text.Json.Serialization.JsonPropertyName("cleanup")]
	public CleanupReport Cleanup { get; set; }

	/// <summary>Runs cleanup cancelled before deleting.</summary>
	[JsonProperty("cancelled")]
	[System.Text.Json.Serialization.JsonPropertyName("cancelled")]
	public List<string> Cancelled { get; set; }

	/// <summary>Caveats.</summary>
	[JsonProperty("warnings")]
	[System.Text.Json.Serialization.JsonPropertyName("warnings")]
	public List<string> Warnings { get; set; } = [];
}

internal sealed class TriggerSmokeRunner(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IRunVerificationServices verification)
	: ITriggerSmokeRunner {

	internal const int DefaultTimeoutSeconds = 15;
	internal const int MaxTimeoutSeconds = 30;
	internal const int MaxWatchedEntities = 5;

	// Processes are considered settled when nothing new started and no state changed for this long.
	private const int QuietMs = 3_000;
	private const int PollIntervalMs = 1_000;
	private const int RequestMs = 10_000;
	private const int CancelWaitMs = 10_000;
	// Below the worker's 120 s per-call budget, with room for the answer.
	private const int TotalBudgetMs = 90_000;
	// A background-mode signal run starts a few seconds after the insert; the wait never settles before this.
	private const int MinimumWaitMs = 6_000;
	private const string ProcessLogEntity = "SysProcessLog";
	private const string RunningStatus = "running";
	private const string ErrorStatus = "error";
	private const string CancelledStatus = "cancelled";

	// Test seams: how the wait sleeps between polls, and the clock it measures the wait with.
	internal Action<int> Sleep { get; set; } = Thread.Sleep;

	internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

	/// <inheritdoc />
	public TriggerSmokeResult Run(TriggerSmokeRequest request) {
		TriggerSmokeResult result = new();
		// Everything - the wait, cancels and deletes - stops starting requests here, so the worker answers before its
		// parent's per-call budget kills it.
		DateTimeOffset stopAt = Clock().AddMilliseconds(TotalBudgetMs);
		List<string> watch = [.. request.Watch.Where(name => !string.Equals(name, request.Entity, StringComparison.Ordinal))];
		// The trigger's own entity is watched too, so cleanup knows what references it.
		WatchBaseline records = verification.Watcher.Capture([request.Entity, .. watch], RequestMs);
		WatchBaseline processes = verification.Watcher.Capture([ProcessLogEntity], RequestMs);

		string triggerId = CreateTrigger(request, out string createError);
		if (triggerId is not null) {
			// Created by this call, so the caller's own record whatever the after-read finds.
			result.Trigger = new CreatedRecord(request.Entity, triggerId, null) { CreatedById = records.CurrentUserContactId };
		}
		// A refused create can still have saved the row - a post-insert handler or a synchronous signal run that
		// threw - so what appeared is read and reported even then; it is just not waited for.
		List<CreatedRecord> runs = WaitForProcesses(processes, triggerId is null ? 0 : request.TimeoutSeconds, stopAt, result);
		WatchOutcome outcome = verification.Watcher.Collect(records, RequestMs);
		result.Warnings.AddRange(outcome.Warnings);
		CreatedRecord trigger = triggerId is null ? null : outcome.Created.FirstOrDefault(record =>
			string.Equals(record.Entity, request.Entity, StringComparison.Ordinal)
			&& string.Equals(record.Id, triggerId, StringComparison.OrdinalIgnoreCase));
		if (trigger is not null) {
			result.Trigger = trigger;
		}
		// Other new rows of the trigger's own entity are reported only when the caller watched that entity, or when the
		// create was refused and one of them may be the trigger.
		bool reportTriggerEntity = triggerId is null || request.Watch.Contains(request.Entity, StringComparer.Ordinal);
		result.Created = outcome.Created.Where(record => !ReferenceEquals(record, trigger)
			&& (reportTriggerEntity || !string.Equals(record.Entity, request.Entity, StringComparison.Ordinal)))
			.ToList();
		result.Success = Judge(request, triggerId, createError, result);
		if (request.Cleanup) {
			CleanUp(records, runs, stopAt, result);
		}
		return result;
	}

	private static bool Judge(TriggerSmokeRequest request, string triggerId, string createError, TriggerSmokeResult result) {
		if (triggerId is null) {
			result.Error = createError;
			return false;
		}
		if (result.Processes.Count == 0) {
			result.Error = $"No process started within {request.TimeoutSeconds} s of creating the record. Check that "
				+ "the process is active, that its signal is 'record added' on this entity, and that its filter matches "
				+ "the values; a process that runs in the background may also start later than the wait.";
			return false;
		}
		List<SmokeProcessRun> failed = result.Processes.Where(run => run.Status == ErrorStatus).ToList();
		if (failed.Count > 0) {
			result.Error = $"{failed.Count} started process run(s) ended in error; processes[].error names each.";
			return false;
		}
		return true;
	}

	private string CreateTrigger(TriggerSmokeRequest request, out string error) {
		error = null;
		try {
			string url = serviceUrlBuilder.Build(ODataKeyFormatter.CollectionPath(request.Entity));
			string body = System.Text.Json.JsonSerializer.Serialize(request.Values ?? new Dictionary<string, JsonElement>());
			string response = applicationClient.ExecuteNonReplayablePostRequest(url, body, RequestMs * 3);
			using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(response) ? "{}" : response);
			if (CreatioResponseError.TryDetect(document.RootElement, CreatioResponseContext.ODataPayload, out string serverError)) {
				string inner = RecordDeletionService.ReadInnerMessage(response);
				error = $"The triggering {request.Entity} record was not created: "
					+ CreatioResponseError.AppendStructuredODataWriteError(SensitiveErrorTextRedactor.Redact(serverError),
						document.RootElement)
					+ (inner is null ? string.Empty : $" The server's own reason: {inner}.");
				return null;
			}
			if (document.RootElement.ValueKind == JsonValueKind.Object
					&& document.RootElement.TryGetProperty("Id", out JsonElement id) && id.ValueKind == JsonValueKind.String) {
				return id.GetString();
			}
			error = $"The triggering {request.Entity} record was not confirmed: the create answered without an Id. "
				+ "Check with odata-read before creating it again.";
			return null;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			error = $"The triggering {request.Entity} record could not be created: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message)
				+ ". The request may still have reached the server; check with odata-read before creating it again.";
			return null;
		}
	}

	// Polls SysProcessLog for runs that appeared since the baseline, and their states, until every run has finished
	// or parked, nothing changed for QuietMs and at least MinimumWaitMs passed - or the timeout passes. A run already
	// in a final state is not read again.
	private List<CreatedRecord> WaitForProcesses(WatchBaseline baseline, int timeoutSeconds, DateTimeOffset stopAt,
		TriggerSmokeResult result) {
		DateTimeOffset started = Clock();
		DateTimeOffset until = started.AddSeconds(timeoutSeconds);
		if (until > stopAt) {
			until = stopAt;
		}
		DateTimeOffset quietSince = started;
		string lastSignature = null;
		Dictionary<string, SmokeProcessRun> final = new(StringComparer.OrdinalIgnoreCase);
		while (true) {
			WatchOutcome outcome = verification.Watcher.Collect(baseline, RequestMs);
			List<CreatedRecord> runs = outcome.Created;
			result.Processes = runs.Select(run => final.TryGetValue(run.Id, out SmokeProcessRun done) ? done : ReadRun(run))
				.ToList();
			foreach (SmokeProcessRun run in result.Processes.Where(IsFinal)) {
				final[run.ProcessId] = run;
			}
			string signature = string.Join("|", result.Processes.Select(run => run.ProcessId + ":" + run.Status));
			if (signature != lastSignature) {
				lastSignature = signature;
				quietSince = Clock();
			}
			DateTimeOffset now = Clock();
			bool settled = result.Processes.Count > 0
				&& result.Processes.All(run => run.Status != RunningStatus || run.WaitingOn.Count > 0)
				&& now - quietSince >= TimeSpan.FromMilliseconds(QuietMs)
				&& now - started >= TimeSpan.FromMilliseconds(Math.Min(MinimumWaitMs, timeoutSeconds * 1000));
			int leftMs = (int)(until - now).TotalMilliseconds;
			if (settled || leftMs <= 0) {
				result.Warnings.AddRange(outcome.Warnings);
				return runs;
			}
			Sleep(Math.Min(PollIntervalMs, leftMs));
		}
	}

	private static bool IsFinal(SmokeProcessRun run) => run.Status is "completed" or ErrorStatus or CancelledStatus;

	private SmokeProcessRun ReadRun(CreatedRecord run) {
		try {
			ProcessRunState state = verification.Monitor.Read(Guid.Parse(run.Id), RequestMs);
			string status = state.StatusValue switch {
				0 => "inactive", 1 => RunningStatus, 2 => "completed", 3 => ErrorStatus, 4 => CancelledStatus, 5 => "cancelling",
				null => "unknown",
				_ => $"unknown-status-{state.StatusValue}"
			};
			return new SmokeProcessRun(run.Id, run.Title, status,
				state.StatusValue == 3 ? UntrustedText.Fenced(state.ErrorSummary) ?? "no error text was logged" : null,
				state.WaitingOn);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return new SmokeProcessRun(run.Id, run.Title, "unknown",
				"its state could not be read: " + (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name), []);
		}
	}

	private void CleanUp(WatchBaseline records, List<CreatedRecord> runs, DateTimeOffset stopAt, TriggerSmokeResult result) {
		string me = records.CurrentUserContactId;
		// Only runs this user started: a scheduled run of another user that happened to start in the window is left.
		List<string> toCancel = result.Processes
			.Where(run => run.Status == RunningStatus)
			.Where(run => me is not null && string.Equals(
				runs.FirstOrDefault(record => record.Id == run.ProcessId)?.CreatedById, me, StringComparison.OrdinalIgnoreCase))
			.Select(run => run.ProcessId)
			.ToList();
		result.Cancelled = [];
		foreach (string processId in toCancel) {
			if (Clock() >= stopAt) {
				result.Warnings.Add($"Run {processId} was not cancelled: the call's time budget ran out.");
				continue;
			}
			if (CancelAndWait(Guid.Parse(processId), result)) {
				result.Cancelled.Add(processId);
				result.Processes = result.Processes
					.Select(run => run.ProcessId == processId ? run with { Status = CancelledStatus, WaitingOn = [] } : run)
					.ToList();
			}
		}
		List<CreatedRecord> all = [.. result.Created];
		if (result.Trigger is not null) {
			all.Add(result.Trigger);
		}
		List<CreatedRecord> mine = me is null
			? []
			: all.Where(record => string.Equals(record.CreatedById, me, StringComparison.OrdinalIgnoreCase)).ToList();
		Dictionary<string, IReadOnlyCollection<string>> references = records.Entities
			.Where(watch => watch.Error is null)
			.ToDictionary(watch => watch.Entity, watch => watch.References, StringComparer.Ordinal);
		result.Cleanup = verification.Cleaner.Delete(mine, references, RequestMs, stopAt) ?? new CleanupReport();
		result.Cleanup.Skipped = all.Except(mine).ToList();
		if (result.Cleanup.Skipped.Count > 0) {
			result.Warnings.Add(me is null
				? "cleanup deleted nothing: the calling user's contact could not be read. cleanup.skipped lists the records."
				: $"cleanup left {result.Cleanup.Skipped.Count} record(s) another user created in the same window; "
					+ "cleanup.skipped lists them.");
		}
		if (result.Cleanup.Failed.Count > 0) {
			result.Warnings.Add($"cleanup left {result.Cleanup.Failed.Count} record(s) in place; cleanup.failed names "
				+ "each with the reason. Delete them with odata-delete, which explains the refusal.");
		}
	}

	private bool CancelAndWait(Guid processId, TriggerSmokeResult result) {
		try {
			string error = verification.Monitor.Cancel(processId, RequestMs);
			if (!string.IsNullOrEmpty(error)) {
				result.Warnings.Add($"Run {processId} was not cancelled: {error}.");
				return false;
			}
			DateTimeOffset until = Clock().AddMilliseconds(CancelWaitMs);
			while (Clock() < until) {
				if (verification.Monitor.Read(processId, RequestMs).IsTerminal) {
					return true;
				}
				Sleep(PollIntervalMs);
			}
			result.Warnings.Add($"Run {processId} had not stopped 10 s after the cancel; records it uses may refuse deletion.");
			return false;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			result.Warnings.Add($"Run {processId} could not be cancelled: "
				+ (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name) + ".");
			return false;
		}
	}
}
