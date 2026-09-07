using System;
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
	IToolCommandResolver commandResolver,
	IRestartOperationRegistry registry) : BaseTool<RestartOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for restart-by-environment-name, referenced in the poll-guidance message.
	/// </summary>
	internal const string RestartByEnvironmentNameToolName = "restart-by-environment-name";

	/// <summary>
	/// Stable MCP tool name for restart-by-credentials, referenced in the poll-guidance message.
	/// </summary>
	internal const string RestartByCredentialsToolName = "restart-by-credentials";

	/// <summary>
	/// Test seam overriding the MCP response deadline used by the readiness wait. <see langword="null"/> in
	/// production (the default <see cref="McpProgressHeartbeat.DefaultResponseDeadline"/> ~150 s applies);
	/// unit tests set a tiny value to deterministically exercise the deadline-exceeded in-progress branch
	/// without racing the real ceiling.
	/// </summary>
	internal TimeSpan? ResponseDeadlineOverride { get; set; }

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Parameters mirror the restart-by-environment-name MCP tool contract; the trailing server/requestContext/cancellationToken are framework-injected. Grouping them into a DTO would break the MCP-reflected JSON schema.")]
	// Long-running starter of the restart family: the worker must outlive the response so restart-status
	// (which shares OperationFamily = Restart and Lifetime = Sticky) reaches the same worker.
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.Sticky,
		OperationFamily = McpToolOperationFamily.Restart,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
		RequiresClientRequests = McpToolClientRequests.Progress,
		SharedFileResource = McpToolSharedFileResource.None,
		StartsOperation = true)]
	[McpServerTool(Name = RestartByEnvironmentNameToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Restarts a Creatio instance by environment name. By default (waitReady=true) waits after the restart until the instance answers an authenticated application-layer round-trip — not merely the liveness health-check ping — and returns only once it is genuinely serving, or after waitTimeoutSeconds. Long-running: streams notifications/progress while waiting; if the MCP response deadline is reached first, returns exit-code 0 with an in-progress note carrying an operation-id — the restart itself already succeeded and the readiness wait continues server-side. Do NOT retry; poll restart-status with the same environment-name (or this operation-id) instead.")]
	public async Task<CommandExecutionResult> RestartInstanceByName(
		[Description("Target Environment name to restart")] [Required] string environmentName,
		[DefaultValue(true)] [Description("Wait after the restart until the instance answers an authenticated application-layer round-trip (not merely the liveness health-check ping); default true")] bool waitReady = true,
		[DefaultValue(600)] [Description("Max seconds to wait for readiness when waitReady is true; default 600, capped at 3600")] int waitTimeoutSeconds = 600,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		// Clamp to a bounded ceiling (Finding 3): the readiness wait pins the session container for its whole
		// duration, so an unbounded caller-chosen timeout is a hardening gap. Clamp here so the tracked options,
		// the in-progress notice, and the readiness budget all reflect the same effective value.
		waitTimeoutSeconds = Math.Clamp(waitTimeoutSeconds, 1, RestartOptions.MaxReadyTimeoutSeconds);
		RestartOptions options = new() {
			Environment = environmentName,
			TimeOut = 30_000,
			WaitReady = waitReady,
			ReadyTimeout = waitTimeoutSeconds
		};
		return await ExecuteWithReadinessWait(
			options, waitReady,
			new RestartWaitContext(RestartByEnvironmentNameToolName, $"environment '{environmentName}'", waitTimeoutSeconds, environmentName),
			server, requestContext, cancellationToken).ConfigureAwait(false);
	}

	// The deprecated camelCase alias "restart-by-environmentName" is no longer a second [McpServerTool]
	// method: it is served by IMcpToolCompatibilityCatalog (alias -> restart-by-environment-name), which
	// the durable call-tool handler and the clio-run executor both resolve through. A duplicate method
	// would now fail the registry's duplicate-name guard by design.
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Parameters mirror the restart-by-credentials MCP tool contract; the trailing server/requestContext/cancellationToken are framework-injected. Grouping them into a DTO would break the MCP-reflected JSON schema.")]
	// Same restart family and the same sticky worker as restart-by-environment-name: the readiness wait
	// outlives the response here too. That this path is deliberately unreportable by restart-status (its
	// tenant key is URI-derived) changes the poll story, not how the call executes.
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.Sticky,
		OperationFamily = McpToolOperationFamily.Restart,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
		RequiresClientRequests = McpToolClientRequests.Progress,
		SharedFileResource = McpToolSharedFileResource.None,
		StartsOperation = true)]
	[McpServerTool(Name = RestartByCredentialsToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Restarts a Creatio instance by credentials. By default (waitReady=true) waits after the restart until the instance answers an authenticated application-layer round-trip — not merely the liveness health-check ping — and returns only once it is genuinely serving, or after waitTimeoutSeconds. Long-running: streams notifications/progress while waiting; if the MCP response deadline is reached first, returns exit-code 0 with an in-progress note — the restart itself already succeeded and the readiness wait continues server-side. Do NOT retry. Note that restart-status CANNOT report a credentials-started restart (it is keyed by a registered environment name); re-check with healthcheck, or use restart-by-environment-name when you need a pollable restart.")]
	public async Task<CommandExecutionResult> RestartInstanceByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false,
		[DefaultValue(true)] [Description("Wait after the restart until the instance answers an authenticated application-layer round-trip (not merely the liveness health-check ping); default true")] bool waitReady = true,
		[DefaultValue(600)] [Description("Max seconds to wait for readiness when waitReady is true; default 600, capped at 3600")] int waitTimeoutSeconds = 600,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default
	) {
		CommandExecutionResult validationError = CommandExecutionResult.ValidateCredentials(url, userName, password);
		if (validationError != null) {
			return validationError;
		}
		// Clamp to a bounded ceiling (Finding 3) — see RestartInstanceByName; keeps the pinned-container wait bounded.
		waitTimeoutSeconds = Math.Clamp(waitTimeoutSeconds, 1, RestartOptions.MaxReadyTimeoutSeconds);
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
			options, waitReady,
			// No registered environment name on the credentials path, so the wait is TRACKED (the
			// registry's lifecycle/eviction invariants hold uniformly) but not ADVERTISED as pollable.
			// Corrected 2026-08-18, story 7 AC-00: the old reasoning here said the two key spaces were
			// structurally disjoint because BuildCacheKey used `Environment ?? Uri`. That is no longer
			// true — the name branch and the URI branch now converge on one normalised target, so a
			// credentials-path record and an environment-named lookup DO collide when they mean the same
			// target with the same credentials. The behaviour is deliberately unchanged and is now merely
			// conservative rather than structural: at this point we cannot tell whether these credentials
			// correspond to a registered environment, so promising a poll target would be a guess. An
			// honest notice beats sending the agent to a target that may answer not-found.
			new RestartWaitContext(RestartByCredentialsToolName, $"'{url}'", waitTimeoutSeconds, null),
			server, requestContext, cancellationToken).ConfigureAwait(false);
	}

	// Shared execution path for both restart tools. waitReady=false preserves the pre-ENG-91315
	// synchronous behavior unchanged. waitReady=true splits the work into two phases (review Finding 2):
	//   Phase 1 — the restart REQUEST runs under the per-tenant execution lock (via BaseTool.InternalExecute,
	//             which acquires and releases the lock). WaitReady=false on the request-only options so
	//             RestartCommand.Execute performs only the request and returns immediately.
	//   Phase 2 — the read-only readiness WAIT runs LOCK-FREE, detached under the heartbeat+deadline helper,
	//             tracked in IRestartOperationRegistry so restart-status can report it after an in-progress
	//             notice. Keeping the multi-minute warm-up out of the locked region is the point: a same-tenant
	//             tool call must not serialize behind it. The session-container is still pinned in-use for the
	//             wait (FR-08) so eviction cannot dispose the health-check client mid-poll.
	// Descriptive context for a readiness wait, bundled into a DTO. Unlike the public
	// [McpServerTool] methods (whose flat parameter lists must stay flat because they are
	// reflected into the MCP JSON schema — see the S107 suppression above), this private
	// helper has no reflected schema, so grouping its descriptive fields is safe and keeps
	// the parameter count within budget.
	private readonly record struct RestartWaitContext(
		string ToolName, string TargetDescription, int WaitTimeoutSeconds, string EnvironmentName);

	private async Task<CommandExecutionResult> ExecuteWithReadinessWait(
		RestartOptions options, bool waitReady, RestartWaitContext waitContext,
		global::ModelContextProtocol.Server.McpServer server, RequestContext<CallToolRequestParams> requestContext,
		CancellationToken cancellationToken) {
		if (!waitReady) {
			return InternalExecute<RestartCommand>(options);
		}

		// Phase 1: restart request only, under the per-tenant execution lock (released on return).
		CommandExecutionResult requestResult = InternalExecute<RestartCommand>(BuildRequestOnlyOptions(options));
		if (requestResult.ExitCode != 0) {
			// The restart request itself failed (or the environment did not resolve) — surface it as-is; there
			// is nothing to wait on, so no operation is tracked.
			return requestResult;
		}

		// Phase 2: readiness wait, lock-free and tracked. Begin BEFORE the deadline race so the operation-id
		// is available for the in-progress notice even when the wait outlives the response.
		string tenantKey = ResolveTenantLockKey(options);
		RestartOperationRecord operation = registry.Begin(tenantKey, waitContext.EnvironmentName);
		try {
			return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				waitContext.ToolName,
				() => RunReadinessWait(options, requestResult, waitContext, tenantKey, operation.OperationId),
				deadline: ResponseDeadlineOverride,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		} catch (McpResponseDeadlineExceededException) {
			return CommandExecutionResult.FromInfo(
				BuildInProgressMessage(
					waitContext.TargetDescription, waitContext.ToolName, waitContext.WaitTimeoutSeconds,
					operation.OperationId, waitContext.EnvironmentName));
		}
	}

	// Runs the read-only readiness poll WITHOUT the per-tenant execution lock (so it does not serialize other
	// same-tenant calls), while still pinning the session container in-use for its duration (FR-08) so a
	// concurrent different-tenant Acquire cannot LRU-evict and dispose the health-check client mid-poll. The
	// registry is finalized in every exit path — including a resolve/health-check throw — so restart-status can
	// never observe an operation stuck "running" (mirrors CompileCreatioTool's registry.Finish guarantee).
	private CommandExecutionResult RunReadinessWait(
		RestartOptions options, CommandExecutionResult requestResult, RestartWaitContext waitContext,
		string tenantKey, string operationId) {
		int readinessExitCode = 1;
		// Pin the session container in-use for the wait (FR-08) WITHOUT taking the per-tenant lock (GetLock) —
		// the readiness poll is read-only and must not serialize other same-tenant calls. Because no GetLock was
		// taken, the release must NOT go through MarkAvailable (which decrements the GetLock-owned in-use count and
		// would stray-decrement an unrelated holder); use the session-container-only release instead (Finding 2).
		McpToolExecutionLock.MarkInUse(tenantKey);
		try {
			RestartCommand readinessCommand = commandResolver.Resolve<RestartCommand>(options);
			bool ready = readinessCommand.WaitForReadiness(options);
			readinessExitCode = ready ? 0 : 1;
			registry.Finish(operationId, readinessExitCode);
			return ready
				? requestResult
				: new CommandExecutionResult(1, [
					new ErrorMessage(
						$"Restart of {waitContext.TargetDescription} was requested successfully, but the application "
						+ $"did not answer its health-check within {waitContext.WaitTimeoutSeconds}s.")
				]);
		} catch (Exception exception) {
			registry.Finish(operationId, 1);
			return CommandExecutionResult.FromException(
				exception, redactSensitive: McpPassthroughRedaction.IsPassthroughKey(tenantKey));
		} finally {
			McpToolExecutionLock.MarkSessionContainerAvailable(tenantKey);
			// No completion signal is sent from here. The private signal (ADR rule 5) matters most to this
			// family — restart-by-credentials is deliberately unreportable through restart-status, so no
			// terminal status exists for a parent to poll — but sending it HERE only covered the calls that
			// reached the readiness wait. waitReady=false, a failed restart request, and both argument
			// refusals return without ever getting here, and each stranded the sticky worker for its whole
			// hard lifetime. WorkerOperationCompletionSignal's choke point now emits it around every exit,
			// and the heartbeat helper leases THIS work so a past-deadline wait is not read as finished.
		}
	}

	// Copies the request-relevant fields with WaitReady cleared so Phase 1's InternalExecute runs only the
	// restart request under the lock. A distinct instance (not a mutation of the caller's options) keeps
	// Phase 2's own resolve unaffected and avoids mutable-argument surprises in tests.
	private static RestartOptions BuildRequestOnlyOptions(RestartOptions source) =>
		new() {
			Environment = source.Environment,
			Login = source.Login,
			Password = source.Password,
			Uri = source.Uri,
			IsNetCore = source.IsNetCore,
			TimeOut = source.TimeOut,
			ReadyTimeout = source.ReadyTimeout,
			WaitReady = false
		};

	/// <summary>
	/// Builds the in-progress notice returned when the readiness wait exceeds the MCP response deadline.
	/// Extracted as a pure function (rather than inlined in the catch block) so its wording is directly
	/// unit-testable without racing the real response-deadline timer.
	/// </summary>
	internal static string BuildInProgressMessage(
		string targetDescription, string toolName, int waitTimeoutSeconds, string operationId,
		string environmentName) =>
		$"Restart of {targetDescription} was accepted and the restart request itself already "
		+ "succeeded; the application is still warming up (MCP response deadline reached, the "
		+ $"readiness wait continues server-side for up to {waitTimeoutSeconds}s). "
		+ BuildPollGuidance(operationId, environmentName)
		+ $" Typical warm-up is 1-10 minutes; do NOT retry {toolName}.";

	// restart-status resolves its tenant key from a REQUIRED environment name; the credentials path has
	// none. Corrected 2026-08-18, story 7 AC-00: this comment used to claim the two keys could NEVER be
	// equal because BuildCacheKey derived them from `options.Environment ?? settings.Uri`. Convergence
	// removed that guarantee — one resolved target now yields one key from either branch, so the two
	// agree whenever the target AND the credentials agree. What survives is the reason that actually
	// matters: from here we cannot tell whether these credentials belong to a registered environment, so
	// any poll target we named would be a guess. Withholding it is now a conservative choice rather than
	// a structural certainty, and it is still the right one — an agent sent into a not-found loop learns
	// nothing.
	private static string BuildPollGuidance(string operationId, string environmentName) =>
		string.IsNullOrWhiteSpace(environmentName)
			? "This restart was started by credentials, not by environment name, so restart-status "
			  + "(which is keyed by a registered environment name) cannot report it. The healthcheck "
			  + "tool only confirms liveness — it is not an authenticated readiness signal — so to "
			  + "confirm the instance is genuinely serving, use restart-by-environment-name, which waits "
			  + "for an authenticated application-layer round-trip and is pollable via restart-status."
			: $"Poll restart-status with environment-name '{environmentName}' (or operation-id "
			  + $"'{operationId}') — exit code 0 means the application answered an authenticated "
			  + "application-layer round-trip and is genuinely serving.";
}
