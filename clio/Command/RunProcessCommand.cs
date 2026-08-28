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
// JsonSerializer and JsonException exist in both stacks; the System.Text.Json ones are aliased because
// the response is logged through Newtonsoft like every sibling command.
using StjSerializer = System.Text.Json.JsonSerializer;
using StjJsonException = System.Text.Json.JsonException;

/// <summary>
/// Options for launching a Creatio business process at runtime.
/// Consumed by the MCP <c>run-process</c> tool, which sets these properties directly.
/// </summary>
// Deliberately NOT [RequiresPackage], and its MCP tool is deliberately NOT [FeatureToggle]-gated:
// ProcessEngineService.svc/RunProcess is built into every Creatio and never calls ProcessDesignService, so
// neither gate has anything to guard, and gating would break consumers on stands without the
// process-designer toggle or the server package. (GetProcessSignatureOptions is ungated too, but for a
// reason that does NOT transfer here — it has a public CLI verb its MCP surface must match; this one has
// no [Verb] at all, like create-/modify-/describe-business-process and validate-process-graph.)
public sealed class RunProcessOptions : EnvironmentOptions {

	/// <summary>
	/// Process code (schema Name). A display caption is NOT accepted: captions are not unique, and this
	/// tool launches a process rather than reading one, so resolving by an ambiguous key could start the
	/// wrong process.
	/// </summary>
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
	/// The run outcome, and the only field that carries it. Either the platform's process status lowercased
	/// (<c>inactive</c>, <c>running</c>, <c>completed</c> — the enum name is <c>Done</c> — <c>error</c>,
	/// <c>cancelled</c>, <c>cancelling</c>, or <c>unknown-status-{n}</c> carrying the raw code for a status
	/// this clio does not know), or one of three states the platform's scale cannot express:
	/// <c>refused</c> (it declined to start the process and nothing ran), <c>queued-background</c> (the
	/// schema starts in background mode, so it returned no handle and no result) and
	/// <c>accepted-still-running</c> (clio answered at the MCP response deadline before Creatio replied).
	/// <see langword="null"/> when the call was rejected before launch — <see cref="Error"/> says why.
	/// </summary>
	[JsonProperty("status")]
	[System.Text.Json.Serialization.JsonPropertyName("status")]
	public string Status { get; set; }

	/// <summary>
	/// The launched process instance id, or <c>null</c> when the platform returned none. It is also the
	/// primary key of the run's <c>SysProcessLog</c> row.
	/// </summary>
	[JsonProperty("processId")]
	[System.Text.Json.Serialization.JsonPropertyName("processId")]
	public string ProcessId { get; set; }

	/// <summary>Values of the requested <c>result-parameters</c>, keyed by code.</summary>
	[JsonProperty("resultParameterValues")]
	[System.Text.Json.Serialization.JsonPropertyName("resultParameterValues")]
	public Dictionary<string, object> ResultParameterValues { get; set; }

	/// <summary>Advisory notes that did not block the launch.</summary>
	[JsonProperty("warnings")]
	[System.Text.Json.Serialization.JsonPropertyName("warnings")]
	public List<string> Warnings { get; set; } = [];

	/// <summary>
	/// Why the call failed, or <see langword="null"/> when it did not — the failure signal of this
	/// response. Set for a rejected call, a refused launch and a run that ended with the Error status, and
	/// the only populated field when the call was rejected before launch.
	/// </summary>
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
	/// <c>Terrasoft.Core.Process.ProcessStatus</c> codes rendered as status names. Code 2 is <c>Done</c> in
	/// the enum and <c>Completed</c> in the lookup row a polled <c>SysProcessLog</c> points at; it is
	/// surfaced as <c>completed</c>, so a caller comparing the two matches on the lookup name, not the code.
	/// </summary>
	private static readonly Dictionary<int, string> StatusNames = new() {
		[0] = "inactive",
		[1] = "running",
		[2] = "completed",
		[3] = "error",
		[4] = "cancelled",
		[5] = "cancelling"
	};

	private const string QueuedBackgroundStatus = "queued-background";
	private const string RefusedStatus = "refused";

	private const int InactiveStatus = 0;
	private const int ErrorStatus = 3;

	/// <summary>
	/// The platform error code raised when a process declares only automatic start events (a signal or a
	/// timer) and therefore has no manual entry point at all.
	/// </summary>
	private const string ManualStartRefusedCode = "ProcessCannotBeManuallyStartedException";

	/// <summary>The note returned when the platform queued the process in background mode.</summary>
	internal static string BuildQueuedBackgroundNote(string processCode) =>
		$"'{processCode}' starts in background mode, so the platform queued it and returned no process id, "
		+ "no status and no result parameters. This is not an error — for a fire-and-forget process the "
		+ "launch IS the outcome. clio cannot report whether the run succeeded; judge it by the process's "
		+ "own effects. Requesting result-parameters forces the same process to run synchronously instead, "
		+ "which is the only way to get a verdict for it.";

	/// <summary>The message returned when the platform refused to start the process.</summary>
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
	/// <returns>
	/// <c>true</c> only for an accepted launch with no failure verdict; <c>false</c> for a rejected call, a
	/// refused launch, and a run that ended with the Error status.
	/// </returns>
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
			return false;
		}

		response = Project(platformResponse, model.Code);
		// The return value feeds Execute's exit code, so it must track the OUTCOME, not merely the fact that
		// a request was sent: a refusal ("nothing was started") and a failed run would otherwise both exit 0.
		return response.Error is null;
	}

	/// <summary>
	/// Maps the platform response onto the tool contract.
	/// </summary>
	/// <remarks>
	/// A startup refusal, a background queueing and an inactive descriptor all arrive with the SAME empty
	/// process id and <c>Inactive</c> status; <c>success</c> and <c>errorInfo</c> are the only
	/// discriminators, so reading the id alone would report a refusal as a successful background launch. Why
	/// the platform behaves this way:
	/// <c>docs/knowledge/platform/runprocess-success-flag-is-not-the-run-verdict.md</c>.
	/// </remarks>
	internal static RunProcessResponse Project(ProcessStartResponse platformResponse, string processCode) {
		if (platformResponse is null) {
			return new RunProcessResponse { Error = "RunProcess returned an empty response" };
		}

		(string errorCode, string errorMessage) = ReadErrorInfo(platformResponse.ErrorInfo);
		bool noHandle = platformResponse.ProcessId == Guid.Empty
			&& platformResponse.ProcessStatus == InactiveStatus;

		if (noHandle && !platformResponse.Success) {
			return new RunProcessResponse {
				Status = RefusedStatus,
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
		// The platform sets success=false for ProcessStatus.Error only while the
		// Feature-SetErrorInfoIfProcessHasFailedExecution flag is on, so a failed run can arrive with
		// success=true. Both signals are read so a failure reported by EITHER surfaces as an error.
		if (!platformResponse.Success || platformResponse.ProcessStatus == ErrorStatus) {
			response.Error = DescribeFailure(errorCode, errorMessage);
		}
		return response;
	}

	/// <summary>
	/// Extracts the platform's <c>errorInfo</c> pair. It is declared <see cref="object"/> on the shared DTO,
	/// so System.Text.Json fills it with a <see cref="JsonElement"/>, and a reflection-based serializer would
	/// render that struct's public surface as <c>{"ValueKind":1}</c> and drop the message — hence reading
	/// the members rather than re-serializing.
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

	private static string ResolveStatusName(int status) =>
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

	private static RunProcessResponse Failure(string error) => new() { Error = error };

	/// <summary>
	/// The message returned when <c>process-name</c> resolved to a different code than it spelled, which
	/// means it was a caption (or the wrong casing) rather than the schema code.
	/// </summary>
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
