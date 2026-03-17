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
[AllureFeature("entity-create-db")]
[NonParallelizable]
public sealed class EntityCreateDbToolE2ETests
{
	private const string ToolName = "entity-create-db";

	[Test]
	[Description("Creates entity schema through backend MCP server (DB-first approach).")]
	[AllureTag(ToolName)]
	[AllureName("Entity create DB tool completes successfully")]
	public async Task EntityCreateDb_Should_Create_Entity_Successfully()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["packageUId"] = "12345678-1234-1234-1234-123456789012",
			["name"] = $"UsrTestEntity{DateTime.Now:yyyyMMddHHmmss}",
			["caption"] = "Test Entity",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.IsError.Should().BeFalse("entity creation should succeed or report specific error");
		result.Content.Should().NotBeEmpty("result should contain response");
	}
}

[TestFixture]
[AllureNUnit]
[AllureFeature("entity-create-lookup-db")]
[NonParallelizable]
public sealed class EntityCreateLookupDbToolE2ETests
{
	private const string ToolName = "entity-create-lookup-db";

	[Test]
	[Description("Creates lookup schema through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity create lookup DB tool completes successfully")]
	public async Task EntityCreateLookupDb_Should_Create_Lookup_Successfully()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["packageUId"] = "12345678-1234-1234-1234-123456789012",
			["name"] = $"UsrTestLookup{DateTime.Now:yyyyMMddHHmmss}",
			["caption"] = "Test Lookup",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.IsError.Should().BeFalse("lookup creation should succeed or report specific error");
		result.Content.Should().NotBeEmpty("result should contain response");
	}
}

[TestFixture]
[AllureNUnit]
[AllureFeature("entity-check-name-db")]
[NonParallelizable]
public sealed class EntityCheckNameDbToolE2ETests
{
	private const string ToolName = "entity-check-name-db";

	[Test]
	[Description("Checks if entity name is taken through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity check name DB tool completes successfully")]
	public async Task EntityCheckNameDb_Should_Check_Name()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = settings.Sandbox.EnvironmentName;

		await using McpServerSession session = await McpServerSession.StartAsync(settings, CancellationToken.None);

		var args = new Dictionary<string, object>
		{
			["name"] = "Contact",
			["environmentName"] = environmentName
		};

		CallToolResult result = await session.CallToolAsync(
			ToolName,
			args,
			CancellationToken.None);

		result.IsError.Should().BeFalse("checking name should succeed");
		result.Content.Should().NotBeEmpty("result should contain availability info");
	}
}

[TestFixture]
[AllureNUnit]
[AllureFeature("entity-list-packages-db")]
[NonParallelizable]
public sealed class EntityListPackagesDbToolE2ETests
{
	private const string ToolName = "entity-list-packages-db";

	[Test]
	[Description("Lists all packages through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity list packages DB tool completes successfully")]
	public async Task EntityListPackagesDb_Should_List_Packages()
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

		result.IsError.Should().BeFalse("listing packages should succeed");
		result.Content.Should().NotBeEmpty("result should contain package list");
	}
}

[TestFixture]
[AllureNUnit]
[AllureFeature("entity-get-schema-db")]
[NonParallelizable]
public sealed class EntityGetSchemaDbToolE2ETests
{
	private const string ToolName = "entity-get-schema-db";

	[Test]
	[Description("Gets entity schema through backend MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("Entity get schema DB tool completes successfully")]
	public async Task EntityGetSchemaDb_Should_Get_Schema()
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

		result.IsError.Should().BeFalse("getting schema should succeed");
		result.Content.Should().NotBeEmpty("result should contain schema info");
	}
}
