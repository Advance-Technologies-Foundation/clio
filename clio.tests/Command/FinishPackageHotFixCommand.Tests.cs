using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class FinishPackageHotFixCommandTestCase
{
	#region Fields: Private

	private FinishPackageHotFixCommand _command;
	private ILogger _logger;
	private IPackageEditableMutator _packageEditableMutator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void Init()
	{
		_packageEditableMutator = Substitute.For<IPackageEditableMutator>();
		_logger = Substitute.For<ILogger>();
		_command = new FinishPackageHotFixCommand(_packageEditableMutator, _logger, new EnvironmentSettings());
	}

	[Test, Category("Unit")]
	public void Execute_FinishesHotFixMode()
	{
		string packageName = "TestPackageName";
		var options = new FinishPackageHotFixCommandOptions
		{
			PackageName = packageName
		};
		var result = _command.Execute(options);
		Assert.AreEqual(0, result);
		_logger.Received().WriteLine($"Finishes hotfix state for package: \"{packageName}\"");
		_logger.Received().WriteLine("Done");
		_packageEditableMutator.Received(1).FinishPackageHotfix(packageName);
	}

	[Test, Category("Unit")]
	public void Execute_ShowsErrorMessage_WhenPackageHotFixModeNotSet()
	{
		var packageName = "TestPackageName";
		var errorMessage = "SomeErrorMessage";
		var options = new FinishPackageHotFixCommandOptions
		{
			PackageName = packageName
		};
		_packageEditableMutator.When(mutator => mutator.FinishPackageHotfix(packageName))
			.Throw(new Exception(errorMessage));
		Assert.AreEqual(1, _command.Execute(options));
		_logger.Received().WriteLine($"Finishes hotfix state for package: \"{packageName}\"");
		_logger.Received().WriteLine(errorMessage);
		_logger.DidNotReceive().WriteLine($"Done.");
	}

	#endregion
}