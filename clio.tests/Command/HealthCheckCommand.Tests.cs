using Autofac;
using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class HealthCheckCommandTestCase
{

    #region Setup/Teardown

    [SetUp]
    public void SetUp()
    {
        _environmentSettings = new EnvironmentSettings
        {
            Login = "Test",
            Password = "Test",
            IsNetCore = false,
            Maintainer = "Test",
            Uri = "http://test.domain.com"
        };
        _applicationClient = Substitute.For<IApplicationClient>();
        _hcCommand = new HealthCheckCommand(_applicationClient, _environmentSettings);
        _options = Substitute.For<HealthCheckOptions>();
    }

    #endregion

    #region Fields: Private

    private EnvironmentSettings _environmentSettings;
    private IApplicationClient _applicationClient;
    private HealthCheckCommand _hcCommand;
    private HealthCheckOptions _options;

    #endregion

    [Test]
    [Category("Unit")]
    public void HealthCheckCommand_FormsCorrectApplicationRequest_WhenWebAppIsTrue()
    {
        _options.WebApp = "true";
        _hcCommand.Execute(_options);
        _applicationClient.Received(1).ExecuteGetRequest(_environmentSettings.Uri + "/api/HealthCheck/Ping");
    }

    [Test]
    [Category("Unit")]
    public void HealthCheckCommand_FormsCorrectApplicationRequest_WhenWebHostIsTrue()
    {
        _options.WebHost = "true";
        _hcCommand.Execute(_options);
        _applicationClient.Received(1).ExecuteGetRequest(_environmentSettings.Uri + "/0/api/HealthCheck/Ping");
    }

    [Test]
    [Category("Unit")]
    public void HealthCheckCommand_IsRegistered()
    {
        BindingsModule bs = new();
        IContainer container = bs.Register(_environmentSettings);
        HealthCheckCommand command = container.Resolve<HealthCheckCommand>();
        Assert.That(command, Is.Not.Null);
    }

}
