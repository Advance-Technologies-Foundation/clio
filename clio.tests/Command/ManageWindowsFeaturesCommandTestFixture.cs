using System;
using System.Collections.Generic;
using System.Management.Automation;
using Autofac;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

public class ManageWindowsFeaturesCommandTestFixture : BaseClioModuleTests
{
    private readonly IWindowsFeatureManager _windowsFeatureManager = Substitute.For<IWindowsFeatureManager>();
    private ManageWindowsFeaturesCommand _sut;

    protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
    {
        base.AdditionalRegistrations(containerBuilder);
        containerBuilder.RegisterInstance(_windowsFeatureManager).As<IWindowsFeatureManager>();
    }

    public override void Setup()
    {
        base.Setup();
        _sut = container.Resolve<ManageWindowsFeaturesCommand>();
    }

    [TestCase("install")]
    [TestCase("uninstall")]
    public void InstallComponent_Calls_WindowsFeatureManager(string actionName)
    {
        // Arrange
        ManageWindowsFeaturesOptions options = new()
        {
            InstallMode = actionName == "install", UnistallMode = actionName == "uninstall"
        };

        // Act
        int actual = _sut.Execute(options);

        // Assert
        actual.Should().Be(0);

        if (actionName == "install")
        {
            _windowsFeatureManager.Received(1).InstallMissingFeatures();
        }

        if (actionName == "ininstall")
        {
            _windowsFeatureManager.Received(1).UnInstallMissingFeatures();
        }
    }

    [Test]
    [Category("Unit")]
    public void GetMissedComponents_ShouldInstallMissedComponents()
    {
        // Arrange
        List<WindowsFeature> existingComponents =
        [
            new() { Name = "Feature3", Installed = false },
            new() { Name = "Feature4", Installed = false }
        ];

        IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
        IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
        ILogger logger = Substitute.For<ILogger>();
        windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
        WindowsFeatureManager windowsFeatureManager =
            new(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger)
            {
                RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"]
            };

        // Act
        List<WindowsFeature> missingComponents = windowsFeatureManager.GetMissedComponents();

        // Assert
        missingComponents.Should().HaveCount(2);
    }

    [Test]
    [Category("Unit")]
    public void GetMissedComponents_CorrectWorking_IfAllFeatureExisting()
    {
        // Arrange
        List<WindowsFeature> existingComponents =
        [
            new() { Name = "Feature1", Installed = true },
            new() { Name = "Feature2", Installed = true }
        ];

        IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
        IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
        ILogger logger = Substitute.For<ILogger>();
        windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
        windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
        WindowsFeatureManager windowsFeatureManager =
            new(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger)
            {
                RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"]
            };

        // Act
        List<WindowsFeature> missingComponents = windowsFeatureManager.GetMissedComponents();

        // Assert
        missingComponents.Should().HaveCount(0);
    }

    [Test]
    [Category("Unit")]
    public void InstallMissingFeatures_NotThrow_IfAllFeatureExisting()
    {
        // Arrange
        List<WindowsFeature> existingComponents =
        [
            new() { Name = "Feature1", Installed = true },
            new() { Name = "Feature2", Installed = true }
        ];

        IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
        IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
        ILogger logger = Substitute.For<ILogger>();
        windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
        windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
        WindowsFeatureManager windowsFeatureManager =
            new(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger)
            {
                RequirmentNETFrameworkFeatures = ["Feature1", "Feature2"]
            };

        // Act
        Action act = () => windowsFeatureManager.InstallMissingFeatures();

        // Assert
        act.Should().NotThrow();
    }

    [Test]
    [Category("Unit")]
    public void InstpallMissingFeatures_ThrowItemNotExistException_IfFeatureMissingOnServer()
    {
        // Arrange
        List<WindowsFeature> existingComponents =
        [
            new() { Name = "Feature1", Installed = true },
            new() { Name = "Feature2", Installed = true }
        ];

        IWorkingDirectoriesProvider wp = Substitute.For<IWorkingDirectoriesProvider>();
        IWindowsFeatureProvider windowsFeatureProvider = Substitute.For<IWindowsFeatureProvider>();
        ILogger logger = Substitute.For<ILogger>();
        windowsFeatureProvider.GetWindowsFeatures().Returns(existingComponents);
        windowsFeatureProvider.GetActiveWindowsFeatures().Returns(["Feature1", "Feature2"]);
        WindowsFeatureManager windowsFeatureManager =
            new(wp, new ConsoleProgressbar(), windowsFeatureProvider, logger)
            {
                RequirmentNETFrameworkFeatures = ["Feature3"]
            };

        // Act
        Action act = () => windowsFeatureManager.InstallMissingFeatures();

        // Assert
        act.Should().Throw<ItemNotFoundException>();
    }
}
