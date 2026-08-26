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
// Both JSON stacks are needed here — the shared ProcessStartArgs/ProcessStartResponse DTOs are
// System.Text.Json-shaped, while the response is logged through Newtonsoft like every sibling command —
// and their JsonSerializer/JsonException type names collide, so the STJ ones are aliased.
using StjSerializer = System.Text.Json.JsonSerializer;
using StjJsonException = System.Text.Json.JsonException;

/// <summary>
/// Options for launching a Creatio business process at runtime.
/// Consumed by the MCP <c>run-process</c> tool, which sets these properties directly.
/// </summary>
// Deliberately NOT [RequiresPackage] and its MCP tool is deliberately NOT [FeatureToggle]-gated, for the
// same reason recorded on GetProcessSignatureOptions: ProcessEngineService.svc/RunProcess is a built-in
// endpoint present on every Creatio and never touches ProcessDesignService. Gating it would also break the
// consumer, which must work on stands without the process-designer toggle or the server package.
// There is no [Verb]: this is an MCP-only capability, matching the ENG-90883 decision for the family.
public sealed class RunProcessOptions : EnvironmentOptions {

	/// <summary>Process code (schema Name) or display caption.</summary>
	public string ProcessName { get; set; } = string.Empty;

	/// <summary>Input parameter values keyed by parameter CODE (never caption).</summary>
	public IReadOnlyDictionary<string, JsonElement> Parameters { get; set; }

	/// <summary>Codes of the parameters whose values are read back after execution.</summary>
	public IReadOnlyList<string> ResultParameters { get; set; }

	/// <summary>HTTP request timeout in seconds. Non-positive means no timeout.</summary>
	public int TimeoutSeconds { get; set; }
}

/// <summary>
/// Structured response for the <c>run-process</c> tool.
/// </summary>
public sealed class RunProcessResponse {

	/// <summary>
	/// Whether clio executed the call: <c>false</c> means validation, resolution or transport failed and
	/// nothing was launched. It is NOT the process verdict — read <see cref="Mode"/> for that.
	/// </summary>
	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>
	/// The run outcome, and the ONLY field that carries it. One of the platform's own process statuses
	/// (<c>inactive</c>, <c>running</c>, <c>completed</c>, <c>error</c>, <c>cancelled</c>, <c>cancelling</c>)
	/// or one of the two no-verdict outcomes: <c>queued-background</c> (the schema starts in background mode,
	/// so the platform returns no handle and no result) and <c>accepted-still-running</c> (clio answered at
	/// the MCP response deadline before Creatio replied).
	/// </summary>
	[JsonProperty("mode")]
	[System.Text.Json.Serialization.JsonPropertyName("mode")]
	public string Mode { get; set; }

	/// <summary>The resolved schema Name, echoed back even when the caller passed a caption.</summary>
	[JsonProperty("resolvedProcessCode")]
	[System.Text.Json.Serialization.JsonPropertyName("resolvedProcessCode")]
	public string ResolvedProcessCode { get; set; }

	/// <summary>
	/// The launched process instance id, or <c>null</c> when the platform returned none. It is also the
	/// primary key of the run's <c>SysProcessLog</c> row.
	/// </summary>
	[JsonProperty("processId")]
	[System.Text.Json.Serialization.JsonPropertyName("processId")]
	public string ProcessId { get; set; }

	/// <summary>Raw platform status code, or <c>null</c> when no response was observed.</summary>
	[JsonProperty("processStatus")]
	[System.Text.Json.Serialization.JsonPropertyName("processStatus")]
	public int? ProcessStatus { get; set; }

	/// <summary>Values of the requested <c>result-parameters</c>, keyed by code.</summary>
	[JsonProperty("resultParameterValues")]
	[System.Text.Json.Serialization.JsonPropertyName("resultParameterValues")]
	public Dictionary<string, object> ResultParameterValues { get; set; }

	/// <summary>Advisory notes that did not block the launch.</summary>
	[JsonProperty("warnings")]
	[System.Text.Json.Serialization.JsonPropertyName("warnings")]
	public List<string> Warnings { get; set; } = [];

	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Launches a Creatio business process through the built-in
/// <c>ServiceModel/ProcessEngineService.svc/RunProcess</c> endpoint, validating the supplied parameter
/// codes against the process signature before any server call.
/// </summary>
public class RunProcessCommand(
	IProcessModelGenerator generator,
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: Command<RunProcessOptions> {

	/// <summary>
	/// Platform status codes as declared by <c>Terrasoft.Core.Process.ProcessStatus</c>. The same scale is
	/// stored in <c>SysProcessStatus.Value</c>, so a polled log row and this response agree.
	/// </summary>
	private static readonly Dictionary<int, string> StatusNames = new() {
		[0] = "inactive",
		[1] = "running",
		[2] = "completed",
		[3] = "error",
		[4] = "cancelled",
		[5] = "cancelling"
	};

	private const string QueuedBackgroundMode = "queued-background";
	private const string RefusedMode = "refused";

	/// <summary><c>ProcessStatus.Inactive</c>, the status carried by an empty descriptor.</summary>
	private const int InactiveStatus = 0;

	/// <summary><c>ProcessStatus.Error</c>.</summary>
	private const int ErrorStatus = 3;

	/// <summary>
	/// The platform error code raised when a process declares only automatic start events (a signal or a
	/// timer) and therefore has no manual entry point at all.
	/// </summary>
	private const string ManualStartRefusedCode = "ProcessCannotBeManuallyStartedException";

	/// <summary>
	/// The note returned when the platform launched the process in background mode. Extracted so its
	/// wording is directly unit-testable.
	/// </summary>
	internal static string BuildQueuedBackgroundNote(string processCode) =>
		$"'{processCode}' starts in background mode, so the platform queued it and returned no process id, "
		+ "no status and no result parameters. This is not an error — for a fire-and-forget process the "
		+ "launch IS the outcome. clio cannot report whether the run succeeded; judge it by the process's "
		+ "own effects. Requesting result-parameters forces the same process to run synchronously instead, "
		+ "which is the only way to get a verdict for it.";

	/// <summary>
	/// The message returned when the platform refused to start the process. Extracted so its wording is
	/// directly unit-testable.
	/// </summary>
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

	/// <summary>
	/// Launches the process and projects the platform response into <paramref name="response"/>.
	/// </summary>
	/// <returns><c>true</c> when the process was launched; otherwise <c>false</c>.</returns>
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
		List<ProcessParameter> signature = model.Parameters ?? [];

		if (!TryBuildParameterValues(options.Parameters, signature, out ProcessStartArgs.ParameterValues[] values,
				out string parameterError)) {
			response = Failure(parameterError);
			response.ResolvedProcessCode = model.Code;
			return false;
		}

		if (!TryValidateResultParameters(options.ResultParameters, signature, out string resultError)) {
			response = Failure(resultError);
			response.ResolvedProcessCode = model.Code;
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
		// maxAttempts stays 1: a retry can duplicate work, and idempotency is a property of the specific
		// process rather than of this transport. The caller decides whether re-running is safe.
		string rawResponse = applicationClient.ExecutePostRequest(url, StjSerializer.Serialize(args),
			ResolveRequestTimeout(options.TimeoutSeconds), maxAttempts: 1);

		ProcessStartResponse platformResponse;
		try {
			platformResponse = StjSerializer.Deserialize<ProcessStartResponse>(rawResponse);
		}
		catch (StjJsonException e) {
			response = Failure($"RunProcess returned a response clio could not read: {e.Message}");
			response.ResolvedProcessCode = model.Code;
			return false;
		}

		response = Project(platformResponse, model.Code);
		// The return value feeds Execute's exit code, so it must track the OUTCOME, not merely the fact that
		// a request was sent: a refusal ("nothing was started") and a failed run would otherwise both exit 0.
		return response.Success;
	}

	/// <summary>
	/// Maps the platform response onto the tool contract.
	/// </summary>
	/// <remarks>
	/// Three different outcomes share the SAME empty-process-id + <c>Inactive</c>-status shape, and
	/// <c>success</c> / <c>errorInfo</c> are the only things that tell them apart:
	/// a STARTUP REFUSAL (<c>ProcessExecutor.CheckCanExecute</c> verifies a manual start event before
	/// anything runs, so a process whose only start events are automatic answers <c>success:false</c> with
	/// <c>ProcessCannotBeManuallyStartedException</c> and nothing started), a BACKGROUND QUEUEING (the
	/// fire-and-forget branch sends the start command to the message bus and returns
	/// <c>new ProcessDescriptor()</c> without waiting, so <c>success:true</c> with no handle), and a
	/// genuinely inactive descriptor. Reading only the id and the status would report a refusal as a
	/// successful background launch.
	/// </remarks>
	internal static RunProcessResponse Project(ProcessStartResponse platformResponse, string processCode) {
		if (platformResponse is null) {
			return new RunProcessResponse {
				Success = false,
				ResolvedProcessCode = processCode,
				Error = "RunProcess returned an empty response"
			};
		}

		(string errorCode, string errorMessage) = ReadErrorInfo(platformResponse.ErrorInfo);
		bool noHandle = platformResponse.ProcessId == Guid.Empty
			&& platformResponse.ProcessStatus == InactiveStatus;

		if (noHandle && !platformResponse.Success) {
			return new RunProcessResponse {
				Success = false,
				Mode = RefusedMode,
				ResolvedProcessCode = processCode,
				Error = BuildRefusalMessage(processCode, errorCode, errorMessage)
			};
		}

		if (noHandle) {
			return new RunProcessResponse {
				Success = true,
				Mode = QueuedBackgroundMode,
				ResolvedProcessCode = processCode,
				Warnings = [BuildQueuedBackgroundNote(processCode)]
			};
		}

		RunProcessResponse response = new() {
			// The platform sets success=false for ProcessStatus.Error only while the
			// Feature-SetErrorInfoIfProcessHasFailedExecution flag is on, so a failed run can arrive with
			// success=true. Both signals are combined so a failure reported by EITHER is a failure here,
			// keeping success == false the honest failure signal every clio MCP tool uses.
			Success = platformResponse.Success && platformResponse.ProcessStatus != ErrorStatus,
			ResolvedProcessCode = processCode,
			Mode = ResolveModeName(platformResponse.ProcessStatus),
			ProcessId = platformResponse.ProcessId.ToString(),
			ProcessStatus = platformResponse.ProcessStatus,
			ResultParameterValues = platformResponse.ResultParameterValues
		};
		if (!response.Success) {
			response.Error = DescribeFailure(errorCode, errorMessage);
		}
		return response;
	}

	/// <summary>
	/// Extracts the platform's <c>errorInfo</c> pair. The field is declared as <see cref="object"/> on the
	/// shared DTO and arrives as a <see cref="JsonElement"/>, so it is read member by member rather than
	/// re-serialized: a reflection-based serializer renders a <see cref="JsonElement"/> as
	/// <c>{"ValueKind":...}</c> and loses the message entirely.
	/// </summary>
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

	private static string ResolveModeName(int status) =>
		StatusNames.TryGetValue(status, out string name) ? name : $"unknown-status-{status}";

	// Timeout.Infinite is the IApplicationClient default and what a long synchronous process needs; a
	// caller-supplied bound is converted to milliseconds. The conversion is clamped because
	// int.MaxValue / 1000 is only about 24 days of seconds, past which the multiplication would wrap to a
	// NEGATIVE timeout — i.e. an absurdly large bound would silently become a near-instant one.
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
			ProcessParameter parameter = FindByCode(signature, code);
			if (parameter is null) {
				error = BuildUnknownCodeError(code, signature, ProcessParameterDirection.Output, "parameters");
				return false;
			}
			if (parameter.Direction == ProcessParameterDirection.Output) {
				error = $"'{code}' is an Output parameter and cannot be assigned through 'parameters'. "
					+ "Read it back by listing it in 'result-parameters' instead.";
				return false;
			}
			// An explicit JSON null means "leave this parameter unset", and the platform expresses unset by
			// the value being ABSENT from parameterValues. Sending an empty string instead would assign a
			// real value — Guid.Empty for a lookup, "" for text — which is a different thing entirely.
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
			ProcessParameter parameter = FindByCode(signature, code);
			if (parameter is null) {
				// The platform verifies every requested result name against the schema BEFORE the process
				// starts and throws ItemNotFoundException, so an unknown code here aborts the launch server
				// side. Catching it first turns that opaque failure into a list of valid codes.
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

	// The platform matches parameter names with StringComparison.Ordinal, so a case-only difference is a
	// miss. It is reported as such rather than silently corrected.
	private static ProcessParameter FindByCode(List<ProcessParameter> signature, string code) =>
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

	/// <summary>
	/// Serializes a supplied value for <c>ProcessStartArgs.ParameterValues.Value</c>, which is a string the
	/// platform parses with invariant formatting. A <see cref="string"/> parameter is passed through
	/// VERBATIM: a value such as a serialized ESQ filter is consumed as-is by the process, and re-encoding
	/// it produces an empty selection instead of an error.
	/// </summary>
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

	private static RunProcessResponse Failure(string error) => new() { Success = false, Error = error };

	/// <inheritdoc />
	public override int Execute(RunProcessOptions options) {
		bool launched = TryRun(options, out RunProcessResponse response);
		logger.WriteInfo(JsonConvert.SerializeObject(response));
		return launched ? 0 : 1;
	}
}
