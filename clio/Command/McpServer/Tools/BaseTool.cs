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
		int result = -1;
		string correlationId = Guid.NewGuid().ToString("N")[..12];
		lock (CommandExecutionLock) {
			dbOperationLogContextAccessor?.ClearLastCompletedPath();
			bool previousPreserveMessages = logger.PreserveMessages;
			logger.PreserveMessages = true;
			try {
				result = command.Execute(options);
				CommandExecutionResult returnResult = new(
					result,
					[.. logger.FlushAndSnapshotMessages(clearMessages: true)],
					dbOperationLogContextAccessor?.LastCompletedPath,
					CorrelationId: correlationId);
				return returnResult;
			}
			catch (Exception e) {
				List<LogMessage> priorLogs = [.. logger.FlushAndSnapshotMessages(clearMessages: true)];
				return CommandExecutionResult.FromException(e, priorLogs, correlationId);
			}
			finally {
				logger.PreserveMessages = previousPreserveMessages;
			}
		}
	}
}
