namespace Clio.Tests.Command
{
	using Clio.Command.RestartCommand;
	using Clio.Common;
	using NSubstitute;
	using NUnit.Framework;

	[TestFixture]
	public class RestartCommandTestCase
	{
		[Test, Category("Unit")]
		public void RestartCommand_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			RestartCommand restartCommand = new RestartCommand(applicationClient);
			var restartOptions = new RestartOptions {
				Login = "Test",
				Password = "Test",
				IsNetCore = false,
				Maintainer = "Test",
				Uri = "http://test.domain.com"
			};
			restartCommand.Restart(restartOptions);
			applicationClient.Received(1).ExecutePostRequest(
				restartOptions.Uri + "/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain",
				"{}");
		}

		[Test, Category("Unit")]
		public void RestartCommand_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFrameworkAndSettingsPickedFromEnvironment() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			RestartCommand restartCommand = new RestartCommand(applicationClient);
			var environmentSettings = new EnvironmentSettings {
				Login = "Test",
				Password = "Test",
				IsNetCore = false,
				Maintainer = "Test",
				Uri = "http://test.domain.com"
			};
			restartCommand.Restart(environmentSettings);
			applicationClient.Received(1).ExecutePostRequest(
				environmentSettings.Uri + "/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain",
				"{}");
		}

		[Test, Category("Unit")]
		public void RestartCommand_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCore() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			RestartCommand restartCommand = new RestartCommand(applicationClient);
			var restartOptions = new RestartOptions {
				Login = "Test",
				Password = "Test",
				IsNetCore = true,
				Maintainer = "Test",
				Uri = "http://test.domain.com"
			};
			restartCommand.Restart(restartOptions);
			applicationClient.Received(1).ExecutePostRequest(
				restartOptions.Uri + "/ServiceModel/AppInstallerService.svc/RestartApp",
				"{}");
		}

		[Test, Category("Unit")]
		public void RestartCommand_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCoreAndSettingsPickedFromEnvironment() {
			IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
			RestartCommand restartCommand = new RestartCommand(applicationClient);
			var environmentSettings = new EnvironmentSettings {
				Login = "Test",
				Password = "Test",
				IsNetCore = true,
				Maintainer = "Test",
				Uri = "http://test.domain.com"
			};
			restartCommand.Restart(environmentSettings);
			applicationClient.Received(1).ExecutePostRequest(
				environmentSettings.Uri + "/ServiceModel/AppInstallerService.svc/RestartApp",
				"{}");
		}
	}
}
