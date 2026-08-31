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

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-process-page-facts MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("get-process-page-facts")]
[NonParallelizable]
public sealed class ProcessPageFactsToolE2ETests : McpContractFixtureBase {
	private const string ToolName = ProcessPageFactsTool.ToolName;
	private const string SeededPage = "ClioMcp_BlankPageToSave";

	[Test]
	[Description("Exposes get-process-page-facts via the get-tool-contract compact index so callers can discover it on the lazy surface.")]
	[AllureTag(ToolName)]
	[AllureName("get-process-page-facts is discoverable on the lazy surface")]
	[AllureDescription("Verifies that get-process-page-facts is discoverable through the get-tool-contract compact index, the surface every process-designer tool is reached from.")]
	public async Task ProcessPageFactsTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		// Long-tail on purpose, and asserted the way this repository asserts long-tail tools: it sits with the
		// four process-designer tools it feeds, none of which are resident either. Making THIS one resident while
		// create-business-process stays long-tail would advertise the prerequisite and hide the operation.
		toolNames.Should().Contain(ToolName,
			because: "get-process-page-facts must be discoverable on the lazy surface (get-tool-contract compact "
				+ "index) even though it is not resident in tools/list — it is where an agent building a "
				+ "Pre-configured page element learns the page's buttons and data sources");
	}

	[Test]
	[Description("Reads the seeded Freedom UI page via get-process-page-facts and verifies the response shape a process descriptor consumes.")]
	[AllureTag(ToolName)]
	[AllureName("get-process-page-facts reports completing-button candidates for a Freedom UI page")]
	[AllureDescription("Calls get-process-page-facts against the seeded Freedom UI page and verifies it succeeds and reports candidates carrying the designer's caption composition, which is what the Pre-configured page process element stores.")]
	public async Task ProcessPageFactsTool_Should_Report_CompletingButtonCandidates() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult result = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = SeededPage,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ProcessPageFactsResponse response =
			EntitySchemaStructuredResultParser.Extract<ProcessPageFactsResponse>(result);

		// Assert
		response.Success.Should().BeTrue(because: $"the seeded page {SeededPage} is a Freedom UI page");
		response.SchemaName.Should().Be(SeededPage);
		response.CompletingButtonCandidates.Should().NotBeNull();
		response.DataSources.Should().NotBeNull(
			because: "an empty list and a missing list mean different things to the process element — absent facts "
				+ "leave its data-source parameters alone");
		// A Freedom UI page inherits Save/Close/Cancel from its template chain, which is the whole reason these
		// facts cannot be read server-side: they are not in the page's own body.
		response.CompletingButtonCandidates.Should().NotBeEmpty(
			because: "the template chain contributes page-completing buttons even to a blank page");
		response.CompletingButtonCandidates.Should().OnlyContain(
			button => !string.IsNullOrWhiteSpace(button.Name)
				&& button.Caption.EndsWith($" | {button.Name}", StringComparison.Ordinal)
				&& button.Event == "clicked",
			because: "the element stores the designer's '<caption> | <name>' composition and the clicked event");
		// The one assertion in the repo that exercises the name-dedup against REAL stand data: a live page carries
		// the same ActionButtonsContainer in two places, so without the collapse this reported 7 entries for 4
		// buttons — and the process element identifies a button by name.
		response.CompletingButtonCandidates.Select(button => button.Name).Should().OnlyHaveUniqueItems(
			because: "duplicated containers on a real page must collapse to one entry per button name");
	}

	#region Methods: Private

	private async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		McpServerSession session = Session;
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	/// <summary>
	/// Resolves an environment the sandbox can actually reach, ignoring the test rather than failing it when none
	/// is available — the same policy the other environment-dependent E2E fixtures follow.
	/// </summary>
	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName)
			&& await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}
		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}
		Assert.Ignore(
			$"get-process-page-facts MCP E2E requires a reachable environment. Configured sandbox environment "
			+ $"'{configuredEnvironmentName}' was not reachable, and fallback environment "
			+ $"'{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	#endregion

}
