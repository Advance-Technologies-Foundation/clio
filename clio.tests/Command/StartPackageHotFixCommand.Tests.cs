using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture(Category = "Unit")]
public class StartPackageHotFixCommandTestCase : BaseCommandTests<StartPackageHotFixCommandOptions>
{

	#region Setup/Teardown

	[SetUp]
	public void Init(){
		_packageEditableMutator = Substitute.For<IPackageEditableMutator>();
		_logger = Substitute.For<ILogger>();
		_command = new StartPackageHotFixCommand(_packageEditableMutator, new EnvironmentSettings()) {
			Logger = _logger
		};
	}

	#endregion

	#region Constants: Private

	private const string PackageName = "TestPackageName";

	#endregion

	#region Fields: Private

	private StartPackageHotFixCommand _command;
	private ILogger _logger;
	private IPackageEditableMutator _packageEditableMutator;

	#endregion


	[Test]
	[Category("Unit")]
	public void Execute_ShowsErrorMessage_WhenPackageHotFixModeNotSet(){
		//Arrange
		const string errorMessage = "SomeErrorMessage";
		StartPackageHotFixCommandOptions options = new() {
			PackageName = PackageName
		};
		_packageEditableMutator.When(mutator => mutator.StartPackageHotfix(PackageName))
			.Throw(new Exception(errorMessage));

		//Act
		Action act = () => _command.Execute(options);

		//Assert
		act.Should().Throw<Exception>().WithMessage(errorMessage);
		_logger.Received().WriteInfo($"Starts hotfix state for package: \"{PackageName}\"");
	}

	[Test]
	[Category("Unit")]
	public void Execute_StartsHotFixMode(){
		//Arrange
		StartPackageHotFixCommandOptions options = new() {
			PackageName = PackageName
		};

		//Act
		int result = _command.Execute(options);

		//Assert
		result.Should().Be(0);
		_logger.Received().WriteInfo($"Starts hotfix state for package: \"{PackageName}\"");
		_packageEditableMutator.Received(1).StartPackageHotfix(PackageName);
	}

}