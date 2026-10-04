using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Common.ObjectRights;

namespace Clio.Command.ObjectRights;

/// <summary>How the destructive gate of an object-rights write ended.</summary>
internal enum ConfirmDecision {
	Approved,
	Cancelled,
	Refused
}

/// <summary>
/// The inputs and steps the object-rights commands share — <c>get-object-rights</c>, <c>set-object-rights</c>,
/// <c>set-default-record-rights</c>, <c>apply-default-record-rights</c> — so their validation and their messages cannot
/// drift apart.
/// </summary>
internal static class ObjectRightsCommandInput {

	/// <summary>
	/// Reads <c>--entity-schema-name</c>: required, trimmed, and a plain schema identifier. Only such a name is read or
	/// written: SQL Server ignores trailing spaces in the <c>SysSchema.Name</c> comparison, so a padded name would reach
	/// the table under a spelling the approval does not show exactly.
	/// </summary>
	/// <param name="raw">The name as the caller passed it.</param>
	/// <param name="logger">Where the refusal is written.</param>
	/// <param name="schemaName">The trimmed name, when valid.</param>
	/// <returns><see langword="true"/> when the name is a valid schema identifier.</returns>
	internal static bool TryReadSchemaName(string raw, ILogger logger, out string schemaName) {
		schemaName = null;
		if (string.IsNullOrWhiteSpace(raw)) {
			logger.WriteError("Error: --entity-schema-name is required.");
			return false;
		}
		if (!ObjectRightsSupport.TryNormalizeSchemaName(raw, out schemaName)) {
			logger.WriteError($"Error: --entity-schema-name '{ObjectRightsSupport.Display(raw)}' is not a schema name "
				+ "(letters, digits and '_' only).");
			return false;
		}
		return true;
	}

	/// <summary>
	/// The request options built from a remote command's timeout and retry arguments and, for a caller bounded by a
	/// deadline (MCP), the time the whole call may take, which starts now.
	/// </summary>
	/// <param name="options">The command's options.</param>
	/// <param name="callBudget">The time every request of the call may take together; <see langword="null"/>: no limit
	/// beyond each request's timeout.</param>
	/// <returns>The request options for the service calls.</returns>
	internal static CreatioRequestOptions RequestOptions(RemoteCommandOptions options, TimeSpan? callBudget = null) => new() {
		TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay,
		Deadline = callBudget is { } budget ? new RequestDeadline(budget) : null
	};

	/// <summary>
	/// Resolves a role or user id to its name. The id must exist: changing rights for an unknown id would write a row
	/// for a principal nobody holds and still report the change as done.
	/// </summary>
	/// <param name="lookup">The SysAdminUnit lookup.</param>
	/// <param name="id">The SysAdminUnit id.</param>
	/// <param name="role">What the id is in the call, for the messages: <c>grantee</c> or <c>author</c>.</param>
	/// <param name="requestOptions">Timeout, retry and deadline settings.</param>
	/// <param name="logger">Where the refusal is written.</param>
	/// <param name="name">The name, when found.</param>
	/// <returns><see langword="true"/> when the id names an existing role or user.</returns>
	internal static bool TryResolveUnitName(IGranteeLookup lookup, Guid id, string role,
		CreatioRequestOptions requestOptions, ILogger logger, out string name) {
		try {
			name = lookup.ResolveGranteeName(id, requestOptions);
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			name = null;
			logger.WriteError($"Error: could not check {role} {id}: {ObjectRightsSupport.DisplayFailure(ex)}");
			return false;
		}
		if (name is null) {
			logger.WriteError($"Error: {role} {id} was not found in SysAdminUnit. Nothing was changed — pass the id of an "
				+ "existing role or user.");
			return false;
		}
		return true;
	}

	/// <summary>
	/// Whether the call's deadline leaves time for the save AND the read-back after it: a save that started later could
	/// still be in flight — or unverified — when the caller's deadline ends the call, and its outcome would then be lost.
	/// Refused before anything is sent, so nothing has changed.
	/// </summary>
	/// <param name="requestOptions">The call's request options.</param>
	/// <param name="schemaName">The object, for the message.</param>
	/// <param name="logger">Where the refusal is written.</param>
	/// <returns><see langword="true"/> when the save may be sent.</returns>
	internal static bool HasTimeToSave(CreatioRequestOptions requestOptions, string schemaName, ILogger logger) {
		if (requestOptions.Deadline is not { } deadline) {
			return true;
		}
		TimeSpan needed = TimeSpan.FromMilliseconds(2.0 * requestOptions.TimeOut);
		if (deadline.Remaining >= needed) {
			return true;
		}
		logger.WriteError($"Error: '{schemaName}': the reads before the save took most of the call's time limit "
			+ $"of {deadline.Budget.TotalSeconds:0} s: {deadline.Remaining.TotalSeconds:0} s are left, and the save and "
			+ $"the read-back need up to {needed.TotalSeconds:0} s. The save was not sent — nothing was changed. Re-run "
			+ "the call.");
		return false;
	}

	/// <summary>
	/// The destructive gate, mirroring other destructive clio commands: <c>--confirm</c> applies without a prompt;
	/// without it an interactive run asks y/n, and a non-interactive run refuses (rather than silently applying). On MCP
	/// the host approval is the gate and the tool passes <c>--confirm</c>.
	/// </summary>
	/// <param name="confirm">The caller passed <c>--confirm</c>.</param>
	/// <param name="console">The console to prompt on.</param>
	/// <param name="logger">Where the prompt and the refusal are written.</param>
	/// <param name="commandName">The command, for the refusal.</param>
	/// <param name="what">What changes, for the prompt: <c>object permissions</c>.</param>
	/// <param name="summary">The one-line summary of the change.</param>
	/// <param name="facts">The facts the operator approves.</param>
	/// <param name="cancelled">What is written when the user answers no.</param>
	/// <param name="hasPreview">The command has a <c>--preview</c> dry run, which the refusal then suggests.</param>
	/// <returns>Whether to apply, the user cancelled, or the run was refused.</returns>
	internal static ConfirmDecision Confirm(bool confirm, IInteractiveConsole console, ILogger logger, string commandName,
		string what, string summary, IEnumerable<string> facts, string cancelled, bool hasPreview = true) {
		if (confirm) {
			return ConfirmDecision.Approved;
		}
		if (!console.IsInteractive) {
			logger.WriteError($"Error: {commandName} is destructive and needs confirmation. Re-run with --confirm to "
				+ $"apply the change{(hasPreview ? " (or --preview to see it first)" : "")}: {summary}");
			return ConfirmDecision.Refused;
		}
		logger.WriteWarning($"About to change {what}: {summary}");
		foreach (string fact in facts) {
			logger.WriteWarning($"  {fact}");
		}
		if (!console.Prompt("Apply this change?")) {
			logger.WriteInfo(cancelled);
			return ConfirmDecision.Cancelled;
		}
		return ConfirmDecision.Approved;
	}

	/// <summary>
	/// The number of existing records of <paramref name="schemaName"/> as a fact for the output — what a user needs to
	/// decide whether, and when, to apply the default record rules to them. A failed count is reported, never thrown:
	/// it never fails the call.
	/// </summary>
	/// <param name="counter">The record counter.</param>
	/// <param name="schemaName">The object.</param>
	/// <param name="requestOptions">The call's request options.</param>
	/// <param name="count">The number of records, when counted.</param>
	/// <returns><c>N existing record(s), counted under the calling account</c>, or why they were not counted.</returns>
	internal static string DescribeRecordCount(IObjectRecordCounter counter, string schemaName,
		CreatioRequestOptions requestOptions, out long? count) {
		try {
			count = counter.CountRecords(schemaName, requestOptions);
			return $"{count} existing record(s), counted under the calling account";
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex) || ex is TimeoutException) {
			count = null;
			return $"existing records not counted ({ObjectRightsSupport.DisplayFailure(ex)})";
		}
	}
}
