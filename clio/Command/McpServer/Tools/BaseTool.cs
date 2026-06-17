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
			// [RequiresPackage] declarations against the per-call target environment. An unmet
			// requirement surfaces as a PackageRequirementException; any other verification failure
			// (HTTP/auth/unknown-environment) surfaces as its own exception. Both are converted to a
			// failed result below, so the command never runs when its requirements are not satisfied.
			resolvedCommand = ResolveCommand<TCommand>(options);
		} catch (PackageRequirementException ex) {
			// Surface the actionable install hint verbatim, without the exception-chain decoration.
			return CommandExecutionResult.FromError(ex.Message);
		} catch (Exception e) {
			return CommandExecutionResult.FromException(e);
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
	/// regardless of its return shape. The command is resolved FIRST (so an unknown-environment
	/// failure from <see cref="ResolveFromCallContainer"/> still surfaces exactly as before), THEN the
	/// requirement is enforced. A <see cref="PackageRequirementException"/> (unmet requirement) and any
	/// other verification failure both propagate to the caller.
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

		return options switch {
									   //Optional environment properties are not used in command resolution for these options, so null is passed explicitly to avoid confusion about which properties are used.
									   CreateTestProjectOptions envOptions when string.IsNullOrWhiteSpace(envOptions.Environment) && string.IsNullOrWhiteSpace(envOptions.Uri)
										   => commandResolver.ResolveWithoutEnvironment<TService>(envOptions),
									   AddPackageOptions envOptions when string.IsNullOrWhiteSpace(envOptions.Environment) && string.IsNullOrWhiteSpace(envOptions.Uri)
										   => commandResolver.ResolveWithoutEnvironment<TService>(envOptions),
									   CreateWorkspaceCommandOptions envOptions when envOptions.Empty
										   && string.IsNullOrWhiteSpace(envOptions.Environment)
										   && string.IsNullOrWhiteSpace(envOptions.Uri)
										   => commandResolver.ResolveWithoutEnvironment<TService>(envOptions),
									   CreateUiProjectOptions envOptions when string.IsNullOrWhiteSpace(envOptions.Environment)
										   && string.IsNullOrWhiteSpace(envOptions.Uri)
										   => commandResolver.ResolveWithoutEnvironment<TService>(envOptions),

									   EnvironmentOptions envOptions => commandResolver.Resolve<TService>(envOptions),
									   var _ => throw new InvalidOperationException(
										   $"Unsupported options type: {options.GetType().Name}")
								   };
	}


	private protected virtual CommandExecutionResult InternalExecute(Command<T> command, T options) {
		// Package requirements are NOT enforced here. [RequiresPackage] is verified against the per-call
		// target environment, which is only known where the env-scoped command is resolved
		// (ResolveCommand). The env-SENSITIVE paths run the gate inside ResolveCommand before reaching
		// this method; the env-INsensitive injected-command path (InternalExecute(T) → this overload)
		// does not carry a target environment, so a package requirement would be meaningless there.
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
