using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins the agent-facing contract of <c>install-dashboards-migrator</c>. The execution shape (heartbeat,
/// deadline, configuration-build reservation) is the same code path <see cref="InstallProcessBuilderToolTests"/>
/// exercises and is not re-tested here.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class InstallDashboardsMigratorToolTests {

	[Test]
	[Category("Unit")]
	[Description("Resolves InstallDashboardsMigratorCommand for the requested environment, forwards only the environment name, and returns the real command exit code.")]
	public async Task InstallDashboardsMigrator_Should_Resolve_Command_For_Environment_And_Return_Exit_Code() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeInstallDashboardsMigratorCommand resolvedCommand = new(exitCode: 1);
		commandResolver.Resolve<InstallDashboardsMigratorCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		InstallDashboardsMigratorTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result =
				await tool.InstallDashboardsMigrator(new InstallDashboardsMigratorArgs("sandbox"));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "the MCP tool must surface the command's own verdict, including a failed outcome check");
			resolvedCommand.CapturedOptions!.Environment.Should().Be("sandbox",
				because: "the environment-name argument maps into InstallDashboardsMigratorOptions");
			resolvedCommand.CapturedOptions.Force.Should().BeFalse(
				because: "the downgrade override is CLI-only; the mapping must leave it at its default");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes destructive, idempotent metadata under the stable tool name, is not feature-gated, and describes the outcome check without quoting a duration.")]
	public void InstallDashboardsMigrator_Should_Expose_Expected_Mcp_Metadata() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(InstallDashboardsMigratorTool)
			.GetMethod(nameof(InstallDashboardsMigratorTool.InstallDashboardsMigrator))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Cast<McpServerToolAttribute>()
			.Single();
		string description = method
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single()
			.Description;

		// Assert
		attribute.Name.Should().Be("install-dashboards-migrator",
			because: "the curated contract, the CLI verb and the docs share this identifier");
		attribute.Destructive.Should().BeTrue(
			because: "the tool runs a configuration build on a live instance and restarts it");
		attribute.Idempotent.Should().BeTrue(
			because: "a sequential re-run installs again or hits the same refusal; both converge");
		typeof(InstallDashboardsMigratorTool).GetCustomAttributes(typeof(FeatureToggleAttribute), true)
			.Should().BeEmpty(because: "a gated primitive is filtered out of MCP registration");
		description.Should().Contain(BundledPackages.DashboardsMigratorPackageName,
			because: "the description names the package the tool installs");
		description.Should().Contain("Ping",
			because: "the description discloses HOW the outcome is checked");
		description.Should().NotMatchRegex(InstallProcessBuilderToolTests.DurationRangePattern,
			because: "elapsed time is a property of the target; a figure here is read as a promise");
		description.Should().NotMatchRegex(InstallProcessBuilderToolTests.DurationFigurePattern,
			because: "a single figure is the same promise as a range");
		typeof(InstallDashboardsMigratorArgs).GetProperties().Select(property => property.Name)
			.Should().BeEquivalentTo([nameof(InstallDashboardsMigratorArgs.EnvironmentName)],
				because: "every member here becomes an agent-reachable argument; --force must stay unreachable");
	}

	private sealed class FakeInstallDashboardsMigratorCommand : InstallDashboardsMigratorCommand {

		private readonly int _exitCode;

		public FakeInstallDashboardsMigratorCommand(int exitCode)
			: base(new EnvironmentSettings(), Substitute.For<Clio.Package.IPackageInstaller>(),
				Substitute.For<IBundledPackageCatalog>(), Substitute.For<Clio.Package.IPackageInstallOutcomeVerifier>(),
				Substitute.For<IServerReadinessWaiter>(), Substitute.For<IRequiredPackageChecker>(),
				Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public InstallDashboardsMigratorOptions CapturedOptions { get; private set; }

		public override int Execute(InstallDashboardsMigratorOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}

	}

}
