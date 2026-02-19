using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class ProgramTestCase : BaseClioModuleTests
{
	IAppUpdater appUpdaterMock = Substitute.For<IAppUpdater>();

	[TearDown]
	public void TearDown() {
		appUpdaterMock.ClearReceivedCalls();
		Program.Container = null;
		Program.AppUpdater = null;
	}

	public override void Setup(){
		base.Setup();
		appUpdaterMock.ClearReceivedCalls();
		Program.Container = null;
		Program.AppUpdater = null;
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		var dataProviderMock = new DataProviderMock();
		containerBuilder.AddSingleton<IDataProvider>(dataProviderMock);
		containerBuilder.AddSingleton<IAppUpdater>(appUpdaterMock);
	}

	[Test, Category("Unit")]
	public void Resolve_DoesNotThrowException_WhenCommandDoesNotNeedEnvironment() {
		CreateWorkspaceCommandOptions options = new CreateWorkspaceCommandOptions();
		bool logAndSettings = false;
		Program.Container = Container;
		var filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		FileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsRepository.FileSystem = FileSystem;
		Program.Resolve<CreateWorkspaceCommand>(options, logAndSettings);
	}

	[Test]
	public void SkipAutoupdateIfUpdateDisable() {
		Program.Container = Container;
		Program.AppUpdater = Substitute.For<IAppUpdater>();
		var filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		FileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsRepository.FileSystem = FileSystem;
		Program.AutoUpdate = false;
		Program.ExecuteCommands(new string[] { "ver", "--clio" });
		Program.AppUpdater.Received(0).CheckUpdate();
	}

}
