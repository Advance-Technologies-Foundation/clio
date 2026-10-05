using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>Options of <c>apply-default-record-rights</c>: apply an object's default record rules to its existing records.</summary>
[Verb("apply-default-record-rights", HelpText =
	"Apply an object's default record rules to its EXISTING records (runs the platform's 'Update record permissions' "
	+ "process; heavy on large tables) (destructive)")]
public class ApplyDefaultRecordRightsOptions : RemoteCommandOptions {

	/// <summary>The object (entity schema) whose existing records get the current rules.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object (entity schema) name whose existing records get the current default record rules.")]
	public string EntitySchemaName { get; set; }

	/// <summary>Wait for the run to end (default true).</summary>
	[Option("wait", Required = false, HelpText =
		"true (default): wait until the run completes, fails or --timeout-seconds pass; false: start it and return the "
		+ "process id.")]
	public bool? Wait { get; set; }

	/// <summary>How long to wait for the run to end.</summary>
	[Option("timeout-seconds", Required = false, Default = DefaultTimeoutSeconds, HelpText =
		"How long to wait for the run to end, in seconds (default 300). When it passes, the run keeps going and the "
		+ "result says 'still running' with its process id.")]
	public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

	/// <summary>Apply without a prompt.</summary>
	[Option("confirm", Required = false, HelpText =
		"Confirm the run without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }

	internal const int DefaultTimeoutSeconds = 300;

	/// <summary>
	/// The time the whole call may take, for a caller whose answer is bounded by a deadline (MCP); not a CLI option. The
	/// wait ends with it too. <see langword="null"/>: no limit beyond each request's timeout and --timeout-seconds.
	/// </summary>
	internal TimeSpan? CallBudget { get; set; }
}

/// <summary>
/// <c>apply-default-record-rights</c>: starts <c>ObjectRecordRightsActualizationProcess</c> once for one object — the
/// platform's "Update record permissions": the rights that came from default rules are deleted and the current rules
/// are applied to every existing record; rights granted by hand stay. Destructive, NOT idempotent (each call starts a
/// run) and confirm-gated. The agent runs it only when the user asks for it.
/// </summary>
public class ApplyDefaultRecordRightsCommand : Command<ApplyDefaultRecordRightsOptions> {

	private const string CommandName = "apply-default-record-rights";

	// Short enough to notice a run that ends in seconds (a small table), long enough not to load the stand.
	internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

	private readonly IObjectRightsReader _reader;
	private readonly IRecordRightsActualization _actualization;
	private readonly IObjectRecordCounter _recordCounter;
	private readonly IRetryDelay _delay;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public ApplyDefaultRecordRightsCommand(IObjectRightsReader reader, IRecordRightsActualization actualization,
		IObjectRecordCounter recordCounter, IRetryDelay delay, IInteractiveConsole console, ILogger logger) {
		_reader = reader;
		_actualization = actualization;
		_recordCounter = recordCounter;
		_delay = delay;
		_console = console;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(ApplyDefaultRecordRightsOptions options) {
		if (!ObjectRightsCommandInput.TryReadSchemaName(options.EntitySchemaName, _logger, out string schemaName)) {
			return 1;
		}
		if (options.TimeoutSeconds <= 0) {
			_logger.WriteError("Error: --timeout-seconds must be a positive number of seconds.");
			return 1;
		}
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options, options.CallBudget);
		ObjectRightsInfo info = _reader.GetObjectRights(schemaName, requestOptions);
		if (!info.IsRead) {
			_logger.WriteError($"Error: '{schemaName}': {info.FailureReason}. Nothing was started.");
			return 1;
		}
		if (!info.AdministratedByRecords) {
			_logger.WriteError($"Error: record permissions on '{schemaName}' are OFF, so its record rights are not "
				+ "evaluated and there is nothing to apply. Turn them on first with set-default-record-rights "
				+ "--enable-record-permissions. Nothing was started.");
			return 1;
		}
		if (info.SchemaUId == Guid.Empty) {
			_logger.WriteError($"Error: '{schemaName}': the service did not return the object's schema UId. Nothing was "
				+ "started.");
			return 1;
		}
		WarnIfAnUpdateIsRunning(schemaName, requestOptions);
		// The count reads the whole table, so it is taken only for the prompt a person reads: with --confirm (and on MCP)
		// the decision was taken before the call, and a refused run needs no count.
		string count = !options.Confirm && _console.IsInteractive
			? $" ({ObjectRightsCommandInput.DescribeRecordCount(_recordCounter, schemaName, requestOptions, out _)})"
			: "";
		string summary = $"Apply the default record rules of '{schemaName}' to its existing records{count}. Rights that "
			+ "came from default rules are replaced by the current rules; rights granted by hand stay. The run is heavy on "
			+ "large tables.";
		switch (ObjectRightsCommandInput.Confirm(options.Confirm, _console, _logger, CommandName, "record rights",
					summary, new[] { $"Current rules: {DefaultRecordRightsFormat.Rules(info.RecordState.Rules)}." },
					"Record-rights update cancelled.", hasPreview: false)) {
			case ConfirmDecision.Cancelled:
				return 0;
			case ConfirmDecision.Refused:
				return 1;
		}
		// The launch is sent once and is not idempotent, so it goes out only while the call's deadline still covers one
		// full request: a launch cut short by the deadline would leave it unknown whether the run started.
		if (!ObjectRightsCommandInput.HasTimeFor(requestOptions, 1, schemaName, "launch", "the launch needs",
				"nothing was started", _logger)) {
			return 1;
		}
		RunProcessResponse started;
		try {
			started = _actualization.Start(info.SchemaUId, requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			return ReportLaunchFailure(schemaName, ex);
		}
		if (started.Error is not null) {
			if (started.Status == RecordRightsActualizationClient.OutcomeUnknownStatus) {
				return ReportLaunchOutcomeUnknown(schemaName, started.Error);
			}
			_logger.WriteError($"Error: '{schemaName}': {ObjectRightsSupport.DisplayError(started.Error)}");
			return 1;
		}
		if (!Guid.TryParse(started.ProcessId, out Guid processId) || processId == Guid.Empty) {
			// Queued in background: the platform gives no id to follow, and the launch itself is the outcome.
			_logger.WriteInfo($"'{schemaName}': the record-rights update was queued; the platform returned no process id "
				+ "to follow. Check the records' rights with get-record-rights later.");
			return 0;
		}
		_logger.WriteInfo($"'{schemaName}': the record-rights update started, process {processId}.");
		return options.Wait ?? true
			? WaitForEnd(schemaName, processId, options.TimeoutSeconds, requestOptions)
			: ReportStillRunning(schemaName, processId, waited: false);
	}

	// A second run started while one is going doubles the load and, for the same object, does the same work twice. The
	// log cannot say which object a run is for, so this only warns — an update of another object is no reason to stop —
	// and a failed check never stops the call.
	private void WarnIfAnUpdateIsRunning(string schemaName, CreatioRequestOptions requestOptions) {
		IReadOnlyList<RunningUpdate> running;
		try {
			running = _actualization.FindRunning(requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
			_logger.WriteWarning($"'{schemaName}': could not check whether a record-rights update is already running "
				+ $"({ObjectRightsSupport.DisplayFailure(ex)}).");
			return;
		}
		if (running.Count == 0) {
			return;
		}
		RunningUpdate newest = running[0];
		// DataService returns the start date in the calling user's time zone, with no zone in the text.
		_logger.WriteWarning($"'{schemaName}': a record-rights update is already running on this environment (process "
			+ $"{newest.ProcessId}, started {ObjectRightsSupport.Display(newest.StartDate)} in the calling user's time "
			+ "zone" + (running.Count > 1 ? $", and {running.Count - 1} more" : "") + "). The log does not say which object it is "
			+ "for: if it is this one, wait for it to end (check SysProcessLog, Id = that process) instead of starting "
			+ "another — two runs at once double the load.");
	}

	// A launch that got no answer may still have started the run, and re-sending it would start a second heavy run.
	private int ReportLaunchFailure(string schemaName, Exception ex) {
		if (ObjectRightsSupport.LeavesOutcomeUnknown(ex)) {
			return ReportLaunchOutcomeUnknown(schemaName, $"no answer: {ObjectRightsSupport.DisplayFailure(ex)}");
		}
		_logger.WriteError($"Error: '{schemaName}': the record-rights update was not started "
			+ $"({ObjectRightsSupport.DisplayFailure(ex)}).");
		return 1;
	}

	// The launch may have started the run though no usable answer came back — no answer, or a body a proxy replaced (an
	// empty or non-JSON 502/504 page).
	private int ReportLaunchOutcomeUnknown(string schemaName, string reason) {
		_logger.WriteError($"Error: '{schemaName}': the launch of the record-rights update got no usable answer "
			+ $"({ObjectRightsSupport.DisplayError(reason)}), so the run MAY already be going. Do NOT start it again: "
			+ $"check the newest {RecordRightsActualizationClient.ProcessName} row in SysProcessLog, or the records' "
			+ "rights with get-record-rights.");
		return 1;
	}

	// Polls the run's SysProcessLog row until it ends or the wait is over. A failed read is retried at the next poll: one
	// fault does not end the wait. Running out of time is not a failure — the run goes on, and the result names the
	// process to check — but "still running" is said only when a status was actually read.
	private int WaitForEnd(string schemaName, Guid processId, int timeoutSeconds, CreatioRequestOptions requestOptions) {
		TimeSpan waitFor = TimeSpan.FromSeconds(timeoutSeconds);
		if (requestOptions.Deadline is { } call && call.Remaining < waitFor) {
			waitFor = call.Remaining;
		}
		RequestDeadline wait = new(waitFor);
		CreatioRequestOptions pollOptions = requestOptions with { Deadline = wait };
		bool statusSeen = false;
		int reads = 0;
		string lastReadError = null;
		while (!wait.IsSpent) {
			reads++;
			try {
				ProcessRunStatus status = _actualization.ReadStatus(processId, pollOptions);
				if (status is not null) {
					statusSeen = true;
					if (status.IsFinal) {
						return ReportEnd(schemaName, processId, status);
					}
				}
			}
			catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
				lastReadError = ObjectRightsSupport.DisplayFailure(ex);
			}
			if (wait.IsSpent) {
				break;
			}
			_delay.Wait(wait.Remaining < PollInterval ? wait.Remaining : PollInterval);
		}
		return statusSeen
			? ReportStillRunning(schemaName, processId, waited: true)
			: ReportStatusUnknown(schemaName, processId, reads == 0
				? "the wait ended before the status could be read"
				: lastReadError ?? "no SysProcessLog row was found");
	}

	// The run was started, but no status of it was ever read (no SysProcessLog row, or every read failed): clio cannot
	// say whether it is going, finished or failed — so it does not say "still running".
	private int ReportStatusUnknown(string schemaName, Guid processId, string reason) {
		_logger.WriteWarning($"'{schemaName}': the record-rights update was started (process {processId}), but its status "
			+ $"could not be read ({reason}). Do NOT start it again: check SysProcessLog (Id = {processId}) for its "
			+ "outcome.");
		return 0;
	}

	private int ReportEnd(string schemaName, Guid processId, ProcessRunStatus status) {
		if (status.StatusId == ProcessRunStatus.Completed) {
			_logger.WriteInfo($"'{schemaName}': the record-rights update completed (process {processId}). Existing "
				+ "records now have the rights of the current default rules; rights granted by hand were kept.");
			return 0;
		}
		_logger.WriteError($"Error: '{schemaName}': the record-rights update ended as "
			+ $"'{ObjectRightsSupport.Display(status.StatusName)}' (process {processId}). Its log is in the process log "
			+ "(SysProcessLog) of the environment.");
		return 1;
	}

	private int ReportStillRunning(string schemaName, Guid processId, bool waited) {
		_logger.WriteInfo($"'{schemaName}': the record-rights update is {(waited ? "still " : "")}running (process "
			+ $"{processId}). Do NOT start it again: check its status in SysProcessLog (Id = {processId}).");
		return 0;
	}
}
