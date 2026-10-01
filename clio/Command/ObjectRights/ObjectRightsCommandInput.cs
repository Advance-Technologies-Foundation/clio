using System;
using Clio.Common;
using Clio.Common.ObjectRights;

namespace Clio.Command.ObjectRights;

/// <summary>
/// The inputs <c>get-object-rights</c> and <c>set-object-rights</c> read the same way, so their validation and their
/// messages cannot drift apart.
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
}
