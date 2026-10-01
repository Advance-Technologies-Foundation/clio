using System;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.McpWorker;
using System.IO;
using System.Linq;
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
	private IClioExecutablePathProvider _clioExecutablePathProvider;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		_operationSystem = Substitute.For<IOperationSystem>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_clioExecutablePathProvider = Substitute.For<IClioExecutablePathProvider>();
		containerBuilder.AddSingleton(_clioExecutablePathProvider);
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
		string executable = FileSystem.Path.Combine(AppContext.BaseDirectory, "clio.exe");
		FileSystem.AddFile(executable, new MockFileData("executable"));
		_clioExecutablePathProvider.Resolve().Returns(new ClioWorkerLaunchDescriptor(executable, [], AppContext.BaseDirectory));
		_registerCommand = Container.GetRequiredService<RegisterCommand>();
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_processExecutor.ClearReceivedCalls();
		_clioExecutablePathProvider.ClearReceivedCalls();
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

	[TestCase(false)]
	[TestCase(true)]
	[Description("Registration materializes a quoted absolute launch, including dotnet assembly arguments, in both ZIP verbs.")]
	public void Execute_ShouldRegisterResolvedLaunch_WhenExecutablePathContainsSpaces(bool useDotnet) {
		// Arrange
		_operationSystem.IsWindows.Returns(true);
		_operationSystem.HasAdminRights().Returns(true);
		string executable = FileSystem.Path.Combine(AppContext.BaseDirectory, "Tools With Spaces",
			useDotnet ? "dotnet.exe" : "clio.exe");
		FileSystem.AddFile(executable, new MockFileData("executable"));
		string assembly = FileSystem.Path.Combine(AppContext.BaseDirectory, "Clio With Spaces", "clio.dll");
		_clioExecutablePathProvider.Resolve().Returns(new ClioWorkerLaunchDescriptor(executable,
			useDotnet ? new[] { assembly } : Array.Empty<string>(), AppContext.BaseDirectory));
		FileSystem.AddDirectory(FileSystem.Path.Combine(AppContext.BaseDirectory, "img"));
		string template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
			"clio", "reg", "clio_context_menu_win.reg"));
		FileSystem.AddFile(FileSystem.Path.Combine(AppContext.BaseDirectory, "reg", "clio_context_menu_win.reg"),
			new MockFileData(template));
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 }));
		string generatedPath = FileSystem.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"clio", "clio_context_menu_win.reg");
		string quotedLaunch = $"\"{executable}\"" + (useDotnet ? $" \"{assembly}\"" : string.Empty);
		string escapedLaunch = quotedLaunch.Replace("\\", "\\\\").Replace("\"", "\\\"");

		// Act
		int result = _registerCommand.Execute(new RegisterOptions());
		string generated = FileSystem.File.ReadAllText(generatedPath);
		string[] deployCommands = generated.Split('\n').Where(line => line.Contains(" deploy-creatio ")).ToArray();

		// Assert
		result.Should().Be(0, because: "an existing absolute executable is safe to register");
		deployCommands.Should().HaveCount(2, because: "both ZIP association locations need the resolved launcher");
		deployCommands.Should().OnlyContain(line => line.TrimEnd() ==
			$"@=\"{escapedLaunch} deploy-creatio --zip-file \\\"%1\\\" --explorer-launch --disable-reset-password\"",
			because: "Explorer must resolve the executable and pass the ZIP as one argument without a command shell");
		generated.Should().NotContain("__CLIO_DEPLOY_LAUNCH__", because: "template markers must never reach the registry");
		_processExecutor.ReceivedCalls().Select(call => call.GetArguments()[0]).Cast<ProcessExecutionOptions>()
			.Should().OnlyContain(options => options.Program == "reg.exe",
				because: "registration imports must not involve command-shell interpretation");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Registration fails before any registry import when the launcher is missing or not absolute.")]
	public void Execute_ShouldRejectLauncher_WhenExecutableCannotBeResolved(bool useBareName) {
		// Arrange
		_operationSystem.IsWindows.Returns(true);
		_operationSystem.HasAdminRights().Returns(true);
		FileSystem.AddDirectory(FileSystem.Path.Combine(AppContext.BaseDirectory, "img"));
		string executable = useBareName ? "clio.exe" : FileSystem.Path.Combine(AppContext.BaseDirectory, "missing.exe");
		_clioExecutablePathProvider.Resolve().Returns(new ClioWorkerLaunchDescriptor(executable, [], AppContext.BaseDirectory));

		// Act
		int result = _registerCommand.Execute(new RegisterOptions());

		// Assert
		result.Should().Be(1, because: "an unresolved launcher would reproduce the Explorer app chooser");
		_processExecutor.ReceivedCalls().Should().BeEmpty(because: "existing registry entries must survive failed launch resolution");
	}

	#endregion
}
