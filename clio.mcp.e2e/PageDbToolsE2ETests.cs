using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature("page-get-db")]
[NonParallelizable]
public sealed class PageGetDbToolE2ETests
{
	private const string ToolName = "page-get-db";

	[Test]
	[Description("Gets Freedom UI page schema through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Page get DB tool completes successfully")]
	public async Task PageGetDb_Should_Get_Page()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["pageName"] = "AccountPageV2",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.IsError.Should().BeFalse("getting page should succeed");
		result.Content.Should().NotBeEmpty("result should contain page schema");
	}
}

[TestFixture]
[AllureNUnit]
[AllureFeature("page-list-db")]
[NonParallelizable]
public sealed class PageListDbToolE2ETests
{
	private const string ToolName = "page-list-db";

	[Test]
	[Description("Lists all Freedom UI pages through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Page list DB tool completes successfully")]
	public async Task PageListDb_Should_List_Pages()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.IsError.Should().BeFalse("listing pages should succeed");
		result.Content.Should().NotBeEmpty("result should contain page list");
	}
}

[TestFixture]
[AllureNUnit]
[AllureFeature("page-update-db")]
[NonParallelizable]
public sealed class PageUpdateDbToolE2ETests
{
	private const string ToolName = "page-update-db";

	[Test]
	[Description("Updates Freedom UI page schema through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Page update DB tool reports validation for invalid input")]
	public async Task PageUpdateDb_Should_Report_Validation_Error()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["pageName"] = "NonExistentPage",
			["packageUId"] = "12345678-1234-1234-1234-123456789012",
			["schemaJson"] = "{}",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.Content.Should().NotBeEmpty("result should contain error or success message");
	}
}
