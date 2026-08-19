using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262: the gate waits on TWO layers — the in-process monitor and the cross-process file handle —
/// and its documented bound must cover both together, not each separately.
/// </summary>
/// <remarks>
/// Each layer used to start its own stopwatch, so a caller that spent most of its budget waiting for the
/// monitor was then granted a fresh full timeout on the handle: a gate documented as bounded at 30 s
/// could block for nearly 60. The callers of this gate are page reads and writes inside an MCP response
/// budget, so an unexpected doubling is a call that returns nothing for twice as long as anyone planned.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
[NonParallelizable]
public class InterprocessFileGateDeadlineTests {

	// Deliberately generous. With the defect the wait is monitorHold + timeout ≈ 3.6 s; with the fix it is
	// ≈ timeout = 2 s. The assertion sits at 2.8 s, leaving ~800 ms of slack on BOTH sides, so neither a
	// loaded agent nor a fast one decides the outcome. A tighter bound would be a better measurement and a
	// worse test — this repository has a documented history of CI flakes from exactly that trade.
	private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan MonitorHold = TimeSpan.FromMilliseconds(1600);
	private static readonly TimeSpan AcceptableCeiling = TimeSpan.FromMilliseconds(2800);

	[Test]
	[Description("A caller that spends most of its budget on the in-process monitor must NOT then be granted a fresh full timeout on the file handle: one deadline spans both layers.")]
	public void Enter_ShouldNotRestartTheClock_WhenTheMonitorWaitConsumedMostOfTheBudget() {
		// Arrange — a real temp directory, a gate with a short explicit bound, and the cross-process half
		// held for the whole test by an exclusive handle this test never releases until the end.
		string root = Path.Combine(Path.GetTempPath(), $"clio-gate-deadline-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		string lockFilePath = Path.Combine(root, "guarded.lock");
		IFileSystem fileSystem = new FileSystem();
		InterprocessFileGate gate = new(fileSystem, GateTimeout);
		Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
		using FileStream foreignHolder = new(
			lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

		using ManualResetEventSlim monitorTaken = new(false);
		Task hold = Task.Run(() => {
			// Occupies the SAME in-process monitor the gate uses, for most of the budget.
			gate.Enter(lockFilePath, () => 0);
		});
		// The holder cannot actually enter (the foreign handle blocks it), which is what keeps the monitor
		// busy for its own full timeout — exactly the arrangement a second caller runs into.
		Thread.Sleep(200);

		// Act
		Stopwatch elapsed = Stopwatch.StartNew();
		try {
			gate.Enter(lockFilePath, () => 0);
		} catch (TimeoutException) {
			// Expected: neither layer can be acquired.
		}
		elapsed.Stop();

		// Assert
		elapsed.Elapsed.Should().BeLessThan(AcceptableCeiling,
			because: $"the gate is bounded at {GateTimeout.TotalSeconds}s across BOTH layers; restarting the clock for the file handle would let a caller block for nearly twice its configured maximum");

		// Cleanup
		try { hold.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
		try { Directory.Delete(root, recursive: true); } catch (IOException) { }
	}
}
