using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class InstallApplicationToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for install-application so prompts, unit tests, and E2E tests share one identifier.")]
	public void InstallApplicationTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = InstallApplicationTool.InstallApplicationToolName;

		// Assert
		toolName.Should().Be("install-application",
			because: "the MCP tool name must remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks install-application as destructive because it installs application packages into a Creatio environment.")]
	public void InstallApplication_Should_Be_Marked_As_Destructive() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(InstallApplicationTool)
			.GetMethod(nameof(InstallApplicationTool.InstallApplication))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(
			because: "install-application changes a target environment and can overwrite deployed application state");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware install-application command and maps explicit MCP arguments into command options.")]
	public void InstallApplication_Should_Resolve_Command_And_Map_Explicit_Arguments() {
		// Arrange
		FakeInstallApplicationCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<InstallApplicationCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		InstallApplicationTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.InstallApplication(new InstallApplicationArgs(
			Name: @"C:\Packages\app.gz",
			ReportPath: @"C:\Logs\install.log",
			CheckCompilationErrors: true,
			EnvironmentName: "dev"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid install-application request should execute the resolved environment-aware command");
		commandResolver.Received(1).Resolve<InstallApplicationCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive mapped install-application options");
		resolvedCommand.CapturedOptions!.Name.Should().Be(@"C:\Packages\app.gz",
			because: "the application package name/path must be forwarded from MCP arguments");
		resolvedCommand.CapturedOptions.ReportPath.Should().Be(@"C:\Logs\install.log",
			because: "the optional report-path must be preserved when it is provided");
		resolvedCommand.CapturedOptions.CheckCompilationErrors.Should().BeTrue(
			because: "the optional compilation-check flag must be forwarded when it is provided");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev",
			because: "the requested environment name must be preserved for environment-aware resolution");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves null option values for report-path and check-compilation-errors when omitted from MCP arguments.")]
	public void InstallApplication_Should_Preserve_Nulls_For_Omitted_Optional_Arguments() {
		// Arrange
		FakeInstallApplicationCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<InstallApplicationCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		InstallApplicationTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.InstallApplication(new InstallApplicationArgs(
			Name: @"C:\Packages\app.gz",
			ReportPath: null,
			CheckCompilationErrors: null,
			EnvironmentName: "dev"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "omitting optional MCP arguments should still produce a valid install-application invocation");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should still receive options when optional MCP arguments are omitted");
		resolvedCommand.CapturedOptions!.ReportPath.Should().BeNull(
			because: "the report-path option should remain null when the caller omits it");
		resolvedCommand.CapturedOptions.CheckCompilationErrors.Should().BeNull(
			because: "the compilation-check flag should remain null when the caller omits it");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured command execution error when the requested environment cannot be resolved for install-application.")]
	public void InstallApplication_Should_Report_Invalid_Environment_As_Command_Result() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<InstallApplicationCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment with key 'missing-env' not found."));
		InstallApplicationTool tool = new(
			new FakeInstallApplicationCommand(),
			ConsoleLogger.Instance,
			commandResolver);

		// Act
		CommandExecutionResult result = tool.InstallApplication(new InstallApplicationArgs(
			Name: @"C:\Packages\app.gz",
			ReportPath: null,
			CheckCompilationErrors: null,
			EnvironmentName: "missing-env"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "resolver failures should be returned as normal command execution envelopes");
		result.Output.Should().ContainSingle(message =>
			message.GetType() == typeof(ErrorMessage) &&
			Equals(message.Value, "Environment with key 'missing-env' not found."),
			because: "the failure should surface the environment-resolution problem to the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for install-application references the exact tool name and keeps all exposed MCP arguments visible to callers.")]
	public void InstallApplicationPrompt_Should_Mention_Tool_Name_And_Arguments() {
		// Arrange

		// Act
		string prompt = InstallApplicationPrompt.InstallApplication(
			name: @"C:\Packages\app.gz",
			environmentName: "dev",
			reportPath: @"C:\Logs\install.log",
			checkCompilationErrors: true);

		// Assert
		prompt.Should().Contain(InstallApplicationTool.InstallApplicationToolName,
			because: "the prompt should reference the exact production tool name");
		prompt.Should().Contain("`name`",
			because: "the prompt should keep the package name/path argument visible to callers");
		prompt.Should().Contain("`report-path`",
			because: "the prompt should keep the report-path argument visible to callers");
		prompt.Should().Contain("`check-compilation-errors`",
			because: "the prompt should keep the compilation-check argument visible to callers");
		prompt.Should().Contain("`environment-name`",
			because: "the prompt should keep the environment-name argument visible to callers");
	}

	private sealed class FakeInstallApplicationCommand : InstallApplicationCommand {
		public InstallApplicationOptions? CapturedOptions { get; private set; }

		public FakeInstallApplicationCommand()
			: base(
				new EnvironmentSettings(),
				Substitute.For<IApplicationInstaller>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(InstallApplicationOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
