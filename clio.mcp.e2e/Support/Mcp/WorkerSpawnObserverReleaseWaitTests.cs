using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Pins <see cref="WorkerSpawnObserver.WaitUntilWorkersAreReleased"/>, the instrument TC-E-601b's cleanup
/// assertion depends on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this fixture exists at all.</b> The race it was written for does not reproduce on a developer
/// workstation, so the wait itself can only ever be exercised there on its happy path — it returns in about
/// a millisecond and proves nothing. Every property that makes it a GATE rather than a delay is, by
/// contrast, fully deterministic offline: point an observer at a directory this test owns and drive the
/// file by hand. Without this fixture the wait could silently stop failing on a leaked worker and the only
/// thing that would notice is the absence of a failure nobody was expecting.
/// </para>
/// <para>
/// <b>The two cases that are the point.</b> A registry that never drains must still FAIL — nothing retries
/// a swallowed unregister, so an entry that stays is a leak rather than a slow reap. And a read that could
/// not be performed must never be mistaken for a registry that holds no worker: the wait polls until it
/// sees an empty answer, so treating an unreadable file as empty would let it hunt for a torn read and
/// certify a leak as a clean release.
/// </para>
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
public sealed class WorkerSpawnObserverReleaseWaitTests {

	/// <summary>
	/// Long enough that a scheduling hiccup cannot expire it, since the tests that use it assert the wait
	/// finished EARLY. Never actually paid: those cases drain.
	/// </summary>
	private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The ceiling for the cases that must EXPIRE. Paid in full by every one of them, so it stays small.
	/// </summary>
	private static readonly TimeSpan ExpiringTimeout = TimeSpan.FromMilliseconds(400);

	/// <summary>How long a draining registry is held before the entry is removed.</summary>
	private static readonly TimeSpan DrainDelay = TimeSpan.FromMilliseconds(250);

	private string _clioHome = string.Empty;

	[SetUp]
	public void SetUp() {
		_clioHome = Path.Combine(Path.GetTempPath(), $"clio-observer-wait-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_clioHome);
	}

	[TearDown]
	public void TearDown() {
		try {
			if (Directory.Exists(_clioHome)) {
				Directory.Delete(_clioHome, recursive: true);
			}
		} catch (IOException) {
			// A temp directory that outlives one test run is not a test result.
		} catch (UnauthorizedAccessException) {
			// Same.
		}
	}

	[Test]
	[Description("An absent registry file is an authoritative observation of 'no worker recorded', so the wait returns at once rather than polling to its ceiling.")]
	public async Task WaitUntilWorkersAreReleased_Should_Return_Released_When_The_Registry_Never_Existed() {
		// Arrange
		await using WorkerSpawnObserver observer = WorkerSpawnObserver.Start(_clioHome);

		// Act
		WorkerReleaseObservation released = observer.WaitUntilWorkersAreReleased(ExpiringTimeout);

		// Assert
		released.RegistryRead.Should().BeTrue(
			because: "the host writes the file on the first registration and persists an empty array rather "
				+ "than deleting it, so absence is a real observation and not a failed read");
		released.StillRecorded.Should().BeEmpty(
			because: "a registry that was never created records no worker");
		released.StillRunning.Should().BeEmpty(
			because: "nothing was ever observed, so no identity can still be running");
		released.Waited.Should().BeLessThan(ExpiringTimeout,
			because: "treating absence as a failed read would poll to the ceiling for a registry that is "
				+ "already in the state being waited for");
	}

	[Test]
	[Description("A registry that drains while the wait is running is reported as released, and the wait ends when it drains rather than at its ceiling.")]
	public async Task WaitUntilWorkersAreReleased_Should_Return_Released_When_The_Registry_Drains_While_Waiting() {
		// Arrange
		WriteRegistry(RegistryWith(DeadWorker()));
		await using WorkerSpawnObserver observer = WorkerSpawnObserver.Start(_clioHome);
		using CancellationTokenSource drain = new();
		Task draining = Task.Run(async () => {
			await Task.Delay(DrainDelay, drain.Token);
			WriteRegistry("[]");
		}, drain.Token);

		// Act
		WorkerReleaseObservation released = observer.WaitUntilWorkersAreReleased(GenerousTimeout);
		await draining;

		// Assert
		released.RegistryRead.Should().BeTrue(
			because: "the registry was readable throughout, so the empty result is an observation");
		released.StillRecorded.Should().BeEmpty(
			because: "the entry was removed before the ceiling, which is the 'slow to release' case the wait exists to tolerate");
		released.Waited.Should().BeGreaterThan(TimeSpan.Zero,
			because: "the entry was present when the wait started, so it cannot have returned on its first read");
		released.Waited.Should().BeLessThan(GenerousTimeout,
			because: "a wait that ran to its ceiling on a registry that DID drain would report a leak that never happened");
	}

	[Test]
	[Description("A registry that never drains still fails: the wait expires and returns the surviving entry, because nothing retries a swallowed unregister.")]
	public async Task WaitUntilWorkersAreReleased_Should_Keep_Reporting_A_Registry_That_Never_Drains() {
		// Arrange
		WriteRegistry(RegistryWith(DeadWorker()));
		await using WorkerSpawnObserver observer = WorkerSpawnObserver.Start(_clioHome);

		// Act
		WorkerReleaseObservation released = observer.WaitUntilWorkersAreReleased(ExpiringTimeout);

		// Assert
		released.StillRecorded.Should().NotBeEmpty(
			because: "an entry that is still recorded at the ceiling is a LEAKED worker — UnregisterWorker "
				+ "swallows a gate failure and nothing retries it, so this never resolves itself and the "
				+ "assertion built on this result must fail");
		released.Waited.Should().BeGreaterThanOrEqualTo(ExpiringTimeout,
			because: "the wait may only give up after its ceiling, or it would report a leak for a registry it never gave a chance to drain");
		released.RegistryRead.Should().BeTrue(
			because: "the file was readable throughout — this case must be distinguishable from an unreadable registry");
	}

	[Test]
	[Description("A registry that cannot be parsed is never mistaken for one that holds no worker: the wait keeps polling and reports that it was never read.")]
	public async Task WaitUntilWorkersAreReleased_Should_Not_Report_A_Drain_When_The_Registry_Cannot_Be_Read() {
		// Arrange
		// Not JSON at all, so JsonDocument.Parse throws and ReadRegistry returns an EMPTY list with its
		// `read` flag false — the exact shape a drained registry has, and the reason the flag has to be
		// what ends the wait. A torn read during the host's own atomic replace produces this same shape.
		WriteRegistry("this is not json");
		await using WorkerSpawnObserver observer = WorkerSpawnObserver.Start(_clioHome);

		// Act
		WorkerReleaseObservation released = observer.WaitUntilWorkersAreReleased(ExpiringTimeout);

		// Assert
		released.RegistryRead.Should().BeFalse(
			because: "an unreadable registry is not a drained one, and the caller has to be able to tell them apart");
		released.Waited.Should().BeGreaterThanOrEqualTo(ExpiringTimeout,
			because: "ending the wait on an unparseable read would let the loop poll until it FOUND a torn "
				+ "read and then certify a leaked worker as a clean release");
	}

	[Test]
	[Description("A process that outlives its registry entry keeps the wait running: the registry is not the stronger condition, because the entry is removed on the kill request rather than on confirmed exit.")]
	public async Task WaitUntilWorkersAreReleased_Should_Keep_Waiting_When_A_Process_Outlives_Its_Registry_Entry() {
		// Arrange
		// THIS process is the stand-in for a worker that was unregistered but has not exited: it is
		// certainly alive, and its recorded identity certainly matches.
		using Process self = Process.GetCurrentProcess();
		(int ProcessId, long StartTimeUtcTicks) live = (self.Id, self.StartTime.ToUniversalTime().Ticks);
		WriteRegistry(RegistryWith(live));
		await using WorkerSpawnObserver observer = WorkerSpawnObserver.Start(_clioHome);
		await WaitUntilObservedAsync(observer, live.ProcessId);
		// The entry goes, the process stays — exactly what ReleaseLease does when Terminate() only REQUESTS
		// termination and UnregisterWorker runs before the child is confirmed gone.
		WriteRegistry("[]");

		// Act
		WorkerReleaseObservation released = observer.WaitUntilWorkersAreReleased(ExpiringTimeout);

		// Assert
		released.StillRecorded.Should().BeEmpty(
			because: "the registry half is satisfied — which is precisely why it cannot be the whole assertion");
		released.StillRunning.Select(worker => worker.ProcessId).Should().Contain(live.ProcessId,
			because: "a drained registry means the kill was ISSUED, never that the process is gone, so "
				+ "liveness is waited for on its own terms");
		released.Waited.Should().BeGreaterThanOrEqualTo(ExpiringTimeout,
			because: "a still-running worker must hold the wait open to its ceiling, or a leaked child that "
				+ "merely lost its registry entry would be reported as released");
	}

	// Bounded, because the observer accumulates identities on a background poll and this test has to act
	// AFTER it has seen one. Failing here would otherwise surface as an unexplained empty StillRunning.
	private static async Task WaitUntilObservedAsync(WorkerSpawnObserver observer, int processId) {
		Stopwatch elapsed = Stopwatch.StartNew();
		while (elapsed.Elapsed < GenerousTimeout) {
			if (observer.Observed.Any(worker => worker.ProcessId == processId)) {
				return;
			}
			await Task.Delay(TimeSpan.FromMilliseconds(25));
		}
		Assert.Fail($"The observer never recorded process {processId} within {GenerousTimeout.TotalSeconds:0}s, "
			+ "so the case it is supposed to set up was never reached.");
	}

	// A pid whose recorded start time is a decade old. Identity is pid AND start time, so IsStillRunning is
	// false even in the unlikely event that something now carries this identifier — which keeps every
	// registry-only case free of any dependence on what else is running on the machine.
	private static (int ProcessId, long StartTimeUtcTicks) DeadWorker() =>
		(999_999, DateTime.UtcNow.AddYears(-10).Ticks);

	private static string RegistryWith(params (int ProcessId, long StartTimeUtcTicks)[] workers) =>
		"[" + string.Join(",", workers.Select(worker =>
			$"{{\"ProcessId\":{worker.ProcessId},\"StartTimeUtcTicks\":{worker.StartTimeUtcTicks}}}")) + "]";

	// Written the way the host writes it — temp file plus atomic replace — so a concurrent read from the
	// observer's own background poll cannot take a sharing violation and make these tests flaky.
	private void WriteRegistry(string json) {
		string directory = Path.Combine(_clioHome, "mcp-workers");
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, "workers.json");
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, json);
		File.Move(temporary, path, overwrite: true);
	}
}
