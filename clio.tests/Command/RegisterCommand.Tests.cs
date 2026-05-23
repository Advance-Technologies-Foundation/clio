using System;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class RegisterCommandTests : BaseCommandTests<RegisterOptions>{
	#region Fields: Private

	private ILogger _logger;
	private IOperationSystem _operationSystem;
	private IProcessExecutor _processExecutor;
	private RegisterCommand _registerCommand;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		_operationSystem = Substitute.For<IOperationSystem>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_operationSystem);
		containerBuilder.AddSingleton(_processExecutor);
		containerBuilder.AddTransient<RegisterCommand>();
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public override void Setup() {
		base.Setup();
		_registerCommand = Container.GetRequiredService<RegisterCommand>();
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_processExecutor.ClearReceivedCalls();
	}

	[Test]
	[Description("Execute should return error and log unsupported-platform message on non-Windows.")]
	public void Execute_WhenPlatformIsNotWindows_ReturnsError() {
		// Arrange
		_operationSystem.IsWindows.Returns(false);

		RegisterOptions options = new();

		// Act
		int result = _registerCommand.Execute(options);

		// Assert
		result.Should().Be(1, because: "register command is Windows-only");
		_logger.Received(1).WriteLine(Arg.Is<string>(message =>
			message.Contains("only supported on: 'windows'", StringComparison.OrdinalIgnoreCase)));
		_processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
	}

	[Test]
	[Description("Execute should return error and log message when running on Windows without admin rights.")]
	public void Execute_WhenPlatformIsWindowsAndNoAdminRights_ReturnsError() {
		// Arrange
		_operationSystem.IsWindows.Returns(true);
		_operationSystem.HasAdminRights().Returns(false);
		RegisterOptions options = new();

		// Act
		int result = _registerCommand.Execute(options);

		// Assert
		result.Should().Be(1, because: "register command requires administrator privileges on Windows");
		_logger.Received(1).WriteLine(Arg.Is<string>(message =>
			message.Contains("need admin rights", StringComparison.OrdinalIgnoreCase)));
		_processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
	}

	[Test]
	[Description("Execute should return error when first registry import returns non-zero exit code.")]
	public void Execute_WhenFirstRegistryImportFails_ReturnsError() {
		// Arrange
		_operationSystem.IsWindows.Returns(true);
		_operationSystem.HasAdminRights().Returns(true);
		string assemblyFolderPath = AppContext.BaseDirectory;
		FileSystem.AddDirectory(FileSystem.Path.Combine(assemblyFolderPath, "img"));
		FileSystem.AddFile(FileSystem.Path.Combine(assemblyFolderPath, "img", "icon.ico"), new MockFileData("icon"));
		FileSystem.AddDirectory(FileSystem.Path.Combine(assemblyFolderPath, "reg"));
		FileSystem.AddFile(FileSystem.Path.Combine(assemblyFolderPath, "reg", "unreg_clio_context_menu_win.reg"),
			new MockFileData("reg content"));
		FileSystem.AddFile(FileSystem.Path.Combine(assemblyFolderPath, "reg", "clio_context_menu_win.reg"),
			new MockFileData("reg content"));
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(
				Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 1, StandardError = "reg failed" }),
				Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 }));
		RegisterOptions options = new();

		// Act
		int result = _registerCommand.Execute(options);

		// Assert
		result.Should().Be(1, because: "register must fail when registry import exits with non-zero code");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("exited with code 1", StringComparison.OrdinalIgnoreCase)));
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>());
	}

	[Test]
	[Description("Execute should return zero when both registry imports succeed.")]
	public void Execute_WhenRegistryImportsSucceed_ReturnsZero() {
		// Arrange
		_operationSystem.IsWindows.Returns(true);
		_operationSystem.HasAdminRights().Returns(true);
		string assemblyFolderPath = AppContext.BaseDirectory;
		FileSystem.AddDirectory(FileSystem.Path.Combine(assemblyFolderPath, "img"));
		FileSystem.AddFile(FileSystem.Path.Combine(assemblyFolderPath, "img", "icon.ico"), new MockFileData("icon"));
		FileSystem.AddDirectory(FileSystem.Path.Combine(assemblyFolderPath, "reg"));
		FileSystem.AddFile(FileSystem.Path.Combine(assemblyFolderPath, "reg", "unreg_clio_context_menu_win.reg"),
			new MockFileData("reg content"));
		FileSystem.AddFile(FileSystem.Path.Combine(assemblyFolderPath, "reg", "clio_context_menu_win.reg"),
			new MockFileData("reg content"));
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 }));
		RegisterOptions options = new();

		// Act
		int result = _registerCommand.Execute(options);

		// Assert
		result.Should().Be(0, because: "register should succeed when all required registry imports succeed");
		_processExecutor.Received(2).ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>());
		_logger.Received(1).WriteLine(Arg.Is<string>(message =>
			message.Contains("successfully registered", StringComparison.OrdinalIgnoreCase)));
	}

	#endregion
}
