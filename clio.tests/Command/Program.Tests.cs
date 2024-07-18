using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using DocumentFormat.OpenXml.Drawing.Charts;
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

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
		var dataProviderMock = new DataProviderMock();
		containerBuilder.RegisterInstance(dataProviderMock).As<IDataProvider>();
		containerBuilder.RegisterInstance(appUpdaterMock).As<IAppUpdater>();
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
	public void TryToRunAutoupdate() {
		Program.Container = _container;
		Program.AppUpdater = Substitute.For<IAppUpdater>();
		var filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		_fileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsRepository.FileSystem = _fileSystem;
		Program.AutoUpdate = true;
		Program.ExecuteCommands(new string[] { "ver", "--clio" });
		Program.AppUpdater.Received(1).CheckUpdate();
	}


	[Test]
	public void SkipAutoupdateIfUpdateDisable() {
		Program.Container = _container;
		Program.AppUpdater = Substitute.For<IAppUpdater>();
		var filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		_fileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsRepository.FileSystem = _fileSystem;
		Program.AutoUpdate = false;
		Program.ExecuteCommands(new string[] { "ver", "--clio" });
		Program.AppUpdater.Received(0).CheckUpdate();
	}


	[Test]
	public void GetAutoUpdaterFromDIByDefault() {
		Program.Container = _container;
		var filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		_fileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsRepository.FileSystem = _fileSystem;
		Program.AutoUpdate = true;
		Program.ExecuteCommands(new string[] { "ver", "--clio" });
		appUpdaterMock.Received(1).CheckUpdate();
	}

	[Test]
	public void GetAutoUpdaterFromDIWithoutInitContainer() {
		var filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		_fileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsRepository.FileSystem = _fileSystem;
		Program.AutoUpdate = true;
		Program.ExecuteCommands(new string[] { "ver", "--clio" }).Should().Be(0);
		Program.AppUpdater.Checked.Should().BeTrue();
	}
}