using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-related-page-addon MCP tool, driven through the real clio MCP server.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetRelatedPageAddonTool.ToolName)]
[NonParallelizable]
public sealed class GetRelatedPageAddonToolE2ETests {
	private const string ToolName = GetRelatedPageAddonTool.ToolName;

	[Test]
	[Description("Advertises the entity-schema-name and package-name inputs for get-related-page-addon through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("get-related-page-addon MCP tool advertises its inputs")]
	[AllureDescription("Starts the real clio MCP server, lists tools, and verifies get-related-page-addon exposes the entity-schema-name and package-name args.")]
	public async Task GetRelatedPageAddon_ShouldAdvertiseInputs_WhenToolsListed() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool tool = tools.Single(candidate => candidate.Name == ToolName);
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement argsSchema = inputSchema.GetProperty("properties").GetProperty("args").GetProperty("properties");
		argsSchema.EnumerateObject().Select(property => property.Name).Should().Contain(
			["entity-schema-name", "package-name"],
			because: "the read tool should advertise the object and package inputs");
	}

	[Test]
	[Description("Reports an invalid environment inside the structured response rather than an MCP binding error.")]
	[AllureTag(ToolName)]
	[AllureName("get-related-page-addon MCP tool reports an invalid environment")]
	[AllureDescription("Starts the real clio MCP server, calls get-related-page-addon with an intentionally missing environment, and verifies the structured response reports the unresolved environment.")]
	public async Task GetRelatedPageAddon_ShouldReportInvalidEnvironment_WhenEnvironmentMissing() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-get-related-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["entity-schema-name"] = "UsrOrder"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<GetRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a resolvable payload should stay inside the structured tool response, not surface as an MCP binding error");
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail before the add-on read");
		response.Error.Should().MatchRegex(
			$"(?is)({System.Text.RegularExpressions.Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the requested environment");
	}

	[Test]
	[Description("Rejects a blank entity-schema-name in the structured response before any remote call.")]
	[AllureTag(ToolName)]
	[AllureName("get-related-page-addon MCP tool rejects a blank entity-schema-name")]
	[AllureDescription("Starts the real clio MCP server, calls get-related-page-addon with a blank entity-schema-name, and verifies the structured response reports the missing field without an MCP binding error.")]
	public async Task GetRelatedPageAddon_ShouldRejectBlankEntitySchemaName_WhenWhitespace() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"noop-{Guid.NewGuid():N}",
					["package-name"] = "Custom",
					["entity-schema-name"] = " "
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<GetRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "input validation should stay inside the structured tool response, not surface as an MCP binding error");
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("entity-schema-name",
			because: "the structured response should name the missing required field");
	}

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
