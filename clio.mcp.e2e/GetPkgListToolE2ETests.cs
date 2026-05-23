using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the list-packages MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("list-packages")]
[NonParallelizable]
public sealed class GetPkgListToolE2ETests {
	private const string ToolName = GetPkgListTool.GetPkgListToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes list-packages for the configured sandbox environment, and verifies that a structured non-empty package list is returned.")]
	[AllureTag(ToolName)]
	[AllureName("Get package list returns structured sandbox packages")]
	[AllureDescription("Uses the real clio MCP server to call list-packages against the configured sandbox environment and verifies the returned structured package list contains at least one item with non-empty name, version, and maintainer fields.")]
	public async Task GetPkgList_Should_Return_Structured_Packages() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to install cliogate and run list-packages end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using GetPkgListArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		GetPkgListActResult actResult = await ActAsync(arrangeContext, settings.Sandbox.EnvironmentName!);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertStructuredPackagesReturned(actResult);
	}

	[AllureStep("Arrange list-packages MCP session")]
	private static async Task<GetPkgListArrangeContext> ArrangeAsync(McpE2ESettings settings) {
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		await ClioCliCommandRunner.EnsureCliogateInstalledAsync(
			settings,
			settings.Sandbox.EnvironmentName!,
			cancellationTokenSource.Token);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new GetPkgListArrangeContext(session, cancellationTokenSource);
	}

	[AllureStep("Act by invoking list-packages through MCP")]
	private static async Task<GetPkgListActResult> ActAsync(GetPkgListArrangeContext arrangeContext, string environmentName) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the list-packages MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		IReadOnlyList<GetPkgListEnvelope> packages = GetPkgListResultParser.Extract(callResult);
		return new GetPkgListActResult(callResult, packages);
	}

	[AllureStep("Assert MCP tool result is successful")]
	private static void AssertToolCallSucceeded(GetPkgListActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "list-packages should return a structured MCP payload for a valid sandbox environment");
	}

	[AllureStep("Assert structured package list is present")]
	private static void AssertStructuredPackagesReturned(GetPkgListActResult actResult) {
		actResult.Packages.Should().NotBeEmpty(
			because: "the sandbox environment should expose at least one package through list-packages");
		actResult.Packages.Should().Contain(package =>
				!string.IsNullOrWhiteSpace(package.Name)
				&& !string.IsNullOrWhiteSpace(package.Version)
				&& package.Maintainer != null,
			because: "the MCP tool should return at least one structured package record with usable fields for agents and assertions");
	}

	private sealed record GetPkgListArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record GetPkgListActResult(
		CallToolResult CallResult,
		IReadOnlyList<GetPkgListEnvelope> Packages);
}
