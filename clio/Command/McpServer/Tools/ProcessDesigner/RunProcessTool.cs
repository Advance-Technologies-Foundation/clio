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

/// <summary>
/// MCP tool surface for launching a Creatio business process at runtime through the built-in
/// <c>ProcessEngineService.svc/RunProcess</c> endpoint.
/// </summary>
/// <remarks>
/// Deliberately NOT <c>[FeatureToggle("process-designer")]</c> and its options are deliberately NOT
/// <c>[RequiresPackage]</c>-gated, unlike the rest of the process-designer suite — the same rationale
/// recorded for <see cref="GetProcessSignatureTool"/>: the endpoint is built into every Creatio and never
/// touches <c>ProcessDesignService</c>, and gating it would break consumers running on stands without the
/// toggle or the server package. Both absences are pinned by tests.
/// <para>
/// The tool does NOT extend <see cref="BaseTool{T}"/>: its work can outlive the MCP response deadline, and
/// <see cref="BaseTool{T}"/>'s typed-response path holds the broad per-tenant execution monitor for the
/// whole call, which would stall every unrelated same-tenant tool behind work nobody is waiting for any
/// more. It follows <see cref="CompileCreatioTool"/> instead, which takes no such lock. No narrow
/// replacement lock is needed here: unlike compilation, the platform happily runs process instances
/// concurrently.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class RunProcessTool(
	ILogger logger,
	IToolCommandResolver commandResolver) {

	internal const string ToolName = "run-process";

	/// <summary>
	/// Test seam overriding the MCP response deadline. <see langword="null"/> in production (the default
	/// ~150 s <see cref="McpProgressHeartbeat.DefaultResponseDeadline"/> applies); tests set a tiny value to
	/// exercise the deadline branch without racing the real ceiling.
	/// </summary>
	internal TimeSpan? ResponseDeadlineOverride { get; set; }

	/// <summary>
	/// The answer returned when clio has to reply before Creatio does. Extracted as a pure function so its
	/// wording is unit-testable without racing the real deadline timer.
	/// </summary>
	internal static string BuildStillRunningNote(string processName) =>
		$"'{processName}' was launched and is still running server-side (the MCP response deadline was "
		+ "reached first). This is NOT a failure and NOT a success — clio has no verdict. The platform "
		+ "exposes no handle for an in-flight synchronous run: the process id only exists in the RunProcess "
		+ "response, and the SysProcessLog row is buffered and written when the run ends, so there is "
		+ "nothing to poll yet. Do NOT re-run this process to find out — a second launch duplicates the "
		+ "work. Judge the outcome from the process's own effects, or from a later SysProcessLog read "
		+ "(odata-read on SysProcessLog, newest row for this process).";

	/// <summary>
	/// Launches a business process on a registered environment.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Run (launch) a Creatio business process on a registered environment. Resolve the parameter CODES with "
		+ "get-process-signature FIRST and key `parameters` by those codes — the platform silently drops a value "
		+ "keyed by a caption. "
		+ "READ THE VERDICT FROM `mode`, NEVER FROM `success`: `success` only says clio executed the call, while the "
		+ "platform itself returns success=true for a FAILED run unless one of its own feature flags is on. "
		+ "`mode` is one of the platform statuses — completed | error | cancelled | running | cancelling | inactive — "
		+ "or one of two NO-VERDICT outcomes. (1) `queued-background`: the process schema starts in background mode, "
		+ "so the platform queued it fire-and-forget and returned NO process id, status or result — for such a process "
		+ "the launch IS the whole outcome, and passing `result-parameters` is what forces it to run synchronously and "
		+ "produce a verdict instead. (2) `accepted-still-running`: the run outlived the MCP response deadline; clio "
		+ "answered first and has no verdict. In BOTH no-verdict cases do NOT re-run to find out — a second launch "
		+ "duplicates the work; judge the outcome from the process's own effects. "
		+ "`mode: running` means the process suspended on something external (a user task, a timer, a signal) and its "
		+ "`processId` is real — it is also the PRIMARY KEY of the run's SysProcessLog row, so poll it with odata-read "
		+ "on SysProcessLog filtered by Id when you need to await completion. "
		+ "This tool NEVER retries: a retry can duplicate work, because idempotency is a property of the specific "
		+ "process and not of the transport. "
		+ "A String parameter is passed through VERBATIM and never re-encoded — a serialized ESQ filter or other "
		+ "structured text must be supplied exactly as the process expects it, since double-encoding it yields an "
		+ "empty selection rather than an error. "
		+ "Unknown parameter codes, an Output parameter in `parameters`, and an Input parameter in `result-parameters` "
		+ "are all rejected BEFORE any server call, listing the codes that are accepted. "
		+ "Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public async Task<RunProcessResponse> RunProcess(
		[Description("Parameters: process-name (required, the process CODE or its caption); parameters (code -> value); "
			+ "result-parameters (codes to read back); timeout (seconds); environment-name preferred.")]
		[Required]
		RunProcessArgs args,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default) {
		RunProcessOptions options = new() {
			ProcessName = args.ProcessName,
			Parameters = args.Parameters,
			ResultParameters = args.ResultParameters,
			TimeoutSeconds = args.Timeout ?? 0,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

		try {
			return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				ToolName,
				() => Launch(options),
				deadline: ResponseDeadlineOverride,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch (McpResponseDeadlineExceededException) {
			return new RunProcessResponse {
				Success = true,
				Mode = "accepted-still-running",
				Warnings = [BuildStillRunningNote(args.ProcessName)]
			};
		}
	}

	private RunProcessResponse Launch(RunProcessOptions options) {
		// Everything, key resolution included, sits inside the guarded body: this method runs on a task
		// detached from the request, so an exception escaping it would surface as a raw MCP transport error
		// (or, past the deadline, be swallowed by the fault-only background observer) instead of the typed
		// failure every other path returns.
		string tenantKey = null;
		bool previousPreserveMessages = logger.PreserveMessages;
		try {
			tenantKey = commandResolver.GetTenantKey(options);
			// Pin the session container for the call so a concurrent different-tenant Acquire cannot
			// LRU-evict and dispose the resolved client mid-run. Released through the
			// session-container-only path because this method never took the GetLock-owned monitor
			// (see the class remarks).
			McpToolExecutionLock.MarkInUse(tenantKey);
			logger.PreserveMessages = true;
			RunProcessCommand command = commandResolver.Resolve<RunProcessCommand>(options);
			command.TryRun(options, out RunProcessResponse response);
			return response;
		}
		catch (Exception e) {
			return new RunProcessResponse {
				Success = false,
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

/// <summary>
/// MCP arguments for the <c>run-process</c> tool.
/// </summary>
public sealed record RunProcessArgs {

	[JsonPropertyName("process-name")]
	[Description("Process code (schema Name), e.g. 'MigrateDashboardsProcess', OR the display caption shown in "
		+ "the process designer. The resolved code is echoed back as resolvedProcessCode.")]
	[Required]
	public required string ProcessName { get; init; }

	[JsonPropertyName("parameters")]
	[Description("Input parameter values as an object keyed by parameter CODE (the 'name' from "
		+ "get-process-signature), never by caption. Values are coerced to the parameter's type; a String "
		+ "parameter is passed through VERBATIM, so structured text such as a serialized ESQ filter must be "
		+ "supplied exactly as the process expects it. A lookup parameter takes the record's Id, not its "
		+ "display name. An Output parameter cannot be assigned here.")]
	public Dictionary<string, JsonElement>? Parameters { get; init; }

	[JsonPropertyName("result-parameters")]
	[Description("Codes of the parameters to read back after execution. An Input parameter is rejected. NOTE: "
		+ "supplying this forces a background-mode process to run SYNCHRONOUSLY, which is the only way to get a "
		+ "verdict for such a process; leave it empty to let the schema's own mode apply. A process that declares "
		+ "no Output parameters legitimately returns nothing here.")]
	public string[]? ResultParameters { get; init; }

	[JsonPropertyName("timeout")]
	[Description("HTTP request timeout in seconds. Omit for no timeout, which is what a long synchronous process "
		+ "needs. This bounds the request, not the MCP response — a run that outlives the response deadline is "
		+ "answered with mode 'accepted-still-running' while it keeps going server-side.")]
	public int? Timeout { get; init; }

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	public string? EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	[Description("Creatio base URI (emergency fallback only; prefer environment-name)")]
	public string? Uri { get; init; }

	[JsonPropertyName("login")]
	[Description(McpToolDescriptions.Login)]
	public string? Login { get; init; }

	[JsonPropertyName("password")]
	[Description(McpToolDescriptions.Password)]
	public string? Password { get; init; }
}
