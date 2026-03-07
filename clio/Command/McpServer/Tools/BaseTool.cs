using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clio;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public abstract class BaseTool<T>(Command<T> command, ILogger logger, IToolCommandResolver commandResolver = null) {
	private readonly Command<T> _command = command;
	private readonly IToolCommandResolver _commandResolver = commandResolver;
	private readonly ILogger _logger = logger;

	private protected CommandExecutionResult InternalExecute(T options) {
		return InternalExecute(_command, options);
	}

	private protected CommandExecutionResult InternalExecute<TCommand>(T options,
		Action<TCommand> configureCommand = null) where TCommand : Command<T> {
		if (options is not EnvironmentOptions environmentOptions) {
			throw new InvalidOperationException(
				$"{GetType().Name} can only resolve commands for options derived from EnvironmentOptions.");
		}
		if (_commandResolver is null) {
			throw new InvalidOperationException(
				$"{GetType().Name} does not support environment-based command resolution.");
		}
		TCommand resolvedCommand = _commandResolver.Resolve<TCommand>(environmentOptions);
		configureCommand?.Invoke(resolvedCommand);
		return InternalExecute(resolvedCommand, options);
	}

	private protected virtual CommandExecutionResult InternalExecute(Command<T> command, T options) {
		int result = -1;
		try {
			result = command.Execute(options);
			Thread.Sleep(500);
			CommandExecutionResult returnResult = new(result, [.. _logger.LogMessages.ToList()]);
			_logger.ClearMessages();
			return returnResult;
		}
		catch (Exception e) {
			List<LogMessage> logMessages = [.. _logger.LogMessages, new ErrorMessage(e.Message)];
			CommandExecutionResult returnResult = new(result, logMessages);
			_logger.ClearMessages();
			return returnResult;
		}
	}
}
