using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using FluentAssertions;
using k8s;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal sealed class InstallerCommandSilentSiteNameTests : BaseCommandTests<PfInstallerOptions> {
	private readonly ICreatioInstallerService _creatioInstallerService = Substitute.For<ICreatioInstallerService>();
	private readonly IDeployCreatioDefaultsResolver _defaultsResolver =
		Substitute.For<IDeployCreatioDefaultsResolver>();
	private readonly IKubernetes _kubernetes = Substitute.For<IKubernetes>();
	private readonly ILogger _logger = Substitute.For<ILogger>();

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_creatioInstallerService);
		containerBuilder.AddSingleton<IDbOperationLogSessionFactory>(_ => NullDbOperationLogSessionFactory.Instance);
		containerBuilder.AddSingleton(_defaultsResolver);
		containerBuilder.AddSingleton(_kubernetes);
		containerBuilder.AddSingleton(_logger);
	}

	public override void TearDown() {
		_creatioInstallerService.ClearReceivedCalls();
		_defaultsResolver.ClearReceivedCalls();
		_kubernetes.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_kubernetes.Dispose();
	}

	[Test]
	[Description("Silent deployment without an explicit or configured site name fails instead of waiting for console input.")]
	public void Execute_ShouldFailWithoutRunningInstaller_WhenSilentSiteNameIsMissing() {
		// Arrange
		InstallerCommand command = Container.GetRequiredService<InstallerCommand>();
		PfInstallerOptions options = new() {
			DbServerName = "local-postgres",
			IsSilent = true,
			ZipFile = @"C:\CreatioBuilds\creatio.zip"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "silent deployment cannot ask a user to provide the missing site name");
		_creatioInstallerService.ReceivedCalls().Should().BeEmpty(
			because: "validation must fail before the installer reaches its interactive site-name prompt");
		string[] errors = _logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Select(call => call.GetArguments().Single()?.ToString())
			.Where(message => message is not null)
			.Cast<string>()
			.ToArray();
		errors.Should().Contain(
			"Site name is required for silent deployment. Specify --site-name or configure deploy-site-name.",
			because: "the error should explain how an unattended caller can provide the required site name");
	}

	[TestCase(0, false)]
	[TestCase(1, true)]
	[Description("Explorer deployment prompts for acknowledgement only when installation fails.")]
	public void Execute_ShouldPromptForExitOnlyOnFailure_WhenLaunchedFromExplorer(int installerResult,
		bool shouldPrompt) {
		// Arrange
		_creatioInstallerService.Execute(Arg.Any<PfInstallerOptions>()).Returns(installerResult);
		InstallerCommand command = Container.GetRequiredService<InstallerCommand>();
		PfInstallerOptions options = new() {
			DbServerName = "local-postgres",
			ExplorerLaunch = true,
			SiteName = "issue874",
			ZipFile = @"C:\CreatioBuilds\creatio.zip"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(installerResult,
			because: "Explorer deployment must preserve the installer exit code while managing its terminal lifetime");
		int promptCount = _logger.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(ILogger.WriteLine)
			&& Equals(call.GetArguments().SingleOrDefault(), "Press enter to exit..."));
		promptCount.Should().Be(shouldPrompt ? 1 : 0,
			because: "Explorer must remain open after failure and close without an acknowledgement prompt after success");
	}
}
