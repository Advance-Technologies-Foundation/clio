using System;
using System.Collections.Generic;
using System.Management.Automation;
using Autofac;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
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

	[OneTimeSetUp]
	public void VerifyWindowsPlatform() {
		if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
			Assert.Ignore("This test class is Windows-only");
		}
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
			UninstallMode = actionName == "uninstall"
		};

		//Act
		var actual = _sut.Execute(options);

		//Assert
		actual.Should().Be(0);
		
		if(actionName == "install") {
			_windowsFeatureManager.Received(1).InstallMissingFeatures();
		}
		if(actionName == "uninstall") {
			_windowsFeatureManager.Received(1).UninstallMissingFeatures();
		}
		
		
	}

	[Test, Category("Unit")]
	public void GetMissedComponents_ShouldInstallMissedComponents() {
		// Arrange
		var existingComponents = new List<WindowsFeature> {
			new WindowsFeature { Name = "Feature3", Installed = false },
			new WindowsFeature { Name = "Feature4", Installed = false }
		};

		IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
		var windowsFeatureManager = new WindowsFeatureManager(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger) {
			RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"],
		};

		// Act
		var missingComponents = windowsFeatureManager.GetMissedComponents();

		// Assert
		missingComponents.Should().HaveCount(2);
	}

	[Test, Category("Unit")]
	public void GetMissedComponents_CorrectWorking_IfAllFeatureExisting() {
		// Arrange
		var existingComponents = new List<WindowsFeature> {
			new WindowsFeature { Name = "Feature1", Installed = true },
			new WindowsFeature { Name = "Feature2", Installed = true }
		};

		IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
		windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
		var windowsFeatureManager = new WindowsFeatureManager(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger) {
			RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"],
		};

		// Act
		var missingComponents = windowsFeatureManager.GetMissedComponents();

		// Assert
		missingComponents.Should().HaveCount(0);
	}

	[Test, Category("Unit")]
	public void InstallMissingFeatures_NotThrow_IfAllFeatureExisting() {
		// Arrange
		var existingComponents = new List<WindowsFeature> {
			new WindowsFeature { Name = "Feature1", Installed = true },
			new WindowsFeature { Name = "Feature2", Installed = true }
		};

		IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
		windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
		var windowsFeatureManager = new WindowsFeatureManager(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger) {
			RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"],
		};

		// Act
		Action act = () =>  windowsFeatureManager.InstallMissingFeatures();
		// Assert
		act.Should().NotThrow();

	}

	[Test, Category("Unit")]
	public void InstpallMissingFeatures_ThrowItemNotExistException_IfFeatureMissingOnServer() {
		// Arrange
		var existingComponents = new List<WindowsFeature> {
			new WindowsFeature { Name = "Feature1", Installed = true },
			new WindowsFeature { Name = "Feature2", Installed = true }
		};

		IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
		windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
		var windowsFeatureManager = new WindowsFeatureManager(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger) {
			RequirmentNETFrameworkFeatures = ["Feature3"]
		};

		// Act
		Action act = () => windowsFeatureManager.InstallMissingFeatures();
		// Assert
		act.Should().Throw<ItemNotFoundException>();

	}

}

