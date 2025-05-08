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
    [TearDown]
    public void TearDown()
    {
        appUpdaterMock.ClearReceivedCalls();
        Program.Container = null;
        Program.AppUpdater = null;
    }

    private readonly IAppUpdater appUpdaterMock = Substitute.For<IAppUpdater>();

    public override void Setup()
    {
        base.Setup();
        appUpdaterMock.ClearReceivedCalls();
        Program.Container = null;
        Program.AppUpdater = null;
    }

    protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
    {
        DataProviderMock dataProviderMock = new();
        containerBuilder.RegisterInstance(dataProviderMock).As<IDataProvider>();
        containerBuilder.RegisterInstance(appUpdaterMock).As<IAppUpdater>();
    }

    [Test]
    [Category("Unit")]
    public void Resolve_DoesNotThrowException_WhenCommandDoesNotNeedEnvironment()
    {
        CreateWorkspaceCommandOptions options = new();
        bool logAndSettings = false;
        Program.Container = container;
        string filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
        fileSystem.AddFile(filePath, new MockFileData(File
            .ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
        SettingsRepository.FileSystem = fileSystem;
        Program.Resolve<CreateWorkspaceCommand>(options, logAndSettings);
    }

    [Test]
    public void SkipAutoupdateIfUpdateDisable()
    {
        Program.Container = container;
        Program.AppUpdater = Substitute.For<IAppUpdater>();
        string filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
        fileSystem.AddFile(filePath, new MockFileData(File
            .ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
        SettingsRepository.FileSystem = fileSystem;
        Program.AutoUpdate = false;
        Program.ExecuteCommands(["ver", "--clio"]);
        Program.AppUpdater.Received(0).CheckUpdate();
    }
}
