using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for <c>set-active-business-process-version</c>. NOT in CI — run manually. The
/// advertised-tool test is hermetic; the functional test builds a process, saves a version of it, activates
/// that version and reads the family back, gated on a reachable environment carrying CrtProcessBuilder
/// 1.6.1.0 or newer and a writable <c>Custom</c> package.
/// </summary>
/// <remarks>
/// This fixture changes what the target environment EXECUTES, and nothing it creates can be removed: a
/// version has no delete, and deleting the root would cancel every <c>SysProcessLog</c> row of the schema. It
/// belongs on a disposable sandbox, never on a stand anyone else uses. Each run leaves one more permanent
/// family behind, which is why the names are Guid-suffixed.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(SetActiveProcessVersionTool.SetActiveProcessVersionToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class SetActiveProcessVersionToolE2ETests {

	private const string ToolName = SetActiveProcessVersionTool.SetActiveProcessVersionToolName;
	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string VersionToolName = ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies set-active-business-process-version is discoverable via the get-tool-contract compact index (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("set-active-business-process-version is discoverable on the lazy surface of the clio MCP server")]
	public async Task SetActiveProcessVersion_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract "
				+ "compact index) even though it is not resident in tools/list");
	}

	[Test]
	[Description("Over the real MCP path: build a process, save a version of it, then activate that version and confirm through describe-business-process that the family really switched — the version is active and the root is not.")]
	[AllureTag(ToolName)]
	[AllureName("set-active-business-process-version switches which member of the family is actual")]
	public async Task SetActiveProcessVersion_Should_SwitchTheActualMemberOfTheFamily() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpActivateE2e{Guid.NewGuid():N}";
		await CallToolExpectingSuccessAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});
		await CallToolExpectingSuccessAsync(context, VersionToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["package-name"] = "Custom"
		});
		string versionName = $"{processName}Custom1";

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["version-name"] = versionName
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("\\u0022exit-code\\u0022:0",
			because: "the transport reports isError:null for a tool that ran and failed, so the exit-code is "
				+ "what actually says the switch happened");
		callResultJson.Should().Contain(versionName,
			because: "the reported version comes from a READ-BACK after the write, so seeing the requested name "
				+ "here is what proves the switch took effect rather than that the request was echoed");
		callResultJson.Should().Contain("Instances already running stay",
			because: "activation reaches new instances only, and the caller is told so on every success");

		// The read-back inside the tool proves the server agrees with itself; describing the family proves the
		// environment as a whole did, and that the previously-actual member really stopped being actual.
		//
		// Parsed rather than substring-matched: the graph is a JSON string nested inside the envelope, so
		// System.Text.Json escapes each of its quotes — which is why the exit-code assertions above search for
		// \\u0022 — and a Contain("\\"isActiveVersion\\": true") looks for a sequence that cannot occur.
		JsonObject describedVersion = DescribedProcessGraph.Read(await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = versionName
			}));
		describedVersion["isActiveVersion"]!.GetValue<bool>().Should().BeTrue(
			because: "the activated version must be the actual one afterwards");
		JsonObject describedRoot = DescribedProcessGraph.Read(await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		describedRoot["isActiveVersion"]!.GetValue<bool>().Should().BeFalse(
			because: "the platform logs and SWALLOWS a failed sibling deactivation, so two members left active "
				+ "is a real outcome — and only reading the OTHER member catches it");
		describedRoot["activeVersionName"]!.GetValue<string>().Should().Be(versionName,
			because: "the family read from the ROOT must point at the member just activated, which is what "
				+ "proves the switch reached the environment rather than only the response");
	}

	[Test]
	[Description("Over the real MCP path, the family ROOT can be made actual again after a version was activated - the 'go back to the original' rollback. The tool description used to state that the target must be a version and not the root; manual testing on ENG-94374 found the platform accepts the root, so a caller believing that text would conclude a rollback to the original is impossible.")]
	[AllureTag(ToolName)]
	[AllureName("set-active-business-process-version can make the family root actual again")]
	public async Task SetActiveProcessVersion_Should_ActivateTheFamilyRoot_WhenRollingBackToTheOriginal() {
		// Arrange — a family whose ACTIVE member is the version, so activating the root is a real switch
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpRootActivateE2e{Guid.NewGuid():N}";
		await CallToolExpectingSuccessAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});
		await CallToolExpectingSuccessAsync(context, VersionToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["package-name"] = "Custom"
		});
		string versionName = $"{processName}Custom1";
		await CallToolExpectingSuccessAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["version-name"] = versionName
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["version-name"] = processName
		});

		// Assert
		// Parsed rather than substring-matched on the serialized envelope, and that is load-bearing TWICE
		// here. The version's code is $"{processName}Custom1", so it CONTAINS the root's: a Contain check
		// passes on an envelope that named the version, which is the exact regression this test exists to
		// catch. And the payload is a JSON string nested inside the envelope, so its quotes arrive escaped -
		// a naive Contain("\"exit-code\":0") searches for a sequence that cannot occur.
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		execution.ExitCode.Should().Be(0,
			because: "activating the root is an ordinary activation, not a refused one");
		// The READ-BACK line, not the joined log. The command echoes the request before the POST
		// ("Activating version '<name>' on '<env>'..."), and on this test that echo already contains
		// processName - so a Contain over every message is satisfied by the request, including in the
		// regression it is meant to catch. Only the line the environment answered with is evidence.
		string readBack = (execution.Output ?? [])
			.Select(message => message.Value ?? string.Empty)
			.Single(value => value.Contains("is now the actual one"));
		readBack.Should().Contain(processName,
			because: "the environment reports which member it considers actual, and that has to be the root");
		readBack.Should().NotContain(versionName,
			because: "the root's code is a PREFIX of the version's, so only the absence of the version's own "
				+ "code separates 'the root is actual' from 'the activation was a no-op'");
		JsonObject describedRoot = DescribedProcessGraph.Read(await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		describedRoot["isActiveVersion"]!.GetValue<bool>().Should().BeTrue(
			because: "the root is the member the runtime executes once it has been made actual");
		JsonObject describedVersion = DescribedProcessGraph.Read(await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = versionName
			}));
		describedVersion["isActiveVersion"]!.GetValue<bool>().Should().BeFalse(
			because: "a sibling deactivation the platform swallowed would leave two members active, and only "
				+ "reading the other member catches it");
	}

	[Test]
	[Description("Over the real MCP path, activating the same version twice succeeds both times and lands in the same state — the tool declares Idempotent=true, and this is what that claim means.")]
	[AllureTag(ToolName)]
	[AllureName("set-active-business-process-version is idempotent for a repeated activation")]
	public async Task SetActiveProcessVersion_Should_Succeed_WhenTheVersionIsAlreadyActual() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpReactivateE2e{Guid.NewGuid():N}";
		await CallToolExpectingSuccessAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});
		await CallToolExpectingSuccessAsync(context, VersionToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["package-name"] = "Custom"
		});
		string versionName = $"{processName}Custom1";
		var activateArgs = new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["version-name"] = versionName
		};
		await CallToolExpectingSuccessAsync(context, ToolName, activateArgs);

		// Act
		CallToolResult second = await CallToolAsync(context, ToolName, activateArgs);

		// Assert
		JsonSerializer.Serialize(second).Should().Contain("\\u0022exit-code\\u0022:0",
			because: "re-activating the version that is already actual is a no-op, not a refusal — an agent that "
				+ "cannot tell whether its first call landed must be able to repeat it safely");
		JsonSerializer.Serialize(second).Should().Contain(versionName,
			because: "the second call reports the same read-back version as the first");
	}

	[Test]
	[Description("Over the real MCP path, supplying neither version-name nor version-uid is refused by the tool itself, naming both options, without reaching the environment.")]
	[AllureTag(ToolName)]
	[AllureName("set-active-business-process-version refuses a request with no version identity")]
	public async Task SetActiveProcessVersion_Should_Refuse_WhenNoVersionIdentityGiven() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = "any-environment"
		});

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain("version-name",
			because: "the refusal must name WHICH rule was broken; a bare failure sends the agent guessing");
	}

	#region Methods: Private

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Activate E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	// The MCP transport reports isError:null for a tool that ran and FAILED - the failure lives in the
	// payload's exit-code. An arrange step checked only for a transport error therefore "succeeds" against
	// a stand where nothing was created, and the act step then fails pointing at the wrong cause.
	private static async Task<CallToolResult> CallToolExpectingSuccessAsync(ArrangeContext context,
		string toolName, Dictionary<string, object?> args) {
		CallToolResult result = await CallToolAsync(context, toolName, args);
		JsonSerializer.Serialize(result).Should().Contain("\\u0022exit-code\\u0022:0",
			because: $"{toolName} had to succeed for the rest of this test to mean anything");
		return result;
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: $"the {toolName} tool must be discoverable via the get-tool-contract compact index before "
				+ "the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (carrying CrtProcessBuilder 1.6.1.0 or "
					+ "newer) to run set-active-business-process-version MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
				Assert.Ignore("set-active-business-process-version MCP E2E requires a reachable configured "
					+ $"sandbox environment. '{environmentName}' was not reachable.");
			}
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
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

	#endregion
}
