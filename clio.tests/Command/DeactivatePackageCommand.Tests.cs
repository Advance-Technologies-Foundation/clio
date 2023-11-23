using System;
using Clio.Command.PackageCommand;
using Clio.Common;
using Clio.Package;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class DeactivatePackageCommandTestCase {

	#region Methods: Public

	[Test, Category("Unit")]
	public void Execute_DeactivatesPackage()
	{
		var packageDeactivator = Substitute.For<IPackageDeactivator>();
		var applicationClient = Substitute.For<IApplicationClient>();
		var logger = Substitute.For<ILogger>();
		var packageName = "TestPackageName";
		packageDeactivator.Deactivate(packageName);
		var command = new DeactivatePackageCommand(packageDeactivator, applicationClient, new EnvironmentSettings(), logger);
		Assert.AreEqual(0, command.Execute(new DeactivatePkgOptions { PackageName = packageName }));
		logger.Received().WriteLine($"Start deactivation package: \"{packageName}\"");
		logger.Received().WriteLine($"Package \"{packageName}\" successfully deactivated.");
	}

	[Test, Category("Unit")]
	public void Execute_ShowsErrorMessage_WhenPackageWasNotDeactivated()
	{
		var packageDeactivator = Substitute.For<IPackageDeactivator>();
		var applicationClient = Substitute.For<IApplicationClient>();
		var logger = Substitute.For<ILogger>();
		var packageName = "TestPackageName";
		var errorMessage = "SomeErrorMessage";
		packageDeactivator.When(deactivator => deactivator.Deactivate(packageName)).Throw(new Exception(errorMessage));
		var command = new DeactivatePackageCommand(packageDeactivator, applicationClient, new EnvironmentSettings(), logger);
		Assert.AreEqual(1, command.Execute(new DeactivatePkgOptions { PackageName = packageName}));
		logger.Received().WriteLine($"Start deactivation package: \"{packageName}\"");
		logger.Received().WriteLine(errorMessage);
		logger.DidNotReceive().WriteLine($"Package \"{packageName}\" successfully deactivated.");
	}

	#endregion

}