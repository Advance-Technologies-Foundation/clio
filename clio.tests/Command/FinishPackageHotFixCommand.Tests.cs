using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
public class FinishPackageHotFixCommandTestCase : BaseCommandTests<FinishPackageHotFixCommandOptions>
{

	#region Setup/Teardown

	[SetUp]
	public void Init(){
		_packageEditableMutator = Substitute.For<IPackageEditableMutator>();
		_logger = Substitute.For<ILogger>();
		_command = new FinishPackageHotFixCommand(_packageEditableMutator, new EnvironmentSettings()) {
			Logger = _logger
		};
	}

	#endregion

	#region Constants: Private

	private const string PackageName = "TestPackageName";

	#endregion

	#region Fields: Private

	private FinishPackageHotFixCommand _command;
	private ILogger _logger;
	private IPackageEditableMutator _packageEditableMutator;

	#endregion

	[Test]
	[Category("Unit")]
	public void Execute_FinishesHotFixMode(){
		//Arrange
		FinishPackageHotFixCommandOptions options = new() {
			PackageName = PackageName
		};

		//Act
		int result = _command.Execute(options);

		//Assert
		result.Should().Be(0);
		_logger.Received().WriteInfo($"Ends hotfix state for package: \"{PackageName}\"");
		_logger.Received().WriteInfo("Done");
		_packageEditableMutator.Received(1).FinishPackageHotfix(PackageName);
	}

	[Test]
	[Category("Unit")]
	public void Execute_ShowsErrorMessage_WhenPackageHotFixModeNotSet(){
		//Arrange
		const string errorMessage = "SomeErrorMessage";
		FinishPackageHotFixCommandOptions options = new() {
			PackageName = PackageName
		};

		_packageEditableMutator.When(mutator => mutator.FinishPackageHotfix(PackageName))
			.Throw(new Exception(errorMessage));

		//Act
		Action act = () => _command.Execute(options);

		//Assert
		act.Should().Throw<Exception>().WithMessage(errorMessage);
		_logger.Received().WriteInfo($"Ends hotfix state for package: \"{PackageName}\"");
	}

}