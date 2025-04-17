namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

public class DeletePackageCommandTestCase
{
	[Test, Category("Unit")]
	public void Delete_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFramework() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		var deleteOptions = new DeletePkgOptions {
			Login = "Test",
			Password = "Test",
			IsNetCore = false,
			Maintainer = "Test",
			Uri = "http://test.domain.com",
			Name = "TestPackage"
		};
		var settingsRepository = new SettingsRepository();
		var environment = settingsRepository.GetEnvironment(deleteOptions);
		DeletePackageCommand deleteCommand = new DeletePackageCommand(applicationClient, environment);
		deleteCommand.Execute(deleteOptions);
		applicationClient.Received(1).ExecutePostRequest(
			deleteOptions.Uri + "/0/ServiceModel/AppInstallerService.svc/DeletePackage",
			"\"TestPackage\"", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}