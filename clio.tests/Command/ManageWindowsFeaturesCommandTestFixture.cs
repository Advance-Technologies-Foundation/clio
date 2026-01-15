using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Runtime.InteropServices;
using Autofac;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

public class ManageWindowsFeaturesCommandTestFixture : BaseClioModuleTests{
	#region Fields: Private

	private readonly IWindowsFeatureManager _windowsFeatureManager = Substitute.For<IWindowsFeatureManager>();

	private ManageWindowsFeaturesCommand _sut;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_windowsFeatureManager).As<IWindowsFeatureManager>();
	}

	#endregion

	#region Methods: Public

	[Test]
	[Category("Unit")]
	[Description("Verifies that GetMissedComponents returns an empty list when all required features are installed")]
	public void GetMissedComponents_CorrectWorking_IfAllFeatureExisting() {
		// Arrange
		List<WindowsFeature> existingComponents = [
			new() { Name = "Feature1", Installed = true },
			new() { Name = "Feature2", Installed = true }
		];

		IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		INetFrameworkVersionChecker netFrameworkVersionChecker = Substitute.For<INetFrameworkVersionChecker>();
		windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
		windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
		WindowsFeatureManager windowsFeatureManager
			= new(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger, netFrameworkVersionChecker) {
				RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"]
			};

		// Act
		List<WindowsFeature> missingComponents = windowsFeatureManager.GetMissedComponents();

		// Assert
		missingComponents.Should().HaveCount(0, "all required features are already installed and active");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that GetMissedComponents returns two components when required features are not installed")]
	public void GetMissedComponents_ShouldInstallMissedComponents() {
		// Arrange
		List<WindowsFeature> existingComponents = [
			new() { Name = "Feature3", Installed = false },
			new() { Name = "Feature4", Installed = false }
		];

		IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		INetFrameworkVersionChecker netFrameworkVersionChecker = Substitute.For<INetFrameworkVersionChecker>();
		windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
		WindowsFeatureManager windowsFeatureManager
			= new(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger, netFrameworkVersionChecker) {
				RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"]
			};

		// Act
		List<WindowsFeature> missingComponents = windowsFeatureManager.GetMissedComponents();

		// Assert
		missingComponents.Should().HaveCount(2,
			"Feature1 and Feature2 are required but not available in the existing components list");
	}

	[TestCase("install")]
	[TestCase("uninstall")]
	[Description(
		"Verifies that Execute method correctly calls WindowsFeatureManager install or uninstall methods based on the mode")]
	public void InstallComponent_Calls_WindowsFeatureManager(string actionName) {
		// Arrange
		ManageWindowsFeaturesOptions options = new() {
			InstallMode = actionName == "install",
			UninstallMode = actionName == "uninstall"
		};

		// Act
		int actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "the command should complete successfully");

		if (actionName == "install") {
			_windowsFeatureManager.Received(1).InstallMissingFeatures();
		}

		if (actionName == "uninstall") {
			_windowsFeatureManager.Received(1).UninstallMissingFeatures();
		}
	}

	[Test]
	[Category("Unit")]
	[Description(
		"Verifies that InstallMissingFeatures does not throw an exception when all required features are already installed")]
	public void InstallMissingFeatures_NotThrow_IfAllFeatureExisting() {
		// Arrange
		List<WindowsFeature> existingComponents = [
			new() { Name = "Feature1", Installed = true },
			new() { Name = "Feature2", Installed = true }
		];

		IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		INetFrameworkVersionChecker netFrameworkVersionChecker = Substitute.For<INetFrameworkVersionChecker>();
		windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
		windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
		WindowsFeatureManager windowsFeatureManager
			= new(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger, netFrameworkVersionChecker) {
				RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"]
			};

		// Act
		Action act = () => windowsFeatureManager.InstallMissingFeatures();

		// Assert
		act.Should().NotThrow("all required features are already installed and no installation is needed");
	}

	[Test]
	[Category("Unit")]
	[Description(
		"Verifies that InstallMissingFeatures throws ItemNotFoundException when a required feature is not available on the server")]
	public void InstallMissingFeatures_ThrowItemNotExistException_IfFeatureMissingOnServer() {
		// Arrange
		List<WindowsFeature> existingComponents = [
			new() { Name = "Feature1", Installed = true },
			new() { Name = "Feature2", Installed = true }
		];

		IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
		IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
		ILogger logger = Substitute.For<ILogger>();
		INetFrameworkVersionChecker netFrameworkVersionChecker = Substitute.For<INetFrameworkVersionChecker>();
		windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
		windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
		WindowsFeatureManager windowsFeatureManager
			= new(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger, netFrameworkVersionChecker) {
				RequirmentNETFrameworkFeatures = ["Feature3"]
			};

		// Act
		Action act = () => windowsFeatureManager.InstallMissingFeatures();

		// Assert
		act.Should()
		   .Throw<ItemNotFoundException>("Feature3 is required but not available in the server's feature list");
	}

	public override void Setup() {
		base.Setup();
		_sut = Container.Resolve<ManageWindowsFeaturesCommand>();
	}

	[OneTimeSetUp]
	public void VerifyWindowsPlatform() {
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			Assert.Ignore("This test class is Windows-only");
		}
	}

	#endregion
}
