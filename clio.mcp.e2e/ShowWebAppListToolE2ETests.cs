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
/// End-to-end tests for the show-webApp-list MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("show-web-app-list")]
public sealed class ShowWebAppListToolE2ETests
{
	private const string ToolName = ShowWebAppListTool.ShowWebAppListToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes show-webApp-list, and verifies that the structured environment payload is returned without masking sensitive values.")]
	[AllureName("Show web app list returns unmasked structured environment settings")]
	[Description("Returns registered web application settings through MCP as structured JSON without masking password fields.")]
	public async Task ShowWebAppList_Should_Return_Unmasked_Structured_Result()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		EnvironmentSettings sandboxEnvironment = RegisteredClioEnvironmentSettingsResolver.Resolve(settings.Sandbox.EnvironmentName!);
		await using ShowWebAppListArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		ShowWebAppListActResult actResult = await ActAsync(arrangeContext);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertStructuredPayloadReturned(actResult);
		AssertSandboxEnvironmentIsPresent(actResult, settings.Sandbox.EnvironmentName!);
		AssertSensitiveValuesAreNotMasked(actResult, settings.Sandbox.EnvironmentName!, sandboxEnvironment);
	}

	[AllureStep("Arrange show-webApp-list MCP session")]
	[AllureDescription("Arrange by starting a real clio MCP server session for the show-webApp-list tool")]
	private static async Task<ShowWebAppListArrangeContext> ArrangeAsync(McpE2ESettings settings)
	{
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ShowWebAppListArrangeContext(session, cancellationTokenSource);
	}

	[AllureStep("Act by invoking show-webApp-list through MCP")]
	[AllureDescription("Act by discovering the show-webApp-list MCP tool and invoking it without arguments")]
	private static async Task<ShowWebAppListActResult> ActAsync(ShowWebAppListArrangeContext arrangeContext)
	{
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the show-webApp-list MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?>(),
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ShowWebAppListEnvironmentEnvelope> environments = ShowWebAppListResultParser.Extract(callResult);
		return new ShowWebAppListActResult(callResult, environments);
	}

	[AllureStep("Assert MCP tool result is successful at protocol level")]
	[AllureDescription("Assert that show-webApp-list returns a normal MCP tool result")]
	private static void AssertToolCallSucceeded(ShowWebAppListActResult actResult)
	{
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "show-webApp-list should return a structured MCP payload instead of a transport-level error");
	}

	[AllureStep("Assert structured environment payload is present")]
	[AllureDescription("Assert that show-webApp-list returns at least one structured environment item")]
	private static void AssertStructuredPayloadReturned(ShowWebAppListActResult actResult)
	{
		actResult.Environments.Should().NotBeEmpty(
			because: "the MCP tool should return the registered environments as structured JSON instead of log lines");
	}

	[AllureStep("Assert sandbox environment is present in result")]
	[AllureDescription("Assert that the configured sandbox environment is included in the returned structured list")]
	private static void AssertSandboxEnvironmentIsPresent(ShowWebAppListActResult actResult, string environmentName)
	{
		actResult.Environments.Should().Contain(
			environment => environment.Name == environmentName,
			because: "the real MCP result should include the configured sandbox environment from clio settings");
	}

	[AllureStep("Assert sensitive values are not masked in MCP result")]
	[AllureDescription("Assert that password and client secret values are returned unmasked for the sandbox environment in the MCP payload")]
	private static void AssertSensitiveValuesAreNotMasked(
		ShowWebAppListActResult actResult,
		string environmentName,
		EnvironmentSettings sandboxEnvironment)
	{
		ShowWebAppListEnvironmentEnvelope environment = actResult.Environments.Single(item => item.Name == environmentName);
		environment.Password.Should().Be(sandboxEnvironment.Password,
			because: "the MCP tool contract should return the real password instead of the masked CLI display value");

		if (!string.IsNullOrWhiteSpace(sandboxEnvironment.ClientSecret))
		{
			environment.ClientSecret.Should().Be(sandboxEnvironment.ClientSecret,
				because: "the MCP tool contract should return the real OAuth client secret instead of the masked CLI display value");
		}
	}

	private sealed record ShowWebAppListArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record ShowWebAppListActResult(
		CallToolResult CallResult,
		IReadOnlyList<ShowWebAppListEnvironmentEnvelope> Environments);
}
