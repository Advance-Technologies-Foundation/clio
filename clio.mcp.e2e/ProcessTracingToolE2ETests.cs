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
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for process tracing (ENG-102111) across the three tools that carry it:
/// <c>create-business-process</c> (<c>isTracing</c>), <c>modify-business-process</c> (<c>setTracing</c>) and
/// <c>describe-business-process</c> (<c>tracing</c>). NOT in CI — run manually against a reachable sandbox carrying
/// CrtProcessBuilder 1.6.6.92 or newer and a writable "Custom" package. The version path is covered by unit tests
/// only: a version can never be deleted, so an E2E that created one would leave it on the stand.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ModifyBusinessProcessTool.ModifyBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessTracingToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;
	private const string NewVersionToolName = ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName;
	private const string WrittenAgainst = "1.6.6.92";

	[Test]
	[Description("Over the real MCP path: a process created with isTracing:true is traced - the build warns about what a traced run stores and when the switch turns itself off, and describe reports the tracing block - and a modify carrying ONLY setTracing enabled:false switches it off, after which describe omits the block (ENG-102111). The switch is a platform user property, not schema metadata, so only a describe on a live stand proves it landed. Needs CrtProcessBuilder 1.6.6.92 on the stand.")]
	[AllureTag(ModifyToolName)]
	[AllureName("create-business-process isTracing switches tracing on and setTracing switches it off")]
	public async Task Tracing_Should_SwitchOnAtCreate_AndOffWithSetTracing() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
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
			DescribeProcessResult tracedGraph = ParseDescribeResult(await DescribeAsync(context, processName));

			// Act
			CallToolResult switchedOff = await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = "[ { \"op\": \"setTracing\", \"enabled\": false } ]"
			});
			DescribeProcessResult untracedGraph = ParseDescribeResult(await DescribeAsync(context, processName));

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
			await DeleteProcessAsync(context.EnvironmentName!, processName);
		}
	}

	[Test]
	[Description("Over the real MCP path: setTracing without 'enabled' is refused with the server's reason and nothing changes (ENG-102111). The process is created TRACED, so a refusal that wrongly switched it off - the misreading of a missing value as false this rule exists to prevent - would show as a missing block. Needs CrtProcessBuilder 1.6.6.92 on the stand.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses setTracing without enabled")]
	public async Task SetTracing_Should_BeRefused_WhenEnabledIsMissing() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
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
			DescribeProcessResult graph = ParseDescribeResult(await DescribeAsync(context, processName));

			// Assert
			SerializeToolText(refused).Should().Contain("'setTracing' requires 'enabled'",
				because: "the server's refusal names the missing argument");
			graph.Tracing.Should().NotBeNull(because: "a refused request changes nothing, and the process started traced");
		} finally {
			await DeleteProcessAsync(context.EnvironmentName!, processName);
		}
	}

	[Test]
	[Description("Over the real MCP path: modify-business-process-as-new-version refuses a batch made ONLY of setTracing BEFORE anything is cloned, so no version is created, and names modify-business-process (ENG-102111, owner decision): a version made only to flip a switch would be permanent. It also covers the version route without leaving an undeletable version on the stand. Needs CrtProcessBuilder 1.6.6.92 on the stand.")]
	[AllureTag(NewVersionToolName)]
	[AllureName("modify-business-process-as-new-version refuses a setTracing-only batch and creates no version")]
	public async Task SetTracing_Should_BeRefusedBeforeAVersionExists_OnTheVersionRoute() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
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
			DescribeProcessResult graph = ParseDescribeResult(await DescribeAsync(context, processName));

			// Assert
			SerializeToolText(refused).Should().Contain("Send it to modify-business-process instead",
				because: "the refusal names the route that switches tracing without creating a version");
			graph.Tracing.Should().BeNull(because: "a refused request switches nothing");
			(graph.Versions ?? new List<DescribedProcessVersion>()).Count(member => !member.IsRoot).Should().Be(0,
				because: "the refusal fires before the clone, so the family still holds only the root");
		} finally {
			await DeleteProcessAsync(context.EnvironmentName!, processName);
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

	private static async Task<CallToolResult> DescribeAsync(ArrangeContext context, string processName) =>
		await CallToolAsync(context, DescribeToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName
		});

	private static string SerializeToolText(CallToolResult callResult) =>
		string.Join("\n", callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));

	private static DescribeProcessResult ParseDescribeResult(CallToolResult callResult) {
		JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
		JsonElement content = JsonSerializer.SerializeToElement(callResult.Content);
		foreach (JsonElement block in content.EnumerateArray()) {
			if (!block.TryGetProperty("text", out JsonElement textElement)
					|| textElement.ValueKind != JsonValueKind.String) {
				continue;
			}
			string? envelopeJson = textElement.GetString();
			if (string.IsNullOrWhiteSpace(envelopeJson)
					|| !envelopeJson.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
				continue;
			}
			using JsonDocument envelope = JsonDocument.Parse(envelopeJson);
			if (!envelope.RootElement.TryGetProperty("execution-log-messages", out JsonElement messages)
					|| messages.ValueKind != JsonValueKind.Array) {
				continue;
			}
			foreach (JsonElement message in messages.EnumerateArray()) {
				if (!message.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.String) {
					continue;
				}
				string? graphJson = value.GetString();
				if (string.IsNullOrWhiteSpace(graphJson)
						|| !graphJson.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
					continue;
				}
				try {
					DescribeProcessResult? graph = JsonSerializer.Deserialize<DescribeProcessResult>(graphJson, options);
					if (graph is { SchemaUId: not null }) {
						return graph;
					}
				} catch (JsonException) {
					// Not the structured-graph log message; keep scanning.
				}
			}
		}
		throw new InvalidOperationException("The describe-business-process MCP result did not contain a structured graph.");
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: $"the {toolName} tool must be discoverable via the get-tool-contract compact index before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with CrtProcessBuilder 1.6.6.92 or newer) to run "
				+ "the process tracing MCP E2E.");
		}
		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore($"The process tracing MCP E2E requires a reachable configured sandbox environment. "
				+ $"'{environmentName}' was not reachable.");
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
}
