using System;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// H1/H2 (ENG-93208): the process working directory is process-global state. Once different tenants
/// run concurrently (per-tenant lock), a workspace tool that PINS cwd could place another tenant's
/// output under the pinned workspace unless every cwd reader AND writer serializes on the single
/// <see cref="McpToolExecutionLock.CwdLock"/>. These tests assert the anchor-resolution readers
/// (get-page's <see cref="PageFileWriter"/>, sync-pages/update-page's <see cref="PageBaselineGuard"/>)
/// acquire that shared lock.
/// <para>
/// <b>Fail-first note.</b> A deterministic test that OBSERVES cross-placement cannot survive the fix:
/// the fix is mutual exclusion, so any test that forces the pin/read to overlap would deadlock once
/// the lock exists. Per the task's permitted fallback, these instead assert that the real reader paths
/// acquire the SAME <see cref="McpToolExecutionLock.CwdLock"/> object — a reader BLOCKS while the test
/// holds CwdLock and completes once it is released. Run against the pre-fix tree these FAIL (the reader
/// did not take CwdLock, so it completed immediately and never blocked); the recorded pre-fix output is
/// in the change report.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
[NonParallelizable]
public sealed class CwdConcurrencyIsolationTests {

	private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan BlockedProbe = TimeSpan.FromMilliseconds(300);

	[Test]
	[Description("PageBaselineGuard.TryArm (the sync-pages/update-page anchor reader) blocks while CwdLock is held and completes once released — proving it serializes on CwdLock.")]
	public void TryArm_ShouldSerializeOnCwdLock_WhenCwdLockIsHeld() {
		// Arrange
		IPageBaselineGuard guard = new PageBaselineGuard(new MockFileSystem());
		PageUpdateOptions options = new() { SchemaName = "UsrConcurrencyProbe" };

		bool cwdLockHeld = false;
		Monitor.Enter(McpToolExecutionLock.CwdLock, ref cwdLockHeld);
		try {
			// Act — while the test holds CwdLock, the reader must not be able to resolve its anchor.
			Task<(string MetaFilePath, bool Armed)> reader = Task.Run(() => guard.TryArm(options, null));
			bool completedWhileHeld = reader.Wait(BlockedProbe);

			// Assert
			completedWhileHeld.Should().BeFalse(
				because: "the anchor reader must acquire CwdLock, so it cannot proceed while the test holds it");

			// Release CwdLock and confirm the reader then completes.
			Monitor.Exit(McpToolExecutionLock.CwdLock);
			cwdLockHeld = false;
			reader.Wait(Generous).Should().BeTrue(
				because: "once CwdLock is released the anchor reader acquires it and resolves");
		}
		finally {
			if (cwdLockHeld) {
				Monitor.Exit(McpToolExecutionLock.CwdLock);
			}
		}
	}

	[Test]
	[Description("PageFileWriter.WritePageFiles (the get-page anchor reader) blocks while CwdLock is held and completes once released — proving it serializes on the SAME CwdLock.")]
	public void WritePageFiles_ShouldSerializeOnCwdLock_WhenCwdLockIsHeld() {
		// Arrange
		IPageFileWriter writer = new PageFileWriter(new MockFileSystem());
		PageGetResponse response = new() { Success = true, Raw = new PageRawInfo { Body = "content" } };

		bool cwdLockHeld = false;
		Monitor.Enter(McpToolExecutionLock.CwdLock, ref cwdLockHeld);
		try {
			// Act — while the test holds CwdLock, the writer must block at its anchor resolution.
			Task<PageGetResponse> reader = Task.Run(() =>
				writer.WritePageFiles(response, "UsrConcurrencyProbe", "env", null, null));
			bool completedWhileHeld = reader.Wait(BlockedProbe);

			// Assert
			completedWhileHeld.Should().BeFalse(
				because: "the get-page anchor writer must acquire the SAME CwdLock, so it cannot proceed while the test holds it");

			// Release CwdLock and confirm it then completes.
			Monitor.Exit(McpToolExecutionLock.CwdLock);
			cwdLockHeld = false;
			reader.Wait(Generous).Should().BeTrue(
				because: "once CwdLock is released the get-page anchor writer acquires it and resolves");
		}
		finally {
			if (cwdLockHeld) {
				Monitor.Exit(McpToolExecutionLock.CwdLock);
			}
		}
	}
}
