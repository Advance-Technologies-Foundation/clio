using System.Text.Json;
using System.Text.Json.Nodes;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
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
/// This fixture is NOT in CI — run manually against a live stand. It is deliberately non-destructive:
/// deploy-creatio is invoked with an existing but corrupt archive so the run fails at the <c>unzip</c> stage and
/// creates nothing. The result-based failure mode and the uninstall tool are unreachable without a real
/// destructive operation, so they are covered at the unit level (StageEventProgressForwarderTests) and
/// intentionally NOT attempted here.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("deploy-creatio")]
[Parallelizable(ParallelScope.Self)]
public sealed class DeployUninstallProgressTests : McpContractFixtureBase {
	private const string ToolName = InstallerCommandTool.DeployCreatioToolName;

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
						["sitePort"] = 5011
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
			TimeSpan.FromSeconds(5),
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
