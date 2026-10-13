using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.EntitySchema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

	/// <summary>
	/// Records that appeared in the watched entities, the trigger excluded. The window is shared with every other session,
	/// so this can include records this call did not cause; cleanup deletes only those it can tie to the trigger.
	/// </summary>
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
	IRunVerificationServices verification,
	IRuntimeEntitySchemaReader schemaReader)
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
	// The baselines are read before the create; they never take more than this of the budget.
	private const int CaptureBudgetMs = 15_000;
	private const int MinimumRequestMs = 1_000;
	private const int GuidDataValueType = 0;
	private const string ProcessElementColumn = "ProcessElementId";
	// Every row has them, and they point at the user, never at the trigger.
	private static readonly HashSet<string> AuditColumns = new(StringComparer.Ordinal) { "CreatedBy", "ModifiedBy" };
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
		DateTimeOffset captureStop = Clock().AddMilliseconds(CaptureBudgetMs);
		List<string> watch = [.. request.Watch.Where(name => !string.Equals(name, request.Entity, StringComparison.OrdinalIgnoreCase))];
		// The trigger's own entity is watched too, so cleanup knows what references it.
		WatchBaseline records = verification.Watcher.Capture([request.Entity, .. watch], RequestMs, captureStop);
		WatchBaseline processes = verification.Watcher.Capture([ProcessLogEntity], RequestMs, captureStop);

		string triggerId = CreateTrigger(request, Budget(stopAt, RequestMs * 3), out string createError);
		if (triggerId is not null) {
			// Created by this call, so the caller's own record whatever the after-read finds.
			result.Trigger = new CreatedRecord(request.Entity, triggerId, null) { CreatedById = records.CurrentUserContactId };
		}
		// A refused create can still have saved the row - a post-insert handler or a synchronous signal run that
		// threw - so what appeared is read and reported even then; it is just not waited for.
		List<CreatedRecord> runs = WaitForProcesses(processes, triggerId is null ? 0 : request.TimeoutSeconds, stopAt, result);
		ReadCreated(request, records, triggerId, stopAt, result);
		result.Success = Judge(request, triggerId, createError, result);
		if (request.Cleanup) {
			CleanUp(request, records, runs, triggerId, stopAt, result);
		}
		return result;
	}

	// Reads the watched entities and splits the trigger off from what else appeared.
	private void ReadCreated(TriggerSmokeRequest request, WatchBaseline records, string triggerId, DateTimeOffset stopAt,
		TriggerSmokeResult result) {
		WatchOutcome outcome = verification.Watcher.Collect(records, RequestMs, stopAt);
		result.Warnings.AddRange(outcome.Warnings.Except(result.Warnings));
		CreatedRecord trigger = triggerId is null ? null : outcome.Created.FirstOrDefault(record =>
			string.Equals(record.Entity, request.Entity, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(record.Id, triggerId, StringComparison.OrdinalIgnoreCase));
		if (trigger is not null) {
			result.Trigger = trigger;
		}
		// Other new rows of the trigger's own entity are reported only when the caller watched that entity, or when the
		// create was refused and one of them may be the trigger.
		bool reportTriggerEntity = triggerId is null || request.Watch.Contains(request.Entity, StringComparer.OrdinalIgnoreCase);
		result.Created = outcome.Created.Where(record => !ReferenceEquals(record, trigger)
			&& (reportTriggerEntity || !string.Equals(record.Entity, request.Entity, StringComparison.OrdinalIgnoreCase)))
			.ToList();
	}

	// The time left before stopAt, at most maxMs and at least 1 ms.
	private int Budget(DateTimeOffset stopAt, int maxMs) =>
		(int)Math.Clamp((stopAt - Clock()).TotalMilliseconds, 1, maxMs);

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

	private string CreateTrigger(TriggerSmokeRequest request, int timeoutMs, out string error) {
		error = null;
		try {
			string url = serviceUrlBuilder.Build(ODataKeyFormatter.CollectionPath(request.Entity));
			string body = System.Text.Json.JsonSerializer.Serialize(request.Values ?? new Dictionary<string, JsonElement>());
			string response = applicationClient.ExecuteNonReplayablePostRequest(url, body, timeoutMs);
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
		Dictionary<string, SmokeProcessRun> last = new(StringComparer.OrdinalIgnoreCase);
		while (true) {
			WatchOutcome outcome = verification.Watcher.Collect(baseline, RequestMs, stopAt);
			List<CreatedRecord> runs = outcome.Created;
			// A run is read only while the wait lasts; one not reached keeps its last state, so a busy window cannot
			// stretch a single poll past the wait.
			result.Processes = runs.Select(run => CurrentState(run, final, last, until, stopAt)).ToList();
			foreach (SmokeProcessRun run in result.Processes) {
				last[run.ProcessId] = run;
				if (IsFinal(run)) {
					final[run.ProcessId] = run;
				}
			}
			string signature = string.Join("|", result.Processes.Select(run => run.ProcessId + ":" + run.Status));
			if (signature != lastSignature) {
				lastSignature = signature;
				quietSince = Clock();
			}
			DateTimeOffset now = Clock();
			bool settled = IsSettled(result.Processes, now - quietSince, now - started, timeoutSeconds);
			int leftMs = (int)(until - now).TotalMilliseconds;
			if (settled || leftMs <= 0) {
				result.Warnings.AddRange(outcome.Warnings);
				return runs;
			}
			Sleep(Math.Min(PollIntervalMs, leftMs));
		}
	}

	// A final run is not read again; others are read while the wait lasts, and keep their last state after it.
	private SmokeProcessRun CurrentState(CreatedRecord run, Dictionary<string, SmokeProcessRun> final,
		Dictionary<string, SmokeProcessRun> last, DateTimeOffset until, DateTimeOffset stopAt) {
		if (final.TryGetValue(run.Id, out SmokeProcessRun done)) {
			return done;
		}
		if (Clock() < until) {
			return ReadRun(run, Budget(stopAt, RequestMs));
		}
		return last.TryGetValue(run.Id, out SmokeProcessRun seen)
			? seen
			: new SmokeProcessRun(run.Id, run.Title, "unknown", "not read: the wait ended first", []);
	}

	private static bool IsSettled(IReadOnlyList<SmokeProcessRun> processes, TimeSpan quiet, TimeSpan waited,
		int timeoutSeconds) =>
		processes.Count > 0
		&& processes.All(run => run.Status != RunningStatus || run.WaitingOn.Count > 0)
		&& quiet >= TimeSpan.FromMilliseconds(QuietMs)
		&& waited >= TimeSpan.FromMilliseconds(Math.Min(MinimumWaitMs, timeoutSeconds * 1000));

	private static bool IsFinal(SmokeProcessRun run) => run.Status is "completed" or ErrorStatus or CancelledStatus;

	private SmokeProcessRun ReadRun(CreatedRecord run, int timeoutMs) {
		try {
			ProcessRunState state = verification.Monitor.Read(Guid.Parse(run.Id), timeoutMs);
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

	// Time links nothing to the trigger: anything another session - the same login included - created or started in
	// the window looks the same. So cleanup acts only on what it can tie to this call: the trigger it created, the
	// caller's new records whose lookup points at the trigger or at another such record, and a run only when it holds
	// one of those records. Everything else is reported in cleanup.skipped and left alone.
	private void CleanUp(TriggerSmokeRequest request, WatchBaseline records, List<CreatedRecord> runs, string triggerId,
		DateTimeOffset stopAt, TriggerSmokeResult result) {
		result.Cancelled = [];
		if (triggerId is null) {
			result.Cleanup = new CleanupReport { Skipped = [.. result.Created] };
			if (result.Created.Count > 0) {
				result.Warnings.Add("cleanup did nothing: the trigger was not confirmed, so nothing in the window can be "
					+ "shown to come from this call. cleanup.skipped lists the records; delete them with odata-delete "
					+ "after checking each.");
			}
			return;
		}
		string me = records.CurrentUserContactId;
		HashSet<string> ours = ProvablyOurs(triggerId, result.Created, me, stopAt, result);
		if (CancelHoldingRuns(ours, runs, me, stopAt, result)) {
			// A run cancelled mid-way can write before it stops; read again so nothing it wrote is missed.
			ReadCreated(request, records, triggerId, stopAt, result);
			ours = ProvablyOurs(triggerId, result.Created, me, stopAt, result);
		}
		List<CreatedRecord> all = [.. result.Created, result.Trigger];
		List<CreatedRecord> mine = all.Where(record => ours.Contains(record.Id)).ToList();
		Dictionary<string, IReadOnlyCollection<string>> references = records.Entities
			.Where(watch => watch.Error is null)
			.ToDictionary(watch => watch.Entity, watch => watch.References, StringComparer.OrdinalIgnoreCase);
		result.Cleanup = verification.Cleaner.Delete(mine, references, RequestMs, stopAt) ?? new CleanupReport();
		result.Cleanup.Skipped = all.Except(mine).ToList();
		if (result.Cleanup.Skipped.Count > 0) {
			result.Warnings.Add(me is null
				? "cleanup deleted only the trigger: the calling user's contact could not be read. cleanup.skipped lists the "
					+ "other records."
				: $"cleanup left {result.Cleanup.Skipped.Count} record(s) it cannot tie to the trigger - created by another "
					+ "user, or by the same login with no lookup to the trigger; cleanup.skipped lists them. Delete them "
					+ "with odata-delete only after checking each is a test record.");
		}
		if (result.Cleanup.Failed.Count > 0) {
			result.Warnings.Add($"cleanup left {result.Cleanup.Failed.Count} record(s) in place; cleanup.failed names "
				+ "each with the reason. Delete them with odata-delete, which explains the refusal.");
		}
	}

	// The trigger, then every new record of the caller whose lookup points at a record already in the set.
	private HashSet<string> ProvablyOurs(string triggerId, IReadOnlyList<CreatedRecord> created, string me,
		DateTimeOffset stopAt, TriggerSmokeResult result) {
		HashSet<string> ours = new(StringComparer.OrdinalIgnoreCase) { triggerId };
		List<CreatedRecord> candidates = me is null ? [] : created
			.Where(record => record.Id is not null && string.Equals(record.CreatedById, me, StringComparison.OrdinalIgnoreCase))
			.ToList();
		Dictionary<string, HashSet<string>> pointsAt = ReadLookupValues(candidates, stopAt, result);
		List<string> linked;
		do {
			linked = candidates
				.Select(record => record.Id)
				.Where(id => !ours.Contains(id) && pointsAt.TryGetValue(id, out HashSet<string> values) && values.Overlaps(ours))
				.ToList();
			ours.UnionWith(linked);
		} while (linked.Count > 0);
		return ours;
	}

	// Per record id, the values of its lookup columns (audit columns excluded). An entity that cannot be read links
	// nothing, so its records stay skipped.
	private Dictionary<string, HashSet<string>> ReadLookupValues(IReadOnlyList<CreatedRecord> records,
		DateTimeOffset stopAt, TriggerSmokeResult result) {
		Dictionary<string, HashSet<string>> values = new(StringComparer.OrdinalIgnoreCase);
		foreach (IGrouping<string, CreatedRecord> entity in records.GroupBy(record => record.Entity, StringComparer.OrdinalIgnoreCase)) {
			try {
				List<string> lookups = schemaReader.GetByName(entity.Key, Budget(stopAt, RequestMs)).Columns
					.Where(column => !string.IsNullOrWhiteSpace(column.ReferenceSchemaName) && !AuditColumns.Contains(column.Name)
						&& CreatedRecordWatcher.IsValidEntityName(column.Name))
					.Select(column => column.Name)
					.ToList();
				if (lookups.Count == 0) {
					continue;
				}
				foreach (JToken row in SelectByIds(entity.Key, lookups, entity.Select(record => record.Id), stopAt)) {
					values[row["Id"]?.ToString() ?? string.Empty] = lookups
						.Select(column => LookupValue(row[column]))
						.Where(value => value is not null)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);
				}
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				result.Warnings.Add($"cleanup could not read the lookups of the new {entity.Key} records, so none of them "
					+ "can be tied to the trigger: " + (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name));
			}
		}
		return values;
	}

	private JArray SelectByIds(string entity, IEnumerable<string> columns, IEnumerable<string> ids, DateTimeOffset stopAt) {
		List<string> idList = ids.ToList();
		JObject selected = new() { ["Id"] = ClassicEntitySchemaQuery.Column("Id") };
		foreach (string column in columns) {
			selected[column] = ClassicEntitySchemaQuery.Column(column);
		}
		JObject query = ClassicEntitySchemaQuery.Query(entity, selected,
			ClassicEntitySchemaQuery.Group(("ids", ClassicEntitySchemaQuery.InFilter("Id", idList, GuidDataValueType))),
			idList.Count);
		string json = applicationClient.ExecutePostRequest(serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			query.ToString(Formatting.None), Budget(stopAt, RequestMs), maxAttempts: 1);
		return DataServiceSelectResponse.ReadRows(json);
	}

	private static string LookupValue(JToken value) {
		string text = value is JObject lookup ? lookup["value"]?.ToString() : value?.ToString();
		return Guid.TryParse(text, out Guid id) && id != Guid.Empty ? id.ToString("D") : null;
	}

	// The runs that hold one of our records through ProcessElementId: the record's live element data row names the
	// run. Only such a run is cancelled, and only one the caller started; a run merely started in the window is not.
	private bool CancelHoldingRuns(HashSet<string> ours, List<CreatedRecord> runs, string me, DateTimeOffset stopAt,
		TriggerSmokeResult result) {
		List<SmokeProcessRun> running = result.Processes.Where(run => run.Status == RunningStatus
			&& me is not null && string.Equals(runs.FirstOrDefault(record => record.Id == run.ProcessId)?.CreatedById, me,
				StringComparison.OrdinalIgnoreCase)).ToList();
		if (running.Count == 0) {
			return false;
		}
		HashSet<string> holding = ReadHoldingRuns([.. result.Created, result.Trigger], ours, stopAt, result);
		bool cancelledAny = false;
		foreach (string processId in running.Select(run => run.ProcessId).Where(holding.Contains)) {
			if (Budget(stopAt, RequestMs) < MinimumRequestMs) {
				result.Warnings.Add($"Run {processId} was not cancelled: the call's time budget ran out.");
				continue;
			}
			if (CancelAndWait(Guid.Parse(processId), stopAt, result)) {
				cancelledAny = true;
				result.Cancelled.Add(processId);
				result.Processes = result.Processes
					.Select(run => run.ProcessId == processId ? run with { Status = CancelledStatus, WaitingOn = [] } : run)
					.ToList();
			}
		}
		if (running.Any(run => !holding.Contains(run.ProcessId))) {
			result.Warnings.Add("Runs still running that hold none of the records cleanup deletes were left running: "
				+ "nothing ties them to the trigger.");
		}
		return cancelledAny;
	}

	private HashSet<string> ReadHoldingRuns(IEnumerable<CreatedRecord> records, HashSet<string> ours, DateTimeOffset stopAt,
		TriggerSmokeResult result) {
		try {
			HashSet<string> elements = ReadProcessElements(records, ours, stopAt);
			return elements.Count == 0
				? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				: SelectByIds("SysProcessElementData", ["SysProcess"], elements, stopAt)
					.Select(row => LookupValue(row["SysProcess"]))
					.Where(run => run is not null)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			result.Warnings.Add("cleanup could not tell which run holds the records it deletes, so no run was cancelled: "
				+ (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name));
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}
	}

	// The ProcessElementId values of our records, for the entities that have the column.
	private HashSet<string> ReadProcessElements(IEnumerable<CreatedRecord> records, HashSet<string> ours,
		DateTimeOffset stopAt) {
		HashSet<string> elements = new(StringComparer.OrdinalIgnoreCase);
		foreach (IGrouping<string, CreatedRecord> entity in records.Where(record => record is not null && ours.Contains(record.Id))
				.GroupBy(record => record.Entity, StringComparer.OrdinalIgnoreCase)) {
			bool hasElement = schemaReader.GetByName(entity.Key, Budget(stopAt, RequestMs)).Columns
				.Any(column => string.Equals(column.Name, ProcessElementColumn, StringComparison.Ordinal));
			if (hasElement) {
				elements.UnionWith(SelectByIds(entity.Key, [ProcessElementColumn], entity.Select(record => record.Id), stopAt)
					.Select(row => LookupValue(row[ProcessElementColumn]))
					.Where(element => element is not null));
			}
		}
		return elements;
	}

	private bool CancelAndWait(Guid processId, DateTimeOffset stopAt, TriggerSmokeResult result) {
		try {
			string error = verification.Monitor.Cancel(processId, Budget(stopAt, RequestMs));
			if (!string.IsNullOrEmpty(error)) {
				result.Warnings.Add($"Run {processId} was not cancelled: {error}.");
				return false;
			}
			DateTimeOffset until = Clock().AddMilliseconds(CancelWaitMs);
			if (until > stopAt) {
				until = stopAt;
			}
			while (Clock() < until) {
				if (verification.Monitor.Read(processId, Budget(until, RequestMs)).IsTerminal) {
					return true;
				}
				Sleep((int)Math.Max(1, Math.Min(PollIntervalMs, (until - Clock()).TotalMilliseconds)));
			}
			result.Warnings.Add($"Run {processId} had not stopped after the cancel in the time left; records it uses may "
				+ "refuse deletion.");
			return false;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			result.Warnings.Add($"Run {processId} could not be cancelled: "
				+ (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name) + ".");
			return false;
		}
	}
}
