using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class BaseToolTests {

	[Test]
	[Category("Unit")]
	[Description("Captures queued log messages deterministically in BaseTool success path without timing delays.")]
	public void InternalExecute_Should_Flush_Queued_Messages_On_Success() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeBaseToolCommand(ConsoleLogger.Instance, exitCode: 0, messageToWrite: "Operation completed.");
		BaseToolHarness tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.Execute(new BaseToolHarnessOptions("success"));
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the wrapped command completed successfully");
		messageValues.Should().Contain("Operation completed.",
			because: "BaseTool should flush queued ConsoleLogger entries before building MCP output");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Captures queued messages and appends exception details in BaseTool exception path.")]
	public void InternalExecute_Should_Flush_Queued_Messages_On_Exception() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		var command = new FakeBaseToolCommand(
			ConsoleLogger.Instance,
			exitCode: 0,
			messageToWrite: "Before failure.",
			executeException: new InvalidOperationException("Boom from command."));
		BaseToolHarness tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.Execute(new BaseToolHarnessOptions("failure"));
		string[] messageValues = result.Output.Select(message => message.Value?.ToString() ?? string.Empty).ToArray();

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "BaseTool keeps the default exit code when command throws before completion");
		messageValues.Should().Contain("Before failure.",
			because: "messages queued prior to the exception should still be returned");
		messageValues.Should().Contain("Boom from command.",
			because: "BaseTool appends the thrown exception message into MCP output");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed record BaseToolHarnessOptions(string Scenario);

	private sealed class BaseToolHarness : BaseTool<BaseToolHarnessOptions> {
		public BaseToolHarness(Command<BaseToolHarnessOptions> command, ILogger logger)
			: base(command, logger) {
		}

		public CommandExecutionResult Execute(BaseToolHarnessOptions options) => InternalExecute(options);
	}

	private sealed class FakeBaseToolCommand : Command<BaseToolHarnessOptions> {
		private readonly ILogger _logger;
		private readonly int _exitCode;
		private readonly string _messageToWrite;
		private readonly Exception _executeException;

		public FakeBaseToolCommand(
			ILogger logger,
			int exitCode,
			string messageToWrite,
			Exception executeException = null) {
			_logger = logger;
			_exitCode = exitCode;
			_messageToWrite = messageToWrite;
			_executeException = executeException;
		}

		public override int Execute(BaseToolHarnessOptions options) {
			_logger.WriteInfo(_messageToWrite);
			if (_executeException is not null) {
				throw _executeException;
			}
			return _exitCode;
		}
	}
}
