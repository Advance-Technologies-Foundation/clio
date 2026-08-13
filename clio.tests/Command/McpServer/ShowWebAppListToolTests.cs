using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ShowWebAppListToolTests
{
	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name for show-webApp-list.")]
	public void ShowWebAppList_Should_Advertise_Stable_Tool_Name()
	{
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ShowWebAppListTool)
			.GetMethod(nameof(ShowWebAppListTool.ShowWebAppList))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act
		string toolName = attribute.Name;

		// Assert
		toolName.Should().Be(ShowWebAppListTool.ShowWebAppListToolName,
			because: "unit tests must track the production MCP tool-name constant instead of duplicating the string literal");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured show-web-app settings with sensitive values masked by default for the MCP response.")]
	public void ShowWebAppList_Should_Return_Masked_Structured_Settings()
	{
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		ILogger logger = Substitute.For<ILogger>();
		settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["sandbox"] = new() {
				Uri = "http://sandbox",
				Login = "Supervisor",
				Password = "super-secret",
				ClientSecret = "oauth-secret",
				DbServerKey = "db-main",
				DbServer = new DbServer {
					Uri = new Uri("http://db-host"),
					WorkingFolder = "C:\\db",
					Login = "db-user",
					Password = "db-password"
				}
			}
		});
		settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		ShowAppListCommand command = new(settingsRepository, logger, Substitute.For<IJsonResponseFormater>());
		ShowWebAppListTool tool = new(command, settingsRepository);

		// Act
		ShowWebAppListToolResult result = tool.ShowWebAppList();

		// Assert
		result.Environments.Should().ContainSingle(because: "the MCP tool should return one structured result per registered environment");
		result.Warnings.Should().BeNull(
			because: "a successful settings reload must not add noise to the response");
		ShowWebAppSettingsResult environment = result.Environments.Single();
		environment.Name.Should().Be("sandbox",
			because: "the MCP tool should preserve the registered environment name");
		environment.Password.Should().Be("****",
			because: "the MCP tool must mask environment passwords by default to avoid leaking secrets to AI agents");
		environment.ClientSecret.Should().Be("****",
			because: "the MCP tool must mask OAuth client secrets by default to avoid leaking secrets to AI agents");
		environment.DbServer.Should().NotBeNull(
			because: "the structured response should preserve nested database server configuration when it exists");
		environment.DbServer!.Password.Should().Be("****",
			because: "the MCP tool must mask nested database server passwords by default");
	}

	[Test]
	[Category("Unit")]
	[Description("Re-reads the settings file before listing so an environment registered after server start is visible.")]
	public void ShowWebAppList_Should_Reload_Settings_From_Disk_Before_Listing()
	{
		// Arrange
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		settingsRepository.AppSettingsFilePath.Returns("/home/user/clio/appsettings.json");
		settingsRepository.GetAllEnvironments().Returns(_ =>
			new Dictionary<string, EnvironmentSettings> {
				["registered-after-start"] = new() { Uri = "http://added-later" }
			});
		ShowAppListCommand command = new(settingsRepository, Substitute.For<ILogger>(),
			Substitute.For<IJsonResponseFormater>());
		ShowWebAppListTool tool = new(command, settingsRepository);

		// Act
		ShowWebAppListToolResult result = tool.ShowWebAppList();

		// Assert
		Received.InOrder(() => {
			settingsRepository.Reload();
			settingsRepository.GetAllEnvironments();
		});
		result.Environments.Select(environment => environment.Name).Should().Contain("registered-after-start",
			because: "the tool whose only job is to show the current environment list must answer from the file, "
				+ "not from the snapshot taken when the MCP server started");
		result.SettingsFilePath.Should().Be("/home/user/clio/appsettings.json",
			because: "the caller has to be able to tell which settings file this server reads");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the previously loaded environments plus a warning when the settings file cannot be re-read.")]
	public void ShowWebAppList_Should_Return_Warning_When_Reload_Fails()
	{
		// Arrange
		const string warning = "Could not re-read appsettings.json: broken. The previously loaded settings are still in use.";
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.Reload().Returns(new SettingsReloadResult(false, null, warning));
		settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			["sandbox"] = new() { Uri = "http://sandbox" }
		});
		ShowAppListCommand command = new(settingsRepository, Substitute.For<ILogger>(),
			Substitute.For<IJsonResponseFormater>());
		ShowWebAppListTool tool = new(command, settingsRepository);

		// Act
		ShowWebAppListToolResult result = tool.ShowWebAppList();

		// Assert
		result.Environments.Should().ContainSingle(
			because: "an unreadable settings file must degrade to the last known list instead of failing the tool");
		result.Warnings.Should().ContainSingle().Which.Should().Be(warning,
			because: "the caller must learn that the returned list may be older than the file");
	}
}
