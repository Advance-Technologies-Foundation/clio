using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using Clio.WebApplication;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class InstallGateToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable install-gate MCP tool name so clients and tests share one contract identifier.")]
	public void InstallGate_Should_Advertise_Stable_Tool_Name() {
		// Act
		string toolName = InstallGateTool.InstallGateToolName;

		// Assert
		toolName.Should().Be("install-gate",
			because: "the MCP contract should keep a stable install-gate tool name");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves InstallGateCommand for the requested environment and returns the real command exit code.")]
	public void InstallGate_Should_Resolve_Command_For_Environment_And_Return_Exit_Code() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeInstallGateCommand resolvedCommand = new(exitCode: 0);
		commandResolver.Resolve<InstallGateCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		InstallGateTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result = tool.InstallGate(new InstallGateArgs("sandbox"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the MCP tool should return the real install-gate command exit code");
			commandResolver.Received(1).Resolve<InstallGateCommand>(Arg.Is<EnvironmentOptions>(options =>
				options.Environment == "sandbox"));
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved install-gate command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.Environment.Should().Be("sandbox",
				because: "the environment-name argument should map into InstallGateOptions");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes non-destructive, idempotent MCP metadata and remediation-oriented description for install-gate.")]
	public void InstallGate_Should_Expose_Expected_Mcp_Metadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(InstallGateTool)
			.GetMethod(nameof(InstallGateTool.InstallGate))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
		System.ComponentModel.DescriptionAttribute description =
			(System.ComponentModel.DescriptionAttribute)typeof(InstallGateTool)
				.GetMethod(nameof(InstallGateTool.InstallGate))!
				.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
				.Single();

		// Assert
		attribute.Name.Should().Be(InstallGateTool.InstallGateToolName,
			because: "the metadata should reuse the production tool-name constant");
		attribute.ReadOnly.Should().BeFalse(
			because: "installing cliogate changes the target environment package state");
		attribute.Destructive.Should().BeFalse(
			because: "installing or updating cliogate is an additive, non-destructive provisioning step");
		attribute.Idempotent.Should().BeTrue(
			because: "re-running install-gate on an already-gated environment is safe");
		description.Description.Should().Contain("cliogate",
			because: "the description should name the package the tool installs");
		description.Description.Should().Contain("restore-workspace",
			because: "the description should point at the gate-dependent flow that motivates this tool");
	}

	private sealed class FakeInstallGateCommand : InstallGateCommand {
		private readonly int _exitCode;

		public FakeInstallGateCommand(int exitCode)
			: base(
				new EnvironmentSettings(),
				Substitute.For<IPackageInstaller>(),
				Substitute.For<IApplication>(),
				Substitute.For<IWorkingDirectoriesProvider>(),
				Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public InstallGateOptions? CapturedOptions { get; private set; }

		public override int Execute(InstallGateOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
}
