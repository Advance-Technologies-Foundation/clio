using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

// Deliberately NOT [RequiresPackage]-gated, unlike the rest of this folder: the endpoint is built
// into every Creatio, so a gate would only break consumers. Does not extend BaseTool
// because its work can outlive the response deadline, and that path pins its session container for the
// whole run rather than taking the per-tenant monitor — same shape as CompileCreatioTool.
[McpServerToolType]
public sealed class RunProcessTool(
	ILogger logger,
	IToolCommandResolver commandResolver) {

	internal const string ToolName = "run-process";

	/// <summary>The canonical field list echoed back when an unknown argument key is refused (ENG-98566).</summary>
	internal const string ValidArgsHint = "Valid: environment-name, process-name, parameters, result-parameters, timeout, uri, login, password.";

	/// <summary>
	/// Refusal for a call whose whole argument object is absent (ENG-98566, Sonar S2259).
	/// </summary>
	/// <remarks>
	/// Stays inline rather than moving into the shared helper: it has to run before any field can be read,
	/// and that is what lets the analyser prove no field read is reached with null (csharpsquid:S2259). See
	/// <see cref="McpToolArgumentSupport.BuildUnknownArgumentError"/>.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	internal const string StillRunningStatus = "still-running";

	/// <summary>The status of an answer whose launch request was never sent; the platform's refusals share it.</summary>
	internal const string NotStartedStatus = RunProcessCommand.NotStartedStatus;

	/// <summary>
	/// run-process's response deadline: the shared <see cref="McpProgressHeartbeat.DefaultResponseDeadline"/>, so the
	/// answer reaches a client that gives up at 60 s first.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It was 150 s until ENG-102333 round 3. The answer at the deadline said "launched and still running" whatever
	/// the call had reached, and logging in and resolving the model can take most of a minute on a busy or reloading
	/// environment, so a 45 s answer could have claimed a launch that had not been sent. Held to 150 s, though, the
	/// call made Claude Code desktop give up at 60 s and restart the MCP server, which killed every other call in
	/// flight - a compile's worker among them (QA). <see cref="RunProcessLaunchGate"/> now records whether the launch
	/// request was sent, so the answer is exact either way: not-started when it was not, and it never will be;
	/// still-running when it was.
	/// </para>
	/// <para>
	/// The accepted cost: a synchronous run that takes 45-150 s used to answer with its verdict and result-parameter
	/// values, and now answers still-running on every client, with no handle to poll. A client that waits longer gets
	/// them back by raising <c>CLIO_MCP_RESPONSE_DEADLINE_SECONDS</c>.
	/// </para>
	/// <para>
	/// run-process is not in the worker cohort (<c>McpWorkerCohort</c>), so it runs in the MCP server process and
	/// <c>CLIO_MCP_RESPONSE_DEADLINE_SECONDS</c> applies to it as to the other tools that race it. Past the deadline the work
	/// carries on detached: a sent request waits for the platform's answer, which is discarded; a withdrawn one stops
	/// at the gate. Were it ever moved into a per-call worker, the parent would kill the worker after the answer and
	/// the gate would keep the answer exact; the per-call budget is above this deadline, so the answer stays
	/// reachable there too.
	/// </para>
	/// </remarks>
	internal static TimeSpan RunProcessResponseDeadline => McpProgressHeartbeat.DefaultResponseDeadline;

	// Test seam; null in production, where RunProcessResponseDeadline applies.
	internal TimeSpan? ResponseDeadlineOverride { get; set; }

	/// <summary>
	/// The note of an answer given at the response deadline after the launch request was sent.
	/// </summary>
	/// <remarks>
	/// "Sent" is not "ran": the answer cannot see whether the environment took the request, and a request that dies
	/// with its connection - the MCP server restarted, or, in a worker, the parent's kill - can be dropped while the
	/// environment still has it queued. So the note names the time the request left and sends the agent to evidence
	/// that can tell, and to the user when there is none. The time is this host's UTC, and SysProcessLog read through
	/// OData can label the environment user's local time as UTC (measured on CompilationHistory, see
	/// <c>docs/knowledge/platform/dataservice-returns-datetimes-in-the-session-zone.md</c>), so the note does not let
	/// a time comparison alone attribute a row to this launch.
	/// </remarks>
	internal static string BuildStillRunningNote(string processName, DateTime sentUtc) =>
		$"The launch request for '{processName}' was sent at "
		+ sentUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)
		+ " and had not been answered when the MCP response deadline was reached. This is NOT a failure and NOT "
		+ "a success — clio has no verdict, and cannot say whether the environment started the run. The platform "
		+ "exposes no handle for an in-flight synchronous run: the process id only exists in the RunProcess "
		+ "response, and the SysProcessLog row is buffered and written when the run ends, so there is "
		+ "nothing to poll yet. Do NOT re-run this process to find out — a second launch duplicates the "
		+ "work. Judge the outcome from the process's own effects, or from a later SysProcessLog read "
		+ "(odata-read on SysProcessLog, newest row for this process). Its times can come back in the environment "
		+ "user's time zone even when marked Z, so a row's time alone cannot tie it to this launch: if neither its "
		+ "effects nor such a row certainly shows this run, tell the user the outcome is unconfirmed and ask before "
		+ "launching it again.";

	/// <summary>
	/// The error of an answer given at the response deadline before the launch request was sent.
	/// </summary>
	/// <remarks>
	/// Exact, not a guess: the answer claimed <see cref="RunProcessLaunchGate"/> first, so the request is never sent.
	/// </remarks>
	internal static string BuildNotLaunchedError(string processName) =>
		$"'{processName}' was not launched: preparing the launch (logging in, reading the process and its "
		+ "parameters from the environment) did not finish within the MCP response deadline, so the launch request "
		+ "was never sent and nothing ran. The environment is answering slowly - for example while it reloads after a "
		+ "compile or a restart - so wait a minute before calling run-process again; if that call is not launched "
		+ "either, tell the user the environment is not answering instead of calling again.";

	// A launch can outlive the MCP response deadline and pins its session container for the whole run, so a
	// wedged run holds the host's resources: the boundary belongs in a worker. Declared only - run-process is not in
	// the shipped worker cohort, so on stdio it still runs in the server process. PerCall with no family because the platform
	// exposes no handle for an in-flight synchronous run (see BuildStillRunningNote), so there is no status
	// poller for a sticky worker to serve. ParentKillDefault is safe here: the process runs server-side in
	// Creatio, so killing the worker abandons the wait, it does not abort the process.
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.Progress,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Run (launch) a Creatio business process; resolve its CODE and parameter codes with get-process-signature "
		+ "first, and read the outcome from `status`. The call answers by the MCP response deadline (45 s by default): "
		+ "a run that has not ended by then answers `still-running` with no verdict and no result-parameter values - "
		+ "do not re-run it - and a launch whose request was not sent yet answers `not-started` (nothing ran). VERSIONS: a code names ONE version, because every saved "
		+ "version is a separate schema with its own code, and the version the platform's own triggers and "
		+ "schedules execute is the family's ACTIVE version - which is usually NOT the family root you reach by "
		+ "the base name. Before launching a process that has versions, read `isActiveVersion` from "
		+ "describe-business-process and launch the code it reports in `activeVersionName`. Measured: this endpoint "
		+ "folds a non-active version's code onto the ACTIVE version, so run-process cannot run a version that is "
		+ "not active - a run 'of' a new version executes the previous one. Pass the active version's code "
		+ "explicitly; to see a new version run before activating it, the user runs it from the process designer. "
		+ "A display caption is still refused - launching must name a code "
		+ "- but the refusal names the code it resolved to, and that IS the active version's code, so the refusal "
		+ "message is the short path to the right one.")]
	public async Task<RunProcessResponse> RunProcess(
		[Description("run-process parameters")]
		[Required]
		RunProcessArgs args,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default) {
		if (args is null) {
			return new RunProcessResponse { Error = NullArgsError };
		}

		// The only unknown-key defence this tool has; the helper's docs say why. ENG-98566.
		string argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(
			args.ExtensionData, ValidArgsHint);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return new RunProcessResponse { Error = argumentError };
		}

		RunProcessOptions options = new() {
			ProcessName = args.ProcessName,
			Parameters = args.Parameters,
			ResultParameters = args.ResultParameters,
			TimeoutSeconds = args.Timeout ?? 0,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password,
			// The same deadline the race below uses, so a failed run's log read never outlives it.
			ResponseDeadline = DateTimeOffset.UtcNow + (ResponseDeadlineOverride ?? RunProcessResponseDeadline),
			LaunchGate = new RunProcessLaunchGate()
		};

		try {
			return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				ToolName,
				() => Launch(options),
				deadline: ResponseDeadlineOverride ?? RunProcessResponseDeadline,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) {
			// The caller stopped waiting (notifications/cancelled, or the server shutting down) before any answer. A
			// launch nobody waits for must not start afterwards: its caller, left without an answer, may well call
			// again, and that would be a second run. A request already sent cannot be taken back.
			options.LaunchGate.TryWithdraw();
			throw;
		}
		catch (McpResponseDeadlineExceededException) {
			if (options.LaunchGate.TryWithdraw()) {
				return new RunProcessResponse {
					Status = NotStartedStatus,
					Error = BuildNotLaunchedError(args.ProcessName)
				};
			}
			return new RunProcessResponse {
				Status = StillRunningStatus,
				Warnings = [BuildStillRunningNote(args.ProcessName, options.LaunchGate.SentUtc ?? DateTime.UtcNow)]
			};
		}
	}

	private RunProcessResponse Launch(RunProcessOptions options) {
		// Key resolution is inside the try: this task is detached from the request, so an escaping exception
		// becomes a raw transport error, or past the deadline is swallowed unobserved.
		string tenantKey = null;
		bool previousPreserveMessages = logger.PreserveMessages;
		try {
			tenantKey = commandResolver.GetTenantKey(options);
			// Pins the session container so a concurrent different-tenant Acquire cannot evict the resolved
			// client mid-run. Released session-container-only: this path never took the GetLock monitor.
			McpToolExecutionLock.MarkInUse(tenantKey);
			logger.PreserveMessages = true;
			RunProcessCommand command = commandResolver.Resolve<RunProcessCommand>(options);
			command.TryRun(options, out RunProcessResponse response);
			return response;
		}
		catch (Exception e) {
			return new RunProcessResponse {
				Error = SensitiveErrorTextRedactor.Redact(e.Message)
			};
		}
		finally {
			logger.ClearMessages();
			logger.PreserveMessages = previousPreserveMessages;
			if (tenantKey is not null) {
				McpToolExecutionLock.MarkSessionContainerAvailable(tenantKey);
			}
		}
	}
}

public sealed record RunProcessArgs {

	/// <summary>
	/// Overflow bag for top-level keys the SDK could not bind to a declared argument (ENG-98566).
	/// Inspected by the tool so a mis-keyed argument is named back to the caller; a bag that is
	/// never read is the failure mode, not the fix.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }

	[JsonPropertyName("process-name")]
	[Description("Process CODE (schema Name), e.g. 'MigrateDashboardsProcess', naming ONE version of a "
		+ "process. A display caption is rejected, naming the code it resolved to - the ACTIVE version's code "
		+ "when the caption belongs to one version family, since a caption is shared by every member; a caption "
		+ "shared by several distinct processes is refused with the candidates, because launching by one could "
		+ "start the wrong process.")]
	[Required]
	public required string ProcessName { get; init; }

	[JsonPropertyName("parameters")]
	[Description("Input values keyed by parameter CODE.")]
	public Dictionary<string, JsonElement>? Parameters { get; init; }

	[JsonPropertyName("result-parameters")]
	[Description("Codes of the parameters to read back after the run; a non-empty list forces a "
		+ "background-mode process to run synchronously. The values come back only when the run ends within the "
		+ "MCP response deadline.")]
	public string[]? ResultParameters { get; init; }

	[JsonPropertyName("timeout")]
	[Description("HTTP request timeout in seconds; omit for none, which is what a long synchronous process needs. "
		+ "It bounds the request, not the MCP response.")]
	public int? Timeout { get; init; }

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	public string? EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	[Description(McpToolDescriptions.Uri)]
	public string? Uri { get; init; }

	[JsonPropertyName("login")]
	[Description(McpToolDescriptions.Login)]
	public string? Login { get; init; }

	[JsonPropertyName("password")]
	[Description(McpToolDescriptions.Password)]
	public string? Password { get; init; }
}
