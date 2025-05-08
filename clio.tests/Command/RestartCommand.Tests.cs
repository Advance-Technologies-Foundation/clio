using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class RestartCommandTestCase
{
    [Test]
    [Category("Unit")]
    public void
        RestartCommand_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetFrameworkAndSettingsPickedFromEnvironment()
    {
        // Arrange
        IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
        EnvironmentSettings environmentSettings = new()
        {
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

    [Test]
    [Category("Unit")]
    public void
        RestartCommand_FormsCorrectApplicationRequest_WhenApplicationRunsUnderNetCoreAndSettingsPickedFromEnvironment()
    {
        // Arrange
        IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
        EnvironmentSettings environmentSettings = new()
        {
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
}
