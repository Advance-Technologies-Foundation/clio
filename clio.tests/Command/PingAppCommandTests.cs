using Autofac;
using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture(Category = "Unit")]
internal class PingAppCommandTests : BaseClioModuleTests
{

    #region Fields: Private

    private readonly IApplicationClient _creatioClient = Substitute.For<IApplicationClient>();

    #endregion

    #region Methods: Protected

    protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
    {
        base.AdditionalRegistrations(containerBuilder);
        containerBuilder.RegisterInstance(_creatioClient).As<IApplicationClient>();
    }

    #endregion

    #region Methods: Public

    public override void Setup()
    { }

    #endregion

    [TestCase(true)]
    [TestCase(false)]
    public void PingAppCommandShouldBeUsesAllRetryOptions(bool isNetCore)
    {
        //Arrange
        EnvironmentSettings.IsNetCore = isNetCore;
        FileSystem = CreateFs();
        BindingsModule bindingModule = new(FileSystem);
        Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);

        PingAppCommand command = Container.Resolve<PingAppCommand>();
        PingAppOptions options = new()
        {
            TimeOut = 1, RetryCount = 2, RetryDelay = 3
        };

        // Act
        command.Execute(options);

        // Assert
        if (isNetCore)
        {
            _creatioClient.Received(1)
                          .ExecuteGetRequest(Arg.Any<string>(), 1, 2, 3);
        }
        else
        {
            _creatioClient.Received(1)
                          .ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 1, 2, 3);
        }
        _creatioClient.ClearReceivedCalls();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PingAppCommandShouldBeUsesAllRetryOptionsOnNet6Environment(bool isNetCore)
    {
        //Arrange
        FileSystem = CreateFs();
        BindingsModule bindingModule = new(FileSystem);
        Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);
        PingAppCommand command = Container.Resolve<PingAppCommand>();
        PingAppOptions options = new()
        {
            TimeOut = 1, RetryCount = 2, RetryDelay = 3, IsNetCore = isNetCore
        };
        command.EnvironmentSettings.IsNetCore = true;

        // Act
        command.Execute(options);

        //Assert
        if (isNetCore)
        { }
        else
        {
            _creatioClient.Received(1).ExecuteGetRequest(Arg.Any<string>(), 1, 2, 3);
        }
        _creatioClient.ClearReceivedCalls();
    }

}
