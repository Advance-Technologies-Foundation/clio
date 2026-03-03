namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
internal class DeletePackageCommandTests : BaseCommandTests<DeletePkgOptions>
{

	private DeletePackageCommand _command;
	private IApplicationClient _applicationClient;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DeletePackageCommand>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
	}

	private DeletePkgOptions CreateOptions(string name = "TestPackage") {
		return new DeletePkgOptions {
			Name = name
		};
	}

	[Test]
	[Description("Verifies that the correct API endpoint and request body are used for package deletion")]
	public void Execute_FormsCorrectApplicationRequest() {
		// Arrange
		var options = CreateOptions();
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");

		// Act
		int result = _command.Execute(options);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(uri => uri.Contains("/ServiceModel/AppInstallerService.svc/DeletePackage")),
			"\"TestPackage\"", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		result.Should().Be(0, because: "command should succeed when server returns success");
	}

	[Test]
	[Description("Verifies that the command returns success when server responds with success=true")]
	public void Execute_ReturnsSuccess_WhenServerReturnsSuccess() {
		// Arrange
		var options = CreateOptions("MyPackage");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "server confirmed package was deleted successfully");
	}

	[Test]
	[Description("Verifies that the command returns failure when server responds with success=false and errorInfo")]
	public void Execute_ReturnsFailure_WhenServerReturnsErrorWithMessage() {
		// Arrange
		var options = CreateOptions("LockedPackage");
		string errorResponse = "{\"success\":false,\"errorInfo\":{\"message\":\"Package is locked and cannot be deleted\"}}";
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(errorResponse);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "server reported that the package could not be deleted");
	}

	[Test]
	[Description("Verifies that the command returns failure when server responds with success=false without errorInfo")]
	public void Execute_ReturnsFailure_WhenServerReturnsErrorWithoutMessage() {
		// Arrange
		var options = CreateOptions("SomePackage");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false}");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "server reported failure even without a detailed error message");
	}

	[Test]
	[Description("Verifies that the command returns failure when server returns empty response")]
	public void Execute_ReturnsFailure_WhenServerReturnsEmptyResponse() {
		// Arrange
		var options = CreateOptions();
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "an empty server response indicates a communication or server-side problem");
	}

	[Test]
	[Description("Verifies that the command returns failure when server returns invalid JSON")]
	public void Execute_ReturnsFailure_WhenServerReturnsInvalidJson() {
		// Arrange
		var options = CreateOptions();
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("not a json");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "an invalid JSON response cannot be reliably interpreted as success");
	}

	[Test]
	[Description("Verifies that the command returns failure when errorInfo has null message")]
	public void Execute_ReturnsFailure_WhenErrorInfoHasNullMessage() {
		// Arrange
		var options = CreateOptions();
		string response = "{\"success\":false,\"errorInfo\":{\"message\":null}}";
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "server reported failure with null error message");
	}

}