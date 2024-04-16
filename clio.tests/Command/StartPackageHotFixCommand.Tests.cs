using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class StartPackageHotFixCommandTestCase
{
	#region Fields: Private

	private StartPackageHotFixCommand _command;
	private ILogger _logger;
	private IPackageEditableMutator _packageEditableMutator;

	#endregion

	#region Methods: Public

	[SetUp]
	public void Init()
	{
		_packageEditableMutator = Substitute.For<IPackageEditableMutator>();
		_logger = Substitute.For<ILogger>();
		_command = new StartPackageHotFixCommand(_packageEditableMutator, _logger, new EnvironmentSettings());
	}

	[Test, Category("Unit")]
	public void Execute_StartsHotFixMode()
	{
		string packageName = "TestPackageName";
		var options = new StartPackageHotFixCommandOptions
		{
			PackageName = packageName
		};
		var result = _command.Execute(options);
		Assert.AreEqual(0, result);
		_logger.Received().WriteLine($"Starts hotfix state for package: \"{packageName}\"");
		_logger.Received().WriteLine("Done");
		_packageEditableMutator.Received(1).StartPackageHotfix(packageName);
	}

	[Test, Category("Unit")]
	public void Execute_ShowsErrorMessage_WhenPackageHotFixModeNotSet()
	{
		var packageName = "TestPackageName";
		var errorMessage = "SomeErrorMessage";
		var options = new StartPackageHotFixCommandOptions
		{
			PackageName = packageName
		};
		_packageEditableMutator.When(mutator => mutator.StartPackageHotfix(packageName))
			.Throw(new Exception(errorMessage));
		Assert.AreEqual(1, _command.Execute(options));
		_logger.Received().WriteLine($"Starts hotfix state for package: \"{packageName}\"");
		_logger.Received().WriteLine(errorMessage);
		_logger.DidNotReceive().WriteLine($"Done.");
	}

	#endregion
}