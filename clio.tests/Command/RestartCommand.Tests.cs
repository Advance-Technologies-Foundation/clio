namespace Clio.Tests.Command
{
	using Clio.Command;
	using Clio.Common;
	using NSubstitute;
	using NUnit.Framework;

	[TestFixture]
	public class RestartCommandTestCase
	{
		[Test, Category("Unit")]
		public void RestartCommand_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFrameworkAndSettingsPickedFromEnvironment() {
			//Arrange
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			var environmentSettings = new EnvironmentSettings {
				Login = "Test",
				Password = "Test",
				IsNetCore = false,
				Maintainer = "Test",
				Uri = "http://test.domain.com"
			};
			RestartCommand restartCommand = new RestartCommand(applicationClient, environmentSettings);
			var options = Substitute.For<RestartOptions>();

			//Act
			restartCommand.Execute(options);

			//Assert
			applicationClient.Received(1).ExecutePostRequest(
				environmentSettings.Uri + "/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain",
				"{}", 100_000,3,1);

		}

		[Test, Category("Unit")]
		public void RestartCommand_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCoreAndSettingsPickedFromEnvironment() {
			//Arrange
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			var environmentSettings = new EnvironmentSettings {
				Login = "Test",
				Password = "Test",
				IsNetCore = true,
				Maintainer = "Test",
				Uri = "http://test.domain.com"
			};
			RestartCommand restartCommand = new RestartCommand(applicationClient, environmentSettings);
			var options = Substitute.For<RestartOptions>();

			//Act
			restartCommand.Execute(options);

			//Assert
			applicationClient.Received(1).ExecutePostRequest(
				environmentSettings.Uri + "/ServiceModel/AppInstallerService.svc/RestartApp",
				"{}", 100_000,3,1);
		}
	}
}
