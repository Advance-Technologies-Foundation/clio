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

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Run (launch) a Creatio business process; resolve its CODE and parameter codes with get-process-signature "
		+ "first, and read the outcome from `status`.")]
	public async Task<RunProcessResponse> RunProcess(
		[Description("run-process parameters")]
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

	[JsonPropertyName("process-name")]
	[Description("Process CODE (schema Name), e.g. 'MigrateDashboardsProcess'. A display caption is rejected, "
		+ "naming the code it resolved to — captions are not unique, so launching by one could start the wrong "
		+ "process.")]
	[Required]
	public required string ProcessName { get; init; }

	[JsonPropertyName("parameters")]
	[Description("Input values keyed by parameter CODE.")]
	public Dictionary<string, JsonElement>? Parameters { get; init; }

	[JsonPropertyName("result-parameters")]
	[Description("Codes of the parameters to read back after the run.")]
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
