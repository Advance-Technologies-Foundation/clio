using Autofac;
using Clio.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

public class ManageWindowsFeaturesCommandTestFixture : BaseClioModuleTests
{

	IWindowsFeatureManager _windowsFeatureManager = Substitute.For<IWindowsFeatureManager>();
	ManageWindowsFeaturesCommand _sut;
	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_windowsFeatureManager).As<IWindowsFeatureManager>();
	}

	public override void Setup(){
		base.Setup();
		_sut = Container.Resolve<ManageWindowsFeaturesCommand>();
	}

	[TestCase("install")]
	[TestCase("uninstall")]
	public void InstallComponent_Calls_WindowsFeatureManager(string actionName){

		//Arrange
		var options = new ManageWindowsFeaturesOptions {
			InstallMode = actionName == "install",
			UnistallMode = actionName == "uninstall"
		};

		//Act
		var actual = _sut.Execute(options);

		//Assert
		actual.Should().Be(0);
		
		if(actionName == "install") {
			_windowsFeatureManager.Received(1).InstallMissingFeatures();
		}
		if(actionName == "ininstall") {
			_windowsFeatureManager.Received(1).UnInstallMissingFeatures();
		}
		
		
	}

}