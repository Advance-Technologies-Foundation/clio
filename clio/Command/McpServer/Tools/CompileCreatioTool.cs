using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for full Creatio compilation and package-only compilation.
/// </summary>
[McpServerToolType]
public sealed class CompileCreatioTool(
	ILogger logger,
	IToolCommandResolver commandResolver,
	ICompileOperationRegistry registry)
{
	/// <summary>
	/// Stable MCP tool name for compilation operations.
	/// </summary>
	internal const string CompileCreatioToolName = "compile-creatio";

	/// <summary>
	/// Test seam overriding the MCP response deadline passed to
	/// <see cref="McpProgressHeartbeat.RunWithProgressAndDeadlineAsync{TResult}(global::ModelContextProtocol.Server.McpServer, global::ModelContextProtocol.Protocol.ProgressToken?, string, System.Func{TResult}, System.TimeSpan?, CancellationToken, System.TimeSpan?)"/>.
	/// <see langword="null"/> in production (the default <see cref="McpProgressHeartbeat.DefaultResponseDeadline"/> ~150 s applies);
	/// unit tests set a tiny value to deterministically exercise the deadline-exceeded in-progress branch
	/// without racing the real ceiling.
	/// </summary>
	internal TimeSpan? ResponseDeadlineOverride { get; set; }

	/// <summary>
	/// Compiles Creatio fully or rebuilds a single package for a registered environment.
	/// </summary>
	[McpServerTool(Name = CompileCreatioToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Long-running, may take several minutes; recompiles a registered Creatio environment and forces a runtime reload. Omit `package-name` to run a full compilation (`clio cc -e ENV_NAME --all`). Provide `package-name` to compile only one package. Call only when: (1) C# schemas were added or modified, (2) `set-fsm-mode` has just been toggled, or (3) the runtime reports a missing-in-runtime/schema-not-found error. Do NOT call after `create-app`, `update-page`, `sync-pages`, `update-entity-schema`, `create-page`, or any Freedom UI page-body edit — those changes are AMD modules applied at runtime and DDL is handled by `update-entity-schema`. Long-running: streams notifications/progress while compiling. If the MCP response deadline is reached first, returns exit-code 0 with an in-progress note carrying an operation-id — the compile is still running server-side; do NOT retry, poll compile-status instead.")]
	public async Task<CommandExecutionResult> CompileCreatio(
		[Description("Compilation parameters")] [Required] CompileCreatioArgs args,
		global::ModelContextProtocol.Server.McpServer server = null,
		RequestContext<CallToolRequestParams> requestContext = null,
		CancellationToken cancellationToken = default)
	{
		if (!string.IsNullOrWhiteSpace(args.PackageName) && args.PackageName.Contains(',', StringComparison.Ordinal))
		{
			return new CommandExecutionResult(1, [
				new ErrorMessage("`package-name` must contain exactly one package name. Comma-separated package lists are not supported by `compile-creatio`.")
			]);
		}

		string packageName = string.IsNullOrWhiteSpace(args.PackageName) ? null : args.PackageName.Trim();
		string tenantKey = commandResolver.GetTenantKey(new EnvironmentOptions { Environment = args.EnvironmentName });

		// Compile<->compile mutual exclusion via a NARROW reservation, not the broad per-tenant execution
		// monitor (review Blocker, ENG-91315). The Creatio core itself serializes compilation and rejects a
		// second concurrent compile, so a same-tenant compile that arrives while one is running fails fast
		// here — matching that reject semantics — instead of queueing on a monitor that would also block
		// every unrelated same-tenant tool for the multi-minute (past-deadline detached) compile duration.
		// Reserved BEFORE registry.Begin so a rejected duplicate creates no tracked record (also curbs the
		// unbounded-record growth a bogus-env loop could cause).
		if (!McpToolExecutionLock.TryReserveCompile(tenantKey))
		{
			return new CommandExecutionResult(1, [
				new ErrorMessage(CompileAlreadyInProgressMessage(args.EnvironmentName))
			]);
		}

		CompileOperationRecord operation = registry.Begin(tenantKey, args.EnvironmentName, packageName);

		try
		{
			return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync(
				server,
				requestContext?.Params?.ProgressToken,
				CompileCreatioToolName,
				() => {
					// Resolution (Resolve<TCommand>) runs OUTSIDE Execute's own try/catch, so an unregistered
					// or otherwise unresolvable environment throws here rather than returning a result. Catch
					// it explicitly so registry.Finish always runs — otherwise the tracked operation would be
					// stuck Running forever for the single most common failure (a bad environment name).
					CommandExecutionResult result;
					try {
						result = packageName is null
							? ExecuteFullCompile(args.EnvironmentName)
							: ExecutePackageCompile(args.EnvironmentName, packageName);
					} catch (EnvironmentResolutionException exception) {
						result = CommandExecutionResult.FromResolverError(exception);
					} catch (Exception exception) {
						// FR-11 (ENG-93208): this passthrough-catch bypasses BaseTool's own exception filter, so
						// scrub the exception chain here too on a credential-passthrough request (same tenant-key
						// signal). Otherwise the raw text would surface unredacted and then be preserved in the
						// compile-status message tail. The trusted stdio / -e path stays full-fidelity.
						result = CommandExecutionResult.FromException(
							exception, redactSensitive: McpPassthroughRedaction.IsPassthroughKey(tenantKey));
					} finally {
						// Release the reservation where the ACTUAL compile work ends. This finally is inside the
						// heartbeat work delegate, so it runs on the (possibly detached, past-deadline)
						// continuation — spanning the real compile duration — and covers a resolution throw too
						// (which happens before Execute's own try/finally is ever entered).
						McpToolExecutionLock.ReleaseCompile(tenantKey);
					}
					registry.Finish(operation.OperationId, result.ExitCode, [.. result.Output]);
					return result;
				},
				deadline: ResponseDeadlineOverride,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		catch (McpResponseDeadlineExceededException)
		{
			return CommandExecutionResult.FromInfo(BuildInProgressMessage(args.EnvironmentName, operation.OperationId));
		}
	}

	/// <summary>
	/// Builds the in-progress notice returned when compilation exceeds the MCP response deadline.
	/// Extracted as a pure function (rather than inlined in the catch block) so its wording is directly
	/// unit-testable without racing the real response-deadline timer.
	/// </summary>
	internal static string BuildInProgressMessage(string environmentName, string operationId) =>
		$"Compilation for '{environmentName}' (operation-id '{operationId}') was accepted "
		+ "and is still running server-side (MCP response deadline reached). Poll compile-status with the "
		+ "same environment-name (or this operation-id) for its current state — do NOT retry compile-creatio: "
		+ "the Creatio core serializes compilation, so a second concurrent compile for the same environment is "
		+ "rejected, not queued. Do NOT issue other environment-bound calls (e.g. restart-by-environment-name) "
		+ "for this environment until compile-status reports completion. Typical full compilation is 3-15 minutes.";

	/// <summary>
	/// Message returned when a same-tenant compile is requested while one is already in flight. The compile
	/// path takes a narrow compile-scoped reservation (not the broad per-tenant monitor), so a duplicate is
	/// failed fast here rather than queued — matching the Creatio core's own reject-on-concurrent-compile
	/// behavior. Extracted for direct unit testing.
	/// </summary>
	internal static string CompileAlreadyInProgressMessage(string environmentName) =>
		$"A compilation is already in progress for '{environmentName}'. The Creatio core serializes "
		+ "compilation (a second concurrent compile is rejected), so this request was not started. Poll "
		+ "compile-status for the running operation and wait for it to finish before compiling again.";

	private CommandExecutionResult ExecuteFullCompile(string environmentName)
	{
		CompileConfigurationOptions options = new()
		{
			Environment = environmentName,
			All = true
		};
		CompileConfigurationCommand command = commandResolver.Resolve<CompileConfigurationCommand>(options);
		return Execute(command, options);
	}

	private CommandExecutionResult ExecutePackageCompile(string environmentName, string packageName)
	{
		CompilePackageOptions options = new()
		{
			Environment = environmentName,
			PackageName = packageName
		};
		CompilePackageCommand command = commandResolver.Resolve<CompilePackageCommand>(options);
		return Execute(command, options);
	}

	private CommandExecutionResult Execute<TOptions>(Command<TOptions> command, TOptions options)
	{
		int exitCode = -1;
		string tenantKey = options is EnvironmentOptions environmentOptions
			? commandResolver.GetTenantKey(environmentOptions)
			: McpToolExecutionLock.SharedFallbackKey;
		// Compile<->compile exclusion is handled by the narrow TryReserveCompile reservation in the caller,
		// NOT by the broad per-tenant execution monitor: the multi-minute compile (detached past the MCP
		// response deadline) must not serialize unrelated same-tenant tools, and the core only forbids a
		// concurrent COMPILE (review Blocker, ENG-91315). So this path takes no GetLock. It still pins the
		// session container in-use for the compile duration (FR-08) so a concurrent different-tenant Acquire
		// cannot LRU-evict and dispose the resolved command's client mid-compile. Because no GetLock was
		// taken, the pin is released via the session-container-only path (MarkSessionContainerAvailable),
		// never MarkAvailable — the latter decrements the GetLock-owned in-use count and would
		// stray-decrement an unrelated holder (Finding 2).
		McpToolExecutionLock.MarkInUse(tenantKey);
		// CompileCreatioTool builds its result from logger.LogMessages, which ConsoleLogger only
		// populates while PreserveMessages is true. Set/restore it locally (like BaseTool,
		// SchemaSyncTool, EntitySchemaTool, AddItemModelTool) so compilation output is captured
		// regardless of transport: the stdio path sets the flag process-wide, the HTTP transport
		// does not, so relying on the global flag returns empty output over HTTP.
		bool previousPreserveMessages = logger.PreserveMessages;
		logger.PreserveMessages = true;
		try
		{
			exitCode = command.Execute(options);
			Thread.Sleep(500);
			// FR-11 (review): redact the self-captured snapshot on a passthrough request before it
			// crosses the MCP boundary — the main path does this via RunCommandUnderHeldLock, this
			// self-managed-capture tool must do it too. No-op off passthrough.
			CommandExecutionResult result = new(exitCode,
				[.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.LogMessages], tenantKey)]);
			logger.ClearMessages();
			return result;
		}
		catch (Exception exception)
		{
			List<LogMessage> logMessages = [
				.. McpPassthroughRedaction.SanitizeAndRedact([.. logger.LogMessages], tenantKey),
				new ErrorMessage(SensitiveErrorTextRedactor.Redact(exception.Message))];
			CommandExecutionResult result = new(1, logMessages);
			logger.ClearMessages();
			return result;
		}
		finally
		{
			logger.PreserveMessages = previousPreserveMessages;
			McpToolExecutionLock.MarkSessionContainerAvailable(tenantKey);
		}
	}
}

/// <summary>
/// MCP arguments for Creatio compilation operations.
/// </summary>
public sealed record CompileCreatioArgs(
	[property: JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[Description("Optional package name. When omitted, the tool performs a full compilation.")]
	string? PackageName);
