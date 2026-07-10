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
	private static readonly object CommandExecutionLock = McpToolExecutionLock.SyncRoot;

	private protected static object CommandExecutionSyncRoot => CommandExecutionLock;

	/// <summary>
	/// Runs <paramref name="executor"/> under the shared MCP execution lock and drains the
	/// process-wide <see cref="ConsoleLogger.LogMessages"/> buffer on exit. Use this from
	/// tools that return a typed response (and therefore cannot go through
	/// <see cref="InternalExecute(Clio.Common.Command{T}, T)"/>) so that log lines produced
	/// inside <paramref name="executor"/> do not leak into the next tool invocation's
	/// <c>execution-log-messages</c> — see DeleteSchemaTool integration trace where create-page
	/// steps surfaced inside an unrelated delete-schema response.
	/// </summary>
	private protected TResponse ExecuteWithCleanLog<TResponse>(Func<TResponse> executor) {
		lock (CommandExecutionLock) {
			try {
				return executor();
			}
			finally {
				logger.ClearMessages();
			}
		}
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
		TCommand resolvedCommand;
		try {
			// ResolveCommand resolves the env-scoped command AND enforces this options type's
			// [RequiresPackage] declarations against the per-call target environment. The gate lives
			// inside ResolveCommand so the typed-response path (tools that call it directly from inside
			// ExecuteWithCleanLog) is package-gated too. An unmet requirement surfaces as a
			// PackageRequirementException; an environment/argument failure as an
			// EnvironmentResolutionException; both become a caller-actionable failed result (exit 1)
			// below, so the command never runs when its preconditions are not satisfied.
			resolvedCommand = ResolveCommand<TCommand>(options);
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
		return InternalExecute(resolvedCommand, options);
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
	private protected TCommand ResolveCommand<TCommand>(T options) where TCommand : Command<T> {
		TCommand resolvedCommand = ResolveFromCallContainer<TCommand>(options);
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
	private TService ResolveFromCallContainer<TService>(T options) {
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
		return EnvironmentScopedCommandExecutor.UsesEnvironmentlessResolution(environmentOptions)
			? commandResolver.ResolveWithoutEnvironment<TService>(environmentOptions)
			: commandResolver.Resolve<TService>(environmentOptions);
	}


	private protected virtual CommandExecutionResult InternalExecute(Command<T> command, T options) {
		// Package requirements are NOT enforced here. [RequiresPackage] is verified against the per-call
		// target environment, which only the env-SENSITIVE generic InternalExecute<TCommand> path knows
		// about; that path runs the gate (EnforcePackageRequirements) before reaching this method. The
		// env-INsensitive injected-command path does not carry a target environment, so a package
		// requirement would be meaningless there.
		string correlationId = Guid.NewGuid().ToString("N")[..12];
		CommandExecutionResult executionResult;
		IReadOnlyList<LogMessage> messagesToForward;
		lock (CommandExecutionLock) {
			dbOperationLogContextAccessor?.ClearLastCompletedPath();
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				int exitCode = command.Execute(options);
				IReadOnlyList<LogMessage> flushedMessages = SanitizeForSerialization(
					logger.FlushAndSnapshotMessages(clearMessages: true));
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
				messagesToForward = priorLogs;
				executionResult = CommandExecutionResult.FromException(e, priorLogs, correlationId);
			}
			finally {
				logger.PreserveMessages = previousPreserveMessages;
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
}
