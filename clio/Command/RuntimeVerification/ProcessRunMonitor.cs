using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.RuntimeVerification;

/// <summary>
/// Reads and cancels one process run by its process id (which is also the primary key of its SysProcessLog row).
/// </summary>
public interface IProcessRunMonitor {

	/// <summary>
	/// Reads the run's status, the first line of its error (or of a failed sub-process's error), and the elements it
	/// is parked on.
	/// </summary>
	/// <param name="processId">The run's process id, as the launch returned it.</param>
	/// <param name="timeoutMs">Bound of the whole read (all its requests together), in milliseconds.</param>
	/// <returns>The state; <see cref="ProcessRunState.Found"/> is false when the log row does not exist yet.</returns>
	/// <exception cref="InvalidOperationException">A SelectQuery failed.</exception>
	ProcessRunState Read(Guid processId, int timeoutMs);

	/// <summary>
	/// Asks the platform to cancel the run. The cancellation is asynchronous: poll <see cref="Read"/> until the status
	/// is terminal.
	/// </summary>
	/// <param name="processId">The run's process id.</param>
	/// <param name="timeoutMs">Bound of the request, in milliseconds.</param>
	/// <returns>An error message, or <see langword="null"/> when the request was accepted.</returns>
	string Cancel(Guid processId, int timeoutMs);
}

internal sealed class ProcessRunMonitor(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	: IProcessRunMonitor {

	private const string ErrorDescriptionColumn = "ErrorDescription";
	private const int GuidDataValueType = 0;
	private const int IntegerDataValueType = 4;
	private const int RunningStatusValue = 1;
	private const int ErrorStatusValue = 3;
	// A secondary read started with less than this left would only time out.
	private const int MinimumSecondaryReadMs = 500;

	/// <inheritdoc />
	public ProcessRunState Read(Guid processId, int timeoutMs) {
		string id = processId.ToString("D");
		DateTimeOffset readUntil = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(1, timeoutMs));
		int Left() => (int)Math.Max(1, (readUntil - DateTimeOffset.UtcNow).TotalMilliseconds);
		JArray rootRows = Select(ClassicEntitySchemaQuery.Query("SysProcessLog",
			new JObject {
				["StatusValue"] = ClassicEntitySchemaQuery.Column("Status.Value"),
				[ErrorDescriptionColumn] = ClassicEntitySchemaQuery.Column(ErrorDescriptionColumn)
			},
			ClassicEntitySchemaQuery.Group(("byId", ClassicEntitySchemaQuery.Eq("Id", id, GuidDataValueType))),
			1), timeoutMs);
		JToken root = rootRows.FirstOrDefault();
		if (root is null) {
			return new ProcessRunState(false, null, null, []);
		}
		int? status = root["StatusValue"]?.Type == JTokenType.Integer ? root["StatusValue"].Value<int>() : null;
		string error = FirstLine(root[ErrorDescriptionColumn]?.ToString())
			?? (status == ErrorStatusValue ? ReadSubProcessError(id, Left()) : null);
		if (status != RunningStatusValue) {
			return new ProcessRunState(true, status, error, []);
		}
		// The root status is already read and stands on its own: a secondary read that cannot finish only loses the
		// elements, never the state.
		int left = Left();
		if (left < MinimumSecondaryReadMs) {
			return new ProcessRunState(true, status, error, []) {
				Note = "The elements the run waits on were not read: too little of the read's time was left."
			};
		}
		try {
			return new ProcessRunState(true, status, error, ReadWaitingElements(id, left));
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return new ProcessRunState(true, status, error, []) {
				Note = "The elements the run waits on could not be read: "
					+ (UntrustedText.Fenced(exception.Message) ?? exception.GetType().Name) + "."
			};
		}
	}

	/// <inheritdoc />
	public string Cancel(Guid processId, int timeoutMs) {
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CancelProcessExecution);
		// One string of ';'-separated ids: the service splits it itself, and silently cancels nothing for a JSON array.
		string body = new JObject { ["processDataIds"] = processId.ToString("D") }.ToString(Formatting.None);
		string response = applicationClient.ExecuteNonReplayablePostRequest(url, body, timeoutMs, maxAttempts: 1);
		if (string.IsNullOrWhiteSpace(response)) {
			return null;
		}
		// The method returns void, so any body is an error envelope (a WCF fault, a login redirect, an IIS page).
		return "the cancel request was answered with an error: "
			+ (UntrustedText.Fenced(response.Length > 300 ? response[..300] : response) ?? "no detail");
	}

	// A sub-process failure leaves the root's own ErrorDescription empty; RootId ties the sub-process rows to it. A
	// platform without RootId fails this read, which only loses the sub-process detail.
	private string ReadSubProcessError(string rootId, int timeoutMs) {
		try {
			JArray rows = Select(ClassicEntitySchemaQuery.Query("SysProcessLog",
				new JObject { [ErrorDescriptionColumn] = ClassicEntitySchemaQuery.Column(ErrorDescriptionColumn) },
				ClassicEntitySchemaQuery.Group(
					("byRoot", ClassicEntitySchemaQuery.Eq("RootId", rootId, GuidDataValueType)),
					("failed", ClassicEntitySchemaQuery.Eq("Status.Value", ErrorStatusValue.ToString(),
						IntegerDataValueType))),
				5), timeoutMs);
			return rows.Select(row => FirstLine(row[ErrorDescriptionColumn]?.ToString()))
				.FirstOrDefault(line => line is not null);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return null;
		}
	}

	private IReadOnlyList<WaitingElement> ReadWaitingElements(string processId, int timeoutMs) {
		JArray rows = Select(ClassicEntitySchemaQuery.Query("SysProcessElementLog",
			new JObject {
				["Caption"] = ClassicEntitySchemaQuery.Column("Caption"),
				["Type"] = ClassicEntitySchemaQuery.Column("Type")
			},
			ClassicEntitySchemaQuery.Group(
				("byProcess", ClassicEntitySchemaQuery.Eq("SysProcess", processId, GuidDataValueType)),
				("running", ClassicEntitySchemaQuery.Eq("Status.Value", RunningStatusValue.ToString(),
					IntegerDataValueType))),
			20), timeoutMs);
		return rows.Select(row => new WaitingElement(
				row["Caption"]?.ToString(),
				ShortTypeName(row["Type"]?.ToString())))
			.ToList();
	}

	private JArray Select(JObject query, int timeoutMs) {
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
		string json = applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None),
			Math.Max(1, timeoutMs), maxAttempts: 1);
		return DataServiceSelectResponse.ReadRows(json);
	}

	// "Terrasoft.Core.Process.ProcessSchemaUserTask" -> "ProcessSchemaUserTask".
	private static string ShortTypeName(string type) {
		if (string.IsNullOrWhiteSpace(type)) {
			return null;
		}
		int dot = type.LastIndexOf('.');
		return dot >= 0 ? type[(dot + 1)..] : type;
	}

	internal static string FirstLine(string text) =>
		string.IsNullOrWhiteSpace(text)
			? null
			: text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
				.Select(line => line.Trim())
				.FirstOrDefault(line => line.Length > 0);
}
