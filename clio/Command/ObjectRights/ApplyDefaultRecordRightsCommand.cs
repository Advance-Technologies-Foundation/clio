using System;
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
		string summary = $"Apply the default record rules of '{schemaName}' to its existing records "
			+ $"({ObjectRightsCommandInput.DescribeRecordCount(_recordCounter, schemaName, requestOptions, out _)}). Rights that came from default rules are replaced by "
			+ "the current rules; rights granted by hand stay. The run is heavy on large tables.";
		switch (ObjectRightsCommandInput.Confirm(options.Confirm, _console, _logger, CommandName, "record rights",
					summary, new[] { $"Current rules: {DefaultRecordRightsFormat.Rules(info.RecordState.Rules)}." },
					"Record-rights update cancelled.", hasPreview: false)) {
			case ConfirmDecision.Cancelled:
				return 0;
			case ConfirmDecision.Refused:
				return 1;
		}
		if (!HasTimeToLaunch(requestOptions, schemaName)) {
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

	// The launch is sent once and is not idempotent, so it goes out only when the call's deadline still covers one full
	// request: a launch cut short by the deadline would leave it unknown whether the run started. Refused before anything
	// is sent, so nothing has started.
	private bool HasTimeToLaunch(CreatioRequestOptions requestOptions, string schemaName) {
		if (requestOptions.Deadline is not { } deadline) {
			return true;
		}
		TimeSpan needed = TimeSpan.FromMilliseconds(requestOptions.TimeOut);
		if (deadline.Remaining >= needed) {
			return true;
		}
		_logger.WriteError($"Error: '{schemaName}': the reads before the launch took most of the call's time limit of "
			+ $"{deadline.Budget.TotalSeconds:0} s: {deadline.Remaining.TotalSeconds:0} s are left, and the launch needs "
			+ $"up to {needed.TotalSeconds:0} s. The launch was not sent — nothing was started. Re-run the call.");
		return false;
	}

	// A launch that got no answer may still have started the run, and re-sending it would start a second heavy run.
	private int ReportLaunchFailure(string schemaName, Exception ex) {
		if (ObjectRightsSupport.LeavesOutcomeUnknown(ex)) {
			_logger.WriteError($"Error: '{schemaName}': the launch of the record-rights update got no answer "
				+ $"({ObjectRightsSupport.DisplayFailure(ex)}), so the run MAY already be going. Do NOT start it again: "
				+ $"check the newest {RecordRightsActualizationClient.ProcessName} row in SysProcessLog, or the records' "
				+ "rights with get-record-rights.");
			return 1;
		}
		_logger.WriteError($"Error: '{schemaName}': the record-rights update was not started "
			+ $"({ObjectRightsSupport.DisplayFailure(ex)}).");
		return 1;
	}

	// Polls the run's SysProcessLog row until it ends or the wait is over. Running out of time is not a failure: the
	// run goes on, and the result names the process to check.
	private int WaitForEnd(string schemaName, Guid processId, int timeoutSeconds, CreatioRequestOptions requestOptions) {
		TimeSpan waitFor = TimeSpan.FromSeconds(timeoutSeconds);
		if (requestOptions.Deadline is { } call && call.Remaining < waitFor) {
			waitFor = call.Remaining;
		}
		RequestDeadline wait = new(waitFor);
		CreatioRequestOptions pollOptions = requestOptions with { Deadline = wait };
		while (!wait.IsSpent) {
			ProcessRunStatus status;
			try {
				status = _actualization.ReadStatus(processId, pollOptions);
			}
			catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
				if (wait.IsSpent) {
					break;
				}
				_logger.WriteWarning($"'{schemaName}': the run's status could not be read "
					+ $"({ObjectRightsSupport.DisplayFailure(ex)}); the run itself was started.");
				return ReportStillRunning(schemaName, processId, waited: true);
			}
			if (status is { IsFinal: true }) {
				return ReportEnd(schemaName, processId, status);
			}
			_delay.Wait(wait.Remaining < PollInterval ? wait.Remaining : PollInterval);
		}
		return ReportStillRunning(schemaName, processId, waited: true);
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
