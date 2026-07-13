using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public abstract class BaseTool<T>(
	Command<T>? command,
	ILogger logger,
	IToolCommandResolver commandResolver = null,
	IDbOperationLogContextAccessor dbOperationLogContextAccessor = null,
	ICredentialPassthroughToolGuard passthroughGuard = null) {

	/// <summary>
	/// Rejects the current call when a credential-passthrough context is active and the invoked
	/// tool (or the invoked branch) is not supported under passthrough (FR-04, ENG-93347).
	/// Guard-only tools call this FIRST, before any Creatio-reaching work, so the caller gets the
	/// single uniform "not supported under credential passthrough" rejection instead of a
	/// confused-deputy call against a registered environment's stored credentials.
	/// </summary>
	/// <param name="toolName">The MCP tool name to name in the rejection message.</param>
	/// <param name="alternativeGuidance">The supported alternative to point the caller at.</param>
	/// <returns>
	/// <see langword="null"/> when the guard should not fire (no guard wired — e.g. stdio direct
	/// construction — or no passthrough context), so the caller proceeds normally; otherwise the
	/// typed rejection envelope (exit code 1 — an EXPECTED, caller-actionable refusal).
	/// </returns>
	private protected CommandExecutionResult RejectIfPassthroughUnsupported(string toolName,
		string alternativeGuidance) {
		if (passthroughGuard is null || !passthroughGuard.IsPassthroughActive) {
			return null;
		}
		return CommandExecutionResult.FromValidationError(
			passthroughGuard.BuildUnsupportedMessage(toolName, alternativeGuidance));
	}

	// FR-05 (ENG-93208): resolves the per-tenant execution-lock key for the given options. Runs the
	// SAME identity branch the command resolves under (credential passthrough / registry / URI) so the
	// lock keys off the exact session the command shares. Environment-less commands and a null resolver
	// fall back to the single shared key (no per-tenant session to protect).
	private protected string ResolveTenantLockKey(EnvironmentOptions options) {
		if (commandResolver is not null && options is not null
			&& !EnvironmentScopedCommandExecutor.UsesEnvironmentlessResolution(options)) {
			return commandResolver.GetTenantKey(options);
		}
		return McpToolExecutionLock.SharedFallbackKey;
	}

	// Runs body under the per-tenant execution lock (FR-05) and marks the session-container entry
	// in-use for the duration (FR-08 in-flight guard) so eviction cannot dispose the container mid-call
	// now that different tenants are no longer serialized by a single global lock. Here MarkInUse runs
	// BEFORE the container is Acquired (Acquire happens inside body → the tool's ResolveCommand); the
	// cache records a PENDING in-use reservation that Acquire applies the moment it creates the entry
	// (FIX 1, order-independent guard), so the guard holds on this typed-response path too — not only on
	// the InternalExecute path where the entry already exists when MarkInUse runs.
	private protected TResult ExecuteUnderTenantLock<TResult>(EnvironmentOptions options, Func<TResult> body) {
		string tenantKey = ResolveTenantLockKey(options);
		lock (McpToolExecutionLock.GetLock(tenantKey)) {
			McpToolExecutionLock.MarkInUse(tenantKey);
			try {
				return body();
			}
			finally {
				McpToolExecutionLock.MarkAvailable(tenantKey);
			}
		}
	}

	/// <summary>
	/// Runs <paramref name="executor"/> under the per-tenant MCP execution lock for
	/// <paramref name="options"/> and clears the flow-local capture buffer on exit. Use this from tools
	/// that return a typed response (and therefore cannot go through
	/// <see cref="InternalExecute(Clio.Common.Command{T}, T)"/>) so that log lines produced inside
	/// <paramref name="executor"/> do not leak into the next tool invocation's <c>execution-log-messages</c>.
	/// </summary>
	private protected TResponse ExecuteWithCleanLog<TResponse>(EnvironmentOptions options, Func<TResponse> executor) =>
		ExecuteUnderTenantLock(options, () => {
			// Establish a FRESH per-flow capture buffer for this scope (FIX 6, ENG-93208), matching
			// InternalExecute. Setting PreserveMessages = true resets the flow-local buffer, so log lines
			// this executor produces are isolated from the process-wide MCP-mode flag (Program.cs) and any
			// parent-flow context rather than merely being cleared on exit. Saved/restored in finally.
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				return executor();
			}
			finally {
				logger.ClearMessages();
				logger.PreserveMessages = previousPreserveMessages;
			}
		});

	/// <summary>
	/// Environment-less overload of <see cref="ExecuteWithCleanLog{TResponse}(EnvironmentOptions, Func{TResponse})"/>
	/// for callers that carry no per-tenant identity (e.g. a pre-computed result). Uses the shared
	/// fallback lock; must NOT be used to execute a per-tenant command (that would serialize tenants).
	/// </summary>
	private protected TResponse ExecuteWithCleanLog<TResponse>(Func<TResponse> executor) =>
		ExecuteWithCleanLog(null, executor);

	/// <summary>
	/// Resolves an environment-scoped command for the current MCP call and runs <paramref name="executor"/>
	/// against it, producing a typed response. This is the typed-response counterpart of
	/// <see cref="InternalExecute{TCommand}(T, Action{TCommand})"/>: it funnels the same
	/// <see cref="ResolveCommand{TCommand}"/> gate (package + Creatio version) so an unmet requirement is
	/// refused uniformly, and it maps a <see cref="CreatioVersionRequirementException"/> to a failure that
	/// carries the stable error code — typed results have no exit code, so the code travels in the message.
	/// A tool passes its own <paramref name="onFailure"/> factory because only the tool knows how to build
	/// its specific response shape.
	/// </summary>
	private protected TResponse ExecuteResolved<TCommand, TResponse>(
		T options,
		Func<TCommand, TResponse> executor,
		Func<string, TResponse> onFailure) where TCommand : Command<T> {
		return ExecuteWithCleanLog(() => {
			try {
				TCommand resolvedCommand = ResolveCommand<TCommand>(options);
				return executor(resolvedCommand);
			}
			catch (CreatioVersionRequirementException ex) {
				return onFailure($"{ex.Message} [{ex.ErrorCode}]");
			}
			catch (Exception ex) {
				return onFailure(ex.Message);
			}
		});
	}

	private protected CommandExecutionResult InternalExecute(T options) {
		if (command is null) {
			throw new InvalidOperationException(
				$"{GetType().Name} does not support direct command execution.");
		}
		return InternalExecute(command, options);
	}

	private protected CommandExecutionResult InternalExecute<TCommand>(T options,
		Action<TCommand> configureCommand = null) where TCommand : Command<T> {
		// Review #5 (ENG-93208): reserve the per-tenant lock AND the session-container in-flight guard
		// BEFORE the first Acquire (symmetric with the typed-response ExecuteUnderTenantLock path). The key
		// is computed up-front via ResolveTenantLockKey — the SAME key ResolveCommand's Acquire caches
		// under (GetTenantKey and Resolve share ResolveSettingsAndKey / BuildPassthroughCacheKey) — so the
		// pending in-use reservation is drained into the container the instant Acquire creates it. Otherwise
		// the Acquire→MarkInUse window spans the package + Creatio-version HTTP gates with InUseCount == 0,
		// during which a concurrent different-tenant Acquire could LRU-evict and dispose the container
		// mid-resolve. This costs one extra settings.Fill (the M2 single-fill optimization is bypassed on
		// this path) — an accepted trade for closing the eviction window.
		string tenantKey = options is EnvironmentOptions environmentOptions
			? ResolveTenantLockKey(environmentOptions)
			: McpToolExecutionLock.SharedFallbackKey;
		CommandExecutionResult result;
		IReadOnlyList<LogMessage> messagesToForward = null;
		string correlationId = null;
		lock (McpToolExecutionLock.GetLock(tenantKey)) {
			McpToolExecutionLock.MarkInUse(tenantKey);
			try {
				TCommand resolvedCommand;
				try {
					// ResolveCommand resolves the env-scoped command AND enforces this options type's
					// [RequiresPackage] and [RequiresCreatioVersion] declarations against the per-call target
					// environment (Acquire happens inside, now guarded by the reservation above). An unmet
					// package requirement surfaces as a PackageRequirementException (exit 1), an
					// unmet/undeterminable version as a CreatioVersionRequirementException (exit code 78,
					// mirroring the CLI dispatch gate), an environment/argument failure as an
					// EnvironmentResolutionException (exit 1); each becomes a caller-actionable failed result.
					resolvedCommand = ResolveCommand<TCommand>(options);
				} catch (PackageRequirementException ex) {
					// Surface the actionable install hint verbatim, without the exception-chain decoration.
					return CommandExecutionResult.FromError(ex.Message);
				} catch (CreatioVersionRequirementException ex) {
					// Expected, caller-actionable refusal (unmet/undeterminable version) → distinct exit code 78.
					return CommandExecutionResult.FromCreatioVersionRequirementError(ex);
				} catch (EnvironmentResolutionException e) {
					// Expected, caller-actionable environment/argument failure → exit code 1.
					return CommandExecutionResult.FromResolverError(e);
				} catch (Exception e) {
					// Unexpected DI/bootstrap/wiring failure → exit code -1, so a real bug is not
					// misreported as a routine validation error and stays diagnosable.
					return CommandExecutionResult.FromException(e);
				}

				configureCommand?.Invoke(resolvedCommand);
				(result, messagesToForward, correlationId) = RunCommandUnderHeldLock(resolvedCommand, options, tenantKey);
			}
			finally {
				McpToolExecutionLock.MarkAvailable(tenantKey);
			}
		}
		// Forward log notifications OUTSIDE the execution lock to avoid blocking other tool invocations on
		// stdio I/O performed by SendNotificationAsync.
		McpLogNotifier.ForwardMessages(messagesToForward, correlationId);
		return result;
	}

	/// <summary>
	/// Resolves an environment-scoped command instance for the current MCP call and enforces the
	/// options type's package and Creatio version requirements before returning it.
	/// </summary>
	/// <remarks>
	/// Both BaseTool execution paths funnel through here — the <see cref="InternalExecute{TCommand}"/>
	/// path and the typed-response path that calls this method directly from inside
	/// <see cref="ExecuteWithCleanLog"/> — so every BaseTool tool is package- and version-gated
	/// uniformly, regardless of its return shape. The command is resolved FIRST (so an
	/// unknown-environment failure surfaces as <see cref="EnvironmentResolutionException"/>), THEN the
	/// Creatio version requirement is enforced, THEN the package requirement — the same relative order
	/// as the CLI dispatch gate (feature-toggle → creatio-version → package), so a command declaring
	/// both attributes refuses with the same error on both surfaces. A
	/// <see cref="PackageRequirementException"/> / <see cref="CreatioVersionRequirementException"/>
	/// (unmet requirement) and any other verification failure propagate to the caller.
	/// </remarks>
	private protected TCommand ResolveCommand<TCommand>(T options) where TCommand : Command<T> =>
		ResolveCommand<TCommand>(options, out _);

	/// <summary>
	/// Overload of <see cref="ResolveCommand{TCommand}(T)"/> that also outputs the cache key the command's
	/// container was acquired under, so the execution path locks / marks-in-use on that exact key (M2).
	/// </summary>
	private protected TCommand ResolveCommand<TCommand>(T options, out string tenantKey) where TCommand : Command<T> {
		TCommand resolvedCommand = ResolveFromCallContainer<TCommand>(options, out tenantKey);
		EnforceCreatioVersionRequirements(options);
		EnforcePackageRequirements(options);
		return resolvedCommand;
	}

	// Enforces this options type's [RequiresPackage] declarations against the per-call target
	// environment. This runs for BOTH BaseTool execution paths because it lives in ResolveCommand,
	// which both the env-SENSITIVE InternalExecute<TCommand> path and the typed-response/
	// ExecuteWithCleanLog path call. [RequiresPackage] is verified against the per-call target
	// environment; the checker is resolved from the SAME environment-scoped container the command
	// came from (see ResolveFromCallContainer), so it queries the correct Creatio instance.
	// A cheap static pre-check runs first: options types without [RequiresPackage] skip resolution
	// entirely, so non-gated tools stay zero-cost and never force an environment. On an unmet
	// requirement this throws PackageRequirementException; any other verification failure
	// (e.g. GetPackages/HTTP/auth failure, unknown environment) propagates as-is.
	private void EnforcePackageRequirements(T options) {
		if (!RequiresPackageAttribute.IsDefinedOn(typeof(T))) {
			return;
		}
		IRequiredPackageChecker checker = ResolveFromCallContainer<IRequiredPackageChecker>(options);
		checker.EnsureRequirements(options);
	}

	// Enforces this options type's [RequiresCreatioVersion] declaration against the per-call target
	// environment. It runs on the ResolveCommand path (before the package gate, mirroring the CLI
	// dispatch order), so BOTH BaseTool execution paths — InternalExecute<TCommand> and the
	// typed-response/ExecuteWithCleanLog path — are version-gated uniformly. Cheap static pre-check first: options types without
	// [RequiresCreatioVersion] skip resolution entirely, so non-gated tools stay zero-cost and never
	// force an environment round-trip — intentionally stricter than the package gate, because the
	// version check exists solely to add that round-trip. An unmet/undeterminable version propagates as
	// CreatioVersionRequirementException (fail-closed; mapped to the distinct exit code 78 by
	// InternalExecute<TCommand> and to a typed failure by each typed-response tool's own catch); a
	// malformed [RequiresCreatioVersion] (e.g. on a non-bool property) propagates as
	// InvalidOperationException so a developer error stays distinguishable from a version refusal.
	private void EnforceCreatioVersionRequirements(T options) {
		if (!RequiresCreatioVersionAttribute.IsDefinedOn(typeof(T))) {
			return;
		}
		ICreatioVersionChecker checker = ResolveFromCallContainer<ICreatioVersionChecker>(options);
		checker.EnsureRequirements(options);
	}

	// Resolves an arbitrary service from the per-call, environment-scoped container using the SAME
	// switch logic the command itself is resolved with. Sharing this with ResolveCommand guarantees the
	// gate's checker is bound to the exact container the command runs in (cached by env key in
	// ToolCommandResolver), not the MCP startup/bootstrap container. The IToolCommandResolver methods are
	// unconstrained, so this works for IRequiredPackageChecker as well as Command<T>.
	private TService ResolveFromCallContainer<TService>(T options) =>
		ResolveFromCallContainer<TService>(options, out _);

	private TService ResolveFromCallContainer<TService>(T options, out string tenantKey) {
		if (options is not EnvironmentOptions) {
			throw new InvalidOperationException(
				$"{GetType().Name} can only resolve commands for options derived from EnvironmentOptions.");
		}

		if (commandResolver is null) {
			throw new InvalidOperationException(
				$"{GetType().Name} does not support environment-based command resolution.");
		}

		if (options is not EnvironmentOptions environmentOptions) {
			throw new InvalidOperationException(
				$"Unsupported options type: {options.GetType().Name}");
		}

		// The env-less special-case decision is shared with the generic clio-run executor
		// (EnvironmentScopedCommandExecutor.UsesEnvironmentlessResolution), so the four current
		// flat tools and the generic executor resolve identically — no behavioral drift.
		// Optional environment properties are intentionally not consulted for the env-less types.
		if (EnvironmentScopedCommandExecutor.UsesEnvironmentlessResolution(environmentOptions)) {
			// Env-less commands build a fresh container per call (no session cache, no per-tenant
			// identity), so the lock keys off the single shared fallback — matching ResolveTenantLockKey.
			tenantKey = McpToolExecutionLock.SharedFallbackKey;
			return commandResolver.ResolveWithoutEnvironment<TService>(environmentOptions);
		}
		// M2: read the key the resolve just cached under (flow-local) instead of recomputing it via
		// GetTenantKey (a second settings.Fill). Captured immediately, before any later checker resolve
		// on this flow overwrites it. A test double / mock that does not track the key yields null →
		// normalized to the shared fallback by GetLock (the lock key is not asserted in unit tests).
		TService resolved = commandResolver.Resolve<TService>(environmentOptions);
		tenantKey = commandResolver.LastResolvedTenantKey ?? McpToolExecutionLock.SharedFallbackKey;
		return resolved;
	}


	private protected virtual CommandExecutionResult InternalExecute(Command<T> command, T options) {
		// Package requirements are NOT enforced here. [RequiresPackage] is verified against the per-call
		// target environment, which only the env-SENSITIVE generic InternalExecute<TCommand> path knows
		// about; that path runs the gate (EnforcePackageRequirements) before reaching this method. The
		// env-INsensitive injected-command path does not carry a target environment, so a package
		// requirement would be meaningless there.
		// The env-SENSITIVE path (InternalExecute<TCommand>) threads the key from ResolveCommand into
		// ExecuteLocked directly (M2); this injected/direct entry computes it here (no prior resolve).
		string tenantKey = options is EnvironmentOptions environmentOptions
			? ResolveTenantLockKey(environmentOptions)
			: McpToolExecutionLock.SharedFallbackKey;
		return ExecuteLocked(command, options, tenantKey);
	}

	// Runs command.Execute under the per-tenant execution lock for tenantKey (FR-05), with the
	// session-container in-flight guard marked for the call duration (FR-08). Used by the injected/direct
	// path (InternalExecute(Command<T>, T)), which has no prior Acquire so it takes the lock and marks
	// in-use here. The generic InternalExecute<TCommand> path already holds the lock and the marker (it
	// reserves them BEFORE Acquire, review #5), so it calls RunCommandUnderHeldLock directly.
	private CommandExecutionResult ExecuteLocked(Command<T> command, T options, string tenantKey) {
		CommandExecutionResult result;
		IReadOnlyList<LogMessage> messagesToForward;
		string correlationId;
		lock (McpToolExecutionLock.GetLock(tenantKey)) {
			// FR-08 in-flight guard: keep the session container from being evicted/disposed mid-call now
			// that a different tenant's Acquire can trigger eviction while this call runs.
			McpToolExecutionLock.MarkInUse(tenantKey);
			try {
				(result, messagesToForward, correlationId) = RunCommandUnderHeldLock(command, options, tenantKey);
			}
			finally {
				McpToolExecutionLock.MarkAvailable(tenantKey);
			}
		}
		// Forward log notifications OUTSIDE the execution lock to avoid blocking other
		// tool invocations on stdio I/O performed by SendNotificationAsync.
		McpLogNotifier.ForwardMessages(messagesToForward, correlationId);
		return result;
	}

	// Executes command.Execute assuming the caller ALREADY holds the per-tenant execution lock for
	// tenantKey and has marked the in-flight guard (FR-05/FR-08). Returns the execution result plus the
	// messages to forward AFTER the lock is released (the caller performs the stdio-I/O forwarding outside
	// the lock). Never takes the lock or the in-use markers itself, so it composes with both the
	// reserve-before-Acquire generic path (review #5) and the injected/direct ExecuteLocked entry.
	private (CommandExecutionResult Result, IReadOnlyList<LogMessage> Messages, string CorrelationId)
		RunCommandUnderHeldLock(Command<T> command, T options, string tenantKey) {
		string correlationId = Guid.NewGuid().ToString("N")[..12];
		dbOperationLogContextAccessor?.ClearLastCompletedPath();
		bool previousPreserveMessages = logger.PreserveMessages;
		logger.PreserveMessages = true;
		try {
			int exitCode = command.Execute(options);
			IReadOnlyList<LogMessage> flushedMessages = McpPassthroughRedaction.SanitizeAndRedact(
				logger.FlushAndSnapshotMessages(clearMessages: true), tenantKey);
			CommandExecutionResult executionResult = new(
				exitCode,
				[.. flushedMessages],
				dbOperationLogContextAccessor?.LastCompletedPath,
				CorrelationId: correlationId);
			return (executionResult, flushedMessages, correlationId);
		}
		catch (Exception e) {
			List<LogMessage> priorLogs = [.. McpPassthroughRedaction.SanitizeAndRedact(
				logger.FlushAndSnapshotMessages(clearMessages: true), tenantKey)];
			// FR-11 (ENG-93208): scrub the exception chain ONLY on a passthrough request (same tenant-key
			// signal RedactForPassthrough uses). The trusted stdio / -e path keeps full-fidelity -1 text.
			CommandExecutionResult executionResult = CommandExecutionResult.FromException(
				e, priorLogs, correlationId, redactSensitive: McpPassthroughRedaction.IsPassthroughKey(tenantKey));
			return (executionResult, priorLogs, correlationId);
		}
		finally {
			logger.PreserveMessages = previousPreserveMessages;
		}
	}

}
