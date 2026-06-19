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
			resolvedCommand = ResolveCommand<TCommand>(options);
		} catch (Exception e) {
			return CommandExecutionResult.FromException(e);
		}

		// Package-requirement gate for environment-bound commands. This runs in the env-SENSITIVE path
		// only, because [RequiresPackage] is verified against the per-call target environment. The checker
		// is resolved from the SAME environment-scoped container the command came from (see
		// ResolveFromCallContainer), so it queries the correct Creatio instance. Run before the execution
		// lock so a refusal does not hold it.
		CommandExecutionResult requirementFailure = EnforcePackageRequirements(options);
		if (requirementFailure is not null) {
			return requirementFailure;
		}

		configureCommand?.Invoke(resolvedCommand);
		return InternalExecute(resolvedCommand, options);
	}

	// Returns a failed result when the per-call environment does not satisfy this options type's
	// [RequiresPackage] declarations, or null when there is nothing to enforce / the requirements are met.
	// Cheap static pre-check first: options types without [RequiresPackage] skip resolution entirely,
	// so non-gated tools stay zero-cost and never force an environment. Nothing escapes this method.
	private CommandExecutionResult EnforcePackageRequirements(T options) {
		if (!RequiresPackageAttribute.IsDefinedOn(typeof(T))) {
			return null;
		}
		try {
			IRequiredPackageChecker checker = ResolveFromCallContainer<IRequiredPackageChecker>(options);
			checker.EnsureRequirements(options);
			return null;
		}
		catch (PackageRequirementException ex) {
			return CommandExecutionResult.FromError(ex.Message);
		}
		catch (Exception ex) {
			return CommandExecutionResult.FromError($"Could not verify package requirements: {ex.Message}");
		}
	}

	private protected TCommand ResolveCommand<TCommand>(T options) where TCommand : Command<T> =>
		ResolveFromCallContainer<TCommand>(options);

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
				IReadOnlyList<LogMessage> flushedMessages = logger.FlushAndSnapshotMessages(clearMessages: true);
				messagesToForward = flushedMessages;
				executionResult = new CommandExecutionResult(
					exitCode,
					[.. flushedMessages],
					dbOperationLogContextAccessor?.LastCompletedPath,
					CorrelationId: correlationId);
			}
			catch (Exception e) {
				List<LogMessage> priorLogs = [.. logger.FlushAndSnapshotMessages(clearMessages: true)];
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
}
