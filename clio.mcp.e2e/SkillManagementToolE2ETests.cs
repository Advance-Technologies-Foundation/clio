using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the multi-agent skill management MCP tools.
/// </summary>
/// <remarks>
/// These exercise the real <c>clio mcp-server</c> path. To avoid mutating the
/// developer's real agent homes (~/.claude, ~/.codex, ...), every test isolates
/// HOME / USERPROFILE to a throwaway temp directory, so no agents are detected
/// and install/delete are clean no-ops. Side-effecting per-agent flows (real
/// CLI calls, marketplace clones) are intentionally not driven here.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureFeature("multi-agent-skill-management")]
[Parallelizable(ParallelScope.Self)]
public sealed class SkillManagementToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(InstallSkillsTool.ToolName)]
	[Description("The real clio MCP server exposes install-skills, update-skill, and delete-skill via the get-tool-contract compact index on the lazy tool surface.")]
	[AllureName("Skill management tools are discoverable on the lazy surface")]
	public async Task SkillManagementTools_ShouldBeAdvertised() {
		// Arrange
		await using var context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(InstallSkillsTool.ToolName,
			because: "install-skills must be discoverable via the get-tool-contract compact index even though it is not resident in tools/list");
		toolNames.Should().Contain(UpdateSkillTool.ToolName,
			because: "update-skill must be discoverable via the get-tool-contract compact index even though it is not resident in tools/list");
		toolNames.Should().Contain(DeleteSkillTool.ToolName,
			because: "delete-skill must be discoverable via the get-tool-contract compact index even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(InstallSkillsTool.ToolName)]
	[Description("install-skills with no detected agents is a clean no-op that exits 0.")]
	[AllureName("Install Skills tool is a no-op when no agents are detected")]
	public async Task InstallSkills_ShouldSucceedAsNoOp_WhenNoAgentsDetected() {
		// Arrange
		await using var context = Arrange();

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			context, InstallSkillsTool.ToolName, new Dictionary<string, object?>());

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0);
	}

	[Test]
	[AllureTag(InstallSkillsTool.ToolName)]
	[Description("install-skills rejects an unknown --target value before dispatching to any agent.")]
	[AllureName("Install Skills tool rejects an unknown target")]
	public async Task InstallSkills_ShouldFail_WhenTargetIsUnknown() {
		// Arrange
		await using var context = Arrange();

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			context, InstallSkillsTool.ToolName, new Dictionary<string, object?> {
				["target"] = "foobar"
			});

		// Assert
		AssertToolCallFailed(actResult);
	}

	[Test]
	[AllureTag(DeleteSkillTool.ToolName)]
	[Description("delete-skill with no detected agents is an idempotent no-op that exits 0.")]
	[AllureName("Delete Skill tool is a no-op when no agents are detected")]
	public async Task DeleteSkill_ShouldSucceedAsNoOp_WhenNoAgentsDetected() {
		// Arrange
		await using var context = Arrange();

		// Act
		SkillManagementActResult actResult = await CallToolAsync(
			context, DeleteSkillTool.ToolName, new Dictionary<string, object?>());

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0);
	}

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string isolatedHome = CreateFixtureDirectory("skill-management-home");
		// Redirect the child clio process's user home so no real agent is detected.
		settings.ProcessEnvironmentVariables["USERPROFILE"] = isolatedHome;
		settings.ProcessEnvironmentVariables["HOME"] = isolatedHome;
	}

	[AllureStep("Call a skill management tool")]
	private static async Task<SkillManagementActResult> CallToolAsync(
		ArrangeContext context,
		string toolName,
		Dictionary<string, object?> arguments) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the requested skill management MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call");

		CallToolResult callResult = await context.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			context.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new SkillManagementActResult(callResult, execution);
	}

	[AllureStep("Assert MCP tool result is successful")]
	private static void AssertToolCallSucceeded(SkillManagementActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a successful invocation should return a normal MCP tool result. Content: {DescribeCallResult(actResult.CallResult)}");
	}

	[AllureStep("Assert MCP tool result failed")]
	private static void AssertToolCallFailed(SkillManagementActResult actResult) {
		bool failed = actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0;
		failed.Should().BeTrue(because: "an invalid request should fail");
	}

	[AllureStep("Assert command exit code")]
	private static void AssertCommandExitCode(SkillManagementActResult actResult, int expectedExitCode) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode,
			because: "the underlying command exit code should match the scenario outcome");
	}

	private static string DescribeCallResult(CallToolResult callResult) {
		if (callResult.Content is null || callResult.Content.Count == 0) {
			return "<no content>";
		}

		return string.Join(" | ", callResult.Content.Select(content => content?.ToString() ?? "<null>"));
	}

	private sealed record SkillManagementActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
