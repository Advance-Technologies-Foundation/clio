using System;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Ipc;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

[TestFixture]
public sealed class ClioIpcHealthProbeTests {
	[TestCase("2025-11-25", 1, 0)]
	[TestCase("2026-07-28", 0, 1)]
	[Description("Selects legacy ping before MCP 2026-07-28 and side-effect-free tools/list discovery for MCP 2026-07-28 and later.")]
	public async Task ExecuteVersionAwareHealthProbeAsync_ShouldSelectSupportedOperation(
		string protocolVersion, int expectedPingCalls, int expectedDiscoveryCalls) {
		// Arrange
		int pingCalls = 0;
		int discoveryCalls = 0;
		Task Ping(CancellationToken _) {
			pingCalls++;
			return Task.CompletedTask;
		}
		Task Discover(CancellationToken _) {
			discoveryCalls++;
			return Task.CompletedTask;
		}

		// Act
		await ClioIpcClient.ExecuteVersionAwareHealthProbeAsync(
			protocolVersion, Ping, Discover, () => { }, CancellationToken.None);

		// Assert
		pingCalls.Should().Be(expectedPingCalls,
			because: "legacy ping must be selected only for protocol versions that still define it");
		discoveryCalls.Should().Be(expectedDiscoveryCalls,
			because: "tools/list must replace ping only for discovery-first protocol versions");
	}

	[TestCase("2025-11-25")]
	[TestCase("2026-07-28")]
	[Description("A failed selected health operation invokes the same disconnect callback used by PingAsync and propagates the original diagnostic.")]
	public async Task ExecuteVersionAwareHealthProbeAsync_ShouldPropagateSelectedFailure(
		string protocolVersion) {
		// Arrange
		InvalidOperationException expected = new("health probe failed");
		int disconnectCalls = 0;
		Task Fail(CancellationToken _) => Task.FromException(expected);
		Func<Task> act = () => ClioIpcClient.ExecuteVersionAwareHealthProbeAsync(
			protocolVersion, Fail, Fail, () => disconnectCalls++, CancellationToken.None);

		// Act
		InvalidOperationException exception = (await act.Should().ThrowAsync<InvalidOperationException>(
			because: "the version-aware health wrapper must preserve the selected probe failure")).Which;

		// Assert
		disconnectCalls.Should().Be(1,
			because: "PingAsync wires this callback to MarkDisconnected so a failed probe clears the handshake and raises its connection events exactly once");
		exception.Should().BeSameAs(expected,
			because: "health-probe failures must retain their original diagnostic for the Ring UI and proof harness");
	}
}
