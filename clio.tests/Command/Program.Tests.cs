using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Command.McpServer;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
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
	[Category("Unit")]
	[Description("Builds the bootstrap container for info without requiring a valid active environment in appsettings.json.")]
	public void Resolve_Should_Not_Throw_For_InfoCommand_When_Active_Environment_Key_Is_Invalid() {
		// Arrange
		Program.Container = null;
		AddWrongActiveEnvironmentFixture();

		// Act
		Action act = () => Program.Resolve<InfoCommand>(new InfoCommandOptions(), false);

		// Assert
		act.Should().NotThrow(
			because: "bootstrap-safe commands should not depend on startup-time EnvironmentSettings registration");
	}

	[Test]
	[Category("Unit")]
	[Description("Builds the bootstrap container for show-web-app-list without requiring a valid active environment in appsettings.json.")]
	public void Resolve_Should_Not_Throw_For_ShowAppListCommand_When_Active_Environment_Key_Is_Invalid() {
		// Arrange
		Program.Container = null;
		AddWrongActiveEnvironmentFixture();

		// Act
		Action act = () => Program.Resolve<ShowAppListCommand>(new AppListOptions(), false);

		// Assert
		act.Should().NotThrow(
			because: "show-web-app-list should run from the bootstrap profile even when ActiveEnvironmentKey is stale");
	}

	[Test]
	[Category("Unit")]
	[Description("Builds the bootstrap container for mcp-server without requiring a valid active environment in appsettings.json.")]
	public void Resolve_Should_Not_Throw_For_McpServerCommand_When_Active_Environment_Key_Is_Invalid() {
		// Arrange
		Program.Container = null;
		AddWrongActiveEnvironmentFixture();

		// Act
		Action act = () => Program.Resolve<McpServerCommand>(new McpServerCommandOptions(), false);

		// Assert
		act.Should().NotThrow(
			because: "the MCP server must start from repaired bootstrap settings instead of failing during container validation");
	}

	[Test]
	[Category("Unit")]
	[Description("Builds the explicit bootstrap registration profile without requiring a valid active environment.")]
	public void BindingsModule_Register_Should_Not_Require_EnvironmentSettings_In_Bootstrap_Profile() {
		// Arrange
		AddWrongActiveEnvironmentFixture();

		// Act
		Action act = () => new BindingsModule(FileSystem).Register(profile: BindingsModuleRegistrationProfile.Bootstrap);

		// Assert
		act.Should().NotThrow(
			because: "bootstrap registration should tolerate invalid ActiveEnvironmentKey values and still validate the startup graph");
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

	private void AddWrongActiveEnvironmentFixture() {
		string filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		FileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsRepository.FileSystem = FileSystem;
	}

}
