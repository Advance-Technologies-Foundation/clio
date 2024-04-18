using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture(Category = "Unit")]
public class PackageHotFixCommandTestCase : BaseCommandTests<PackageHotFixCommandOptions>
{

	#region Setup/Teardown

	[SetUp]
	public void Init(){
		_packageEditableMutator = Substitute.For<IPackageEditableMutator>();
		_logger = Substitute.For<ILogger>();
		_command = new PackageHotFixCommand(_packageEditableMutator, new EnvironmentSettings()) {
			Logger = _logger
		};
	}

	#endregion

	#region Constants: Private

	private const string PackageName = "TestPackageName";

	#endregion

	#region Fields: Private

	private PackageHotFixCommand _command;
	private ILogger _logger;
	private IPackageEditableMutator _packageEditableMutator;

	#endregion


	[Test]
	[Category("Unit")]
	public void Execute_ShowsErrorMessage_WhenPackageHotFixModeNotSet(){
		//Arrange
		const string errorMessage = "SomeErrorMessage";
		PackageHotFixCommandOptions options = new() {
			PackageName = PackageName,
			Enable = true
		};
		_packageEditableMutator.When(mutator => mutator.SetPackageHotfix(PackageName, true))
			.Throw(new Exception(errorMessage));

		//Act
		Action act = () => _command.Execute(options);

		//Assert
		act.Should().Throw<Exception>().WithMessage(errorMessage);
		_logger.Received().WriteInfo($"Enable hotfix state for package: \"{PackageName}\"");
	}

	[Test]
	[Category("Unit")]
	public void Execute_StartsHotFixMode(){
		//Arrange
		PackageHotFixCommandOptions options = new() {
			PackageName = PackageName,
			Enable = true
		};

		//Act
		int result = _command.Execute(options);

		//Assert
		result.Should().Be(0);
		_logger.Received().WriteInfo($"Enable hotfix state for package: \"{PackageName}\"");
		_logger.Received().WriteInfo("Done");
		_packageEditableMutator.Received(1).SetPackageHotfix(PackageName, true);
	}

	[Test]
	[Category("Unit")]
	public void Execute_DisableHotFixMode() {
		//Arrange
		PackageHotFixCommandOptions options = new() {
			PackageName = PackageName,
			Enable = false
		};

		//Act
		int result = _command.Execute(options);

		//Assert
		result.Should().Be(0);
		_logger.Received().WriteInfo($"Disable hotfix state for package: \"{PackageName}\"");
		_logger.Received().WriteInfo("Done");
		_packageEditableMutator.Received(1).SetPackageHotfix(PackageName, false);
	}


}