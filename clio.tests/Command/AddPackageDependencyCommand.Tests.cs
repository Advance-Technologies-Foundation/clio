using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class AddPackageDependencyCommandTestCase : BaseCommandTests<AddPackageDependencyOptions>
{

	#region Constants: Private

	private const string PackageName = "MyApp";

	#endregion

	#region Fields: Private

	private AddPackageDependencyCommand _command;
	private ILogger _logger;
	private IPackageDependencyManager _packageDependencyManager;

	#endregion

	#region Setup/Teardown

	public override void Setup() {
		base.Setup();
		// Resolve the SUT from the container so the real BindingsModule wiring of
		// IPackageDependencyManager -> AddPackageDependencyCommand is exercised; a broken/missing
		// registration must fail here rather than passing against a hand-constructed instance.
		_command = Container.GetRequiredService<AddPackageDependencyCommand>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_packageDependencyManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageDependencyManager = Substitute.For<IPackageDependencyManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient<IPackageDependencyManager>(_ => _packageDependencyManager);
	}

	#endregion

	[Test]
	[Description("Returns success and forwards the parsed dependency name to the manager for a single dependency.")]
	public void Execute_ShouldReturnZeroAndAddDependency_WhenSingleDependencyProvided() {
		// Arrange
		AddPackageDependencyOptions options = new() {
			PackageName = PackageName,
			Dependencies = ["CrtLeadOppMgmtApp"]
		};
		_packageDependencyManager
			.AddDependencies(PackageName, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Returns(["CrtLeadOppMgmtApp"]);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a successful dependency add must return the success exit code");
		_packageDependencyManager.Received(1).AddDependencies(PackageName, Arg.Is<IEnumerable<PackageDependencySpec>>(
			specs => specs.Single().Name == "CrtLeadOppMgmtApp" && specs.Single().Version == null));
		_logger.Received().WriteInfo("Done");
	}

	[Test]
	[Description("Parses a 'name:version' dependency entry into a name and an explicit version.")]
	public void Execute_ShouldParseExplicitVersion_WhenDependencyHasVersionSuffix() {
		// Arrange
		AddPackageDependencyOptions options = new() {
			PackageName = PackageName,
			Dependencies = ["CrtLeadOppMgmtApp:8.2.1.123"]
		};
		_packageDependencyManager
			.AddDependencies(PackageName, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Returns(["CrtLeadOppMgmtApp"]);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid versioned dependency must be accepted");
		_packageDependencyManager.Received(1).AddDependencies(PackageName, Arg.Is<IEnumerable<PackageDependencySpec>>(
			specs => specs.Single().Name == "CrtLeadOppMgmtApp" && specs.Single().Version == "8.2.1.123"));
	}

	[Test]
	[Description("Returns an error exit code and never calls the manager when no dependency is supplied.")]
	public void Execute_ShouldReturnOneAndNotCallManager_WhenNoDependencyProvided() {
		// Arrange
		AddPackageDependencyOptions options = new() {
			PackageName = PackageName,
			Dependencies = []
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "an empty dependency list is a usage error");
		_packageDependencyManager.DidNotReceive()
			.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>());
		_logger.Received().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Returns an error exit code and logs the failure when the manager throws.")]
	public void Execute_ShouldReturnOneAndLogError_WhenManagerThrows() {
		// Arrange
		const string errorMessage = "Package with name \"MyApp\" not found in the environment.";
		AddPackageDependencyOptions options = new() {
			PackageName = PackageName,
			Dependencies = ["CrtLeadOppMgmtApp"]
		};
		_packageDependencyManager
			.AddDependencies(PackageName, Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new Exception(errorMessage));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a manager failure must surface as a non-zero exit code");
		_logger.Received().WriteError(errorMessage);
	}

}
