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
	[Description("Returns structured show-web-app settings without masking sensitive values for the MCP response.")]
	public void ShowWebAppList_Should_Return_Unmasked_Structured_Settings()
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
		ShowAppListCommand command = new(settingsRepository, logger);
		ShowWebAppListTool tool = new(command);

		// Act
		IReadOnlyList<ShowWebAppSettingsResult> result = tool.ShowWebAppList();

		// Assert
		result.Should().ContainSingle(because: "the MCP tool should return one structured result per registered environment");
		ShowWebAppSettingsResult environment = result.Single();
		environment.Name.Should().Be("sandbox",
			because: "the MCP tool should preserve the registered environment name");
		environment.Password.Should().Be("super-secret",
			because: "the MCP tool must not mask environment passwords");
		environment.ClientSecret.Should().Be("oauth-secret",
			because: "the MCP tool must not mask OAuth client secrets");
		environment.DbServer.Should().NotBeNull(
			because: "the structured response should preserve nested database server configuration when it exists");
		environment.DbServer!.Password.Should().Be("db-password",
			because: "the MCP tool must not mask nested database server passwords");
	}
}
