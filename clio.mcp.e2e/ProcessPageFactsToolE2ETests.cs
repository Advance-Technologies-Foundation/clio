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
		// MEASURED on the stand, and the correction of an assumption this test used to encode. It asserted that
		// "the template chain contributes page-completing buttons even to a blank page" — it does not. The seeded
		// fixture is a BLANK page: it has no record to save, so nothing above it in the chain contributes a
		// Save/Close/Cancel button, and the honest answer is an empty list. A form page is the opposite case
		// (Accounts_FormPage reports four), which is why the empty result here is about THIS page and not about
		// the projection.
		response.CompletingButtonCandidates.Should().BeEmpty(
			because: "a blank page carries no completing button — the assumption that every page inherits one "
				+ "was wrong, and this fixture is what disproved it");
		// The half that matters more: an empty list must NOT read as a clean answer. An element built on this
		// page could never finish at run time, so the tool flags it — and nothing else in the suite exercises
		// that warning end to end.
		response.Warnings.Should().NotBeNullOrEmpty(
				because: "an empty candidate list is ambiguous between 'no buttons' and 'shape not recognised'")
			.And.Contain(warning => warning.Contains("can never finish at run time", StringComparison.Ordinal),
				because: "the warning has to name the run-time consequence, not merely report a count");
		response.CompletingButtonCandidates.Select(button => button.Name).Should().OnlyHaveUniqueItems(
			because: "one entry per button name, whatever the page carries");
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
