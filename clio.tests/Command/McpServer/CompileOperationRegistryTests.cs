using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CompileOperationRegistryTests {

	[Test]
	[Description("Begin creates a running record with no exit code or finish timestamp yet, and makes it the latest for its tenant.")]
	public void Begin_Should_Create_Running_Record_And_Make_It_Latest() {
		// Arrange
		CompileOperationRegistry registry = new();

		// Act
		CompileOperationRecord created = registry.Begin("tenant-a", "sandbox", null);

		// Assert
		created.Status.Should().Be(CompileOperationStatus.Running, because: "a freshly begun operation has not finished yet");
		created.ExitCode.Should().BeNull(because: "a running operation has no exit code");
		created.FinishedUtc.Should().BeNull(because: "a running operation has not finished");
		registry.GetLatest("tenant-a").Should().BeEquivalentTo(created,
			because: "the newly begun operation must immediately be visible as the tenant's latest");
	}

	[Test]
	[Description("Finish with exit code 0 marks the tracked operation Succeeded and records the finish timestamp and exit code.")]
	public void Finish_Should_Mark_Succeeded_When_ExitCode_Zero() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileOperationRecord created = registry.Begin("tenant-a", "sandbox", null);

		// Act
		CompileOperationRecord finished = registry.Finish(created.OperationId, 0, [new InfoMessage("done")]);

		// Assert
		finished.Status.Should().Be(CompileOperationStatus.Succeeded, because: "exit code 0 means the compile succeeded");
		finished.ExitCode.Should().Be(0, because: "the finalized record must carry the reported exit code");
		finished.FinishedUtc.Should().NotBeNull(because: "a finished operation must carry a finish timestamp");
		registry.GetById(created.OperationId)!.Status.Should().Be(CompileOperationStatus.Succeeded,
			because: "the update must be visible on subsequent lookups by id");
	}

	[Test]
	[Description("Finish with a non-zero exit code marks the tracked operation Failed.")]
	public void Finish_Should_Mark_Failed_When_ExitCode_NonZero() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileOperationRecord created = registry.Begin("tenant-a", "sandbox", "MyPackage");

		// Act
		CompileOperationRecord finished = registry.Finish(created.OperationId, 1, [new ErrorMessage("boom")]);

		// Assert
		finished.Status.Should().Be(CompileOperationStatus.Failed, because: "a non-zero exit code means the compile failed");
		finished.ExitCode.Should().Be(1, because: "the finalized record must carry the reported exit code");
		finished.PackageName.Should().Be("MyPackage",
			because: "finishing an operation must not lose the package name recorded at Begin");
	}

	[Test]
	[Description("GetLatest returns null for a tenant that has never started a compile operation.")]
	public void GetLatest_Should_ReturnNull_ForUnknownTenant() {
		// Arrange
		CompileOperationRegistry registry = new();

		// Act
		CompileOperationRecord result = registry.GetLatest("never-seen-tenant");

		// Assert
		result.Should().BeNull(because: "no operation was ever tracked for this tenant");
	}

	[Test]
	[Description("GetById returns null for an operation id that was never issued by Begin.")]
	public void GetById_Should_ReturnNull_ForUnknownId() {
		// Arrange
		CompileOperationRegistry registry = new();

		// Act
		CompileOperationRecord result = registry.GetById("not-a-real-id");

		// Assert
		result.Should().BeNull(because: "no operation was ever tracked under this id");
	}

	[Test]
	[Description("A second Begin for the same tenant replaces the tenant's latest pointer, without discarding the first operation's own record.")]
	public void Begin_Should_UpdateLatest_WhenCalledAgainForSameTenant() {
		// Arrange
		CompileOperationRegistry registry = new();

		// Act
		CompileOperationRecord first = registry.Begin("tenant-a", "sandbox", null);
		CompileOperationRecord second = registry.Begin("tenant-a", "sandbox", "PkgB");

		// Assert
		registry.GetLatest("tenant-a")!.OperationId.Should().Be(second.OperationId,
			because: "the most recently begun operation must become the tenant's latest");
		registry.GetById(first.OperationId).Should().NotBeNull(
			because: "an earlier operation must remain individually queryable by id even after a newer one starts");
	}

	[Test]
	[Description("Different tenants never see each other's latest operation.")]
	public void GetLatest_Should_NotCrossTenants() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileOperationRecord forA = registry.Begin("tenant-a", "sandbox-a", null);
		CompileOperationRecord forB = registry.Begin("tenant-b", "sandbox-b", null);

		// Act
		CompileOperationRecord latestA = registry.GetLatest("tenant-a");
		CompileOperationRecord latestB = registry.GetLatest("tenant-b");

		// Assert
		latestA!.OperationId.Should().Be(forA.OperationId, because: "tenant-a's latest must be its own operation");
		latestB!.OperationId.Should().Be(forB.OperationId, because: "tenant-b's latest must be its own operation");
	}

	[Test]
	[Description("Finish caps the retained message tail to the last MessageTailCap lines.")]
	public void Finish_Should_CapMessageTail_ToConfiguredLimit() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileOperationRecord created = registry.Begin("tenant-a", "sandbox", null);
		List<LogMessage> manyMessages = Enumerable.Range(1, CompileOperationRegistry.MessageTailCap + 10)
			.Select(i => (LogMessage)new InfoMessage($"line-{i}"))
			.ToList();

		// Act
		CompileOperationRecord finished = registry.Finish(created.OperationId, 0, manyMessages);

		// Assert
		finished.MessageTail.Should().HaveCount(CompileOperationRegistry.MessageTailCap,
			because: "the tail must be capped so a large compile log does not grow the registry unbounded");
		finished.MessageTail.Last().Should().Be($"line-{CompileOperationRegistry.MessageTailCap + 10}",
			because: "the cap must keep the MOST RECENT lines, not the earliest ones");
	}

	[Test]
	[Description("A detached over-deadline compile finalizing (Finish) while compile-status readers poll concurrently transitions the record from Running to Succeeded without throwing or losing the tenant's latest pointer.")]
	public void Finish_Should_Transition_Running_To_Succeeded_Under_Concurrent_Reads() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileOperationRecord begun = registry.Begin("tenant-a", "sandbox", null);
		begun.Status.Should().Be(CompileOperationStatus.Running,
			because: "the operation is running until the detached compile finishes");

		// Act — run the detached Finish alongside a burst of concurrent status reads (compile-status polling),
		// mirroring the over-deadline path where Finish lands on a background thread while an agent polls.
		Task finisher = Task.Run(() => registry.Finish(begun.OperationId, 0, Array.Empty<LogMessage>()));
		Parallel.For(0, 128, _ => {
			registry.GetLatest("tenant-a");
			registry.GetById(begun.OperationId);
		});
		finisher.Wait();

		// Assert
		registry.GetById(begun.OperationId)!.Status.Should().Be(CompileOperationStatus.Succeeded,
			because: "the concurrent finalize must be observed once it completes, with no lost update");
		registry.GetLatest("tenant-a")!.OperationId.Should().Be(begun.OperationId,
			because: "concurrent reads and the finalize must not corrupt the tenant's latest-operation pointer");
	}
}
