using System;

using Clio.Command.PackageCommand;
using Clio.Common;
using Clio.Package;
using Clio.Package.Responses;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class ActivatePackageCommandTestCase
{
    [Test]
    [Category("Unit")]
    public void Execute_ActivatesPackage()
    {
        IPackageActivator? packageActivator = Substitute.For<IPackageActivator>();
        IApplicationClient? applicationClient = Substitute.For<IApplicationClient>();
        ILogger? logger = Substitute.For<ILogger>();
        string packageName = "TestPackageName";
        string packageActivatedWithError = "PackageActivatedWithError";
        string notActivatedPackage = "NotActivatedPackage";
        packageActivator.Activate(packageName).Returns(new[]
        {
            new PackageActivationResultDto { PackageName = packageName, Success = true },
            new PackageActivationResultDto
            {
                PackageName = packageActivatedWithError, Success = true, Message = "SomeError"
            },
            new PackageActivationResultDto { PackageName = notActivatedPackage, Success = false }
        });
        ActivatePackageCommand command = new (packageActivator, applicationClient, new EnvironmentSettings(), logger);
        command.Execute(new ActivatePkgOptions { PackageName = packageName }).Should().Be(0);
        logger.Received().WriteLine($"Start activation package: \"{packageName}\"");
        logger.Received().WriteLine($"Package \"{packageName}\" successfully activated.");
        logger.Received().WriteLine($"Package \"{packageActivatedWithError}\" was activated with errors.");
        logger.Received().WriteLine($"Package \"{notActivatedPackage}\" was not activated.");
    }

    [Test]
    [Category("Unit")]
    public void Execute_ShowsErrorMessage_WhenErrorOccured()
    {
        IPackageActivator? packageActivator = Substitute.For<IPackageActivator>();
        IApplicationClient? applicationClient = Substitute.For<IApplicationClient>();
        ILogger? logger = Substitute.For<ILogger>();
        string packageName = "TestPackageName";
        string errorMessage = "SomeErrorMessage";
        packageActivator.When(activator => activator.Activate(packageName)).Throw(new Exception(errorMessage));
        ActivatePackageCommand command = new (packageActivator, applicationClient, new EnvironmentSettings(), logger);
        command.Execute(new ActivatePkgOptions { PackageName = packageName }).Should().Be(1);
        logger.Received().WriteLine(errorMessage);
        logger.DidNotReceive().WriteLine($"Package \"{packageName}\" successfully activated.");
    }
}
