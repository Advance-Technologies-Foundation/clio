using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-98566 review finding 6. Stand-free end-to-end proof that EVERY member of the process-designer family
/// names an unrecognised argument back to the caller instead of dropping it.
/// </summary>
/// <remarks>
/// <para>
/// The repository's AGENTS.md requires e2e coverage for every changed MCP tool and states that unit tests are
/// necessary but insufficient. Nine tools changed; this fixture covers all nine over the real
/// <c>clio mcp-server</c> process.
/// </para>
/// <para>
/// It needs no Creatio, and that is a property of the guard rather than a convenience: the unknown-key check
/// is the FIRST thing each tool does, ahead of the environment resolve and the package requirement, so the
/// refusal is produced before anything reaches out. The environment named below is deliberately one that does
/// not exist - if a tool ever answered about the environment instead of about the key, this fixture would say
/// so rather than passing quietly.
/// </para>
/// <para>
/// The call goes through the WRAPPED <c>{"args":{...}}</c> shape, which is what the published schema asks for
/// and the one shape <c>McpToolErrorFilter</c> never classifies. There is deliberately no null-args case
/// here: measured, <c>{"args":null}</c> is answered by the SDK binder before the tool body runs, so such a
/// test would assert something that does not happen - see
/// <c>docs/knowledge/McpServer/a-null-args-object-never-reaches-a-long-tail-tool.md</c>.
/// </para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature("process-designer")]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class ProcessDesignerUnknownArgumentE2ETests : McpContractFixtureBase {

	/// <summary>The mis-key sent to every tool: a plausible typo, not a nonsense token.</summary>
	private const string UnknownKey = "procesName";

	/// <summary>
	/// The whole shipped family, named through the tools' own constants so a rename cannot leave a stale
	/// string here. <c>list-user-tasks</c> is included because the shipped contract counts it in this family
	/// even though it lives outside the ProcessDesigner folder - the gap review finding 8 identified.
	/// </summary>
	private static IEnumerable<TestCaseData> FamilyCases() {
		yield return Case(CreateBusinessProcessTool.CreateBusinessProcessToolName);
		yield return Case(ModifyBusinessProcessTool.ModifyBusinessProcessToolName);
		yield return Case(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName);
		yield return Case(SetActiveProcessVersionTool.SetActiveProcessVersionToolName);
		yield return Case(DescribeProcessTool.ToolName);
		yield return Case(GetProcessSignatureTool.ToolName);
		// run-process is the ONE family member whose args record declares a `required` member
		// (RunProcessArgs.ProcessName). A `required` property is enforced by the SERIALIZER, so a payload
		// omitting it fails to bind and is answered with "argument 'args' ... must be an object" before the
		// tool body - and therefore before its unknown-key guard - ever runs. Measured, not assumed: without
		// process-name this case failed with exactly that message. Supplying it is what puts the call on the
		// path under test rather than on the binder's.
		yield return Case(RunProcessTool.ToolName, ("process-name", "UsrOrder_Handle"));
		yield return Case(ValidateProcessGraphTool.ToolName);
		yield return Case(ListUserTasksTool.ListUserTasksToolName);
	}

	private static TestCaseData Case(string toolName, params (string Key, string Value)[] extraArgs) =>
		new TestCaseData(toolName, extraArgs.ToDictionary(pair => pair.Key, pair => pair.Value))
			.SetArgDisplayNames(toolName);

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		// The shipping default of a fresh install: an empty Features map. Pinned by construction so the
		// fixture does not inherit whatever the developer's own appsettings happens to say.
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
	[TestCaseSource(nameof(FamilyCases))]
	[Description("Over the real MCP server, every process-designer tool names an unrecognised argument back "
		+ "to the caller rather than letting the serializer drop it and answering a validation mistake with a "
		+ "plausible success. Needs no Creatio: the guard precedes the environment resolve.")]
	[AllureFeature("process-designer")]
	public async Task EveryFamilyTool_Should_Name_AnUnrecognizedArgument(
			string toolName, Dictionary<string, string> extraArgs) {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		Dictionary<string, object?> args = new() {
			["environment-name"] = $"missing-env-{Guid.NewGuid():N}",
			[UnknownKey] = "UsrOrder_Handle"
		};
		foreach (KeyValuePair<string, string> extra in extraArgs) {
			args[extra.Key] = extra.Value;
		}

		// Act
		CallToolResult result = await Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
		string rendered = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

		// Assert
		rendered.Should().Contain(UnknownKey,
			because: $"{toolName} must NAME the key it could not bind - the serializer drops it silently on "
				+ "this wrapped path, so a caller who mis-keys an argument has no other way to see the loss");
		rendered.Should().NotContain("Object reference not set",
			because: $"{toolName} must answer the mistake, not fault on it");
	}
}
