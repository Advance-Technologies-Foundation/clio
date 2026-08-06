using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for <c>install-process-builder</c>.
/// </summary>
/// <remarks>
/// The fixture runs against an isolated <c>CLIO_HOME</c> whose <c>Features</c> map is EMPTY, so the real
/// MCP server starts with <c>process-designer</c> off — the shipping default. That is not incidental
/// setup: it is the whole point of this fixture. <c>install-process-builder</c> is the remediation the
/// gated process-designer tools point at, so it must stay reachable on exactly the server where those
/// tools are invisible. Asserting that with the feature state pinned by construction (rather than read
/// from whatever the developer's own appsettings happens to say) is what makes the invariant a test
/// instead of a coincidence.
/// <para>
/// The unit twin (<c>InstallProcessBuilderToolTests.InstallProcessBuilderTool_Should_Not_Be_FeatureGated</c>)
/// asserts the ABSENCE of the attribute; this asserts the CONSEQUENCE — that the real server, having
/// filtered its primitives, still advertises the tool.
/// </para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(InstallProcessBuilderTool.InstallProcessBuilderToolName)]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class InstallProcessBuilderContractToolE2ETests : McpContractFixtureBase {

	private const string ToolName = InstallProcessBuilderTool.InstallProcessBuilderToolName;

	/// <summary>
	/// The five <c>[FeatureToggle("process-designer")]</c> MCP tools whose refusals name
	/// <c>install-process-builder</c> as the fix.
	/// </summary>
	private static readonly string[] FeatureGatedProcessDesignerTools = [
		CreateBusinessProcessTool.CreateBusinessProcessToolName,
		ModifyBusinessProcessTool.ModifyBusinessProcessToolName,
		DescribeProcessTool.ToolName,
		ListUserTasksTool.ListUserTasksToolName,
		ValidateProcessGraphTool.ToolName
	];

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		// An EMPTY Features map means process-designer is off, which is the shipping default and the
		// condition under which the remediation tool has to be reachable.
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
			  "Features": {},
			  "Environments": {
			    "dev": {
			      "Uri": "http://localhost",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""",
			GetType().Name);
	}

	[Test]
	[Description("With process-designer off, the real MCP server still advertises install-process-builder while hiding the five gated tools that name it as the remediation.")]
	[AllureTag(ToolName)]
	[AllureName("install-process-builder stays reachable while the process-designer tools are gated off")]
	[AllureDescription("Starts the real clio MCP server with an isolated CLIO_HOME whose Features map is empty, and verifies that install-process-builder is reachable while the five [FeatureToggle(\"process-designer\")] tools are not — so the advertised remediation is not filtered out by the very gate that makes it necessary.")]
	public async Task InstallProcessBuilder_Should_StayReachable_WhileProcessDesignerToolsAreGatedOff() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "install-process-builder is the remediation the gated tools point at, so a server with "
				+ "process-designer off must still advertise it — a gated primitive is filtered out of "
				+ "registration and would be unreachable exactly when it is needed");
		toolNames.Should().NotIntersectWith(FeatureGatedProcessDesignerTools,
			because: "this fixture's CLIO_HOME leaves Features empty, so the five [FeatureToggle(\"process-designer\")] "
				+ "tools must be invisible on every MCP surface — if they were reachable here the asymmetry this "
				+ "test claims to prove would not exist and the assertion above would be vacuous");
		toolNames.Should().Contain(GetProcessSignatureTool.ToolName,
			because: "get-process-signature is the other deliberately ungated member of the process-designer "
				+ "namespace (it reads the built-in DataService), which shows the gate is per-tool rather than "
				+ "per-namespace");
	}

	[Test]
	[Description("get-tool-contract returns the curated install-process-builder contract: environment-name required, and a description that states the outcome check without claiming the install needs no restart.")]
	[AllureTag(ToolName)]
	[AllureName("install-process-builder advertises a curated contract with the corrected restart wording")]
	[AllureDescription("Reads the curated install-process-builder contract over the real MCP path and verifies the required argument, the list-user-tasks follow-up flow, and that the description does not reassert the retracted \"no application restart\" claim.")]
	public async Task InstallProcessBuilder_Contract_Should_Describe_Arguments_And_Outcome_Verification() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await GetContractAsync(context, ToolName);

		// Assert
		response.Success.Should().BeTrue(
			because: "install-process-builder carries a curated contract, so a named lookup must succeed");
		response.Tools.Should().NotBeNull(
			because: "a successful named contract lookup must carry the tool definition");
		ToolContractDefinition contract = response.Tools!.Single(tool => tool.Name == ToolName);
		contract.InputSchema.Required.Should().Equal(["environment-name"],
			because: "the environment is the tool's only argument and it is mandatory — the package ships inside "
				+ "clio, so there is nothing else for the caller to supply");
		contract.PreferredFlow.Tools.Should().Equal([ToolName],
			because: "the flow must stop at this tool. It used to name list-user-tasks as a confirmation step, "
				+ "which contradicted the assertion above in this same fixture: that tool is feature-gated and "
				+ "absent from this very server, so the contract was telling an agent to call something it "
				+ "could not see");
		contract.PreferredFlow.Tools.Should().NotContain(ListUserTasksTool.ListUserTasksToolName,
			because: "naming a [FeatureToggle]-gated tool in the flow of an ungated one is the drift this pins: "
				+ "the two halves of this fixture would otherwise assert a contradiction and call it correct");
		contract.PreferredFlow.Notes.Should().NotMatchRegex(@"(?i)which\s+build\s+is\s+serving",
			because: "that capability was a ProcessDesignService.GetVersion operation which was implemented and "
				+ "then REVERTED. The probe left behind is ListUserTasks, which proves the service answers but "
				+ "not which assembly answered, so a contract claiming otherwise tells an agent an upgrade is "
				+ "verified when the outgoing build could have answered — the one failure this command exists "
				+ "to catch");
		contract.Description.Should().Contain(BundledPackages.ProcessBuilderPackageName,
			because: "the contract must name the package it installs so an agent can match it against the refusal "
				+ "text of the tool that sent it here");
		contract.Description.Should().NotMatchRegex(@"(?i)no\s+(application\s+)?restart",
			because: "the live runs disproved that claim on both runtimes — .NET Framework recycles itself and the "
				+ "installer restarts .NET hosts — so the contract must not tell an agent a restart does not happen");
		contract.Preconditions.Should().NotBeNullOrEmpty(
			because: "the tool needs package-install permission plus SysPackage read access on a registered "
				+ "environment, and an agent that reads the contract before calling should learn that from it. "
				+ "CanManageProcessDesign is NOT this tool's requirement — it gates the process-designer tools "
				+ "the caller retries afterwards");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes install-process-builder with an unknown environment name, and verifies a readable structured failure rather than a transport error.")]
	[AllureTag(ToolName)]
	[AllureName("install-process-builder reports invalid environment failures")]
	[AllureDescription("Calls install-process-builder with an unregistered environment name over the real MCP path and verifies the result stays a structured command-execution envelope with exit code 1 and a human-readable diagnostic.")]
	public async Task InstallProcessBuilder_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		string invalidEnvironmentName = $"missing-install-process-builder-env-{Guid.NewGuid():N}";
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));

		// Act
		CallToolResult callResult = await CallToolAsync(context, invalidEnvironmentName);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an unresolvable environment is an expected command outcome, so it must come back as a normal "
				+ "command-execution envelope rather than an MCP transport error");
		execution.ExitCode.Should().Be(1,
			because: $"install-process-builder routes through the shared BaseTool resolver catch, which returns "
				+ $"FromResolverError (ExitCode=1) for an expected environment-resolution failure, not the "
				+ $"unexpected-exception code -1. Actual execution: {DescribeExecution(execution)}");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "a failed execution should emit error diagnostics");
		string combinedOutput = string.Join(
			Environment.NewLine,
			(execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|not registered)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	private static async Task<ToolContractGetResponse> GetContractAsync(ArrangeContext context, string toolName) {
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { toolName } }
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "a named contract lookup for an advertised tool is a valid request shape");
		return EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string environmentName) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the install-process-builder tool must be discoverable before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = environmentName }
			},
			context.CancellationTokenSource.Token);
	}

	private static string DescribeExecution(CommandExecutionEnvelope execution) {
		string messages = execution.Output is null
			? "<no messages>"
			: string.Join(" | ", execution.Output.Select(message => $"{message.MessageType}: {message.Value}"));
		return $"ExitCode={execution.ExitCode}; Messages={messages}";
	}
}
