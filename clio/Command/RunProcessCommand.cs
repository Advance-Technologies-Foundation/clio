namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

	// When the MCP response deadline falls, set by the tool: past it the tool answers still-running or not-started, so the read of
	// a failed run's log gets only what is left. Null on a path with no such deadline.
	internal DateTimeOffset? ResponseDeadline { get; set; }

	// Set by the MCP tool: decides, with the tool's deadline answer, whether the launch request is sent at all. Null
	// on a path with no response deadline, which always sends.
	internal RunProcessLaunchGate LaunchGate { get; set; }

	// Set by the command when it sends the launch: the version read running alongside it, so the tool's
	// still-running answer can name the version too. Null until then.
	internal Task<ProcessVersionFacts> VersionFacts { get; set; }
}

/// <summary>
/// Decides, once, between sending a run-process launch request and answering that nothing was launched.
/// </summary>
/// <remarks>
/// <para>
/// The MCP tool answers at its response deadline while the launch carries on detached in the server process. Before
/// ENG-102333 the answer was "launched and still running" whatever the launch had reached, so it was held back to
/// 150 s - long enough that a client giving up at 60 s restarted the MCP server instead. Resolving the model
/// and logging in come first and can take most of a minute on a busy or reloading environment, so "the request was
/// sent" is the one fact the answer can rest on, and this gate makes it exact: whichever side claims the gate first
/// wins, so a request is never sent after the answer said it was not.
/// </para>
/// <para>
/// A sent request still does not prove a launch: a request the environment had only queued can be dropped with its
/// connection when the MCP server restarts. The answer says so. A caller that cancels its call withdraws the gate
/// too, so a launch nobody is waiting for never starts afterwards.
/// </para>
/// </remarks>
internal sealed class RunProcessLaunchGate {

	private const int Open = 0;
	private const int Sent = 1;
	private const int Withdrawn = 2;

	private int _state = Open;
	private long _sentUtcTicks;

	/// <summary>Gets when the launch request was sent, or <see langword="null"/> when it was not.</summary>
	internal DateTime? SentUtc => Volatile.Read(ref _state) == Sent
		? new DateTime(Interlocked.Read(ref _sentUtcTicks), DateTimeKind.Utc)
		: null;

	/// <summary>Claims the gate for sending the launch request.</summary>
	/// <param name="nowUtc">The time the request is sent.</param>
	/// <returns><see langword="true"/> to send it; <see langword="false"/> when the answer already said nothing was launched.</returns>
	internal bool TryMarkSent(DateTime nowUtc) {
		// Written before the exchange, which is a full fence, so a reader that sees Sent sees the time too.
		Interlocked.Exchange(ref _sentUtcTicks, nowUtc.Ticks);
		return Interlocked.CompareExchange(ref _state, Sent, Open) == Open;
	}

	/// <summary>Claims the gate for an answer that says nothing was launched.</summary>
	/// <returns><see langword="true"/> when the request had not been sent, and now never will be.</returns>
	internal bool TryWithdraw() => Interlocked.CompareExchange(ref _state, Withdrawn, Open) == Open;
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

	// The code the call named and the launch request carried - the version a synchronous run executes (a queued one
	// is re-resolved by the platform; see the warnings). Null when the call was rejected before a request was sent.
	[JsonProperty("versionName")]
	[System.Text.Json.Serialization.JsonPropertyName("versionName")]
	public string VersionName { get; set; }

	// Whether the process library reports that version as the family's active one. Null when it could not be
	// established - never read as "active".
	[JsonProperty("isActiveVersion")]
	[System.Text.Json.Serialization.JsonPropertyName("isActiveVersion")]
	public bool? IsActiveVersion { get; set; }

	// The code of the version the platform's own triggers start, when it could be established.
	[JsonProperty("activeVersionName")]
	[System.Text.Json.Serialization.JsonPropertyName("activeVersionName")]
	public string ActiveVersionName { get; set; }

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
	IProcessRunLogReader processRunLogReader,
	IProcessVersionLibReader versionLibReader,
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
	internal const string NotStartedStatus = "not-started";

	private const int InactiveStatus = 0;
	private const int ErrorStatus = 3;

	private const string ManualStartRefusedCode = "ProcessCannotBeManuallyStartedException";

	// ProcessModel.GetScriptTaskMethod looks the element's <name>Execute up in the methods wrapper of the LOADED
	// assembly, so a script task saved since the last compile is missing there and the run throws this when it
	// reaches the element - after the elements before it ran. An exception the script itself throws is swallowed
	// by the element and arrives with NO errorCode, so this code does not come from user C#; another platform
	// lookup can still raise it, hence "most often".
	private const string MissingCompiledMethodCode = "KeyNotFoundException";

	// The platform puts the exception's type name in errorCode (ResponseUtils.CreateErrorInfo, measured as the short
	// name); a full name is accepted too, so the hint does not depend on which of the two a platform version sends.
	private static bool IsMissingCompiledMethod(string errorCode) =>
		string.Equals(errorCode, MissingCompiledMethodCode, StringComparison.Ordinal)
		|| (errorCode?.EndsWith("." + MissingCompiledMethodCode, StringComparison.Ordinal) ?? false);

	// The read of a failed run's log gets what is left of the MCP response deadline, less this margin for building
	// and sending the answer, and is skipped when less than the minimum would be left.
	private const int LogReadMarginMs = 3_000;
	private const int MinimumLogReadMs = 1_000;

	internal static string BuildMissingCompiledMethodHint(string processCode) =>
		$" This is most often a script task saved since '{processCode}' was last compiled, so the compiled code "
		+ "does not have it "
		+ "yet: the run stops when it reaches that element, and the elements before it may already have run. If a "
		+ "process-name compile of it succeeded only minutes ago, the runtime may still be reloading: run again in "
		+ "about two minutes before compiling again. Otherwise ask the user, then run compile-creatio with "
		+ $"process-name set to '{processCode}'.";

	// ENG-101880: run-process launches exactly the version it is given, so a version that is not the active one
	// runs as asked - and a caller who meant "the process" must hear that the environment itself runs another.
	internal static string BuildNotActiveVersionNote(string processCode, string activeVersionName) =>
		$"'{processCode}' ran as asked, but it is NOT the active version of its process: the platform's own "
		+ "triggers, schedules and sub-process calls start "
		+ (string.IsNullOrWhiteSpace(activeVersionName) ? "the active version" : DescribeSchemaName(activeVersionName))
		+ ". Such a run verifies a version without activating it; activating it is a separate step "
		+ "(set-active-business-process-version), taken only when the user asks for it.";

	internal static string BuildQueuedBackgroundNote(string processCode) =>
		$"'{processCode}' starts in background mode, so the platform queued it and returned no process id, "
		+ "no status and no result parameters. This is not an error — for a fire-and-forget process the "
		+ "launch IS the outcome. clio cannot report whether the run succeeded; judge it by the process's "
		+ "own effects. Requesting result-parameters forces the same process to run synchronously instead, "
		+ "which is the only way to get a verdict for it.";

	internal static string BuildRefusalMessage(string processCode, string errorCode, string message) {
		// Fenced: the platform's wording reaches an agent through the MCP result, and it can quote stored text.
		string detail = UntrustedText.Fenced(message) ?? "the platform returned no details";
		if (string.Equals(errorCode, ManualStartRefusedCode, StringComparison.Ordinal)) {
			return $"'{processCode}' cannot be launched: {detail}. Nothing was started. A process whose only "
				+ "start events are automatic runs when its own trigger fires (a record signal, a timer, a "
				+ "schedule) and has no manual entry point, so no run-process call can ever start it — cause "
				+ "the trigger instead, or add a manual start event to the process.";
		}
		return $"'{processCode}' was not started: {detail}."
			+ DescribeErrorCode(errorCode);
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

		// RunSpecifiedProcessVersion, not RunProcess: RunProcess resolves the code to the family's ACTIVE version, so
		// a run "of" a new version silently executed the previous one (ENG-101880). For the active version's own
		// code the two run the same schema. See docs/knowledge/platform/runprocess-runs-the-active-version.md
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RunSpecifiedProcessVersion);
		if (options.LaunchGate is not null && !options.LaunchGate.TryMarkSent(DateTime.UtcNow)) {
			// The MCP answer already told the caller nothing was launched; sending now would make that false.
			response = new RunProcessResponse {
				Status = NotStartedStatus,
				Error = $"'{model.Code}' was not launched: the MCP call was answered or cancelled before the launch request was sent."
			};
			return false;
		}
		// Read alongside the launch, never ahead of it: before the gate it would spend the response deadline that
		// decides whether anything is launched at all. The tool reads the same task for a still-running answer.
		Task<ProcessVersionFacts> versionFacts = StartVersionRead(model.Id);
		options.VersionFacts = versionFacts;
		// maxAttempts stays 1: idempotency belongs to the specific process, not to this transport, so a
		// retry can duplicate work.
		string rawResponse = applicationClient.ExecutePostRequest(url, StjSerializer.Serialize(args),
			ResolveRequestTimeout(options.TimeoutSeconds), maxAttempts: 1);

		if (string.IsNullOrWhiteSpace(rawResponse)) {
			response = Failure("The launch returned an empty response");
			return false;
		}

		ProcessStartResponse platformResponse;
		try {
			platformResponse = StjSerializer.Deserialize<ProcessStartResponse>(rawResponse);
		}
		catch (StjJsonException e) {
			response = Failure($"The launch returned a response clio could not read: {e.Message}");
			return false;
		}

		response = BuildResponse(platformResponse, model.Code);
		AddVersion(response, model.Code, AwaitVersionFacts(versionFacts, options.ResponseDeadline));
		AddLoggedError(platformResponse, response, options.ResponseDeadline);
		// Feeds Execute's exit code, so it tracks the outcome rather than "a request was sent" — a refusal
		// and a failed run would otherwise both exit 0.
		return response.Error is null;
	}

	// The version read is a courtesy on top of the launch: a failure of any kind - the reader's own guard does not
	// cover an authentication or ATF expression exception - leaves the facts unestablished, never the launch undone.
	private Task<ProcessVersionFacts> StartVersionRead(Guid schemaUId) =>
		Task.Run(() => {
			try {
				return versionLibReader.Read(schemaUId.ToString("D"));
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				return new ProcessVersionFacts { Warning = "the version read failed" };
			}
		});

	// Waits for the version read within what is left of the response deadline, as the log read does; null when the
	// read has not answered by then.
	private static ProcessVersionFacts AwaitVersionFacts(Task<ProcessVersionFacts> read, DateTimeOffset? deadline) {
		int? waitMs = ResolveLogReadTimeout(deadline, DateTimeOffset.UtcNow);
		return waitMs is not null && read.Wait(Math.Min(waitMs.Value, VersionReadWaitMs)) ? read.Result : null;
	}

	// The reader's own budget is 10 s; waiting past it gains nothing.
	private const int VersionReadWaitMs = 11_000;

	// Names the version the call launched, and whether the process library reports it as the active one. A refusal
	// gets the facts but no note, since nothing ran. An unestablished flag is said too: the base name runs the family
	// root, so "which version ran" is not something a caller can assume.
	internal static void AddVersion(RunProcessResponse response, string processCode, ProcessVersionFacts facts) {
		response.VersionName = processCode;
		response.IsActiveVersion = facts?.IsActiveVersion;
		response.ActiveVersionName = facts?.ActiveVersionName;
		if (response.Status == NotStartedStatus) {
			return;
		}
		string note = (facts?.IsActiveVersion, response.Status == QueuedBackgroundStatus) switch {
			(true, _) => null,
			(false, true) => BuildQueuedNotActiveVersionNote(processCode),
			(false, false) => BuildNotActiveVersionNote(processCode, response.ActiveVersionName),
			(null, true) => BuildQueuedUnknownVersionNote(processCode),
			(null, false) => BuildUnknownVersionNote(processCode)
		};
		if (note is not null) {
			(response.Warnings ??= []).Add(note);
		}
	}

	// A schema name is stored text the server returns, so one that is not a plain code is fenced before it reaches an
	// agent - the same rule CompileBusinessProcessCommand applies to the names it echoes.
	private static string DescribeSchemaName(string name) =>
		SchemaCode.IsMatch(name) ? $"'{name}'" : UntrustedText.Fenced(name);

	private static readonly System.Text.RegularExpressions.Regex SchemaCode = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z",
		System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

	internal static string BuildUnknownVersionNote(string processCode) =>
		$"'{processCode}' ran as asked, but whether it is the active version of its process could not be "
		+ "established, so this answer cannot say whether the platform's own triggers run the same version; "
		+ "describe-business-process reports isActiveVersion and activeVersionName.";

	internal static string BuildQueuedUnknownVersionNote(string processCode) =>
		$"The platform queued this run, and whether '{processCode}' is the active version of its process could not "
		+ "be established: its background queue starts the ACTIVE version, so if this one is not active, the launch "
		+ "ran another version. Check with describe-business-process; to run this version for certain, launch it "
		+ "with result-parameters, which makes the run synchronous.";

	// The platform's background queue re-resolves a queued run to the ACTIVE version (StartProcessCommandConsumer
	// reads GetActiveVersionItemByUId), so a queued run is not evidence that this version ran. Traced in source, not
	// measured: no stand has answered queued-background to this route yet.
	internal static string BuildQueuedNotActiveVersionNote(string processCode) =>
		$"'{processCode}' is NOT the active version of its process, and the platform queued this run: its background "
		+ "queue starts the ACTIVE version, so this launch may not have run the version you named. To run this "
		+ "version, launch it again with result-parameters, which makes the run synchronous.";

	// A run that failed inside an element - a script that threw, a formula that could not be computed - comes back
	// with only "check the process log" and no errorCode: the platform swallows the element's exception before
	// RunProcess builds its answer. It logged it, though, on the run's SysProcessLog row before returning, so the
	// failure names it. Best effort: when the log cannot be read the generic message stays, and a warning says
	// where to look. The read runs inside the MCP response deadline, so it gets only what is left of it: past the
	// deadline the tool answers still-running, and the failure - with what the log said - would be lost.
	private void AddLoggedError(ProcessStartResponse platformResponse, RunProcessResponse response,
		DateTimeOffset? responseDeadline) {
		if (response.Error is null || platformResponse is null || platformResponse.ProcessId == Guid.Empty) {
			return;
		}
		(string errorCode, _) = ReadErrorInfo(platformResponse.ErrorInfo);
		if (!string.IsNullOrWhiteSpace(errorCode)) {
			return;
		}
		int? readTimeoutMs = ResolveLogReadTimeout(responseDeadline, DateTimeOffset.UtcNow);
		if (readTimeoutMs is null) {
			(response.Warnings ??= []).Add("The error this run logged was not read, because too little of the "
				+ $"response time was left; it is in the process log of run {platformResponse.ProcessId}.");
			return;
		}
		string logged;
		try {
			logged = processRunLogReader.ReadErrorSummary(platformResponse.ProcessId, readTimeoutMs.Value);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			// Reading the log is a courtesy on top of a failure already reported; no failure of it may replace
			// that report. Its text goes to the console line only, rendered for a terminal (the MCP tool clears the
			// captured log), never into the response.
			logger.WriteWarning($"Could not read the process log of run {platformResponse.ProcessId}: "
				+ (UntrustedText.ForConsole(exception.GetReadableMessageException()) ?? "no detail reported"));
			(response.Warnings ??= []).Add(
				$"The error this run logged could not be read; it is in the process log of run {platformResponse.ProcessId}.");
			return;
		}
		string fenced = UntrustedText.Fenced(logged);
		if (fenced is not null) {
			response.Error += $" The process log of this run reports: {fenced}";
		}
	}

	// What the log read may spend: everything when no response deadline applies, otherwise what is left of it less
	// the margin, or null when that is under the minimum.
	internal static int? ResolveLogReadTimeout(DateTimeOffset? responseDeadline, DateTimeOffset now) {
		if (responseDeadline is null) {
			return int.MaxValue;
		}
		double left = (responseDeadline.Value - now).TotalMilliseconds - LogReadMarginMs;
		return left < MinimumLogReadMs ? null : (int)Math.Min(left, int.MaxValue);
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

		// With UseOldStartupExceptionHandling on, the platform rethrows a run's exception and returns no id, which
		// reads as a refusal - but the missing compiled method is thrown when the flow REACHES the script task, so
		// the elements before it ran. Reported as a failed run, so a caller does not re-run them as not-started.
		if (noHandle && !platformResponse.Success
				&& IsMissingCompiledMethod(errorCode)) {
			return new RunProcessResponse {
				Status = StatusNames[ErrorStatus],
				Error = DescribeFailure(errorCode, errorMessage, processCode)
			};
		}

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
			response.Error = DescribeFailure(errorCode, errorMessage, processCode);
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

	private static string DescribeFailure(string errorCode, string message, string processCode) {
		// Fenced: the platform's wording reaches an agent through the MCP result, and it can quote stored text.
		string detail = UntrustedText.Fenced(message) ?? "the platform returned no error details";
		return $"The process run failed: {detail}."
			+ DescribeErrorCode(errorCode)
			+ (IsMissingCompiledMethod(errorCode)
				? BuildMissingCompiledMethodHint(processCode)
				: string.Empty);
	}

	// The platform fills errorCode with the exception's type name (ResponseUtils.CreateErrorInfo), so an identifier
	// is shown as it is; anything else is the server's text like the message, and is fenced.
	private static string DescribeErrorCode(string errorCode) {
		if (string.IsNullOrWhiteSpace(errorCode)) {
			return string.Empty;
		}
		return ErrorCodeShape.IsMatch(errorCode) ? $" [{errorCode}]" : $" [{UntrustedText.Fenced(errorCode)}]";
	}

	private static readonly System.Text.RegularExpressions.Regex ErrorCodeShape = new(@"\A[A-Za-z_][A-Za-z0-9_.]{0,127}\z",
		System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

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
