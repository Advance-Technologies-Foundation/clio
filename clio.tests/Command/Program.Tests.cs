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
	[Description("Builds the bootstrap container for list-environments without requiring a valid active environment in appsettings.json.")]
	public void Resolve_Should_Not_Throw_For_ShowAppListCommand_When_Active_Environment_Key_Is_Invalid() {
		// Arrange
		Program.Container = null;
		AddWrongActiveEnvironmentFixture();

		// Act
		Action act = () => Program.Resolve<ShowAppListCommand>(new AppListOptions(), false);

		// Assert
		act.Should().NotThrow(
			because: "list-environments should run from the bootstrap profile even when ActiveEnvironmentKey is stale");
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

	[Test]
	[Category("Unit")]
	[Description("Register with EnvironmentScoped profile and OAuth env must not make network calls at container build time.")]
	public void Register_Should_Not_Throw_When_EnvironmentScoped_With_OAuth_Env_And_Unreachable_Server() {
		// Arrange — env has ClientId/ClientSecret/AuthAppUri pointing to an unreachable server
		EnvironmentSettings oauthSettings = new() {
			Uri = "http://ts1-infr-web02:88/studioenu",
			Login = "Supervisor",
			Password = "Supervisor1!",
			IsNetCore = false,
			ClientId = "E8A83F215D9C6D9401BCCFB254A2CA99",
			ClientSecret = "79B921B45DE74C4CB75B162FCD90A1BE352166EC5FAB04B2BD8C83D40CBDF26D",
			AuthAppUri = "https://localhost:9999/unreachable/connect/token"
		};

		// Act
		Action act = () => new BindingsModule(FileSystem).Register(oauthSettings,
			profile: BindingsModuleRegistrationProfile.EnvironmentScoped);

		// Assert — CreatioClient must be lazy: no OAuth token fetch during DI registration
		act.Should().NotThrow(
			because: "CreatioClient must be registered lazily so OAuth token fetch happens only when the client is first used, not at container build time");
	}

	[Test]
	[Category("Unit")]
	[Description("dconf --build uses a local zip and must not connect to env even when default env has OAuth credentials.")]
	public void Resolve_Should_Not_Throw_For_DownloadConfigurationCommand_When_Default_Env_Has_OAuth() {
		// Arrange — appsettings with a valid default env that has OAuth credentials
		Program.Container = null;
		AddOAuthDefaultEnvironmentFixture();

		// Act — dconf --build scenario: options carry no explicit env, default env resolved from config
		Action act = () => Program.Resolve<DownloadConfigurationCommand>(
			new DownloadConfigurationCommandOptions(), false);

		// Assert
		act.Should().NotThrow(
			because: "dconf --build uses a local zip file and must not attempt OAuth authentication at startup");
	}

	private void AddWrongActiveEnvironmentFixture() {
		string filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		FileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsRepository.FileSystem = FileSystem;
	}

	private void AddOAuthDefaultEnvironmentFixture() {
		string filePath = Path.Combine(Environment.CurrentDirectory, SettingsRepository.AppSettingsFile);
		FileSystem.AddFile(filePath, new MockFileData(File
			.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-oauth-default-env.json"))));
		SettingsRepository.FileSystem = FileSystem;
	}

}
