using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for <c>install-dashboards-migrator</c>: the tool is reachable on a
/// default server, its curated contract describes the one argument and the outcome check, and an unknown
/// environment comes back as a structured failure. The install itself needs a stand and is verified by hand.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(InstallDashboardsMigratorTool.InstallDashboardsMigratorToolName)]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class InstallDashboardsMigratorContractToolE2ETests : McpContractFixtureBase {

	private const string ToolName = InstallDashboardsMigratorTool.InstallDashboardsMigratorToolName;

	[Test]
	[Description("get-tool-contract returns the curated install-dashboards-migrator contract: environment-name is the only required argument and the description states the outcome check without quoting a duration.")]
	[AllureTag(ToolName)]
	[AllureName("install-dashboards-migrator advertises a curated contract")]
	public async Task InstallDashboardsMigrator_Contract_Should_Describe_Arguments_And_Outcome_Verification() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { ToolName } }
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse response =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(because: "the tool carries a curated contract, so a named lookup succeeds");
		ToolContractDefinition contract = response.Tools!.Single(tool => tool.Name == ToolName);
		contract.InputSchema.Required.Should().Equal(["environment-name"],
			because: "the registered environment is the only thing the caller supplies; --force is CLI-only");
		contract.Description.Should().Contain("Ping",
			because: "an agent must learn that success means the package's own service answered, not that the install call returned");
		contract.Description.Should().NotMatchRegex(@"(?i)\d+\s*(s|sec|secs|second|seconds|min|mins|minute|minutes|hr|hrs|hour|hours)\b",
			because: "elapsed time belongs to the target environment and a figure here is repeated to users as a promise");
		contract.Description.Should().MatchRegex(@"(?i)ask the user before every call",
			because: "ENG-102116: this long-tail tool's contract is the only text an agent reads before calling it, and "
				+ "clio-run collapses its Destructive flag, so the consent rule must be stated here");
		contract.Description.Should().MatchRegex(
			@"(?i)builds the environment's configuration and restarts the site for every connected user",
			because: "the cost the user is asked to accept must be named, not implied");
		contract.Description.Should().NotContain("otherwise safe",
			because: "a re-run is another configuration build and restart, which needs its own approval");
		contract.Preconditions.Should().Contain(precondition => precondition.Contains("approved this install"),
			because: "an agent checking preconditions before the call must meet the user's approval among them");
	}

	[Test]
	[Description("Invokes install-dashboards-migrator with an unknown environment name over the real MCP server and verifies a readable structured failure rather than a transport error.")]
	[AllureTag(ToolName)]
	[AllureName("install-dashboards-migrator reports invalid environment failures")]
	public async Task InstallDashboardsMigrator_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		string invalidEnvironmentName = $"missing-install-dashboards-migrator-env-{Guid.NewGuid():N}";
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName, because: "the tool must be discoverable before the end-to-end call");

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = invalidEnvironmentName }
			},
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an unresolvable environment is an expected command outcome, not an MCP transport error");
		execution.ExitCode.Should().Be(1,
			because: "the shared BaseTool resolver catch returns FromResolverError (ExitCode=1) for an environment-resolution failure");
		string combinedOutput = string.Join(
			Environment.NewLine,
			(execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|not registered)",
			because: "the failure should tell a human that the requested environment is not registered");
	}

}
