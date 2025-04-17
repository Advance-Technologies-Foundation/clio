using Autofac;
using Clio.Command;
using Clio.UserEnvironment;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class RegAppCommandTests: BaseClioModuleTests
{
	private ISettingsRepository _settingsRepository;

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_settingsRepository = Substitute.For<ISettingsRepository>();
		containerBuilder.RegisterInstance(_settingsRepository).As<ISettingsRepository>();
	}

	[Test]
	public void RegAppCommandTests_ActivateFromDI_ShouldReturnInstance() {
		var command = Container.Resolve<RegAppCommand>();
		Assert.That(command != null);
	}

	[Test]
	public void RegAppCommand_ShouldNotThrowException_WithCfgOpenParaameters() {
		var command = Container.Resolve<RegAppCommand>();
		RegAppOptions openCfgOpts = new RegAppOptions() {
			EnvironmentName = "open"
		};
		Assert.DoesNotThrow(() => command.Execute(openCfgOpts));
		_settingsRepository.Received(1).OpenFile();
	}
}