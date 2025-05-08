using System;

using Clio.Command.PackageCommand;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class DeactivatePackageCommandTestCase
{
    [Test]
    [Category("Unit")]
    public void Execute_DeactivatesPackage()
    {
        IPackageDeactivator? packageDeactivator = Substitute.For<IPackageDeactivator>();
        IApplicationClient? applicationClient = Substitute.For<IApplicationClient>();
        ILogger? logger = Substitute.For<ILogger>();
        string packageName = "TestPackageName";
        packageDeactivator.Deactivate(packageName);
        DeactivatePackageCommand command =
            new (packageDeactivator, applicationClient, new EnvironmentSettings()) { Logger = logger };
        command.Execute(new DeactivatePkgOptions { PackageName = packageName }).Should().Be(0);
        logger.Received().WriteLine($"Start deactivation package: \"{packageName}\"");
        logger.Received().WriteLine($"Package \"{packageName}\" successfully deactivated.");
    }

    [Test]
    [Category("Unit")]
    public void Execute_ShowsErrorMessage_WhenPackageWasNotDeactivated()
    {
        IPackageDeactivator? packageDeactivator = Substitute.For<IPackageDeactivator>();
        IApplicationClient? applicationClient = Substitute.For<IApplicationClient>();
        ILogger? logger = Substitute.For<ILogger>();
        string packageName = "TestPackageName";
        string errorMessage = "SomeErrorMessage";
        packageDeactivator.When(deactivator => deactivator.Deactivate(packageName)).Throw(new Exception(errorMessage));
        DeactivatePackageCommand command =
            new (packageDeactivator, applicationClient, new EnvironmentSettings()) { Logger = logger };
        command.Execute(new DeactivatePkgOptions { PackageName = packageName }).Should().Be(1);
        logger.Received().WriteLine($"Start deactivation package: \"{packageName}\"");
        logger.Received().WriteLine(errorMessage);
        logger.DidNotReceive().WriteLine($"Package \"{packageName}\" successfully deactivated.");
    }
}
