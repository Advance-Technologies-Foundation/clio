using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(SchemaNamePrefixTool.GetSchemaNamePrefixToolName)]
[NonParallelizable]
public sealed class SchemaNamePrefixToolE2ETests {

	private const string ToolName = SchemaNamePrefixTool.GetSchemaNamePrefixToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes get-schema-name-prefix against the configured sandbox environment, and verifies the structured response returns the active SchemaNamePrefix system setting.")]
	[AllureTag(ToolName)]
	[AllureName("Get schema name prefix returns structured SchemaNamePrefix payload")]
	[AllureDescription("Uses the real clio MCP server to call get-schema-name-prefix against the configured reachable sandbox environment and verifies that the structured response reports success and returns a non-null prefix value.")]
	public async Task GetSchemaNamePrefix_Should_Return_Structured_Prefix_Response() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3), requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName
			});
		SchemaNamePrefixResult response = EntitySchemaStructuredResultParser.Extract<SchemaNamePrefixResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-schema-name-prefix should return a structured success payload for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "reading SchemaNamePrefix should succeed when the environment is reachable and the system setting is accessible");
		response.SchemaNamePrefix.Should().NotBeNullOrWhiteSpace(
			because: "the sandbox environment should have SchemaNamePrefix configured to a non-empty value");
		response.Error.Should().BeNull(
			because: "no error message should be present when the tool call succeeds");
	}

	private static async Task<CallToolResult> CallToolAsync(
		ArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the get-schema-name-prefix tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed through the lazy surface");
		return await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(
		McpE2ESettings settings,
		TimeSpan timeout,
		bool requireReachableEnvironment) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string? environmentName = requireReachableEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: settings.Sandbox.EnvironmentName;
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run SchemaNamePrefix MCP E2E tests.");
		}

		if (!await CanReachEnvironmentAsync(settings, configuredEnvironmentName!)) {
			Assert.Ignore($"SchemaNamePrefix MCP E2E requires a reachable sandbox environment. '{configuredEnvironmentName}' was not reachable.");
		}

		return configuredEnvironmentName!;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
