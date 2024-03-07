using System;
using Clio.Command.PackageCommand;
using Clio.Common;
using Clio.Package;
using Clio.Package.Responses;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class ActivatePackageCommandTestCase {

	#region Methods: Public

	[Test, Category("Unit")]
	public void Execute_ActivatesPackage()
	{
		var packageActivator = Substitute.For<IPackageActivator>();
		var applicationClient = Substitute.For<IApplicationClient>();
		var logger = Substitute.For<ILogger>();
		var packageName = "TestPackageName";
		var packageActivatedWithError = "PackageActivatedWithError";
		var notActivatedPackage = "NotActivatedPackage";
		packageActivator.Activate(packageName).Returns(new [] {
			new PackageActivationResultDto {
				PackageName = packageName,
				Success = true
			},
			new PackageActivationResultDto {
				PackageName = packageActivatedWithError,
				Success = true,
				Message = "SomeError"
			},
			new PackageActivationResultDto {
				PackageName = notActivatedPackage,
				Success = false
			}
		});
		var command = new ActivatePackageCommand(packageActivator, applicationClient, new EnvironmentSettings(), logger);
		Assert.AreEqual(0, command.Execute(new ActivatePkgOptions { PackageName = packageName }));
		logger.Received().WriteLine($"Start activation package: \"{packageName}\"");
		logger.Received().WriteLine($"Package \"{packageName}\" successfully activated.");
		logger.Received().WriteLine($"Package \"{packageActivatedWithError}\" was activated with errors.");
		logger.Received().WriteLine($"Package \"{notActivatedPackage}\" was not activated.");
	}

	[Test, Category("Unit")]
	public void Execute_ShowsErrorMessage_WhenErrorOccured()
	{
		var packageActivator = Substitute.For<IPackageActivator>();
		var applicationClient = Substitute.For<IApplicationClient>();
		var logger = Substitute.For<ILogger>();
		var packageName = "TestPackageName";
		var errorMessage = "SomeErrorMessage";
		packageActivator.When(activator => activator.Activate(packageName)).Throw(new Exception(errorMessage));
		var command = new ActivatePackageCommand(packageActivator, applicationClient, new EnvironmentSettings(), logger);
		Assert.AreEqual(1, command.Execute(new ActivatePkgOptions { PackageName = packageName}));
		logger.Received().WriteLine(errorMessage);
		logger.DidNotReceive().WriteLine($"Package \"{packageName}\" successfully activated.");
	}

	#endregion

}