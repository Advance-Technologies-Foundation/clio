using System;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RestartOperationRegistryTests {

	[Test]
	[Description("Begin creates a running readiness-wait record and makes it the tenant's latest; Finish with exit 0 marks it Ready.")]
	public void Begin_Then_Finish_Tracks_Ready_State() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartOperationRecord created = registry.Begin("tenant-a", "sandbox");
		created.Status.Should().Be(RestartOperationStatus.Running, because: "a freshly begun readiness wait is still polling");

		// Act
		RestartOperationRecord finished = registry.Finish(created.OperationId, 0);

		// Assert
		finished.Status.Should().Be(RestartOperationStatus.Ready, because: "exit 0 means the instance answered its health-check");
		registry.GetLatest("tenant-a")!.OperationId.Should().Be(created.OperationId,
			because: "the begun operation must be the tenant's latest");
		registry.GetById(created.OperationId)!.Status.Should().Be(RestartOperationStatus.Ready,
			because: "the finalized state must be visible on lookup by id");
	}

	[Test]
	[Description("Finish with a non-zero exit code marks the readiness wait TimedOut.")]
	public void Finish_Should_Mark_TimedOut_When_ExitCode_NonZero() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartOperationRecord created = registry.Begin("tenant-a", "sandbox");

		// Act
		RestartOperationRecord finished = registry.Finish(created.OperationId, 1);

		// Assert
		finished.Status.Should().Be(RestartOperationStatus.TimedOut,
			because: "a non-zero readiness result means the instance did not answer before the timeout");
	}

	[Test]
	[Description("Finding 5: a terminal readiness-wait record idle past the TTL is evicted on the next Begin sweep and its tenant pointer pruned, while a still-running wait is never evicted.")]
	public void Idle_Terminal_Records_Are_Evicted_But_Running_Ones_Are_Kept() {
		// Arrange
		DateTime now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		RestartOperationRegistry registry = new(TimeSpan.FromMinutes(5), maxEntries: 100, () => now);
		RestartOperationRecord running = registry.Begin("tenant-run", "envR");
		RestartOperationRecord finished = registry.Begin("tenant-fin", "envF");
		registry.Finish(finished.OperationId, 0);

		// Act
		now = now.AddMinutes(6);
		registry.Begin("tenant-trigger", "envT");

		// Assert
		registry.GetById(finished.OperationId).Should().BeNull(
			because: "a terminal record idle past the TTL must be evicted so the process stays memory-bounded");
		registry.GetLatest("tenant-fin").Should().BeNull(
			because: "evicting a record must prune its dangling latest-per-tenant pointer too");
		registry.GetById(running.OperationId).Should().NotBeNull(
			because: "a still-running readiness wait must never be evicted, or restart-status would report it as not found");
	}
}
