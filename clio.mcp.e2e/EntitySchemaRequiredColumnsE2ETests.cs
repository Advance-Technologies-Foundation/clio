using System.Text.Json;
using Allure.NUnit.Attributes;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Exercises required-column filtering and argument rejection against a real sandbox.</summary>
[TestFixture]
[Category("E2E")]
[NonParallelizable]
[AllureFeature("entity-schema")]
public sealed class EntitySchemaRequiredColumnsE2ETests : McpContractFixtureBase {
	private const string ToolName = GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName;

	[TestCase(false)]
	[TestCase(true)]
	[Category("McpE2E.NoEnvironment")]
	[Description("Verifies the reported name alias resolves a single current contract rather than returning the entire catalogue.")]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("Get tool contract resolves the name alias")]
	[AllureDescription("Calls the real MCP server with flat and wrapped name aliases and verifies one named contract advertises required-only.")]
	public async Task GetContract_ShouldReturnNamedContract_WhenNameAliasIsUsed(bool wrapped) {
		// Arrange
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
		Dictionary<string, object?> args = new() { ["name"] = ToolName };

		// Act
		CallToolResult result = await Session.CallToolAsync(ToolContractGetTool.ToolName,
			wrapped ? new Dictionary<string, object?> { ["args"] = args } : args, timeout.Token);
		ToolContractGetResponse response = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(result);

		// Assert
		result.IsError.Should().NotBeTrue(because: "the supported name recovery must remain usable");
		response.Success.Should().BeTrue(because: "a known tool must resolve");
		response.Tools.Should().ContainSingle(because: "a named request must not fall back to the entire catalogue");
		JsonSerializer.Serialize(response).Should().Contain("required-only", because: "discovery must advertise the new supported filter");
	}

	[TestCase(null)]
	[TestCase("CrtCoreBase")]
	[Category("McpE2E.Sandbox")]
	[Description("Compares required-only output with the actual required metadata in merged and package-layer reads.")]
	[AllureTag(ToolName)]
	[AllureName("Required columns match actual Creatio metadata")]
	[AllureDescription("Reads Contact through the real MCP process with default, false, and true required-only values in merged and package-layer modes.")]
	public async Task GetProperties_ShouldFilterRequiredColumns_WhenRequested(string? package) {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
		Dictionary<string, object?> args = new() {
			["environment-name"] = settings.Sandbox.EnvironmentName,
			["schema-name"] = "Contact"
		};
		if (package is not null) {
			args["package-name"] = package;
		}

		// Act
		EntitySchemaPropertiesInfo all = await ReadAsync(args, timeout.Token);
		args["required-only"] = false;
		EntitySchemaPropertiesInfo unfiltered = await ReadAsync(args, timeout.Token);
		args["required-only"] = true;
		EntitySchemaPropertiesInfo required = await ReadAsync(args, timeout.Token);

		// Assert
		all.Columns.Should().NotBeNullOrEmpty(because: "the fixture must read actual Creatio metadata");
		all.Columns.Should().Contain(column => !column.Required, because: "the fixture must prove optional columns are removed");
		all.Columns.Should().Contain(column => column.Required, because: "the live fixture must prove required columns survive filtering");
		unfiltered.Should().BeEquivalentTo(all, because: "explicit false must preserve the default response");
		required.Columns.Should().BeEquivalentTo(all.Columns!.Where(column => column.Required),
			because: "the filtered response must contain exactly the columns Creatio marks required");
		(required with { Columns = all.Columns }).Should().BeEquivalentTo(all,
			because: "filtering must preserve all schema metadata including unfiltered counts");
		TestContext.Progress.WriteLine($"{package ?? "merged"}: {all.Columns!.Count} columns, {required.Columns!.Count} required");
	}

	[TestCase("search-pattern", "Name")]
	[TestCase("required-only", "yes")]
	[Category("McpE2E.NoEnvironment")]
	[Description("Rejects unsupported fields and invalid boolean values through the real MCP process.")]
	[AllureTag(ToolName)]
	[AllureName("Schema properties rejects invalid arguments")]
	[AllureDescription("Calls the real MCP process with an unsupported field or invalid boolean and checks actionable diagnostics before environment resolution.")]
	public async Task GetProperties_ShouldRejectInvalidArguments_WhenWrapped(string key, string value) {
		// Arrange
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
		Dictionary<string, object?> args = new() {
			["environment-name"] = "missing-issue-1551-environment",
			["schema-name"] = "Contact",
			[key] = value
		};

		// Act
		CallToolResult result = await Session.CallToolAsync(ToolName,
			new Dictionary<string, object?> { ["args"] = args }, timeout.Token);

		// Assert
		result.IsError.Should().BeTrue(because: "invalid arguments must not produce a successful schema response");
		string diagnostic = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		diagnostic.Should().Contain(key, because: "the error must identify the offending argument before environment resolution");
		if (key == "search-pattern") {
			diagnostic.Should().Contain("Valid:", because: "unknown arguments must list the accepted fields");
			diagnostic.Should().Contain("required-only", because: "the supported filter must be discoverable from the error");
		}
	}

	private async Task<EntitySchemaPropertiesInfo> ReadAsync(Dictionary<string, object?> args, CancellationToken token) {
		CallToolResult result = await Session.CallToolAsync(ToolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		result.IsError.Should().NotBeTrue(because: "valid schema reads must succeed on the sandbox");
		return EntitySchemaStructuredResultParser.Extract<EntitySchemaPropertiesInfo>(result);
	}
}
