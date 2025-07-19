using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class RestartCommandTestCase : BaseCommandTests<RestartOptions> {

	/// <summary>
	/// Verifies that RestartCommand forms the correct application request when the application runs under .NET Core and settings are picked from the environment.
	/// </summary>
	[Test]
	[Description("Ensures RestartCommand sends the correct request for .NET Core environments.")]
	public void RestartCommand_FormsCorrectRequest_ForNetCoreEnvironment() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test",
			Password = "Test",
			IsNetCore = true,
			Maintainer = "Test",
			Uri = "http://test.domain.com"
		};
		RestartCommand restartCommand = new(applicationClient, environmentSettings);
		RestartOptions options = new();

		// Act
		restartCommand.Execute(options);

		// Assert
		applicationClient.Received(1).ExecutePostRequest(
			environmentSettings.Uri + "/ServiceModel/AppInstallerService.svc/RestartApp",
			"{}", 100_000, 3);
	}

	/// <summary>
	/// Verifies that RestartCommand forms the correct application request when the application runs under .NET Framework and settings are picked from the environment.
	/// </summary>
	[Test]
	[Description("Ensures RestartCommand sends the correct request for .NET Framework environments.")]
	public void RestartCommand_FormsCorrectRequest_ForNetFrameworkEnvironment() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		EnvironmentSettings environmentSettings = new() {
			Login = "Test",
			Password = "Test",
			IsNetCore = false,
			Maintainer = "Test",
			Uri = "http://test.domain.com"
		};
		RestartCommand restartCommand = new(applicationClient, environmentSettings);
		RestartOptions options = new();

		// Act
		restartCommand.Execute(options);

		// Assert
		applicationClient.Received(1).ExecutePostRequest(
			environmentSettings.Uri + "/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain",
			"{}", 100_000, 3);
	}

}
