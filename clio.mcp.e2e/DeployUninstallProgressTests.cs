using System.Text.Json;
using System.Text.Json.Nodes;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Proves that typed <see cref="ClioStageEvent"/> progress notifications arrive MID-CALL over the real
/// <c>clio mcp-server</c> for the deploy THROWN-STAGE path, carried in the <c>notifications/progress</c>
/// <c>_meta.clioStageEvent</c> envelope (story 4, FR-08/FR-15).
/// </summary>
/// <remarks>
/// This fixture is deliberately non-destructive:
/// deploy-creatio is invoked with an existing but corrupt archive so the run fails at the <c>unzip</c> stage and
/// creates nothing. Uninstall is invoked for a fixture-only environment whose URI does not match an IIS site,
/// proving the target-resolution failure contract without deleting an application, database, or files.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("deploy-creatio")]
[Parallelizable(ParallelScope.Self)]
public sealed class DeployUninstallProgressTests : McpContractFixtureBase {
	private const string ToolName = InstallerCommandTool.DeployCreatioToolName;
	private const string UninstallToolName = "uninstall-creatio";
	private const string MissingEnvironmentName = "mcp-e2e-missing-uninstall-target";

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string iisRoot = CreateFixtureDirectory("deploy-progress-iis-root");
		JsonObject appSettings = new() {
			["Autoupdate"] = false,
			["iis-clio-root-path"] = iisRoot,
			["Environments"] = new JsonObject {
				[MissingEnvironmentName] = new JsonObject {
					["Uri"] = "http://127.0.0.1:65534",
					["Login"] = "fixture-user",
					["Password"] = "fixture-password",
					["IsNetCore"] = true
				}
			}
		};
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome(
			appSettings.ToJsonString(),
			"deploy-progress-clio-home");
	}

	[Test]
	[Description("Invokes deploy-creatio with a corrupt archive over the real MCP server and verifies typed manifest→unzip-failed→run-completed(failure) events arrive mid-call in notifications/progress _meta.clioStageEvent, with no secret material on the wire and nothing deployed.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy creatio streams typed stage events via progress _meta on the thrown-stage path")]
	[AllureDescription("Registers a raw notifications/progress handler on the real clio MCP server, calls deploy-creatio with an invalid zip path, and asserts the mid-call _meta.clioStageEvent stream is the versioned ClioStageEvent contract (SchemaVersion=1) with the manifest, a failed unzip stage, and a terminal failure run-completed — non-destructively.")]
	public async Task DeployCreatio_Should_Stream_Typed_Stage_Events_Via_Progress_Meta_When_Archive_Is_Invalid() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		arrangeContext.Session.StartCapturingProgressNotifications();
		string corruptZipFile = Path.Combine(Path.GetTempPath(), $"corrupt-creatio-{Guid.NewGuid():N}.zip");
		await File.WriteAllTextAsync(corruptZipFile, "not a zip archive", arrangeContext.CancellationTokenSource.Token);

		// Act — use the same explicit-token + raw-handler path as ClioRing. The SDK's typed progress
		// overload installs a competing handler and drops _meta during deserialization.
		try {
			await arrangeContext.Session.CallToolWithRawProgressAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["siteName"] = $"e2e-{Guid.NewGuid():N}",
						["zipFile"] = corruptZipFile,
						["sitePort"] = 5011,
						// Cross the FakeKubernetes/no-defaults preflight on clean CI agents. The corrupt
						// archive fails at unzip before this deliberately nonexistent server is resolved.
						["dbServerName"] = "e2e-unused-before-unzip"
					}
				},
				arrangeContext.CancellationTokenSource.Token);
		}
		finally {
			File.Delete(corruptZipFile);
		}

		// Assert
		IReadOnlyList<JsonNode> rawParams = await arrangeContext.Session.WaitForCapturedProgressAsync(
			nodes => ExtractStageEvents(nodes).LastOrDefault()?.EventType
				== ClioStageEventContract.EventTypes.RunCompleted,
			TimeSpan.FromSeconds(30),
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ClioStageEvent> events = ExtractStageEvents(rawParams);

		events.Should().NotBeEmpty(
			because: "the deploy thrown-stage path must stream typed stage events over notifications/progress _meta");
		events.Should().OnlyContain(stageEvent => stageEvent.SchemaVersion == ClioStageEventContract.SchemaVersion,
			because: "every _meta envelope must be the versioned ClioStageEvent contract (SchemaVersion=1)");

		ClioStageEvent manifest = events[0];
		manifest.EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest,
			because: "the first typed event must be the up-front manifest (AC-09 sequence)");
		manifest.Stages.Should().Contain(entry => entry.StageId == StageIds.Unzip,
			because: "the deploy manifest must enumerate the unzip stage");

		events.Should().Contain(
			stageEvent => stageEvent.Stage != null
				&& stageEvent.Stage.StageId == StageIds.Unzip
				&& stageEvent.Stage.Status == ClioStageEventContract.StageStatuses.Failed,
			because: "a corrupt archive must surface as a failed unzip stage");

		ClioStageEvent terminal = events[^1];
		terminal.EventType.Should().Be(ClioStageEventContract.EventTypes.RunCompleted,
			because: "the last typed event must be the terminal run-completed (AC-09 sequence)");
		terminal.RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			because: "the invalid-archive run must terminate in failure — nothing is deployed");

		string wire = string.Join('\n', rawParams.Select(node => node.ToJsonString())).ToLowerInvariant();
		wire.Should().NotContainAny(["password=", "pwd=", "user id=", "bearer "],
			because: "no connection string, credential, or token may cross the wire (AC-ERR; redaction is at source)");
	}

	[Test]
	[Description("Invokes uninstall-creatio for a fixture-only environment with no matching IIS site and verifies the MCP result fails with a typed terminal failure event without deleting anything.")]
	[AllureTag(UninstallToolName)]
	[AllureName("Uninstall creatio rejects an unresolved IIS target")]
	[AllureDescription("Calls uninstall-creatio over the real MCP server for an isolated environment whose URI cannot correlate to an IIS site, then verifies exit code 1 and manifest-to-terminal-failure progress without touching a real instance.")]
	public async Task UninstallCreatio_Should_Fail_Without_Deleting_When_Iis_Target_Cannot_Be_Resolved() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		arrangeContext.Session.StartCapturingProgressNotifications();

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolWithRawProgressAsync(
			UninstallToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = MissingEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		execution.ExitCode.Should().Be(1,
			because: "an uninstall whose environment URI cannot resolve to an IIS site must fail instead of reporting a false success");
		execution.Output.Should().Contain(message =>
			message.MessageType == LogDecoratorType.Error &&
			message.Value != null &&
			message.Value.Contains("Could not correlate", StringComparison.OrdinalIgnoreCase),
			because: "the caller must receive an actionable target-resolution error");

		IReadOnlyList<JsonNode> rawParams = await arrangeContext.Session.WaitForCapturedProgressAsync(
			nodes => ExtractStageEvents(nodes).LastOrDefault()?.EventType
				== ClioStageEventContract.EventTypes.RunCompleted,
			TimeSpan.FromSeconds(30),
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ClioStageEvent> events = ExtractStageEvents(rawParams);
		events[0].EventType.Should().Be(ClioStageEventContract.EventTypes.Manifest,
			because: "target validation failures must still begin with the uninstall manifest");
		events[^1].RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.Failure,
			because: "the unresolved target must terminate the typed progress stream as a failure");
		events[^1].RunCompleted!.ErrorCode.Should().Be("uninstall-target-not-found",
			because: "Ring and other MCP consumers need a stable machine-readable failure classification");
	}

	// Reads the typed ClioStageEvent out of each captured progress params node, skipping any
	// notifications/progress that do not carry a _meta.clioStageEvent (e.g. plain heartbeat beats).
	private static IReadOnlyList<ClioStageEvent> ExtractStageEvents(IReadOnlyList<JsonNode> rawParams) {
		List<ClioStageEvent> events = [];
		foreach (JsonNode node in rawParams) {
			JsonNode? envelope = node["_meta"]?["clioStageEvent"];
			if (envelope is null) {
				continue;
			}

			ClioStageEvent? stageEvent = envelope.Deserialize<ClioStageEvent>(ClioStageEventContract.SerializerOptions);
			if (stageEvent is not null) {
				events.Add(stageEvent);
			}
		}

		return events;
	}

}
