using Clio.Command;
using Clio.Common;
using Clio.Common.IIS;
using Clio.Common.SystemServices;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Description("Tests for HostsCommand - verifies documentation exists for hosts command")]
[Property("Module", "Command")]
public class HostsCommandTests : BaseCommandTests<HostsOptions>
{
	#region Fields: Private

	private ILogger _logger;
	private ISettingsRepository _settingsRepository;
	private ISystemServiceManager _serviceManager;
	private IIISSiteDetector _iisSiteDetector;
	private HostsCommand _hostsCommand;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_serviceManager = Substitute.For<ISystemServiceManager>();
		_iisSiteDetector = Substitute.For<IIISSiteDetector>();
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_serviceManager);
		containerBuilder.AddSingleton(_iisSiteDetector);
		containerBuilder.AddTransient<HostsCommand>();
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public override void Setup() {
		base.Setup();
		_hostsCommand = Container.GetRequiredService<HostsCommand>();
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Execute with --json emits a JSON array and suppresses progress messages when no hosts are found.")]
	public void Execute_WhenJson_EmitsJsonArrayAndSuppressesProgress() {
		// Arrange
		HostsOptions options = new() { Json = true };

		// Act
		int result = _hostsCommand.Execute(options);

		// Assert
		result.Should().Be(0, because: "listing hosts as JSON succeeds even when no hosts exist");
		_logger.Received(1).WriteLine(Arg.Is<string>(s => s.Contains("[")));
		_logger.DidNotReceive().WriteInfo(Arg.Any<string>());
	}

	#endregion
}
