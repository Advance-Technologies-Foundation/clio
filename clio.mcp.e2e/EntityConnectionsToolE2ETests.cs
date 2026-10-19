using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Contract-level end-to-end tests for get-entity-connections and update-entity-connections, driven through the real
/// clio MCP server without a Creatio environment.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetEntityConnectionsTool.ToolName)]
[NonParallelizable]
public sealed class EntityConnectionsToolE2ETests : McpContractFixtureBase {
	private const string GetToolName = GetEntityConnectionsTool.ToolName;
	private const string UpdateToolName = UpdateEntityConnectionsTool.ToolName;

	[Test]
	[Description("Exposes both Connections tools through the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(GetToolName)]
	[AllureTag(UpdateToolName)]
	[AllureName("Connections MCP tools are discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies get-entity-connections and update-entity-connections are reachable through the get-tool-contract compact index although they are not resident in tools/list.")]
	public async Task EntityConnectionsTools_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await Session.ListReachableToolNamesAsync(deadline.Token);

		// Assert
		toolNames.Should().Contain(GetToolName,
			because: "get-entity-connections must be discoverable through get-tool-contract");
		toolNames.Should().Contain(UpdateToolName,
			because: "update-entity-connections must be discoverable through get-tool-contract");
	}

	[Test]
	[Description("get-entity-connections reports an invalid environment inside the structured response rather than an MCP binding error.")]
	[AllureTag(GetToolName)]
	[AllureName("get-entity-connections reports an invalid environment")]
	[AllureDescription("Calls get-entity-connections with an intentionally missing environment and verifies the structured response reports the unresolved environment.")]
	public async Task GetEntityConnections_ShouldReportInvalidEnvironment_WhenEnvironmentMissing() {
		// Arrange
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
		string environmentName = $"missing-connections-env-{Guid.NewGuid():N}";

		// Act
		EntityConnectionsResponse response = await CallAsync(GetToolName, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["entity-schema-name"] = "Account",
			["package-name"] = "Custom"
		}, deadline.Token);

		// Assert
		AllureApi.Step("The read fails on the environment, not on argument binding", () => {
			response.Success.Should().BeFalse(because: "the environment does not exist");
			response.Error.Should().MatchRegex(
				$"(?is)({System.Text.RegularExpressions.Regex.Escape(environmentName)}|environment.*not.*found|not found)",
				because: "the failure should come from resolving the requested environment");
		});
	}

	[Test]
	[Description("update-entity-connections binds the connections array and reports an invalid environment inside the structured response, with no write.")]
	[AllureTag(UpdateToolName)]
	[AllureName("update-entity-connections binds the connections array and reports an invalid environment")]
	[AllureDescription("Calls update-entity-connections with a connections array and an intentionally missing environment and verifies the array binds and the structured response reports the unresolved environment.")]
	public async Task UpdateEntityConnections_ShouldReportInvalidEnvironment_WhenEnvironmentMissing() {
		// Arrange
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
		string environmentName = $"missing-connections-env-{Guid.NewGuid():N}";

		// Act
		EntityConnectionsResponse response = await CallAsync(UpdateToolName, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["entity-schema-name"] = "Account",
			["package-name"] = "Custom",
			["connections"] = new[] { "Owner", "PrimaryContact" }
		}, deadline.Token);

		// Assert
		AllureApi.Step("The write fails on the environment, not on argument binding", () => {
			response.Success.Should().BeFalse(because: "the environment does not exist");
			response.Changed.Should().BeNull(because: "nothing was written");
			response.Error.Should().MatchRegex(
				$"(?is)({System.Text.RegularExpressions.Regex.Escape(environmentName)}|environment.*not.*found|not found)",
				because: "the failure should come from resolving the environment, not from rejecting the array");
		});
	}

	[Test]
	[Description("get-entity-connections rejects a blank entity-schema-name before any remote call.")]
	[AllureTag(GetToolName)]
	[AllureName("get-entity-connections rejects a blank entity-schema-name")]
	[AllureDescription("Calls get-entity-connections with a blank entity-schema-name and verifies the structured response names the missing field.")]
	public async Task GetEntityConnections_ShouldRejectBlankEntitySchemaName() {
		// Arrange
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));

		// Act
		EntityConnectionsResponse response = await CallAsync(GetToolName, new Dictionary<string, object?> {
			["environment-name"] = $"noop-{Guid.NewGuid():N}",
			["entity-schema-name"] = " ",
			["package-name"] = "Custom"
		}, deadline.Token);

		// Assert
		response.Success.Should().BeFalse(because: "a blank entity-schema-name fails validation");
		response.Error.Should().Contain("entity-schema-name", because: "the missing field is named");
	}

	[Test]
	[Description("update-entity-connections refuses an unknown argument such as an 'add' convenience list instead of silently dropping it.")]
	[AllureTag(UpdateToolName)]
	[AllureName("update-entity-connections refuses an unknown argument")]
	[AllureDescription("Calls update-entity-connections with an extra 'add' list and verifies the structured response names the unknown argument before any environment is resolved.")]
	public async Task UpdateEntityConnections_ShouldRefuseUnknownArgument() {
		// Arrange
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));

		// Act
		EntityConnectionsResponse response = await CallAsync(UpdateToolName, new Dictionary<string, object?> {
			["environment-name"] = $"noop-{Guid.NewGuid():N}",
			["entity-schema-name"] = "Account",
			["package-name"] = "Custom",
			["connections"] = new[] { "Owner" },
			["add"] = new[] { "PrimaryContact" }
		}, deadline.Token);

		// Assert
		response.Success.Should().BeFalse(because: "a dropped 'add' list would turn into a removal of connections");
		response.Error.Should().Contain("'add'", because: "the unknown argument is named");
	}

	private async Task<EntityConnectionsResponse> CallAsync(
		string toolName, Dictionary<string, object?> args, CancellationToken token) {
		CallToolResult result = await Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, token);
		result.IsError.Should().NotBeTrue(
			because: "a resolvable payload stays inside the structured tool response, not an MCP binding error");
		return EntitySchemaStructuredResultParser.Extract<EntityConnectionsResponse>(result);
	}
}
