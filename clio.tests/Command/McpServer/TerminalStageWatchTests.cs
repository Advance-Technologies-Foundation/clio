using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Relay;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262: <see cref="TerminalStageWatch.RestartSilenceClock"/> moves the start of the first silence
/// window to the tools/call request, and only while the run has not emitted anything yet.
/// </summary>
/// <remarks>
/// Every assertion compares the watch with a local <see cref="Stopwatch"/> started on the right side of the
/// call under test, so the outcome follows from the order of the calls rather than from how loaded the host
/// is.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class TerminalStageWatchTests {

	/// <summary>How long each test lets the clock run before restarting it.</summary>
	private static readonly TimeSpan ElapsedBeforeRestart = TimeSpan.FromMilliseconds(200);

	[Test]
	[Category("Unit")]
	[Description("Before the run has said anything, restarting the silence clock starts the first window again, so the handshake that preceded tools/call is not charged to it.")]
	public async Task RestartSilenceClock_ShouldRestartTheFirstWindow_WhenNoStageEventHasArrived() {
		// Arrange
		TerminalStageWatch sut = new(syntheticProgressToken: null);
		await Task.Delay(ElapsedBeforeRestart);
		Stopwatch sinceJustBeforeRestart = Stopwatch.StartNew();

		// Act
		sut.RestartSilenceClock();
		TimeSpan sinceLastStageEvent = sut.SinceLastStageEvent;

		// Assert
		sinceLastStageEvent.Should().BeLessThanOrEqualTo(sinceJustBeforeRestart.Elapsed,
			because: "the clock was restarted after the local stopwatch started, so it cannot have run longer than that stopwatch");
	}

	[Test]
	[Category("Unit")]
	[Description("Once the run has emitted a stage event, restarting the silence clock is ignored: only the run's own events may move it, so a later restart can never hide a run that has fallen silent.")]
	public async Task RestartSilenceClock_ShouldKeepTheClock_WhenAStageEventHasArrived() {
		// Arrange
		TerminalStageWatch sut = new(syntheticProgressToken: null);
		sut.Observe(ManifestNotification());
		Stopwatch sinceJustAfterTheEvent = Stopwatch.StartNew();
		await Task.Delay(ElapsedBeforeRestart);

		// Act
		sut.RestartSilenceClock();
		TimeSpan elapsedSinceTheEvent = sinceJustAfterTheEvent.Elapsed;
		TimeSpan sinceLastStageEvent = sut.SinceLastStageEvent;

		// Assert
		sinceLastStageEvent.Should().BeGreaterThanOrEqualTo(elapsedSinceTheEvent,
			because: "the clock must still count from the stage event, which came before the local stopwatch started");
		sut.Snapshot().StageEventCount.Should().Be(1,
			because: "the test is only meaningful if the watch actually recorded the stage event");
	}

	private static JsonRpcNotification ManifestNotification() {
		ClioStageEvent manifest = new(
			ClioStageEventContract.SchemaVersion,
			ClioStageEventContract.EventTypes.Manifest,
			Guid.NewGuid(),
			0,
			ClioStageEventContract.Operations.Deploy,
			Stages: [new ClioStageManifestEntry("unpack", "Unpack", 0, 1, false)]);
		return new JsonRpcNotification {
			Method = NotificationMethods.ProgressNotification,
			Params = new JsonObject {
				["progressToken"] = "caller-token",
				["progress"] = 0,
				["_meta"] = new JsonObject {
					["clioStageEvent"] = JsonSerializer.SerializeToNode(manifest, ClioStageEventContract.SerializerOptions)
				}
			}
		};
	}
}
