using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ListUserTasksCommandTests {
	private IListUserTasksService _listUserTasksService;
	private ILogger _logger;
	private ListUserTasksCommand _command;

	[SetUp]
	public void Setup() {
		_listUserTasksService = Substitute.For<IListUserTasksService>();
		_logger = Substitute.For<ILogger>();
		_command = new ListUserTasksCommand(_listUserTasksService, _logger);
	}

	[TearDown]
	public void TearDown() {
		_listUserTasksService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Category("Unit")]
	[Description("Requests the user task catalog for the supplied environment and writes each task plus a total to the logger on success.")]
	public void Execute_ShouldWriteTasksAndReturnZero_WhenServiceReturnsTasks() {
		// Arrange
		ListUserTasksOptions options = new() { Environment = "sandbox" };
		_listUserTasksService.GetUserTasks("sandbox").Returns(new List<UserTaskInfoResult> {
			new("ReadDataUserTask", "cb455b6f-78ff-4b1e-b241-c2bbc0b37e9f"),
			new("ActivityUserTask", "b5c726f2-af5b-4381-bac6-913074144308")
		});

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a successful user-task lookup should return the standard success exit code");
		_listUserTasksService.Received(1).GetUserTasks("sandbox");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("ReadDataUserTask")));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("Total user tasks: 2")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs a readable error when the call omits environment-name.")]
	public void Execute_ShouldFail_WhenEnvironmentIsMissing() {
		// Act
		int result = _command.Execute(new ListUserTasksOptions());

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when the environment is missing");
		_listUserTasksService.DidNotReceiveWithAnyArgs().GetUserTasks(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs the service exception message when the service throws.")]
	public void Execute_ShouldFail_WhenServiceThrows() {
		// Arrange
		ListUserTasksOptions options = new() { Environment = "sandbox" };
		_listUserTasksService.GetUserTasks(Arg.Any<string>())
			.Returns<IReadOnlyList<UserTaskInfoResult>>(_ =>
				throw new InvalidOperationException("ListUserTasks failed."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should propagate service-level failures as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("ListUserTasks failed.")));
	}
}
