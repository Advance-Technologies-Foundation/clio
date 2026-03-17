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
[AllureFeature("binding-create-db")]
[NonParallelizable]
public sealed class BindingCreateDbToolE2ETests
{
	private const string ToolName = "binding-create-db";

	[Test]
	[Description("Creates data binding through backend MCP server (DB-first approach).")]
	[AllureTag(ToolName)]
	[AllureName("Binding create DB tool completes successfully")]
	public async Task BindingCreateDb_Should_Create_Binding_Successfully()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["schemaName"] = "UsrTestEntity",
			["packageUId"] = "12345678-1234-1234-1234-123456789012",
			["rowsJson"] = "[{\"UsrName\":\"Test\",\"UsrValue\":123}]",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.IsError.Should().BeFalse("binding creation should succeed or report specific error");
		result.Content.Should().NotBeEmpty("result should contain response");
	}
}

[TestFixture]
[AllureNUnit]
[AllureFeature("binding-get-columns-db")]
[NonParallelizable]
public sealed class BindingGetColumnsDbToolE2ETests
{
	private const string ToolName = "binding-get-columns-db";

	[Test]
	[Description("Gets binding columns through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Binding get columns DB tool completes successfully")]
	public async Task BindingGetColumnsDb_Should_Get_Columns()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["schemaName"] = "Contact",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.IsError.Should().BeFalse("getting columns should succeed");
		result.Content.Should().NotBeEmpty("result should contain column list");
	}
}
