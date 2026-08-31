using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the Approval element (ENG-92713) over the real MCP path. NOT in CI — run manually,
/// gated on the <c>process-designer</c> feature and a reachable environment carrying a CrtProcessBuilder that
/// supports the <c>approval</c> block.
/// <para>The record under approval is sourced from a PROCESS PARAMETER on purpose: a fixed record id would tie
/// the fixture to a specific row existing on whatever stand runs it, while the parameter route exercises the
/// same retyping and mapping path without that coupling.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ApprovalElementToolE2ETests {

	private const string ToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	// Contact ships in CrtBase, so it exists on every stand. It is used only as the approval OBJECT — the element
	// is never executed here, so whether Contact has approvals configured does not affect what this asserts.
	private const string ApprovalObjectName = "Contact";

	[Test]
	[Description("Over the real MCP path, create-business-process builds an approval element and describe-business-process reads it back: the element resolves to the dedicated approval build type, its approval block carries the resolved object NAME, the purpose default and the delegation flag, and the visa schema derived server-side.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds an approval element and describe reads the block back")]
	public async Task CreateBusinessProcess_Should_BuildApprovalElement_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpApprovalE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildApprovalDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an approval element with an object and a parameter-sourced record must build without a transport error");
		// The success LINE, not merely the name: the command logs "Building process '<name>'..." BEFORE it calls the
		// server, so a name match alone also passes when the build then fails — which would send a rejected
		// descriptor into the describe parser and surface as an unrelated error.
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line (run against an environment "
				+ "whose CrtProcessBuilder supports the approval element)");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement approval = graph.Elements.Single(element => element.Name == "Approval1");
		approval.BuildType.Should().Be("approval",
			because: "an Approval element round-trips to its dedicated build token, not the generic userTask — "
				+ "which only holds while its handler is registered before the generic one");
		approval.Approval.Should().NotBeNull(
			because: "describe surfaces an Approval element's configuration in its own approval block; a null here "
				+ "on a successful build is the silent-drop signature of a server that predates the element");
		approval.Approval!.Object.Should().Be(ApprovalObjectName,
			because: "the block reports the object as a resubmittable NAME, not only the stored schema UId");
		approval.Approval.ObjectUId.Should().NotBeNullOrWhiteSpace(
			because: "the stored UId is reported alongside the name for traceability");
		approval.Approval.Purpose.Should().Be("Discount over 20% requires approval",
			because: "the supplied purpose is stored as a plain constant and read back verbatim");
		approval.Approval.AllowDelegation.Should().BeTrue(
			because: "the delegation flag round-trips through build and describe");
		approval.Approval.RecordId.Should().NotBeNullOrWhiteSpace(
			because: "a parameter-sourced record is stored as a meta-path the server builds, and describe echoes it");
		approval.Approval.ApprovalSchemaUId.Should().NotBeNullOrWhiteSpace(
			because: "the visa schema is DERIVED from the approval object server-side — an empty one would mean the "
				+ "element saves, compiles and runs green while raising no visa");
	}

	[Test]
	[Description("The generic route stays supported: a userTask element naming ApprovalUserTask still accepts an approval block, because the server keys the element on its referenced schema rather than on the descriptor's type token.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process accepts an approval block on the generic userTask route")]
	public async Task CreateBusinessProcess_Should_AcceptApprovalBlock_OnGenericUserTaskRoute() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpApprovalGenericE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildGenericUserTaskApprovalDescriptor(processName)
		});

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "the userTask + ApprovalUserTask route is the one that already worked before the dedicated "
				+ "token existed, and it must keep working for a caller on an older habit");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement approval = graph.Elements.Single(element => element.Name == "Approval1");
		approval.Approval.Should().NotBeNull(
			because: "identity keys on the referenced task schema, so the block configures the element either way");
		approval.Approval!.Object.Should().Be(ApprovalObjectName);
	}

	[Test]
	[Description("An approval block carried by an element kind that cannot apply it is REFUSED by the server rather than silently dropped, so a caller mistake cannot ship as a process that reports success while configuring nothing.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a misplaced approval block")]
	public async Task CreateBusinessProcess_Should_RefuseApprovalBlock_OnAnotherElementKind() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpApprovalMisplacedE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildMisplacedApprovalDescriptor(processName)
		});

		// Assert
		string resultJson = JsonSerializer.Serialize(callResult);
		resultJson.Should().NotContain("created (UId:",
			because: "a misplaced approval block must abort the build, not save a process the caller did not ask for");
		resultJson.Should().Contain("approval",
			because: "the refusal has to name the block it is refusing, so the caller can find the mistake");
	}

	#region Methods: Private

	private static string BuildApprovalDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Approval E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "RecordToApprove", "type": "Guid", "direction": "In" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Approval1", "type": "approval", "caption": "Discount approval",
		      "approval": {
		        "object": "{{ApprovalObjectName}}",
		        "recordId": { "processParameter": "RecordToApprove" },
		        "purpose": "Discount over 20% requires approval",
		        "allowDelegation": true
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Approval1" },
		    { "source": "Approval1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildGenericUserTaskApprovalDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Approval Generic E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "RecordToApprove", "type": "Guid", "direction": "In" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Approval1", "type": "userTask", "userTaskName": "ApprovalUserTask",
		      "approval": {
		        "object": "{{ApprovalObjectName}}",
		        "recordId": { "processParameter": "RecordToApprove" }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Approval1" },
		    { "source": "Approval1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildMisplacedApprovalDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Approval Misplaced E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent",
		      "approval": { "object": "{{ApprovalObjectName}}" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "StartEvent1", "target": "EndEvent1" } ]
		}
		""";

	// Deserializes the described graph (the Info log-message value inside the clio command envelope) into the
	// typed model, the same way the create-business-process fixture does.
	private static DescribeProcessResult ParseDescribeGraph(CallToolResult describeResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value)
				&& value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		return JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
	}

	private static async Task<CallToolResult> DescribeAsync(ArrangeContext context, string processCode) =>
		await context.Session.CallToolAsync(
			DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processCode
				}
			},
			context.CancellationTokenSource.Token);

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context,
			Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the create-business-process tool must be discoverable before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore(
					"Configure McpE2E:Sandbox:EnvironmentName (with a CrtProcessBuilder that supports the approval "
					+ "element) to run the Approval MCP E2E tests.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
				Assert.Ignore(
					$"Approval MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
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
