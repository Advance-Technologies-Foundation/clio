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
// because its work can outlive the response deadline, and that path holds the per-tenant monitor for the
// whole call — same shape as CompileCreatioTool.
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
	/// The guard below reads <c>args.ExtensionData</c> and every check after it reads a real field, so
	/// exactly one place may decide what a null <c>args</c> means - and it is this one. An <c>args?.</c>
	/// on the first line followed by an unconditional dereference on the next READS as null-safe while
	/// only moving the NullReferenceException three lines down, where it escapes as a raw transport
	/// fault instead of an answer. Hard to reach behind [Required] and the SDK missing-parameter error,
	/// but "hard to reach" is not the same as handled.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	internal const string StillRunningStatus = "still-running";

	// Test seam; null in production, where the default deadline applies.
	internal TimeSpan? ResponseDeadlineOverride { get; set; }

	internal static string BuildStillRunningNote(string processName) =>
		$"'{processName}' was launched and is still running server-side (the MCP response deadline was "
		+ "reached first). This is NOT a failure and NOT a success — clio has no verdict. The platform "
		+ "exposes no handle for an in-flight synchronous run: the process id only exists in the RunProcess "
		+ "response, and the SysProcessLog row is buffered and written when the run ends, so there is "
		+ "nothing to poll yet. Do NOT re-run this process to find out — a second launch duplicates the "
		+ "work. Judge the outcome from the process's own effects, or from a later SysProcessLog read "
		+ "(odata-read on SysProcessLog, newest row for this process).";

	// A launch can outlive the MCP response deadline and holds the per-tenant monitor for the whole call, so a
	// wedged run wedges the host: the boundary belongs in a worker. PerCall with no family because the platform
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
		+ "first, and read the outcome from `status`. VERSIONS: a code names ONE version, because every saved "
		+ "version is a separate schema with its own code, and the version the platform's own triggers and "
		+ "schedules execute is the family's ACTIVE version - which is usually NOT the family root you reach by "
		+ "the base name. Before launching a process that has versions, read `isActiveVersion` from "
		+ "describe-business-process and launch the code it reports in `activeVersionName`. Whether this endpoint "
		+ "itself folds a non-active code onto the active version is NOT established, so do not rely on it: pass "
		+ "the active version's code explicitly. A display caption is still refused - launching must name a code "
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

		// ENG-98566. This tool is LONG-TAIL - absent from McpCoreToolProfile - so McpToolErrorFilter's
		// unknown-key classifier never runs on it in ANY payload shape: TryRefuseCallArgumentsCore bails at
		// TryGetToolMethod because MatchedPrimitive is null for a tool that is not advertised. Even a
		// RESIDENT tool is only classified in the FLAT shape - an already-wrapped {"args":{...}} call is
		// passed through untouched. So the overflow bag plus this check is the ONLY thing standing between
		// a mis-keyed call and a plausible success. Do not delete it because the normalizer exists.
		string argumentError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArgsHint);
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
				Status = StillRunningStatus,
				Warnings = [BuildStillRunningNote(args.ProcessName)]
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
	public Dictionary<string, JsonElement> ExtensionData { get; init; }

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
		+ "background-mode process to run synchronously.")]
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
