using System;
using System.IO;
using Clio;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class DownloadConfigurationToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable consolidated MCP tool name 'download-configuration' and the supported source discriminator values.")]
	public void DownloadConfiguration_Should_Advertise_Stable_Contract() {
		// Arrange / Act
		string toolName = DownloadConfigurationTool.DownloadConfigurationToolName;
		string[] sources = [DownloadConfigurationTool.SourceEnvironment, DownloadConfigurationTool.SourceBuild];

		// Assert
		toolName.Should().Be("download-configuration",
			because: "clients and tests should share one stable tool name for the consolidated dconf flow");
		sources.Should().BeEquivalentTo(["environment", "build"],
			because: "the supported source discriminator values must remain stable");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a fresh command instance for source='environment' and executes it from the requested workspace path.")]
	public void DownloadConfiguration_EnvironmentSource_Should_Resolve_Command_And_Use_Requested_Workspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dconf-env-{Guid.NewGuid():N}")).FullName;
		FakeDownloadConfigurationCommand defaultCommand = new();
		FakeDownloadConfigurationCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DownloadConfigurationCommand>(Arg.Any<DownloadConfigurationCommandOptions>())
			.Returns(resolvedCommand);
		DownloadConfigurationTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		try {
			// Act
			CommandExecutionResult result = tool.DownloadConfiguration(new DownloadConfigurationArgs(
				Source: DownloadConfigurationTool.SourceEnvironment,
				WorkspacePath: workspacePath,
				EnvironmentName: "dev"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the environment source should forward a valid command payload");
			commandResolver.Received(1).Resolve<DownloadConfigurationCommand>(Arg.Is<DownloadConfigurationCommandOptions>(options =>
				options.Environment == "dev" &&
				string.IsNullOrWhiteSpace(options.BuildZipPath)));
			defaultCommand.CapturedOptions.Should().BeNull(
				because: "the environment-aware tool path should execute the resolved command instance");
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved command should receive the forwarded dconf options");
			resolvedCommand.CapturedOptions!.Environment.Should().Be("dev",
				because: "the requested environment name must be preserved");
			NormalizeTempPathAlias(resolvedCommand.CapturedWorkingDirectory).Should().Be(NormalizeTempPathAlias(workspacePath),
				because: "the tool should execute from the requested workspace so dconf writes into the correct `.application` folder");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "the MCP tool should restore the original working directory after execution");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.SetCurrentDirectory(originalDirectory);
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Executes source='build' against the injected command and uses the requested workspace path as the working directory.")]
	public void DownloadConfiguration_BuildSource_Should_Execute_Injected_Command_And_Use_Requested_Workspace() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string originalDirectory = Directory.GetCurrentDirectory();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dconf-build-{Guid.NewGuid():N}")).FullName;
		string buildPath = Path.Combine(Path.GetTempPath(), $"creatio-{Guid.NewGuid():N}.zip");
		FakeDownloadConfigurationCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DownloadConfigurationTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		try {
			// Act
			CommandExecutionResult result = tool.DownloadConfiguration(new DownloadConfigurationArgs(
				Source: DownloadConfigurationTool.SourceBuild,
				WorkspacePath: workspacePath,
				BuildPath: buildPath));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the build source should forward a valid command payload");
			commandResolver.DidNotReceiveWithAnyArgs().Resolve<DownloadConfigurationCommand>(default!);
			defaultCommand.CapturedOptions.Should().NotBeNull(
				because: "the build source should execute the injected command directly");
			defaultCommand.CapturedOptions!.BuildZipPath.Should().Be(buildPath,
				because: "the requested build path must be preserved");
			NormalizeTempPathAlias(defaultCommand.CapturedWorkingDirectory).Should().Be(NormalizeTempPathAlias(workspacePath),
				because: "the tool should execute from the requested workspace so the downloaded configuration lands in that workspace");
			Directory.GetCurrentDirectory().Should().Be(originalDirectory,
				because: "the MCP tool should restore the original working directory after execution");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.SetCurrentDirectory(originalDirectory);
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects relative workspace paths before executing dconf so the MCP contract stays explicit and portable.")]
	public void DownloadConfiguration_BuildSource_Should_Reject_Relative_Workspace_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeDownloadConfigurationCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DownloadConfigurationTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		// Act
		CommandExecutionResult result = tool.DownloadConfiguration(new DownloadConfigurationArgs(
			Source: DownloadConfigurationTool.SourceBuild,
			WorkspacePath: @"relative\workspace",
			BuildPath: Path.Combine(Path.GetTempPath(), "creatio.zip")));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the MCP tool should fail fast when the caller does not provide an absolute workspace path");
		result.Output.Should().Contain(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Workspace path must be absolute: relative\workspace"),
			because: "the failure should explain why the workspace path was rejected");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the command should not run when the workspace path is invalid");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects relative build paths before executing dconf so the MCP contract stays explicit and portable.")]
	public void DownloadConfiguration_BuildSource_Should_Reject_Relative_Build_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dconf-relative-build-{Guid.NewGuid():N}")).FullName;
		FakeDownloadConfigurationCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DownloadConfigurationTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		try {
			// Act
			CommandExecutionResult result = tool.DownloadConfiguration(new DownloadConfigurationArgs(
				Source: DownloadConfigurationTool.SourceBuild,
				WorkspacePath: workspacePath,
				BuildPath: Path.Combine("relative", "creatio.zip")));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "the MCP tool should fail fast when the caller does not provide an absolute build path");
			result.Output.Should().Contain(message =>
				message.GetType() == typeof(ErrorMessage) &&
				Equals(message.Value, $"Build path must be absolute: {Path.Combine("relative", "creatio.zip")}"),
				because: "the failure should explain why the build path was rejected");
			defaultCommand.CapturedOptions.Should().BeNull(
				because: "the command should not run when the build path is invalid");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects whitespace build paths before executing dconf so the build source cannot fall back into a different command mode.")]
	public void DownloadConfiguration_BuildSource_Should_Reject_Whitespace_Build_Path() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		string workspacePath = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"dconf-whitespace-build-{Guid.NewGuid():N}")).FullName;
		FakeDownloadConfigurationCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DownloadConfigurationTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		try {
			// Act
			CommandExecutionResult result = tool.DownloadConfiguration(new DownloadConfigurationArgs(
				Source: DownloadConfigurationTool.SourceBuild,
				WorkspacePath: workspacePath,
				BuildPath: "   "));

			// Assert
			result.ExitCode.Should().Be(-1,
				because: "the MCP tool should fail fast when the caller omits the required build path value");
			defaultCommand.CapturedOptions.Should().BeNull(
				because: "the command should not run when the build path is invalid");
		}
		finally {
			ConsoleLogger.Instance.ClearMessages();
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects Windows drive-relative workspace paths so dconf cannot bypass the absolute-path requirement on Windows hosts.")]
	public void DownloadConfiguration_BuildSource_Should_Reject_Windows_Drive_Relative_Workspace_Path() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Windows drive-relative paths are only meaningful on Windows.");
		}

		ConsoleLogger.Instance.ClearMessages();
		FakeDownloadConfigurationCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DownloadConfigurationTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new System.IO.Abstractions.FileSystem());

		// Act
		CommandExecutionResult result = tool.DownloadConfiguration(new DownloadConfigurationArgs(
			Source: DownloadConfigurationTool.SourceBuild,
			WorkspacePath: @"C:workspace",
			BuildPath: Path.Combine(Path.GetTempPath(), "creatio.zip")));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "drive-relative Windows paths are rooted but not fully qualified and must not satisfy the MCP contract");
		result.Output.Should().Contain(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, @"Workspace path must be absolute: C:workspace"),
			because: "the failure should explain why the drive-relative workspace path was rejected");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the command should not run when the workspace path is invalid");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for download-configuration keeps the workspace-path requirement visible for both source modes.")]
	public void DownloadConfigurationPrompt_Should_Mention_Workspace_Path_For_Both_Modes() {
		// Arrange / Act
		string environmentPrompt = DownloadConfigurationPrompt.DownloadConfigurationByEnvironment("dev", @"C:\workspace");
		string buildPrompt = DownloadConfigurationPrompt.DownloadConfigurationByBuild(@"C:\creatio.zip", @"C:\workspace");

		// Assert
		environmentPrompt.Should().Contain("workspace-path",
			because: "the environment-mode prompt should tell agents how to target the correct local workspace");
		environmentPrompt.Should().Contain(DownloadConfigurationTool.DownloadConfigurationToolName,
			because: "the prompt should reference the exact MCP tool name");
		buildPrompt.Should().Contain("workspace-path",
			because: "the build-mode prompt should tell agents how to target the correct local workspace");
		buildPrompt.Should().Contain(DownloadConfigurationTool.DownloadConfigurationToolName,
			because: "the prompt should reference the exact MCP tool name");
	}

	private sealed class FakeDownloadConfigurationCommand : DownloadConfigurationCommand {
		public DownloadConfigurationCommandOptions? CapturedOptions { get; private set; }

		public string? CapturedWorkingDirectory { get; private set; }

		public FakeDownloadConfigurationCommand()
			: base(
				Substitute.For<IApplicationDownloader>(),
				Substitute.For<IZipBasedApplicationDownloader>(),
				Substitute.For<IWorkspace>(),
				Substitute.For<ILogger>(),
				Substitute.For<Clio.Common.IFileSystem>(),
				Substitute.For<ISettingsRepository>()) {
		}

		public override int Execute(DownloadConfigurationCommandOptions options) {
			CapturedOptions = options;
			CapturedWorkingDirectory = Directory.GetCurrentDirectory();
			return 0;
		}
	}

	private static string? NormalizeTempPathAlias(string? path) {
		if (path is null) {
			return null;
		}

		return path.StartsWith("/private/var/", StringComparison.Ordinal)
			? path.Substring("/private".Length)
			: path;
	}
}
