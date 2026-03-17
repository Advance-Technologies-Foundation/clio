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
[AllureFeature("application-get-info-db")]
[NonParallelizable]
public sealed class ApplicationGetInfoDbToolE2ETests
{
	private const string ToolName = "application-get-info-db";

	[Test]
	[Description("Gets application info through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Application get info DB tool completes successfully")]
	public async Task ApplicationGetInfoDb_Should_Get_Application_Info()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["args"] = new Dictionary<string, object>
			{
				["appCode"] = "CoreCRM",
				["environmentName"] = environmentName
			}
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.IsError.Should().BeFalse("getting application info should succeed");
		result.Content.Should().NotBeEmpty("result should contain application info");
	}
}

[TestFixture]
[AllureNUnit]
[AllureFeature("application-get-list-db")]
[NonParallelizable]
public sealed class ApplicationGetListDbToolE2ETests
{
	private const string ToolName = "application-get-list-db";

	[Test]
	[Description("Lists all applications through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Application get list DB tool completes successfully")]
	public async Task ApplicationGetListDb_Should_Get_Application_List()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		TestContext.WriteLine($"Testing {ToolName} with environment: {environmentName}");

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["args"] = new Dictionary<string, object>
			{
				["environmentName"] = environmentName
			}
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		if (result.IsError == true)
		{
			TestContext.WriteLine("ERROR: Tool returned error!");
			TestContext.WriteLine($"Content count: {result.Content?.Count ?? 0}");
			if (result.Content != null)
			{
				foreach (var item in result.Content)
				{
					TestContext.WriteLine($"  - Type: {item.Type}");
					TestContext.WriteLine($"  - ToString: {item}");
				}
			}
		}

		(result.IsError == true).Should().BeFalse("getting application list should succeed");
		result.Content.Should().NotBeEmpty("result should contain application list");
	}
}
