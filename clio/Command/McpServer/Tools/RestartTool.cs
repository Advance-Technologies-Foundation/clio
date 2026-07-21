using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class RestartTool(
	RestartCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<RestartOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for restart-by-environment-name, referenced in the poll-guidance message.
	/// </summary>
	internal const string RestartByEnvironmentNameToolName = "restart-by-environment-name";

	/// <summary>
	/// Stable MCP tool name for restart-by-credentials, referenced in the poll-guidance message.
	/// </summary>
	internal const string RestartByCredentialsToolName = "restart-by-credentials";

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Parameters mirror the restart-by-environment-name MCP tool contract; the trailing server/requestContext/cancellationToken are framework-injected. Grouping them into a DTO would break the MCP-reflected JSON schema.")]
	[McpServerTool(Name = RestartByEnvironmentNameToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Restarts a Creatio instance by environment name. By default (waitReady=true) polls the instance's health-check endpoint after the restart request and returns only once it answers, or after waitTimeoutSeconds. Long-running: streams notifications/progress while waiting; if the MCP response deadline is reached first, returns exit-code 0 with an in-progress note — the restart itself already succeeded and the wait continues server-side. Do NOT retry; poll readiness with the clio CLI healthcheck verb (clio healthcheck -e <environment>) instead.")]
	public async Task<CommandExecutionResult> RestartInstanceByName(
		[Description("Target Environment name to restart")] [Required] string environmentName,
		[DefaultValue(true)] [Description("Poll the application after restart until it answers health-check; default true")] bool waitReady = true,
		[DefaultValue(600)] [Description("Max seconds to wait for readiness when waitReady is true; default 600")] int waitTimeoutSeconds = 600,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		RestartOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000,
			WaitReady = waitReady,
			ReadyTimeout = waitTimeoutSeconds
		};
		return await ExecuteWithReadinessWait(
			options, waitReady, RestartByEnvironmentNameToolName, $"environment '{environmentName}'",
			waitTimeoutSeconds, server, requestContext, cancellationToken).ConfigureAwait(false);
	}

	// The deprecated camelCase alias "restart-by-environmentName" is no longer a second [McpServerTool]
	// method: it is served by IMcpToolCompatibilityCatalog (alias -> restart-by-environment-name), which
	// the durable call-tool handler and the clio-run executor both resolve through. A duplicate method
	// would now fail the registry's duplicate-name guard by design.
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Parameters mirror the restart-by-credentials MCP tool contract; the trailing server/requestContext/cancellationToken are framework-injected. Grouping them into a DTO would break the MCP-reflected JSON schema.")]
	[McpServerTool(Name = RestartByCredentialsToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Restarts a Creatio instance by credentials. By default (waitReady=true) polls the instance's health-check endpoint after the restart request and returns only once it answers, or after waitTimeoutSeconds. Long-running: streams notifications/progress while waiting; if the MCP response deadline is reached first, returns exit-code 0 with an in-progress note — the restart itself already succeeded and the wait continues server-side. Do NOT retry; poll readiness with the clio CLI healthcheck verb (clio healthcheck -e <environment>) instead.")]
	public async Task<CommandExecutionResult> RestartInstanceByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false,
		[DefaultValue(true)] [Description("Poll the application after restart until it answers health-check; default true")] bool waitReady = true,
		[DefaultValue(600)] [Description("Max seconds to wait for readiness when waitReady is true; default 600")] int waitTimeoutSeconds = 600,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default
	) {
		CommandExecutionResult validationError = CommandExecutionResult.ValidateCredentials(url, userName, password);
		if (validationError != null) {
			return validationError;
		}
		RestartOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore,
			TimeOut = 30_000,
			WaitReady = waitReady,
			ReadyTimeout = waitTimeoutSeconds
		};
		return await ExecuteWithReadinessWait(
			options, waitReady, RestartByCredentialsToolName, $"'{url}'",
			waitTimeoutSeconds, server, requestContext, cancellationToken).ConfigureAwait(false);
	}

	// Shared execution path for both restart tools: waitReady=false preserves the pre-ENG-91315
	// synchronous behavior unchanged; waitReady=true wraps the same call in the heartbeat+deadline
	// helper so MCP clients keep receiving progress instead of hitting an inactivity/hard-ceiling
	// timeout while the instance warms up (ENG-91274 / ENG-91316 patterns).
	private async Task<CommandExecutionResult> ExecuteWithReadinessWait(
		RestartOptions options, bool waitReady, string toolName, string targetDescription,
		int waitTimeoutSeconds, global::ModelContextProtocol.Server.McpServer server, RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken) {
		if (!waitReady) {
			return InternalExecute<RestartCommand>(options);
		}

		try {
			return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				toolName,
				() => InternalExecute<RestartCommand>(options),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		} catch (McpResponseDeadlineExceededException) {
			return CommandExecutionResult.FromInfo(BuildInProgressMessage(targetDescription, toolName, waitTimeoutSeconds));
		}
	}

	/// <summary>
	/// Builds the in-progress notice returned when the readiness wait exceeds the MCP response deadline.
	/// Extracted as a pure function (rather than inlined in the catch block) so its wording is directly
	/// unit-testable without racing the real response-deadline timer.
	/// </summary>
	internal static string BuildInProgressMessage(string targetDescription, string toolName, int waitTimeoutSeconds) =>
		$"Restart of {targetDescription} was accepted and the restart request itself already "
		+ "succeeded; the application is still warming up (MCP response deadline reached, the "
		+ $"readiness wait continues server-side for up to {waitTimeoutSeconds}s). Poll readiness "
		+ "by running the clio CLI healthcheck verb against the same environment/credentials "
		+ "(e.g. `clio healthcheck -e <environment>`) — exit code 0 means the application is ready. "
		+ $"Typical warm-up is 1-10 minutes; do NOT retry {toolName}.";
}
