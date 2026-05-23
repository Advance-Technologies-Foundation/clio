namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using Clio;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
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

[TestFixture]
internal class DeletePackageCommandWithVerificationTests : BaseCommandTests<DeletePkgOptions>
{

	private DeletePackageCommand _command;
	private IApplicationClient _applicationClient;
	private IApplicationPackageListProvider _packageListProvider;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DeletePackageCommand>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_packageListProvider.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
		containerBuilder.AddTransient<IApplicationPackageListProvider>(_ => _packageListProvider);
	}

	private DeletePkgOptions CreateOptions(string name = "TestPackage") {
		return new DeletePkgOptions {
			Name = name
		};
	}

	[Test]
	[Description("Verifies that deletion is reported as success when package is removed despite server error")]
	public void Execute_ReturnsSuccess_WhenPackageRemovedDespiteServerError() {
		// Arrange
		var options = CreateOptions("test3");
		string errorResponse = "{\"success\":false,\"errorInfo\":{\"message\":\"Uninstall application error\"}}";
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(errorResponse);
		
		// Package list no longer contains the package (successful deletion)
		_packageListProvider.GetPackages().Returns([]);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "package was successfully removed even though server reported error");
		_packageListProvider.Received(1).GetPackages();
	}

	[Test]
	[Description("Verifies that deletion is reported as failure when package still exists and server reports error")]
	public void Execute_ReturnsFailure_WhenPackageStillExistsAndServerReportsError() {
		// Arrange
		var options = CreateOptions("CrtWebForm");
		string errorResponse = "{\"success\":false,\"errorInfo\":{\"message\":\"Item was created by third-party publisher\"}}";
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(errorResponse);
		
		// Package still exists in the list (deletion failed)
		var descriptor = new PackageDescriptor { Name = "CrtWebForm" };
		var packageInfo = new PackageInfo(descriptor, "/path", new List<string>());
		_packageListProvider.GetPackages().Returns(new[] { packageInfo });

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "package still exists and server reported error");
		_packageListProvider.Received(1).GetPackages();
	}

	[Test]
	[Description("Verifies that verification is performed even when server reports success")]
	public void Execute_VerifiesPackageRemoval_EvenWhenServerReportsSuccess() {
		// Arrange
		var options = CreateOptions("TestPkg");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
		
		_packageListProvider.GetPackages().Returns([]);

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "package was removed and server confirmed success");
		_packageListProvider.Received(1).GetPackages();
	}

	[Test]
	[Description("Verifies that false positives are caught when server reports success but package still exists")]
	public void Execute_ReturnsFailure_WhenServerReportsSuccessButPackageStillExists() {
		// Arrange
		var options = CreateOptions("LockedPkg");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true}");
		
		// Package still exists (server lied about success)
		var descriptor = new PackageDescriptor { Name = "LockedPkg" };
		var packageInfo = new PackageInfo(descriptor, "/path", new List<string>());
		_packageListProvider.GetPackages().Returns(new[] { packageInfo });

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "package still exists despite server claiming success");
		_packageListProvider.Received(1).GetPackages();
	}

	[Test]
	[Description("Verifies that verification failures are handled gracefully")]
	public void Execute_HandlesVerificationFailure_Gracefully() {
		// Arrange
		var options = CreateOptions("TestPkg");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"Some error\"}}");
		
		// Verification throws exception
		_packageListProvider.GetPackages().Returns(_ => throw new Exception("Connection failed"));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "when verification fails, assume package still exists to be safe");
	}

}