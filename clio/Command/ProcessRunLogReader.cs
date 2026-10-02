using System;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json.Linq;

namespace Clio.Command;

/// <summary>
/// Reads what the platform logged for one process run.
/// </summary>
public interface IProcessRunLogReader {

	/// <summary>
	/// The first line of the error the platform logged for the run <paramref name="processId"/> - the failed
	/// element's exception as <c>Type: message</c> - read from <c>SysProcessLog.ErrorDescription</c>.
	/// </summary>
	/// <param name="processId">The run's process id, as RunProcess returned it.</param>
	/// <returns>The first non-empty line, or <see langword="null"/> when the run logged no error.</returns>
	/// <remarks>
	/// RunProcess answers a run whose script task threw with only "check the process log": the platform swallows
	/// the element's exception before it builds the answer (<c>ProcessActivity.HandleExecutionError</c>) and leaves
	/// <c>errorCode</c> empty, but it writes <c>exception.ToString()</c> to the run's log row, synchronously,
	/// before RunProcess returns. The rest of that text is the stack trace.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The SelectQuery failed.</exception>
	string ReadErrorSummary(Guid processId);
}

internal sealed class ProcessRunLogReader(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	: IProcessRunLogReader {

	// DataValueType 0 is Guid, as in ClassicEntitySchemaQuery's UId filters.
	private const int GuidDataValueType = 0;

	/// <inheritdoc />
	public string ReadErrorSummary(Guid processId) {
		JObject query = ClassicEntitySchemaQuery.Query("SysProcessLog",
			new JObject { ["ErrorDescription"] = ClassicEntitySchemaQuery.Column("ErrorDescription") },
			ClassicEntitySchemaQuery.Group(
				("byId", ClassicEntitySchemaQuery.Eq("Id", processId.ToString(), GuidDataValueType))),
			1);
		JArray rows = ClassicEntitySchemaQuery.Select(applicationClient, serviceUrlBuilder, query);
		string text = rows.FirstOrDefault()?["ErrorDescription"]?.ToString();
		if (string.IsNullOrWhiteSpace(text)) {
			return null;
		}
		return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Trim())
			.FirstOrDefault(line => line.Length > 0);
	}
}
