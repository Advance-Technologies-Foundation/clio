using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-103024: trigger-smoke creates a record, reports the signal-started run and what it created, and cleans up -
/// against the configured sandbox stand.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(TriggerSmokeTool.ToolName)]
[NonParallelizable]
public sealed class TriggerSmokeToolE2ETests : McpContractFixtureBase {

	private const string ToolName = TriggerSmokeTool.ToolName;

	// A process this fixture builds once per stand: on Contact added with a Name starting 'clio-smoke', add one
	// Activity. The filter keeps it from firing on any other contact of the shared sandbox.
	private const string SignalProcessCode = "UsrClioSmokeSignal";
	private const string ActivityTitle = "clio smoke activity";

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("trigger-smoke is discoverable through the get-tool-contract index and advertised as destructive.")]
	[AllureTag(ToolName)]
	[AllureName("trigger-smoke is discoverable and destructive")]
	public async Task TriggerSmoke_Should_Be_Discoverable_And_Destructive() {
		// Arrange
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrange.Session.GetToolContractIndexAsync(arrange.CancellationTokenSource.Token);
		ToolContractIndexEntry? entry = index.FirstOrDefault(item => item.Name == ToolName);

		// Assert
		entry.Should().NotBeNull(because: "a long-tail tool must be in the index to be found at all");
		entry!.Destructive.Should().BeTrue(because: "it creates and deletes records");
	}

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("A Contact whose name the signal process matches starts one run; the answer names it as completed, lists the Activity it added, and cleanup deletes the Activity and the Contact.")]
	[AllureTag(ToolName)]
	[AllureName("trigger-smoke reports the signal-started run and cleans up")]
	public async Task TriggerSmoke_Should_Report_The_Signal_Run_And_Clean_Up() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run trigger-smoke against the sandbox stand.");
		}
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			"trigger-smoke MCP E2E requires the configured sandbox environment to be set and reachable.");
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));
		await EnsureSignalProcessAsync(arrange, environmentName);

		// Act
		JsonElement response = await RunAsync(arrange, environmentName, $"clio-smoke-{Guid.NewGuid():N}",
			process: SignalProcessCode);
		// The activity is removed by the test whatever an assertion below says, so the shared stand is left clean.
		string? activityId = FindActivityId(response);
		try {
			// Assert
			response.GetProperty("success").GetBoolean().Should().BeTrue(because: response.ToString());
			JsonElement run = response.GetProperty("processes").EnumerateArray()
				.Should().ContainSingle(because: "only the run of the named process counts; any other run of the window "
					+ "goes to unrelated").Which;
			run.GetProperty("status").GetString().Should().Be("completed",
				because: "the signal process adds one activity and ends");
			response.GetProperty("created").EnumerateArray()
				.Should().Contain(item => item.GetProperty("title").GetString() == ActivityTitle,
					because: "the activity the signal run added must be reported");
			response.GetProperty("cleanup").GetProperty("failed").GetArrayLength().Should().Be(0,
				because: "the trigger contact is the caller's and deletable");
			response.GetProperty("cleanup").GetProperty("deleted").EnumerateArray()
				.Should().Contain(item => item.GetProperty("entity").GetString() == "Contact",
					because: "the trigger itself is cleaned up too");
			// This process's activity has no lookup to the contact, so nothing ties it to the trigger: time alone never
			// licenses a delete, and cleanup leaves it in cleanup.skipped (the tool description says so).
			response.GetProperty("cleanup").GetProperty("skipped").EnumerateArray()
				.Should().Contain(item => item.GetProperty("title").GetString() == ActivityTitle,
					because: "a record not tied to the trigger is reported in cleanup.skipped, not deleted");
		}
		finally {
			if (activityId is not null) {
				await DeleteAsync(arrange, environmentName, activityId);
			}
		}
	}

	// The activity the run added, from cleanup.skipped or, failing that, from created.
	private static string? FindActivityId(JsonElement response) {
		foreach (JsonElement list in new[] {
				response.TryGetProperty("cleanup", out JsonElement cleanup) && cleanup.TryGetProperty("skipped", out JsonElement skipped)
					? skipped : default,
				response.TryGetProperty("created", out JsonElement created) ? created : default
			}) {
			if (list.ValueKind != JsonValueKind.Array) {
				continue;
			}
			foreach (JsonElement item in list.EnumerateArray()) {
				if (item.GetProperty("title").GetString() == ActivityTitle) {
					return item.GetProperty("id").GetString();
				}
			}
		}
		return null;
	}

	private static async Task DeleteAsync(ArrangeContext arrange, string environmentName, string activityId) =>
		await arrange.Session.CallToolAsync(ODataDeleteTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName, ["entity"] = "Activity", ["id"] = activityId, ["confirm"] = true
				}
			},
			arrange.CancellationTokenSource.Token);

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("A record the signal filter does not match starts no run of the named process: the check fails with an error that says so, processes is empty, and the trigger is still cleaned up.")]
	[AllureTag(ToolName)]
	[AllureName("trigger-smoke explains a record that starts nothing")]
	public async Task TriggerSmoke_Should_Explain_A_Record_That_Starts_Nothing() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run trigger-smoke against the sandbox stand.");
		}
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			"trigger-smoke MCP E2E requires the configured sandbox environment to be set and reachable.");
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));
		// The process must exist, or a quiet answer would prove nothing.
		await EnsureSignalProcessAsync(arrange, environmentName);

		// Act
		JsonElement response = await RunAsync(arrange, environmentName, $"clio-e2e-quiet-{Guid.NewGuid():N}", timeout: 4,
			process: SignalProcessCode);

		// Assert
		response.GetProperty("success").GetBoolean().Should().BeFalse(because: "the process under test never ran");
		response.GetProperty("error").GetString().Should().Contain("No run of this call started",
			because: "an empty result is explained, not reported as a pass");
		response.TryGetProperty("processes", out JsonElement processes).Should().BeTrue(because: "processes is always answered");
		processes.GetArrayLength().Should().Be(0,
			because: "the signal filter does not match this name, and runs of other processes go to unrelated");
		response.GetProperty("cleanup").GetProperty("deleted").EnumerateArray()
			.Should().Contain(item => item.GetProperty("entity").GetString() == "Contact",
				because: "the trigger is cleaned up even when nothing ran");
	}

	private static async Task<JsonElement> RunAsync(ArrangeContext arrange, string environmentName, string name,
		int timeout = 15, string? process = null) {
		CallToolResult result = await arrange.Session.CallToolAsync(ClioRunDestructiveTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = ToolName,
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["entity"] = "Contact",
					["values"] = new Dictionary<string, object?> { ["Name"] = name },
					["watch"] = new[] { "Activity" },
					["timeout"] = timeout,
					["process"] = process
				}
			},
			arrange.CancellationTokenSource.Token);
		string text = result.Content.OfType<TextContentBlock>().First().Text;
		return JsonDocument.Parse(text).RootElement.Clone();
	}

	private static async Task EnsureSignalProcessAsync(ArrangeContext arrange, string environmentName) {
		string descriptor = JsonSerializer.Serialize(new Dictionary<string, object?> {
			["name"] = SignalProcessCode,
			["caption"] = "clio smoke signal (ENG-103024)",
			["packageName"] = "Custom",
			["elements"] = new object[] {
				new Dictionary<string, object?> {
					["name"] = "Start1", ["type"] = "signalStart", ["caption"] = "Contact added",
					["signal"] = new Dictionary<string, object?> { ["entity"] = "Contact", ["on"] = "added" },
					["filter"] = new Dictionary<string, object?> {
						["logicalOperation"] = "and",
						["conditions"] = new object[] {
							new Dictionary<string, object?> { ["column"] = "Name", ["comparison"] = "startWith", ["value"] = "clio-smoke" }
						}
					}
				},
				new Dictionary<string, object?> {
					["name"] = "AddActivity", ["type"] = "addData", ["caption"] = "Add activity",
					["addData"] = new Dictionary<string, object?> {
						["source"] = "Activity",
						["values"] = new object[] { new Dictionary<string, object?> { ["column"] = "Title", ["value"] = ActivityTitle } }
					}
				},
				new Dictionary<string, object?> { ["name"] = "End1", ["type"] = "endEvent", ["caption"] = "End" }
			},
			["flows"] = new object[] {
				new Dictionary<string, object?> { ["source"] = "Start1", ["target"] = "AddActivity" },
				new Dictionary<string, object?> { ["source"] = "AddActivity", ["target"] = "End1" }
			}
		});
		CallToolResult result = await arrange.Session.CallToolAsync(ClioRunDestructiveTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = "create-business-process",
				["args"] = new Dictionary<string, object?> { ["environment-name"] = environmentName, ["descriptor"] = descriptor }
			},
			arrange.CancellationTokenSource.Token);
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		if (text.Contains("created (UId:") || text.Contains("already exists", StringComparison.OrdinalIgnoreCase)) {
			return;
		}
		Assert.Ignore($"The signal process could not be prepared on '{environmentName}' (CrtProcessBuilder missing or "
			+ $"outdated?): {text}");
	}
}
