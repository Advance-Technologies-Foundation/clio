namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Clio.Command.ProcessModel;
using Clio.Command.StartProcess;
using Clio.Common;
using ErrorOr;
using Newtonsoft.Json;
using StjSerializer = System.Text.Json.JsonSerializer;
using StjJsonException = System.Text.Json.JsonException;

// Deliberately NOT [RequiresPackage]: the endpoint is built into every Creatio, so a gate would only
// break consumers on stands without the package.
public sealed class RunProcessOptions : EnvironmentOptions {

	// A display caption is rejected: captions are not unique.
	public string ProcessName { get; set; } = string.Empty;

	public IReadOnlyDictionary<string, JsonElement> Parameters { get; set; }

	public IReadOnlyList<string> ResultParameters { get; set; }

	public int TimeoutSeconds { get; set; }
}

public sealed class RunProcessResponse {

	// A platform status lowercased, or not-started / queued-background / still-running. Null when the call
	// was rejected before launch.
	[JsonProperty("status")]
	[System.Text.Json.Serialization.JsonPropertyName("status")]
	public string Status { get; set; }

	// Also the primary key of the run's SysProcessLog row.
	[JsonProperty("processId")]
	[System.Text.Json.Serialization.JsonPropertyName("processId")]
	public string ProcessId { get; set; }

	[JsonProperty("resultParameterValues")]
	[System.Text.Json.Serialization.JsonPropertyName("resultParameterValues")]
	public Dictionary<string, object> ResultParameterValues { get; set; }

	[JsonProperty("warnings")]
	[System.Text.Json.Serialization.JsonPropertyName("warnings")]
	public List<string> Warnings { get; set; } = [];

	// The failure signal of this response.
	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class RunProcessCommand(
	IProcessModelGenerator generator,
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: Command<RunProcessOptions> {

	// Code 2 is `Done` in Terrasoft.Core.Process.ProcessStatus but `Completed` in the SysProcessStatus
	// lookup a polled SysProcessLog points at, so a caller comparing the two matches on the lookup name.
	private static readonly Dictionary<int, string> StatusNames = new() {
		[0] = "inactive",
		[1] = "running",
		[2] = "completed",
		[3] = "error",
		[4] = "cancelled",
		[5] = "cancelling"
	};

	private const string QueuedBackgroundStatus = "queued-background";
	private const string NotStartedStatus = "not-started";

	private const int InactiveStatus = 0;
	private const int ErrorStatus = 3;

	private const string ManualStartRefusedCode = "ProcessCannotBeManuallyStartedException";

	internal static string BuildQueuedBackgroundNote(string processCode) =>
		$"'{processCode}' starts in background mode, so the platform queued it and returned no process id, "
		+ "no status and no result parameters. This is not an error — for a fire-and-forget process the "
		+ "launch IS the outcome. clio cannot report whether the run succeeded; judge it by the process's "
		+ "own effects. Requesting result-parameters forces the same process to run synchronously instead, "
		+ "which is the only way to get a verdict for it.";

	internal static string BuildRefusalMessage(string processCode, string errorCode, string message) {
		string detail = string.IsNullOrWhiteSpace(message) ? "the platform returned no details" : message;
		if (string.Equals(errorCode, ManualStartRefusedCode, StringComparison.Ordinal)) {
			return $"'{processCode}' cannot be launched: {detail}. Nothing was started. A process whose only "
				+ "start events are automatic runs when its own trigger fires (a record signal, a timer, a "
				+ "schedule) and has no manual entry point, so no run-process call can ever start it — cause "
				+ "the trigger instead, or add a manual start event to the process.";
		}
		return $"'{processCode}' was not started: {detail}."
			+ (string.IsNullOrWhiteSpace(errorCode) ? string.Empty : $" [{errorCode}]");
	}

	// True only for an accepted launch with no failure verdict.
	public virtual bool TryRun(RunProcessOptions options, out RunProcessResponse response) {
		if (string.IsNullOrWhiteSpace(options.ProcessName)) {
			response = Failure("process-name is required");
			return false;
		}

		ErrorOr<ProcessModel.ProcessModel> resolved = generator.Generate(new GenerateProcessModelCommandOptions {
			Code = options.ProcessName,
			Culture = "en-US"
		});
		if (resolved.IsError) {
			response = Failure(string.Join("; ", resolved.Errors.Select(e => $"{e.Code} - {e.Description}")));
			return false;
		}

		ProcessModel.ProcessModel model = resolved.Value;
		if (!string.Equals(model.Code, options.ProcessName, StringComparison.Ordinal)) {
			response = Failure(BuildCaptionRejectedMessage(options.ProcessName, model.Code));
			return false;
		}

		List<ProcessParameter> signature = model.Parameters ?? [];

		if (!TryBuildParameterValues(options.Parameters, signature, out ProcessStartArgs.ParameterValues[] values,
				out string parameterError)) {
			response = Failure(parameterError);
			return false;
		}

		if (!TryValidateResultParameters(options.ResultParameters, signature, out string resultError)) {
			response = Failure(resultError);
			return false;
		}

		return TryLaunch(options, model, values, out response);
	}

	private bool TryLaunch(RunProcessOptions options, ProcessModel.ProcessModel model,
		ProcessStartArgs.ParameterValues[] values, out RunProcessResponse response) {
		ProcessStartArgs args = new() {
			SchemaName = model.Code,
			Values = values,
			Result = options.ResultParameters?.ToArray()
		};

		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RunProcess);
		// maxAttempts stays 1: idempotency belongs to the specific process, not to this transport, so a
		// retry can duplicate work.
		string rawResponse = applicationClient.ExecutePostRequest(url, StjSerializer.Serialize(args),
			ResolveRequestTimeout(options.TimeoutSeconds), maxAttempts: 1);

		if (string.IsNullOrWhiteSpace(rawResponse)) {
			response = Failure("RunProcess returned an empty response");
			return false;
		}

		ProcessStartResponse platformResponse;
		try {
			platformResponse = StjSerializer.Deserialize<ProcessStartResponse>(rawResponse);
		}
		catch (StjJsonException e) {
			response = Failure($"RunProcess returned a response clio could not read: {e.Message}");
			return false;
		}

		response = BuildResponse(platformResponse, model.Code);
		// Feeds Execute's exit code, so it tracks the outcome rather than "a request was sent" — a refusal
		// and a failed run would otherwise both exit 0.
		return response.Error is null;
	}

	// A refusal, a background queueing and an inactive descriptor arrive with the SAME empty id and
	// Inactive status; success and errorInfo are the only discriminators. See
	// docs/knowledge/platform/runprocess-success-flag-is-not-the-run-verdict.md
	internal static RunProcessResponse BuildResponse(ProcessStartResponse platformResponse, string processCode) {
		if (platformResponse is null) {
			return new RunProcessResponse { Error = "RunProcess returned an empty response" };
		}

		(string errorCode, string errorMessage) = ReadErrorInfo(platformResponse.ErrorInfo);
		bool noHandle = platformResponse.ProcessId == Guid.Empty
			&& platformResponse.ProcessStatus == InactiveStatus;

		if (noHandle && !platformResponse.Success) {
			return new RunProcessResponse {
				Status = NotStartedStatus,
				Error = BuildRefusalMessage(processCode, errorCode, errorMessage)
			};
		}

		if (noHandle) {
			return new RunProcessResponse {
				Status = QueuedBackgroundStatus,
				Warnings = [BuildQueuedBackgroundNote(processCode)]
			};
		}

		RunProcessResponse response = new() {
			Status = ResolveStatusName(platformResponse.ProcessStatus),
			ProcessId = platformResponse.ProcessId.ToString(),
			ResultParameterValues = platformResponse.ResultParameterValues
		};
		// A failed run can arrive with success=true: the platform only clears that flag while
		// Feature-SetErrorInfoIfProcessHasFailedExecution is on. Both signals are read.
		if (!platformResponse.Success || platformResponse.ProcessStatus == ErrorStatus) {
			response.Error = DescribeFailure(errorCode, errorMessage);
		}
		return response;
	}

	// Read member by member: the field is object and holds a JsonElement, which re-serializes to
	// {"ValueKind":1} and drops the message.
	internal static (string ErrorCode, string Message) ReadErrorInfo(object errorInfo) {
		if (errorInfo is not JsonElement element || element.ValueKind != JsonValueKind.Object) {
			return (null, null);
		}
		return (ReadStringMember(element, "errorCode"), ReadStringMember(element, "message"));
	}

	private static string ReadStringMember(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement member) && member.ValueKind == JsonValueKind.String
			? member.GetString()
			: null;

	private static string DescribeFailure(string errorCode, string message) {
		string detail = string.IsNullOrWhiteSpace(message)
			? "the platform returned no error details"
			: message;
		return $"The process run failed: {detail}."
			+ (string.IsNullOrWhiteSpace(errorCode) ? string.Empty : $" [{errorCode}]");
	}

	private static string ResolveStatusName(int status) =>
		StatusNames.TryGetValue(status, out string name) ? name : $"unknown-status-{status}";

	// Clamped because int.MaxValue / 1000 is only ~24 days of seconds: past that the multiplication wraps
	// NEGATIVE, turning an absurdly large bound into a near-instant one.
	private static int ResolveRequestTimeout(int timeoutSeconds) =>
		timeoutSeconds <= 0
			? System.Threading.Timeout.Infinite
			: (int)Math.Min((long)timeoutSeconds * 1000L, int.MaxValue);

	private static bool TryBuildParameterValues(IReadOnlyDictionary<string, JsonElement> supplied,
		List<ProcessParameter> signature, out ProcessStartArgs.ParameterValues[] values, out string error) {
		values = [];
		error = null;
		if (supplied is null || supplied.Count == 0) {
			return true;
		}

		List<ProcessStartArgs.ParameterValues> built = [];
		foreach ((string code, JsonElement value) in supplied) {
			ProcessParameter parameter = FindParameter(signature, code);
			if (parameter is null) {
				error = BuildUnknownCodeError(code, signature, ProcessParameterDirection.Output, "parameters");
				return false;
			}
			if (parameter.Direction == ProcessParameterDirection.Output) {
				error = $"'{code}' is an Output parameter and cannot be assigned through 'parameters'. "
					+ "Read it back by listing it in 'result-parameters' instead.";
				return false;
			}
			// The platform expresses "unset" by the value being ABSENT. An empty string would instead assign
			// a real value: Guid.Empty for a lookup, "" for text.
			if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) {
				continue;
			}
			if (!TryCoerce(parameter, value, out string serialized, out error)) {
				return false;
			}
			built.Add(new ProcessStartArgs.ParameterValues { Name = parameter.Name, Value = serialized });
		}

		values = [.. built];
		return true;
	}

	private static bool TryValidateResultParameters(IReadOnlyList<string> requested,
		List<ProcessParameter> signature, out string error) {
		error = null;
		if (requested is null || requested.Count == 0) {
			return true;
		}

		foreach (string code in requested) {
			ProcessParameter parameter = FindParameter(signature, code);
			if (parameter is null) {
				// The platform verifies result names before the process starts and throws
				// ItemNotFoundException, so catching it first turns an opaque abort into a list of codes.
				error = BuildUnknownCodeError(code, signature, ProcessParameterDirection.Input,
					"result-parameters");
				return false;
			}
			if (parameter.Direction == ProcessParameterDirection.Input) {
				error = $"'{code}' is an Input parameter and carries no run result, so it cannot be read "
					+ "through 'result-parameters'. Pass it in 'parameters' instead.";
				return false;
			}
		}

		return true;
	}

	// The platform matches names with StringComparison.Ordinal, so a case-only difference is a miss.
	private static ProcessParameter FindParameter(List<ProcessParameter> signature, string code) =>
		signature.FirstOrDefault(p => string.Equals(p.Name, code, StringComparison.Ordinal));

	private static string BuildUnknownCodeError(string code, List<ProcessParameter> signature,
		ProcessParameterDirection excluded, string argumentName) {
		ProcessParameter caseInsensitiveMatch = signature
			.FirstOrDefault(p => string.Equals(p.Name, code, StringComparison.OrdinalIgnoreCase));
		if (caseInsensitiveMatch is not null) {
			return $"'{code}' does not match any parameter of the process. Parameter codes are "
				+ $"case-sensitive — did you mean '{caseInsensitiveMatch.Name}'?";
		}

		string[] accepted = [.. signature
			.Where(p => p.Direction != excluded)
			.Select(p => p.Name)
			.OrderBy(name => name, StringComparer.Ordinal)];
		string acceptedList = accepted.Length == 0
			? "the process declares none"
			: string.Join(", ", accepted);
		return $"'{code}' is not a parameter of the process. Codes accepted by '{argumentName}': "
			+ $"{acceptedList}. Use the parameter CODE from get-process-signature, not its caption — the "
			+ "platform silently drops a value keyed by a caption.";
	}

	// A string parameter is passed through VERBATIM: re-encoding a serialized ESQ filter produces an empty
	// selection instead of an error.
	internal static bool TryCoerce(ProcessParameter parameter, JsonElement value, out string serialized,
		out string error) {
		serialized = null;
		error = null;
		Type clrType = parameter.DataValueTypeResolved;
		bool isLookup = parameter.ReferenceSchemaUId.HasValue
			&& parameter.ReferenceSchemaUId.Value != Guid.Empty;

		if (clrType == typeof(string) && !isLookup) {
			serialized = value.ValueKind == JsonValueKind.String
				? value.GetString()
				: value.GetRawText();
			return true;
		}

		if (clrType == typeof(Guid) || isLookup) {
			return TryCoerceGuid(parameter, value, out serialized, out error);
		}

		if (clrType == typeof(bool)) {
			return TryCoerceBoolean(parameter, value, out serialized, out error);
		}

		if (clrType == typeof(DateTime)) {
			return TryCoerceDateTime(parameter, value, out serialized, out error);
		}

		serialized = value.ValueKind == JsonValueKind.String
			? value.GetString()
			: value.GetRawText();
		return true;
	}

	private static bool TryCoerceGuid(ProcessParameter parameter, JsonElement value, out string serialized,
		out string error) {
		serialized = null;
		error = null;
		string raw = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
		if (Guid.TryParse(raw, out Guid parsed)) {
			serialized = parsed.ToString("D", CultureInfo.InvariantCulture);
			return true;
		}
		error = $"'{parameter.Name}' expects a record id (Guid) but received '{raw}'. A lookup parameter "
			+ "takes the record's Id, never its display name — resolve the id first (for example with "
			+ "odata-read on the referenced object).";
		return false;
	}

	private static bool TryCoerceBoolean(ProcessParameter parameter, JsonElement value, out string serialized,
		out string error) {
		serialized = null;
		error = null;
		if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) {
			serialized = value.GetBoolean() ? "true" : "false";
			return true;
		}
		if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed)) {
			serialized = parsed ? "true" : "false";
			return true;
		}
		error = $"'{parameter.Name}' expects a boolean but received {value.ValueKind}.";
		return false;
	}

	private static bool TryCoerceDateTime(ProcessParameter parameter, JsonElement value, out string serialized,
		out string error) {
		serialized = null;
		error = null;
		string raw = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
		if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
				out DateTime parsed)) {
			serialized = parsed.ToString("o", CultureInfo.InvariantCulture);
			return true;
		}
		error = $"'{parameter.Name}' expects a date/time but received '{raw}'. Pass it in an invariant "
			+ "format such as 2026-08-26T13:45:00Z.";
		return false;
	}

	private static RunProcessResponse Failure(string error) => new() { Error = error };

	internal static string BuildCaptionRejectedMessage(string supplied, string resolvedCode) =>
		$"'{supplied}' is not a process CODE. It resolved to '{resolvedCode}', so it was a display caption "
		+ "or the wrong casing. Pass the code: a caption is not unique and is not what the platform "
		+ "launches by, and this tool starts a process rather than reading one, so an ambiguous key could "
		+ $"start the wrong one. Use '{resolvedCode}', or get-process-signature to confirm the code.";

	/// <inheritdoc />
	public override int Execute(RunProcessOptions options) {
		bool launched = TryRun(options, out RunProcessResponse response);
		logger.WriteInfo(JsonConvert.SerializeObject(response));
		return launched ? 0 : 1;
	}
}
