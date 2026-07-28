using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// FR-05 (ENG-93208): in-process concurrency proof that the per-tenant execution lock serializes the
/// SAME tenant while letting DIFFERENT tenants run concurrently. No live Creatio; coordination uses
/// events/barriers rather than timing sleeps (the bounded waits are correctness bounds, not pacing).
/// </summary>
[TestFixture]
[Category("Integration")]
[Property("Module", "McpServer")]
public sealed class TenantLockConcurrencyTests {

	private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

	[Test]
	[Description("Two calls for the SAME tenant key serialize: the second cannot enter while the first holds the lock.")]
	public void GetLock_ShouldSerializeSameTenant_WhenTwoCallsUseSameKey() {
		// Arrange
		ITenantExecutionLockProvider provider = TenantExecutionLockProvider.Shared;
		string key = "same-tenant-" + Guid.NewGuid();
		using ManualResetEventSlim firstAcquired = new(false);
		using ManualResetEventSlim firstMayRelease = new(false);
		using ManualResetEventSlim secondAcquired = new(false);

		Task first = Task.Run(() => {
			lock (provider.GetLock(key)) {
				firstAcquired.Set();
				firstMayRelease.Wait(Generous);
			}
		});
		firstAcquired.Wait(Generous).Should().BeTrue(
			because: "the first caller must acquire the tenant lock before the second contends for it");

		Task second = Task.Run(() => {
			lock (provider.GetLock(key)) {
				secondAcquired.Set();
			}
		});

		// Act — while the first still holds the SAME-key lock, the second must NOT be able to acquire it.
		bool secondEnteredWhileFirstHeld = secondAcquired.Wait(TimeSpan.FromMilliseconds(500));

		// Assert
		secondEnteredWhileFirstHeld.Should().BeFalse(
			because: "the SAME tenant key must serialize — the second call cannot enter while the first holds the lock");

		// Release the first and confirm the second then proceeds.
		firstMayRelease.Set();
		Task.WaitAll(first, second);
		secondAcquired.IsSet.Should().BeTrue(
			because: "once the first releases the same-tenant lock, the second acquires it");
	}

	[Test]
	[Description("Two calls for DIFFERENT tenant keys run concurrently: both are inside their locks at the same time.")]
	public void GetLock_ShouldRunConcurrently_WhenTwoCallsUseDifferentKeys() {
		// Arrange
		ITenantExecutionLockProvider provider = TenantExecutionLockProvider.Shared;
		string keyA = "tenant-a-" + Guid.NewGuid();
		string keyB = "tenant-b-" + Guid.NewGuid();
		using Barrier bothInside = new(2);
		bool aReachedBarrier = false;
		bool bReachedBarrier = false;

		// Act — each task enters its own tenant lock and then waits on the shared barrier. If the two
		// locks serialized, one task could never enter while the other is inside, so the barrier would
		// time out; both reaching it proves the locks are independent.
		Task a = Task.Run(() => {
			lock (provider.GetLock(keyA)) {
				aReachedBarrier = bothInside.SignalAndWait(Generous);
			}
		});
		Task b = Task.Run(() => {
			lock (provider.GetLock(keyB)) {
				bReachedBarrier = bothInside.SignalAndWait(Generous);
			}
		});
		Task.WaitAll(a, b);

		// Assert
		aReachedBarrier.Should().BeTrue(
			because: "tenant A holds its own lock while tenant B holds a different lock, so A is not blocked by B");
		bReachedBarrier.Should().BeTrue(
			because: "tenant B holds its own lock while tenant A holds a different lock, so B is not blocked by A");
	}
}
