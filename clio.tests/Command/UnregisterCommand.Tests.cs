using System;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class UnregisterCommandTests : BaseCommandTests<UnregisterOptions>{
	#region Fields: Private

	private ILogger _logger;
	private IProcessExecutor _processExecutor;
	private UnregisterCommand _unregisterCommand;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_processExecutor);
		containerBuilder.AddTransient<UnregisterCommand>();
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public override void Setup() {
		base.Setup();
		_unregisterCommand = Container.GetRequiredService<UnregisterCommand>();
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_processExecutor.ClearReceivedCalls();
	}

	[Test]
	[Description("Execute should invoke registry delete commands and succeed for unregister.")]
	public void Execute_InvokesRegDeleteCommandsAndReturnsZero() {
		// Arrange
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));
		UnregisterOptions options = new();

		// Act
		int result = _unregisterCommand.Execute(options);

		// Assert
		result.Should().Be(0, because: "unregister should complete when process executor does not fail");
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "cmd" &&
			o.Arguments == "/c reg delete HKEY_CLASSES_ROOT\\Folder\\shell\\clio /f" &&
			o.SuppressErrors));
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Program == "cmd" &&
			o.Arguments == "/c reg delete HKEY_CLASSES_ROOT\\*\\shell\\clio /f" &&
			o.SuppressErrors));
		_logger.Received(1).WriteLine(Arg.Is<string>(message =>
			message.Contains("successfully unregistered", StringComparison.OrdinalIgnoreCase)));
	}

	[Test]
	[Description("Execute should return error and log exception when unregister process execution fails.")]
	public void Execute_WhenProcessExecutorThrows_ReturnsError() {
		// Arrange
		_processExecutor
			.When(x => x.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()))
			.Do(_ => throw new InvalidOperationException("reg failure"));
		UnregisterOptions options = new();

		// Act
		int result = _unregisterCommand.Execute(options);

		// Assert
		result.Should().Be(1, because: "unregister should fail when process execution throws");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("reg failure", StringComparison.OrdinalIgnoreCase)));
	}

	[Test]
	[Description("Execute should return error and terminate early when process exit code is non-zero.")]
	public void Execute_WhenProcessExitCodeIsNonZero_ReturnsError() {
		// Arrange
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 5,
				StandardError = "access denied"
			}));
		UnregisterOptions options = new();

		// Act
		int result = _unregisterCommand.Execute(options);

		// Assert
		result.Should().Be(1, because: "unregister should fail when process exit code is non-zero");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("exited with code 5", StringComparison.OrdinalIgnoreCase)));
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "/c reg delete HKEY_CLASSES_ROOT\\Folder\\shell\\clio /f"));
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.Arguments == "/c reg delete HKEY_CLASSES_ROOT\\*\\shell\\clio /f"));
	}

	#endregion
}
