using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class ProgramTestCase : BaseClioModuleTests
{

    #region Setup/Teardown

    [TearDown]
    public void TearDown()
    {
        appUpdaterMock.ClearReceivedCalls();
        Program.Container = null;
        Program.AppUpdater = null;
    }

    #endregion

    #region Fields: Private

    private readonly IAppUpdater appUpdaterMock = Substitute.For<IAppUpdater>();

    #endregion

    #region Methods: Protected

    protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
    {
        DataProviderMock dataProviderMock = new();
        containerBuilder.RegisterInstance(dataProviderMock).As<IDataProvider>();
        containerBuilder.RegisterInstance(appUpdaterMock).As<IAppUpdater>();
    }

    #endregion

    #region Methods: Public

    public override void Setup()
    {
        base.Setup();
        appUpdaterMock.ClearReceivedCalls();
        Program.Container = null;
        Program.AppUpdater = null;
    }

    #endregion

    [Test]
    [Category("Unit")]
    public void Resolve_DoesNotThrowException_WhenCommandDoesNotNeedEnvironment()
    {
        CreateWorkspaceCommandOptions options = new();
        bool logAndSettings = false;
        Program.Container = Container;
        string filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
        FileSystem.AddFile(filePath, new MockFileData(File
            .ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
        SettingsRepository.FileSystem = FileSystem;
        Program.Resolve<CreateWorkspaceCommand>(options, logAndSettings);
    }

    [Test]
    public void SkipAutoupdateIfUpdateDisable()
    {
        Program.Container = Container;
        Program.AppUpdater = Substitute.For<IAppUpdater>();
        string filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
        FileSystem.AddFile(filePath, new MockFileData(File
            .ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
        SettingsRepository.FileSystem = FileSystem;
        Program.AutoUpdate = false;
        Program.ExecuteCommands(new[]
        {
            "ver", "--clio"
        });
        Program.AppUpdater.Received(0).CheckUpdate();
    }

}
