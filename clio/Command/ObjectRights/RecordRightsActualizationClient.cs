using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.StartProcess;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.ObjectRights;

/// <summary>
/// Starts <c>ObjectRecordRightsActualizationProcess</c> — the platform's "Update record permissions" — for one object,
/// and reads the state of that run.
/// </summary>
public interface IRecordRightsActualization {
	/// <summary>
	/// Starts the process once for the object <paramref name="schemaUId"/>. It is never re-sent: a retry would start a
	/// second run.
	/// </summary>
	/// <param name="schemaUId">The UId of the object's (base) entity schema.</param>
	/// <param name="requestOptions">Timeout and deadline settings; the attempts are always one.</param>
	/// <returns>The launch as the platform answered it: the process id and status, or why it was not started.</returns>
	RunProcessResponse Start(Guid schemaUId, CreatioRequestOptions requestOptions);

	/// <summary>Reads the status of the run <paramref name="processId"/> from its <c>SysProcessLog</c> row.</summary>
	/// <param name="processId">The process id the launch returned.</param>
	/// <param name="requestOptions">Timeout, retry and deadline settings.</param>
	/// <returns>The status, or <see langword="null"/> when the run has no log row.</returns>
	ProcessRunStatus ReadStatus(Guid processId, CreatioRequestOptions requestOptions);

	/// <summary>
	/// Finds runs of the update that are still going on the environment, for any object: <c>SysProcessLog</c> does not
	/// record which object a run is for in a column that can be filtered on.
	/// </summary>
	/// <param name="requestOptions">Timeout, retry and deadline settings.</param>
	/// <returns>The running runs, newest first (at most a few).</returns>
	IReadOnlyList<RunningUpdate> FindRunning(CreatioRequestOptions requestOptions);
}

/// <summary>A run of the record-rights update that is still going.</summary>
/// <param name="ProcessId">The run's process id (its <c>SysProcessLog</c> Id).</param>
/// <param name="StartDate">When it started, as the service returned it.</param>
public sealed record RunningUpdate(Guid ProcessId, string StartDate);

/// <summary>The status of a process run, as its <c>SysProcessLog</c> row holds it.</summary>
/// <param name="StatusId">The <c>SysProcessStatus</c> id.</param>
/// <param name="StatusName">The status's display name, as the service returned it.</param>
public sealed record ProcessRunStatus(Guid StatusId, string StatusName) {

	/// <summary>The run is still going.</summary>
	public static readonly Guid Running = Guid.Parse("ed2ae277-b6e2-df11-971b-001d60e938c6");

	/// <summary>The run finished.</summary>
	public static readonly Guid Completed = Guid.Parse("815c9586-b6e2-df11-971b-001d60e938c6");

	/// <summary>The run failed.</summary>
	public static readonly Guid Error = Guid.Parse("f942c08d-b6e2-df11-971b-001d60e938c6");

	/// <summary>The run was cancelled.</summary>
	public static readonly Guid Canceled = Guid.Parse("1be78f3e-234d-4d6a-869a-dc07253fd2f3");

	/// <summary>The run ended: completed, failed or cancelled.</summary>
	public bool IsFinal => StatusId == Completed || StatusId == Error || StatusId == Canceled;
}

/// <summary>
/// Starts <c>ObjectRecordRightsActualizationProcess</c> through <c>ProcessEngineService.svc/RunProcess</c> and reads its
/// <c>SysProcessLog</c> status. It lives with the commands because the launch answer is interpreted by
/// <see cref="RunProcessCommand.BuildResponse"/>, the same rules <c>run-process</c> applies.
/// </summary>
public sealed class RecordRightsActualizationClient : IRecordRightsActualization {

	/// <summary>
	/// The <see cref="RunProcessResponse.Status"/> of a launch whose answer could not be read (an empty or non-JSON body,
	/// as a proxy 502/504 returns): the run may have started.
	/// </summary>
	internal const string OutcomeUnknownStatus = "outcome-unknown";

	/// <summary>The platform process that applies an object's default record rules to its existing records.</summary>
	internal const string ProcessName = "ObjectRecordRightsActualizationProcess";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;

	/// <summary>Creates the client.</summary>
	public RecordRightsActualizationClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder) {
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
	}

	/// <inheritdoc />
	public RunProcessResponse Start(Guid schemaUId, CreatioRequestOptions requestOptions) {
		ProcessStartArgs args = new() {
			SchemaName = ProcessName,
			Values = new[] {
				new ProcessStartArgs.ParameterValues { Name = "EntitySchemaUId", Value = schemaUId.ToString("D") }
			}
		};
		CreatioRequestOptions sendOptions = requestOptions.ForNextRequest();
		// One attempt: a re-sent launch would start a second run of a heavy process.
		string body = _applicationClient.ExecutePostRequest(_urlBuilder.Build(ServiceUrlBuilder.KnownRoute.RunProcess),
			JsonSerializer.Serialize(args), sendOptions.TimeOut, maxAttempts: 1);
		if (string.IsNullOrWhiteSpace(body)) {
			return new RunProcessResponse { Status = OutcomeUnknownStatus, Error = "RunProcess returned an empty response" };
		}
		ProcessStartResponse started;
		try {
			started = JsonSerializer.Deserialize<ProcessStartResponse>(body);
		}
		catch (JsonException e) {
			return new RunProcessResponse {
				Status = OutcomeUnknownStatus, Error = $"RunProcess returned a response clio could not read: {e.Message}"
			};
		}
		return RunProcessCommand.BuildResponse(started, ProcessName);
	}

	/// <inheritdoc />
	public ProcessRunStatus ReadStatus(Guid processId, CreatioRequestOptions requestOptions) {
		object query = SelectQueryHelper.BuildSelectQuery("SysProcessLog",
			new[] { new SelectQueryHelper.SelectQueryColumnDefinition("Status", "Status") },
			new[] { new SelectQueryHelper.SelectQueryFilterDefinition("Id", processId, SelectQueryHelper.GuidDataValueType) },
			1);
		CreatioRequestOptions sendOptions = requestOptions.ForNextRequest();
		StatusSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<StatusSelectResponse>(_applicationClient,
			_urlBuilder, query, sendOptions.TimeOut, sendOptions.MaxAttempts, sendOptions.RetryDelay);
		LookupValue status = response.Rows?.FirstOrDefault()?.Status;
		return status is null ? null : new ProcessRunStatus(status.Value, status.DisplayValue);
	}

	/// <inheritdoc />
	public IReadOnlyList<RunningUpdate> FindRunning(CreatioRequestOptions requestOptions) {
		object query = SelectQueryHelper.BuildSelectQuery("SysProcessLog",
			new[] {
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
				new SelectQueryHelper.SelectQueryColumnDefinition("StartDate", "StartDate", OrderDirection: 2, OrderPosition: 0)
			},
			new[] {
				new SelectQueryHelper.SelectQueryFilterDefinition("SysSchema.Name", ProcessName,
					SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("Status", ProcessRunStatus.Running,
					SelectQueryHelper.GuidDataValueType)
			},
			MaxRunningShown);
		CreatioRequestOptions sendOptions = requestOptions.ForNextRequest();
		RunningSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<RunningSelectResponse>(_applicationClient,
			_urlBuilder, query, sendOptions.TimeOut, sendOptions.MaxAttempts, sendOptions.RetryDelay);
		return response.Rows?.Select(row => new RunningUpdate(row.Id, row.StartDate)).ToArray()
			?? Array.Empty<RunningUpdate>();
	}

	// Enough to say "one is going" and name it; the warning does not list every run.
	private const int MaxRunningShown = 3;

	private sealed class RunningSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<RunningRow> Rows { get; set; }
	}

	private sealed class RunningRow {
		[JsonPropertyName("Id")]
		public Guid Id { get; set; }

		[JsonPropertyName("StartDate")]
		public string StartDate { get; set; }
	}

	private sealed class StatusSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<StatusRow> Rows { get; set; }
	}

	private sealed class StatusRow {
		[JsonPropertyName("Status")]
		public LookupValue Status { get; set; }
	}

	private sealed class LookupValue {
		[JsonPropertyName("value")]
		public Guid Value { get; set; }

		[JsonPropertyName("displayValue")]
		public string DisplayValue { get; set; }
	}
}
