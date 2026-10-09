using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2EArrange;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for process tracing (ENG-102111) across the three tools that carry it:
/// <c>create-business-process</c> (<c>isTracing</c>), <c>modify-business-process</c> (<c>setTracing</c>) and
/// <c>describe-business-process</c> (<c>tracing</c>). NOT in CI — run manually against a reachable sandbox carrying
/// CrtProcessBuilder 1.6.6.92 or newer and a writable "Custom" package. On the version path only the REFUSAL is
/// exercised end to end - it fires before any clone, so nothing is created; a version can never be deleted, so the
/// accepted mixed batch stays with the unit tests.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ModifyBusinessProcessTool.ModifyBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessTracingToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string NewVersionToolName = ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName;
	private const string WrittenAgainst = "1.6.6.92";

	[Test]
	[Description("Over the real MCP path: a process created with isTracing:true is traced - the build warns about what a traced run stores and when the switch turns itself off, and describe reports the tracing block - and a modify carrying ONLY setTracing enabled:false switches it off, after which describe omits the block (ENG-102111). The switch is a platform user property, not schema metadata, so only a describe on a live stand proves it landed. Needs CrtProcessBuilder 1.6.6.92 on the stand.")]
	[AllureTag(ModifyToolName)]
	[AllureName("create-business-process isTracing switches tracing on and setTracing switches it off")]
	public async Task Tracing_Should_SwitchOnAtCreate_AndOffWithSetTracing() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync("process tracing", WrittenAgainst);
		string processName = $"UsrClioBpTracingE2e{Guid.NewGuid():N}";
		CallToolResult created = await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildTracedDescriptor(processName)
		});
		IgnoreWhenProcessBuilderIsBehind(SerializeToolText(created), WrittenAgainst);
		string createdText = SerializeToolText(created);
		createdText.Should().Contain("created (UId:",
			because: $"the arrange must actually create '{processName}', or the test measures nothing");
		try {
			DescribeProcessResult tracedGraph = ParseDescribedProcess(await DescribeAsync(context, processName));

			// Act
			CallToolResult switchedOff = await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = "[ { \"op\": \"setTracing\", \"enabled\": false } ]"
			});
			DescribeProcessResult untracedGraph = ParseDescribedProcess(await DescribeAsync(context, processName));

			// Assert
			createdText.Should().Contain("Tracing is now ON",
				because: "the build must warn about what a traced run stores, which a plain success never says");
			createdText.Should().Contain("ProcessParameterTracingDisableTimeoutDays",
				because: "the warning names the setting that switches tracing off by itself");
			tracedGraph.Tracing.Should().NotBeNull(because: "a process built with isTracing:true is traced");
			tracedGraph.Tracing!.Enabled.Should().BeTrue(because: "the block is reported only while tracing is on");
			if (tracedGraph.Tracing.TurnOffDate != null) {
				tracedGraph.Tracing.TurnOffDate.Should().MatchRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$",
					because: "the last traced day travels as an invariant yyyy-MM-dd date");
			}
			SerializeToolText(switchedOff).Should().Contain("\"exit-code\":0",
				because: "a setTracing-only batch is a complete, valid edit");
			untracedGraph.Tracing.Should().BeNull(
				because: "after setTracing enabled:false the server omits the block, which means runs are not traced");
		} finally {
			await DeleteProcessAsync(context.EnvironmentName, processName);
		}
	}

	[Test]
	[Description("Over the real MCP path: setTracing without 'enabled' is refused with the server's reason and nothing changes (ENG-102111). The process is created TRACED, so a refusal that wrongly switched it off - the misreading of a missing value as false this rule exists to prevent - would show as a missing block. Needs CrtProcessBuilder 1.6.6.92 on the stand.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses setTracing without enabled")]
	public async Task SetTracing_Should_BeRefused_WhenEnabledIsMissing() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync("process tracing", WrittenAgainst);
		string processName = $"UsrClioBpTracingRefuseE2e{Guid.NewGuid():N}";
		CallToolResult created = await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildTracedDescriptor(processName)
		});
		IgnoreWhenProcessBuilderIsBehind(SerializeToolText(created), WrittenAgainst);
		SerializeToolText(created).Should().Contain("created (UId:",
			because: $"the arrange must actually create '{processName}', or the test measures nothing");
		try {
			// Act
			CallToolResult refused = await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = "[ { \"op\": \"setTracing\" } ]"
			});
			DescribeProcessResult graph = ParseDescribedProcess(await DescribeAsync(context, processName));

			// Assert
			SerializeToolText(refused).Should().Contain("'setTracing' requires 'enabled'",
				because: "the server's refusal names the missing argument");
			graph.Tracing.Should().NotBeNull(because: "a refused request changes nothing, and the process started traced");
		} finally {
			await DeleteProcessAsync(context.EnvironmentName, processName);
		}
	}

	[Test]
	[Description("Over the real MCP path: modify-business-process-as-new-version refuses a batch made ONLY of setTracing BEFORE anything is cloned, so no version is created, and names modify-business-process (ENG-102111, owner decision): a version made only to flip a switch would be permanent. It also covers the version route without leaving an undeletable version on the stand. Needs CrtProcessBuilder 1.6.6.92 on the stand.")]
	[AllureTag(NewVersionToolName)]
	[AllureName("modify-business-process-as-new-version refuses a setTracing-only batch and creates no version")]
	public async Task SetTracing_Should_BeRefusedBeforeAVersionExists_OnTheVersionRoute() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync("process tracing", WrittenAgainst);
		string processName = $"UsrClioBpTracingVersionE2e{Guid.NewGuid():N}";
		CallToolResult created = await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildTracedDescriptor(processName, isTracing: false)
		});
		IgnoreWhenProcessBuilderIsBehind(SerializeToolText(created), WrittenAgainst);
		SerializeToolText(created).Should().Contain("created (UId:",
			because: $"the arrange must actually create '{processName}', or the test measures nothing");
		try {
			// Act
			CallToolResult refused = await CallToolAsync(context, NewVersionToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = "[ { \"op\": \"setTracing\", \"enabled\": true } ]"
			});
			DescribeProcessResult graph = ParseDescribedProcess(await DescribeAsync(context, processName));

			// Assert
			SerializeToolText(refused).Should().Contain("Send it to modify-business-process instead",
				because: "the refusal names the route that switches tracing without creating a version");
			graph.Tracing.Should().BeNull(because: "a refused request switches nothing");
			(graph.Versions ?? new List<DescribedProcessVersion>()).Count(member => !member.IsRoot).Should().Be(0,
				because: "the refusal fires before the clone, so the family still holds only the root");
		} finally {
			await DeleteProcessAsync(context.EnvironmentName, processName);
		}
	}

	private static string BuildTracedDescriptor(string processName, bool isTracing = true) =>
		JsonSerializer.Serialize(new Dictionary<string, object?> {
			["name"] = processName,
			["caption"] = "clio tracing e2e",
			["packageName"] = "Custom",
			["isTracing"] = isTracing,
			["elements"] = new[] {
				new Dictionary<string, object?> { ["name"] = "Start1", ["type"] = "startEvent", ["caption"] = "Start" },
				new Dictionary<string, object?> { ["name"] = "End1", ["type"] = "endEvent", ["caption"] = "End" }
			},
			["flows"] = new[] { new Dictionary<string, object?> { ["source"] = "Start1", ["target"] = "End1" } }
		});

	/// <summary>Deserialises the graph through the shared reader, which locates it by content.</summary>
	private static DescribeProcessResult ParseDescribedProcess(CallToolResult describeResult) =>
		JsonSerializer.Deserialize<DescribeProcessResult>(
			DescribedProcessGraph.Read(describeResult).ToJsonString(),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

	private static string SerializeToolText(CallToolResult callResult) =>
		string.Join(Environment.NewLine, callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
