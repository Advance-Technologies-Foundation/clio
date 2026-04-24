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
		configureCommand?.Invoke(resolvedCommand);
		return InternalExecute(resolvedCommand, options);
	}

	private protected TCommand ResolveCommand<TCommand>(T options) where TCommand : Command<T> {
		if (options is not EnvironmentOptions environmentOptions) {
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
										   => commandResolver.ResolveWithoutEnvironment<TCommand>(envOptions),
									   AddPackageOptions envOptions when string.IsNullOrWhiteSpace(envOptions.Environment) && string.IsNullOrWhiteSpace(envOptions.Uri)
										   => commandResolver.ResolveWithoutEnvironment<TCommand>(envOptions),
									   CreateWorkspaceCommandOptions envOptions when envOptions.Empty
										   && string.IsNullOrWhiteSpace(envOptions.Environment)
										   && string.IsNullOrWhiteSpace(envOptions.Uri)
										   => commandResolver.ResolveWithoutEnvironment<TCommand>(envOptions),

									   EnvironmentOptions envOptions => commandResolver.Resolve<TCommand>(envOptions),
									   var _ => throw new InvalidOperationException(
										   $"Unsupported options type: {options.GetType().Name}")
								   };
	}


	private protected virtual CommandExecutionResult InternalExecute(Command<T> command, T options) {
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
