using System;
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
public class CreatePackageCommandTestCase : BaseCommandTests<CreatePackageOptions>
{

	#region Fields: Private

	private CreatePackageCommand _command;
	private ILogger _logger;
	private IRemotePackageCreator _packageCreator;

	#endregion

	#region Setup/Teardown

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreatePackageCommand>();
		_command.Logger = _logger;
	}

	public override void TearDown() {
		_packageCreator.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageCreator = Substitute.For<IRemotePackageCreator>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient<IRemotePackageCreator>(_ => _packageCreator);
	}

	#endregion

	#region Methods: Private

	private static RemotePackageCreateResult Created(string incompleteReason = null) =>
		new(Guid.NewGuid(), "UsrCalls", "Customer", "desc", ["CrtBase"], 0, null, incompleteReason);

	#endregion

	[Test]
	[Description("Forwards every option to the package creator and maps the readback into a successful response.")]
	public void CreatePackage_ShouldForwardOptionsAndMapTheReadback() {
		// Arrange
		CreatePackageOptions options = new() {
			PackageName = "Calls", Description = "desc", Dependencies = ["CrtBase"], ApplicationCode = "UsrApp"
		};
		_packageCreator.Create(Arg.Any<RemotePackageCreateRequest>()).Returns(Created());

		// Act
		CreatePackageResponse response = _command.CreatePackage(options);

		// Assert
		_packageCreator.Received(1).Create(Arg.Is<RemotePackageCreateRequest>(request =>
			request.PackageName == "Calls" && request.Description == "desc"
			&& request.Dependencies.Count == 1 && request.Dependencies[0] == "CrtBase"
			&& request.ApplicationCode == "UsrApp"));
		response.Success.Should().BeTrue(because: "every step succeeded");
		response.PackageCreated.Should().BeTrue(because: "the package exists");
		response.PackageName.Should().Be("UsrCalls", because: "the response reports the stored name");
		response.Editable.Should().BeTrue(because: "InstallType 0 is editable");
		response.Error.Should().BeNull(because: "nothing failed");
	}

	[Test]
	[Description("Reports a refusal as package-created=false with the refusal message.")]
	public void CreatePackage_ShouldReportNothingCreated_WhenCreatorRefuses() {
		// Arrange
		_packageCreator.Create(Arg.Any<RemotePackageCreateRequest>())
			.Throws(new InvalidOperationException("Package \"UsrCalls\" already exists in the environment. Nothing was created."));

		// Act
		CreatePackageResponse response = _command.CreatePackage(new CreatePackageOptions { PackageName = "Calls" });

		// Assert
		response.Success.Should().BeFalse(because: "the request was refused");
		response.PackageCreated.Should().BeFalse(because: "a refusal changes nothing");
		response.Error.Should().Contain("already exists", because: "the reason is relayed to the caller");
	}

	[Test]
	[Description("Reports a created package whose dependencies failed as success=false with package-created=true.")]
	public void CreatePackage_ShouldReportCreatedButFailed_WhenResultIsIncomplete() {
		// Arrange
		_packageCreator.Create(Arg.Any<RemotePackageCreateRequest>()).Returns(Created("dependencies were not applied"));

		// Act
		CreatePackageResponse response = _command.CreatePackage(new CreatePackageOptions { PackageName = "Calls" });

		// Assert
		response.Success.Should().BeFalse(because: "a requested step did not complete");
		response.PackageCreated.Should().BeTrue(because: "the package exists and must not be created again");
		response.Error.Should().Contain("dependencies were not applied", because: "the caller must learn what is missing");
	}

	[Test]
	[Description("Returns exit code 0 and logs Done when the package is created with every dependency.")]
	public void Execute_ShouldReturnZero_WhenPackageIsCreated() {
		// Arrange
		_packageCreator.Create(Arg.Any<RemotePackageCreateRequest>()).Returns(Created());

		// Act
		int exitCode = _command.Execute(new CreatePackageOptions { PackageName = "Calls" });

		// Assert
		exitCode.Should().Be(0, because: "the package was created");
		_logger.Received().WriteInfo("Done");
	}

	[Test]
	[Description("Returns exit code 1 and logs the error when the request is refused.")]
	public void Execute_ShouldReturnOne_WhenRequestIsRefused() {
		// Arrange
		_packageCreator.Create(Arg.Any<RemotePackageCreateRequest>())
			.Throws(new InvalidOperationException("refused"));

		// Act
		int exitCode = _command.Execute(new CreatePackageOptions { PackageName = "Calls" });

		// Assert
		exitCode.Should().Be(1, because: "a refusal is a failed command");
		_logger.Received().WriteError("refused");
	}


	[Test]
	[Description("Reports package-created as null when the creator cannot tell whether the package was stored.")]
	public void CreatePackage_ShouldReportUnknownOutcome_WhenCreatorCannotTell() {
		// Arrange
		_packageCreator.Create(Arg.Any<RemotePackageCreateRequest>())
			.Throws(new PackageCreationOutcomeUnknownException("check list-packages", new Exception("reset")));

		// Act
		CreatePackageResponse response = _command.CreatePackage(new CreatePackageOptions { PackageName = "Calls" });

		// Assert
		response.Success.Should().BeFalse(because: "the request did not complete");
		response.PackageCreated.Should().BeNull(because: "neither created nor refused can be claimed");
		response.Error.Should().Contain("list-packages", because: "the caller is told how to find out");
	}

}
