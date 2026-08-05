using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class InstallProcessBuilderToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable install-process-builder MCP tool name so clients, guidance and the curated contract share one identifier.")]
	public void InstallProcessBuilder_Should_Advertise_Stable_Tool_Name() {
		// Act
		string toolName = InstallProcessBuilderTool.InstallProcessBuilderToolName;

		// Assert
		toolName.Should().Be("install-process-builder",
			because: "the process-designer tools' refusal hint names this exact verb, so renaming it would "
				+ "point users and agents at a tool that does not exist");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves InstallProcessBuilderCommand for the requested environment and returns the real command exit code.")]
	public async Task InstallProcessBuilder_Should_Resolve_Command_For_Environment_And_Return_Exit_Code() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeInstallProcessBuilderCommand resolvedCommand = new(exitCode: 0);
		commandResolver.Resolve<InstallProcessBuilderCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		InstallProcessBuilderTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result =
				await tool.InstallProcessBuilder(new InstallProcessBuilderArgs("sandbox"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the MCP tool should return the real command exit code, including the failure the "
					+ "command raises when the service does not answer after installing");
			commandResolver.Received(1).Resolve<InstallProcessBuilderCommand>(Arg.Is<EnvironmentOptions>(
				options => options.Environment == "sandbox"));
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.Environment.Should().Be("sandbox",
				because: "the environment-name argument should map into InstallProcessBuilderOptions");
			resolvedCommand.CapturedOptions.Force.Should().BeFalse(
				because: "force must default to false when the argument is omitted, so an agent following the "
					+ "refusal hint cannot accidentally force a recompile on a healthy environment");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Maps force=true onto the command options, so the documented remedy for a present-but-uncompiled package is reachable over MCP.")]
	public async Task InstallProcessBuilder_Should_Map_Force_Onto_Options() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeInstallProcessBuilderCommand resolvedCommand = new(exitCode: 0);
		commandResolver.Resolve<InstallProcessBuilderCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		InstallProcessBuilderTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			await tool.InstallProcessBuilder(new InstallProcessBuilderArgs("sandbox", Force: true));

			// Assert
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved command should run");
			resolvedCommand.CapturedOptions!.Force.Should().BeTrue(
				because: "without this mapping the tool would advertise force in its description while the "
					+ "command kept taking the already-installed short-circuit, leaving an environment that "
					+ "has the package but never compiled it with no remedy over MCP");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes non-destructive, idempotent MCP metadata and a remediation-oriented description naming the package and the tools it unblocks.")]
	public void InstallProcessBuilder_Should_Expose_Expected_Mcp_Metadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(InstallProcessBuilderTool)
			.GetMethod(nameof(InstallProcessBuilderTool.InstallProcessBuilder))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
		System.ComponentModel.DescriptionAttribute description =
			(System.ComponentModel.DescriptionAttribute)typeof(InstallProcessBuilderTool)
				.GetMethod(nameof(InstallProcessBuilderTool.InstallProcessBuilder))!
				.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
				.Single();

		// Assert
		attribute.Name.Should().Be(InstallProcessBuilderTool.InstallProcessBuilderToolName,
			because: "the metadata should reuse the production tool-name constant");
		attribute.ReadOnly.Should().BeFalse(
			because: "installing the package changes the target environment's package state");
		attribute.Destructive.Should().BeTrue(
			because: "the flag is what clio's core-rules guidance ties 'confirm the target environment with "
				+ "the user first' to, and this tool runs a configuration build on a live instance and "
				+ "restarts it, with recovery from a failed compile being an explicit RestoreFromBackup "
				+ "rather than a rollback. It is additive in what it ADDS, which argued for false at first, "
				+ "but compile-creatio and restart-by-environment-name are both true and this tool causes "
				+ "the effects of both. install-gate's false is not the precedent: it ships a prebuilt "
				+ "assembly and never makes the target rebuild");
		attribute.Idempotent.Should().BeTrue(
			because: "the command short-circuits on an already-current environment, so re-running is safe");
		description.Description.Should().Contain(BundledPackages.ProcessBuilderPackageName,
			because: "the description should name the package the tool installs");
		description.Description.Should().Contain("create-business-process",
			because: "the description should name a process-designer tool whose refusal motivates this one, "
				+ "so an agent can connect the refusal to the remedy");
		description.Description.Should().Contain("ListUserTasks",
			because: "the description must disclose that the tool verifies the outcome, not just the install, "
				+ "so a caller understands why a successful install can still fail");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool must not be feature-gated, or the remediation the process-designer tools point at would be unreachable exactly when it is needed.")]
	public void InstallProcessBuilderTool_Should_Not_Be_FeatureGated() {
		// Arrange & Act
		object[] toggles = typeof(InstallProcessBuilderTool)
			.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(
			because: "a gated primitive is filtered out of MCP registration, so gating the installer would "
				+ "hide it while the gated process-designer tools keep telling callers to run it");
	}

	private sealed class FakeInstallProcessBuilderCommand : InstallProcessBuilderCommand {
		private readonly int _exitCode;

		public FakeInstallProcessBuilderCommand(int exitCode)
			: base(
				new EnvironmentSettings(),
				Substitute.For<IPackageInstaller>(),
				Substitute.For<IWorkingDirectoriesProvider>(),
				Substitute.For<IFileSystem>(),
				Substitute.For<IRequiredPackageChecker>(),
				Substitute.For<IApplicationClient>(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IServerReadinessWaiter>(),
				Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public InstallProcessBuilderOptions? CapturedOptions { get; private set; }

		public override int Execute(InstallProcessBuilderOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
}
