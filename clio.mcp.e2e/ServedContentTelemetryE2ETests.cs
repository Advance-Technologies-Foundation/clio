using System.Text;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Knowledge;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-100157: the real stdio server counts the guidance and contracts it served and stamps the totals on
/// the telemetry events the same process records.
/// </summary>
/// <remarks>
/// Keep exactly ONE test in this fixture. The counters are cumulative for the server process the fixture
/// owns, and the "nothing served yet" assertion holds only while no other test has used that process.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("send-telemetry")]
[NonParallelizable]
public sealed class ServedContentTelemetryE2ETests : McpContractFixtureBase {
	private const string TelemetryHomeEnvironmentVariable = "CLIO_TELEMETRY_HOME";
	private const string TelemetryEnabledEnvironmentVariable = "CLIO_TELEMETRY_ENABLED";
	private const string SyntheticSourceAlias = "synthetic";

	private static readonly string[] ServedContentKeys = [
		"guidance_reads", "guidance_rereads", "guidance_bytes", "contract_reads", "contract_bytes",
		"guidance_library_version"
	];

	private readonly SyntheticKnowledgeNuGetFixture _fixture;
	private string _telemetryHome = null!;

	public ServedContentTelemetryE2ETests() {
		_fixture = SyntheticKnowledgeNuGetFixture.Create();
		_fixture.PublishValid("1.0.0", sequence: 10, revision: "served-content");
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_fixture.Dispose();
	}

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("{}", "served-content-home");
		// A fixture-owned telemetry home, so the test never reads or writes the developer's real consent.
		_telemetryHome = CreateFixtureDirectory("served-content-telemetry");
		settings.ProcessEnvironmentVariables[TelemetryHomeEnvironmentVariable] = _telemetryHome;
		// Local storage only: a granted-consent event must never be uploaded to the real collector.
		settings.ProcessEnvironmentVariables[TelemetryEnabledEnvironmentVariable] = "false";
	}

	[Test]
	[AllureTag(SendTelemetryTool.ToolName)]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("send-telemetry stamps what the same session served")]
	[AllureDescription("Starts the real MCP server over an isolated home with a verified synthetic knowledge library, reads one article three times and one contract, then records stage events and verifies the stored event carries the exact served totals - and that an event recorded before anything was served carries none.")]
	[Description("Stamps the guidance and contract totals the same stdio process served on the stage it records, at the size the agent received, and stamps nothing before anything was served.")]
	public async Task SendTelemetry_ShouldStampServedContent_WhenTheSameProcessServedIt() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		string beforeSessionId = Guid.NewGuid().ToString();
		string afterSessionId = Guid.NewGuid().ToString();
		await AllureApi.Step("Install the verified synthetic knowledge library", async () => {
			await AssertCommandSucceeded(context, KnowledgeManagementTools.AddKnowledgeSourceToolName,
				new Dictionary<string, object?> {
					["alias"] = SyntheticSourceAlias,
					["libraryId"] = SyntheticKnowledgeNuGetFixture.LibraryId,
					["type"] = "nuget",
					["location"] = _fixture.Feed.ServiceIndexUri.AbsoluteUri,
					["packageId"] = SyntheticKnowledgeNuGetFixture.PackageId,
					["trustedKeyId"] = _fixture.KeyId,
					["trustedPublicKeyPath"] = _fixture.PublicKeyPath,
					["enabled"] = true,
					["priority"] = 100,
					["participation"] = "authoritative",
					["confirmed"] = true
				});
			await AssertCommandSucceeded(context, KnowledgeManagementTools.InstallKnowledgeToolName,
				new Dictionary<string, object?> { ["source"] = SyntheticSourceAlias });
		});

		// Act
		CallToolResult beforeResult = await AllureApi.Step("Record a stage before anything is served",
			() => SendStage(context, beforeSessionId, consent: "granted"));
		List<CallToolResult> reads = await AllureApi.Step("Read the same article three times", async () => {
			List<CallToolResult> results = [];
			for (int read = 0; read < 3; read++) {
				results.Add(await CallSelectedGuide(context));
			}
			return results;
		});
		CallToolResult contract = await AllureApi.Step("Read the send-telemetry contract once",
			() => context.Session.CallToolAsync(
				ToolContractGetTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["tool-names"] = new[] { SendTelemetryTool.ToolName }
					}
				},
				context.CancellationTokenSource.Token));
		CallToolResult afterResult = await AllureApi.Step("Record a stage after serving",
			() => SendStage(context, afterSessionId, consent: null));

		// Assert
		AllureApi.Step("Every call succeeded", () => {
			beforeResult.IsError.Should().NotBeTrue(because: "the consent-granting stage must be recorded");
			reads.Should().OnlyContain(read => read.IsError != true,
				because: "the verified synthetic article must be served every time");
			contract.IsError.Should().NotBeTrue(because: "the send-telemetry contract must be served");
			afterResult.IsError.Should().NotBeTrue(because: "the second stage must be recorded");
		});
		AllureApi.Step("The stage recorded before anything was served carries no served totals", () => {
			ReadEventAttributes(beforeSessionId).Keys.Should().NotContain(ServedContentKeys,
				because: "a process that served nothing stamps nothing - zeros would read as a session that cost nothing");
		});
		Dictionary<string, JsonElement> after = ReadEventAttributes(afterSessionId);
		AllureApi.Step("guidance_reads counts every served article", () => {
			IntValue(after, "guidance_reads").Should().Be(3,
				because: "the article was served three times");
		});
		AllureApi.Step("guidance_rereads counts the reads of an article the session already had", () => {
			IntValue(after, "guidance_rereads").Should().Be(2,
				because: "the second and third reads served an article this session had already received");
		});
		AllureApi.Step("guidance_bytes equals the guidance text the agent received", () => {
			IntValue(after, "guidance_bytes").Should().Be(reads.Sum(TextBytes),
				because: "the count is the size of the result text the agent received over the wire");
		});
		AllureApi.Step("contract_reads counts the one contract response", () => {
			IntValue(after, "contract_reads").Should().Be(1,
				because: "one contract response was served");
		});
		AllureApi.Step("contract_bytes equals the contract text the agent received", () => {
			IntValue(after, "contract_bytes").Should().Be(TextBytes(contract),
				because: "the count is the size of the contract text the agent received over the wire");
		});
		AllureApi.Step("A third-party library version is not reported", () => {
			after.Keys.Should().NotContain("guidance_library_version",
				because: "the synthetic library is not clio's own, and a third-party library's version is its owner's data");
		});
		AllureApi.Step("The stamped event is schema 3", () => {
			after.Should().ContainKey("schema_version", because: "every event carries its payload shape");
			after["schema_version"].GetProperty("string_value").GetString().Should().Be("3",
				because: "the served totals are the schema 3 payload shape");
		});
	}

	private static async Task AssertCommandSucceeded(
		ArrangeContext context,
		string command,
		Dictionary<string, object?> args) {
		CallToolResult result = await context.Session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = command,
				["args"] = args
			},
			context.CancellationTokenSource.Token);
		result.IsError.Should().NotBeTrue(because: $"{command} must succeed to arrange an active library");
		Text(result).Should().Contain("\"success\":true", because: $"{command} must report success");
	}

	private static async Task<CallToolResult> SendStage(ArrangeContext context, string sessionId, string? consent) {
		Dictionary<string, object?> args = new() {
			["session_id"] = sessionId,
			["event_name"] = "workflow_started",
			["workflow"] = "app-creation"
		};
		if (consent is not null) {
			args["telemetry_consent"] = consent;
		}
		// send-telemetry is long-tail: CallToolAsync routes it through clio-run, the path an agent uses.
		return await context.Session.CallToolAsync(
			SendTelemetryTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
	}

	private static async Task<CallToolResult> CallSelectedGuide(ArrangeContext context) =>
		await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["name"] = SyntheticKnowledgeNuGetFixture.SelectedGuideName
				}
			},
			context.CancellationTokenSource.Token);

	private Dictionary<string, JsonElement> ReadEventAttributes(string sessionId) {
		string eventsDirectory = Path.Combine(_telemetryHome, "events");
		string eventFile = Directory.GetFiles(eventsDirectory, "*.json")
			.Should().ContainSingle(path => File.ReadAllText(path).Contains(sessionId, StringComparison.Ordinal),
				because: "each stage is stored as exactly one event file").Subject;
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(eventFile));
		return document.RootElement.GetProperty("attributes")
			.EnumerateArray()
			.ToDictionary(attribute => attribute.GetProperty("key").GetString()!,
				attribute => attribute.GetProperty("value").Clone());
	}

	private static long IntValue(Dictionary<string, JsonElement> attributes, string key) {
		attributes.Should().ContainKey(key, because: $"'{key}' is stamped once the process has served content");
		return attributes[key].GetProperty("int_value").GetInt64();
	}

	private static string Text(CallToolResult result) =>
		string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

	private static long TextBytes(CallToolResult result) => Encoding.UTF8.GetByteCount(Text(result));
}
