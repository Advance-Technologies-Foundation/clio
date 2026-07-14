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
[NonParallelizable]
[Property("Module", "Command")]
internal class UnregisterCommandTests : BaseCommandTests<UnregisterOptions>{
	#region Fields: Private

	private ILogger _logger;
	private IMacOsFinderIntegration _macOsFinderIntegration;
	private IMacOsMenuBarIntegration _macOsMenuBarIntegration;
	private IOperationSystem _operationSystem;
	private IProcessExecutor _processExecutor;
	private UnregisterCommand _unregisterCommand;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_operationSystem = Substitute.For<IOperationSystem>();
		_macOsFinderIntegration = Substitute.For<IMacOsFinderIntegration>();
		_macOsMenuBarIntegration = Substitute.For<IMacOsMenuBarIntegration>();
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_processExecutor);
		containerBuilder.AddSingleton(_operationSystem);
		containerBuilder.AddSingleton(_macOsFinderIntegration);
		containerBuilder.AddSingleton(_macOsMenuBarIntegration);
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
	[Description("Execute should remove the Finder quick action and return zero on macOS.")]
	public void Execute_WhenPlatformIsMacOs_RemovesQuickActionAndReturnsZero() {
		// Arrange
		_operationSystem.IsMacOS.Returns(true);
		_macOsFinderIntegration.UninstallAsync().Returns(Task.CompletedTask);
		UnregisterOptions options = new();

		// Act
		int result = _unregisterCommand.Execute(options);

		// Assert
		result.Should().Be(0, because: "unregister removes the Finder quick action on macOS");
		_macOsFinderIntegration.Received(1).UninstallAsync();
		_macOsMenuBarIntegration.Received(1).UninstallAsync();
		_processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
		_logger.Received(1).WriteLine(Arg.Is<string>(message =>
			message.Contains("menu bar app unregistered", StringComparison.OrdinalIgnoreCase)));
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
	[Description("Execute should log only the message without stack trace when exception occurs in normal mode")]
	public void Execute_WhenProcessExecutorThrows_LogsMessageOnly_InNormalMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		try {
			_processExecutor
				.When(x => x.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()))
				.Do(_ => throw new Exception("reg failure"));

			_unregisterCommand.Execute(new UnregisterOptions());

			_logger.Received(1).WriteError("reg failure");
			_logger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Execute should log full stack trace when exception occurs in debug mode")]
	public void Execute_WhenProcessExecutorThrows_LogsFullStackTrace_InDebugMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = true;
		try {
			_processExecutor
				.When(x => x.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()))
				.Do(_ => throw new Exception("reg failure"));

			_unregisterCommand.Execute(new UnregisterOptions());

			_logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
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
