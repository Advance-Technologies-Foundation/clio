using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature("application-create-db")]
[NonParallelizable]
public sealed class ApplicationCreateDbToolE2ETests
{
	private const string ToolName = ApplicationCreateDbTool.ToolName;

	[Test]
	[Description("Creates a Creatio application through backend MCP server (DB-first approach).")]
	[AllureTag(ToolName)]
	[AllureName("Application create DB tool completes successfully")]
	[AllureDescription("Calls application-create-db through the real MCP server and verifies successful application creation.")]
	public async Task ApplicationCreateDb_Should_Create_Application_Successfully()
	{
		McpE2ESettings settings = McpE2ESettings.Load();
		string environmentName = settings.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(McpE2ESettings.Load());

		var args = new Dictionary<string, object>
		{
			["name"] = $"TestApp_{DateTime.Now:yyyyMMddHHmmss}",
			["code"] = $"UsrTestApp{DateTime.Now:yyyyMMddHHmmss}",
			["templateCode"] = "AppFreedomUI",
			["iconBackground"] = "#FF5733",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.Client.CallToolAsync(
			ToolName,
			args,
			session.CancellationToken);

		result.IsError.Should().BeFalse("application creation should succeed");
		result.Content.Should().NotBeEmpty("result should contain response content");
	}

	[Test]
	[Description("Reports failure when required parameters are missing.")]
	[AllureTag(ToolName)]
	[AllureName("Application create DB reports validation errors")]
	[AllureDescription("Calls application-create-db with missing required parameters and verifies error handling.")]
	public async Task ApplicationCreateDb_Should_Report_Validation_Errors()
	{
		McpE2ESettings settings = McpE2ESettings.Load();
		string environmentName = settings.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(McpE2ESettings.Load());

		var args = new Dictionary<string, object>
		{
			["name"] = "TestApp",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.Client.CallToolAsync(
			ToolName,
			args,
			session.CancellationToken);

		result.IsError.Should().BeTrue("missing required parameters should cause validation error");
	}
}
