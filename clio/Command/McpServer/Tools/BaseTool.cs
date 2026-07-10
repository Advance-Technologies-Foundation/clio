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
	IDbOperationLogContextAccessor dbOperationLogContextAccessor = null) {

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

	/// <summary>
	/// Returns the per-tenant execution lock for <paramref name="options"/>. Tools that lock directly
	/// (rather than through <see cref="ExecuteWithCleanLog{TResponse}(EnvironmentOptions, Func{TResponse})"/>)
	/// acquire it so DIFFERENT tenants no longer serialize against each other.
	/// </summary>
	private protected object GetTenantExecutionLock(EnvironmentOptions options) =>
		McpToolExecutionLock.GetLock(ResolveTenantLockKey(options));

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

	private protected CommandExecutionResult InternalExecute(T options) {
		if (command is null) {
			throw new InvalidOperationException(
				$"{GetType().Name} does not support direct command execution.");
		}
		return InternalExecute(command, options);
	}

	private protected CommandExecutionResult InternalExecute<TCommand>(T options,
		Action<TCommand> configureCommand = null) where TCommand : Command<T> {
		TCommand resolvedCommand;
		// M2 (ENG-93208): capture the SAME cache key the container was acquired under so the execution
		// lock / in-flight guard key off it directly — no second settings.Fill via GetTenantKey.
		string tenantKey;
		try {
			// ResolveCommand resolves the env-scoped command AND enforces this options type's
			// [RequiresPackage] declarations against the per-call target environment. The gate lives
			// inside ResolveCommand so the typed-response path (tools that call it directly from inside
			// ExecuteWithCleanLog) is package-gated too. An unmet requirement surfaces as a
			// PackageRequirementException; an environment/argument failure as an
			// EnvironmentResolutionException; both become a caller-actionable failed result (exit 1)
			// below, so the command never runs when its preconditions are not satisfied.
			resolvedCommand = ResolveCommand<TCommand>(options, out tenantKey);
		} catch (PackageRequirementException ex) {
			// Surface the actionable install hint verbatim, without the exception-chain decoration.
			return CommandExecutionResult.FromError(ex.Message);
		} catch (EnvironmentResolutionException e) {
			// Expected, caller-actionable environment/argument failure → exit code 1.
			return CommandExecutionResult.FromResolverError(e);
		} catch (Exception e) {
			// Unexpected DI/bootstrap/wiring failure → exit code -1, so a real bug is not
			// misreported as a routine validation error and stays diagnosable.
			return CommandExecutionResult.FromException(e);
		}

		// Creatio-version gate for environment-bound commands. It runs AFTER ResolveCommand has already
		// resolved the env-scoped command and enforced the package gate (see ResolveCommand), so the
		// environment is validated by the time we get here and the version checker never hits an
		// unknown-env. Like the package gate it runs only in the env-SENSITIVE path, resolves its checker
		// from the SAME environment-scoped container the command came from, and runs before the execution
		// lock so a refusal does not hold it. Package is NOT re-enforced here — ResolveCommand already did
		// it for BOTH execution paths, and adding it again would double-gate.
		CommandExecutionResult versionRequirementFailure = EnforceCreatioVersionRequirements(options);
		if (versionRequirementFailure is not null) {
			return versionRequirementFailure;
		}

		configureCommand?.Invoke(resolvedCommand);
		// Use the key threaded from ResolveCommand (M2) rather than recomputing it via ResolveTenantLockKey.
		return ExecuteLocked(resolvedCommand, options, tenantKey);
	}

	/// <summary>
	/// Resolves an environment-scoped command instance for the current MCP call and enforces the
	/// options type's package requirements before returning it.
	/// </summary>
	/// <remarks>
	/// Both BaseTool execution paths funnel through here — the <see cref="InternalExecute{TCommand}"/>
	/// path and the typed-response path that calls this method directly from inside
	/// <see cref="ExecuteWithCleanLog"/> — so every BaseTool tool is package-gated uniformly,
	/// regardless of its return shape. The command is resolved FIRST (so an unknown-environment failure
	/// surfaces as <see cref="EnvironmentResolutionException"/>), THEN the requirement is enforced. A
	/// <see cref="PackageRequirementException"/> (unmet requirement) and any other verification failure
	/// both propagate to the caller.
	/// </remarks>
	private protected TCommand ResolveCommand<TCommand>(T options) where TCommand : Command<T> =>
		ResolveCommand<TCommand>(options, out _);

	/// <summary>
	/// Overload of <see cref="ResolveCommand{TCommand}(T)"/> that also outputs the cache key the command's
	/// container was acquired under, so the execution path locks / marks-in-use on that exact key (M2).
	/// </summary>
	private protected TCommand ResolveCommand<TCommand>(T options, out string tenantKey) where TCommand : Command<T> {
		TCommand resolvedCommand = ResolveFromCallContainer<TCommand>(options, out tenantKey);
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

	// Returns a failed result (exit code 78) when the per-call environment does not satisfy this options
	// type's [RequiresCreatioVersion] declaration (too old, or undeterminable → fail-closed), or null when
	// there is nothing to enforce / the requirement is met. Cheap static pre-check first: options types
	// without [RequiresCreatioVersion] skip resolution entirely, so non-gated tools stay zero-cost and
	// never force an environment round-trip — intentionally stricter than the package gate, because the
	// version check exists solely to add that round-trip. Mirrors the CLI gate
	// (Program.TryGetCreatioVersionRequirementError): only CreatioVersionRequirementException is mapped to
	// the version exit code; a malformed [RequiresCreatioVersion] (e.g. on a non-bool property) surfaces as
	// an InvalidOperationException that flows to the catch-all (exit code -1), NOT into the version-gate
	// failure — a developer error must stay distinguishable from a version refusal. Nothing escapes here.
	private CommandExecutionResult EnforceCreatioVersionRequirements(T options) {
		if (!RequiresCreatioVersionAttribute.IsDefinedOn(typeof(T))) {
			return null;
		}
		try {
			ICreatioVersionChecker checker = ResolveFromCallContainer<ICreatioVersionChecker>(options);
			checker.EnsureRequirements(options);
			return null;
		}
		catch (CreatioVersionRequirementException ex) {
			// Expected, caller-actionable refusal (unmet/undeterminable version) → distinct exit code 78.
			return CommandExecutionResult.FromCreatioVersionRequirementError(ex);
		}
		catch (Exception ex) {
			// Unexpected failure while verifying the version requirement (a malformed attribute, or a
			// non-soft-degrading checker error) → exit code -1, never collapsed into the version exit code.
			return CommandExecutionResult.FromException(ex);
		}
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
	// session-container in-flight guard marked for the call duration (FR-08). tenantKey is supplied by
	// the caller — the container-resolution key from ResolveCommand on the env-sensitive path (M2), or
	// the computed key on the injected/direct path — so the lock keys off the exact shared session.
	private CommandExecutionResult ExecuteLocked(Command<T> command, T options, string tenantKey) {
		string correlationId = Guid.NewGuid().ToString("N")[..12];
		CommandExecutionResult executionResult;
		IReadOnlyList<LogMessage> messagesToForward;
		lock (McpToolExecutionLock.GetLock(tenantKey)) {
			// FR-08 in-flight guard: keep the session container from being evicted/disposed mid-call now
			// that a different tenant's Acquire can trigger eviction while this call runs.
			McpToolExecutionLock.MarkInUse(tenantKey);
			dbOperationLogContextAccessor?.ClearLastCompletedPath();
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				int exitCode = command.Execute(options);
				IReadOnlyList<LogMessage> flushedMessages = RedactForPassthrough(
					SanitizeForSerialization(logger.FlushAndSnapshotMessages(clearMessages: true)), tenantKey);
				messagesToForward = flushedMessages;
				executionResult = new CommandExecutionResult(
					exitCode,
					[.. flushedMessages],
					dbOperationLogContextAccessor?.LastCompletedPath,
					CorrelationId: correlationId);
			}
			catch (Exception e) {
				List<LogMessage> priorLogs = [.. SanitizeForSerialization(
					logger.FlushAndSnapshotMessages(clearMessages: true))];
				RedactForPassthrough(priorLogs, tenantKey);
				messagesToForward = priorLogs;
				// FR-11 (ENG-93208): scrub the exception chain ONLY on a passthrough request (same tenant-key
				// signal RedactForPassthrough uses). The trusted stdio / -e path keeps full-fidelity -1 text.
				executionResult = CommandExecutionResult.FromException(
					e, priorLogs, correlationId, redactSensitive: IsPassthroughKey(tenantKey));
			}
			finally {
				logger.PreserveMessages = previousPreserveMessages;
				McpToolExecutionLock.MarkAvailable(tenantKey);
			}
		}
		// Forward log notifications OUTSIDE the execution lock to avoid blocking other
		// tool invocations on stdio I/O performed by SendNotificationAsync.
		McpLogNotifier.ForwardMessages(messagesToForward, correlationId);
		return executionResult;
	}

	// The MCP SDK serializes the returned CommandExecutionResult with System.Text.Json, which walks
	// LogMessage.Value (typed object). A TableMessage carries a raw ConsoleTable whose object graph
	// reaches System.Text.Encoding.Preamble (a ReadOnlySpan<byte> ref struct) — System.Text.Json throws
	// on that, and the SDK turns the throw into IsError=true (e.g. experimental list-features, ENG-92149).
	// Project every non-string Value to its rendered string form (the same text the console writes via
	// table.ToString()) before it enters the serialized envelope. This is general — any LogMessage with a
	// non-serializable Value is made safe — and console rendering is untouched because the console reads
	// from the live ConsoleLogger buffer, not this detached MCP snapshot.
	private static IReadOnlyList<LogMessage> SanitizeForSerialization(IReadOnlyList<LogMessage> messages) {
		foreach (LogMessage message in messages.Where(message => message.Value is not (null or string))) {
			message.Value = message.Value.ToString();
		}
		return messages;
	}

	// FIX 2 (ENG-93208, FR-11): scrub each command log-message value before it crosses the MCP boundary,
	// but ONLY on a credential-passthrough request. On the public multi-tenant passthrough edge the
	// returned CommandExecutionResult AND the forwarded log notifications are copied into the model/host
	// transcript (often a third-party LLM), and a command log line routinely carries the target URI/host
	// (or a Bearer/cookie/password value on a failure). The trusted stdio / -e path is NOT redacted so it
	// keeps full-fidelity logs (no diagnosability regression). The passthrough signal is the resolved
	// tenant key: only a passthrough context produces a key with ToolCommandResolver.PassthroughKeyPrefix
	// (env-sensitive path threads it from the resolve; the injected/direct path only ever holds the shared
	// fallback or a registry/URI key). Mirrors SanitizeForSerialization's per-message in-place walk and
	// runs AFTER it, so a table-derived value already stringified is scrubbed too. The local
	// db-operation log FILE is intentionally left at full fidelity — redaction hits only this detached,
	// MCP-crossing snapshot, never the console or file sink.
	private static IReadOnlyList<LogMessage> RedactForPassthrough(IReadOnlyList<LogMessage> messages, string tenantKey) {
		if (!IsPassthroughKey(tenantKey)) {
			return messages;
		}
		foreach (LogMessage message in messages) {
			if (message.Value is string text) {
				message.Value = SensitiveErrorTextRedactor.Redact(text);
			}
		}
		return messages;
	}

	private static bool IsPassthroughKey(string tenantKey) =>
		tenantKey is not null
		&& tenantKey.StartsWith(ToolCommandResolver.PassthroughKeyPrefix, StringComparison.OrdinalIgnoreCase);
}
