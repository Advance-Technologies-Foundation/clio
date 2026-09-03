using System.Diagnostics;
using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-95262 Stage 2 (story 2): end-to-end containment coverage for the MCP worker process supervisor.
/// These tests spawn real operating-system processes and kill them, then assert on process EXISTENCE —
/// the only observation that distinguishes containment from a closed pipe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why existence and not exit codes.</b> ADR rule 6 is about what happens to a worker's DESCENDANTS,
/// and a descendant that survived reports nothing at all. The Stage-0 prototype leaked exactly one
/// orphan this way, so every assertion here is "this process id is gone", revalidated against the
/// recorded identity so a reused identifier cannot make a leak look like a pass.
/// </para>
/// <para>
/// <b>Platform split, stated rather than hidden.</b> TC-E-201 and TC-E-202 close R-8a on Unix.
/// TC-E-203 closes R-8b on Windows and is SKIPPED with an explicit reason elsewhere: one
/// cross-platform criterion satisfied by a Unix-only run would read as green on every platform, which
/// is the exact failure the threat model warns about.
/// </para>
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-worker-execution-boundary")]
[NonParallelizable]
public sealed class McpWorkerContainmentE2ETests {

	private const string SelfPromotingWorkerArgument = "--self-promote-and-spawn-descendant";
	private const string SpawnSelfPromotingWorkerArgument = "--spawn-self-promoting-worker";
	private static readonly TimeSpan IdentityWait = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan DisappearanceWait = TimeSpan.FromSeconds(20);

	private string _scratchDirectory = string.Empty;

	[SetUp]
	public void SetUp() {
		_scratchDirectory = Path.Combine(Path.GetTempPath(), $"clio-worker-containment-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_scratchDirectory);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_scratchDirectory)) {
			try {
				Directory.Delete(_scratchDirectory, recursive: true);
			} catch (IOException) {
				// A scratch directory left behind must not fail a containment assertion.
			}
		}
	}

	[Test]
	[Description("TC-E-201 (R-8a, Unix): force-killing the parent of a worker that has promoted itself to process-group leader and armed parent-death signalling removes BOTH the worker and the descendant it spawned — the case in which the Stage-0 prototype leaked one orphan.")]
	[AllureTag("mcp-worker")]
	[AllureName("Unix parent death takes the worker and its descendant")]
	public async Task ParentDeath_ShouldRemoveTheWorkerAndItsDescendant_OnUnix() {
		// Arrange
		if (OperatingSystem.IsWindows()) {
			Assert.Ignore(
				"R-8a is the Unix mechanism (process-group promotion plus parent-death signalling). The Windows guarantee is job-object kill-on-close and is asserted by TC-E-203.");
		}
		string workerIdentityPath = Path.Combine(_scratchDirectory, "worker.identity");
		string descendantIdentityPath = Path.Combine(_scratchDirectory, "descendant.identity");
		// A three-generation chain, because the process being force-killed must not be the test host:
		// the intermediate stands in for the parent clio, and the worker stands in for the real worker
		// mode, whose own half of containment (setpgid plus parent-death arming) ships with Stage 3 and is
		// re-proven there against the real binary.
		using Process intermediate = StartFixture(SpawnSelfPromotingWorkerArgument, workerIdentityPath,
			descendantIdentityPath);
		FixtureIdentity worker = await ReadIdentityAsync(workerIdentityPath);
		FixtureIdentity descendant = await ReadIdentityAsync(descendantIdentityPath);
		IsRunning(worker).Should().BeTrue(
			because: "the stand-in worker must be running before the parent is killed, or the test proves nothing");
		IsRunning(descendant).Should().BeTrue(
			because: "the worker's own descendant must be running before the parent is killed");

		// Act — SIGKILL, so the parent runs no cleanup whatsoever.
		intermediate.Kill(entireProcessTree: false);
		await intermediate.WaitForExitAsync(CancellationToken.None);
		bool workerGone = await WaitUntilGoneAsync(worker);
		bool descendantGone = await WaitUntilGoneAsync(descendant);

		// Assert
		workerGone.Should().BeTrue(
			because: "a worker whose parent died must not keep running: that is the orphan the prototype leaked");
		descendantGone.Should().BeTrue(
			because: "containment is about DESCENDANTS — the worker's group kill has to take the process it spawned, which is why parent death is signalled as SIGTERM plus a handler and never as SIGKILL");
	}

	[Test]
	[Description("TC-E-202: when the response budget expires the supervisor kills the worker together with the descendant it spawned, and answers with a bounded, explicit outcome rather than hanging.")]
	[AllureTag("mcp-worker")]
	[AllureName("Budget expiry kills the worker and its descendants")]
	public async Task BudgetExpiry_ShouldKillTheWorkerAndItsDescendants_AndReportABoundedOutcome() {
		// Arrange
		if (OperatingSystem.IsWindows()) {
			Assert.Ignore(
				"This assertion is written against Unix process-group containment; the Windows equivalent is TC-E-203.");
		}
		string workerIdentityPath = Path.Combine(_scratchDirectory, "worker.identity");
		string descendantIdentityPath = Path.Combine(_scratchDirectory, "descendant.identity");
		IWorkerProcessSupervisor supervisor = CreateSupervisor();
		WorkerSpawnRequest request = new() {
			Budget = TimeSpan.FromSeconds(2),
			LaunchOverride = new ClioWorkerLaunchDescriptor(ResolveFixtureExecutable(),
				[SelfPromotingWorkerArgument, workerIdentityPath, descendantIdentityPath],
				_scratchDirectory)
		};

		// Act
		using IWorkerLease lease = await supervisor.SpawnContainedAsync(request, CancellationToken.None);
		FixtureIdentity descendant = await ReadIdentityAsync(descendantIdentityPath);
		WorkerRunResult result = await supervisor.WaitWithinBudgetAsync(lease, CancellationToken.None);
		bool descendantGone = await WaitUntilGoneAsync(descendant);

		// Assert
		result.Status.Should().Be(WorkerRunStatus.BudgetExpired,
			because: "the worker outlives its budget on purpose, and the parent must classify that explicitly instead of waiting for a transport to give up");
		result.Termination.Should().Be(WorkerTerminationOutcome.ContainedGroupKilled,
			because: "a promoted worker must be killed through its own process group, which is what covers everything it spawned");
		result.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
			because: "the budget bounds the call by killing the child, needing no cooperation from whatever the child was waiting on");
		lease.HasExited.Should().BeTrue(
			because: "the worker itself must be gone once its budget expired");
		descendantGone.Should().BeTrue(
			because: "the descendant is the process a pipe close would have left running, so its disappearance is the containment claim");
	}

	[Test]
	[Description("TC-E-203 (R-8b, Windows): a worker created inside a kill-on-close job object takes the descendant it spawned as its first act with it when the job is terminated, which an assign-after-start implementation would leak.")]
	[AllureTag("mcp-worker")]
	[AllureName("Windows job object contains the worker subtree")]
	public async Task JobObjectContainment_ShouldRemoveTheWorkerSubtree_OnWindows() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore(
				"R-8b requires a Windows host: the job object, its kill-on-close limit and the CREATE_SUSPENDED assignment do not exist on this platform. This test closes R-8b only on the Windows agents of Team_Atf_ClioMcpE2eTests.");
		}
		string workerIdentityPath = Path.Combine(_scratchDirectory, "worker.identity");
		string descendantIdentityPath = Path.Combine(_scratchDirectory, "descendant.identity");
		IWorkerProcessSupervisor supervisor = CreateSupervisor();
		WorkerSpawnRequest request = new() {
			Budget = TimeSpan.FromMinutes(5),
			LaunchOverride = new ClioWorkerLaunchDescriptor(ResolveFixtureExecutable(),
				[SelfPromotingWorkerArgument, workerIdentityPath, descendantIdentityPath],
				_scratchDirectory)
		};

		// Act
		IWorkerLease lease = await supervisor.SpawnContainedAsync(request, CancellationToken.None);
		FixtureIdentity worker = await ReadIdentityAsync(workerIdentityPath);
		// The fixture spawns this descendant as its FIRST act, so it lands inside the window that an
		// "assign the job after Process.Start" implementation leaves open. ADR section 2.4 measured such a
		// grandchild SURVIVING the parent's force-kill; here it must die with the job.
		FixtureIdentity descendant = await ReadIdentityAsync(descendantIdentityPath);
		WorkerTerminationOutcome outcome = supervisor.KillContained(lease);
		bool workerGone = await WaitUntilGoneAsync(worker);
		bool descendantGone = await WaitUntilGoneAsync(descendant);
		lease.Dispose();

		// Assert
		outcome.Should().Be(WorkerTerminationOutcome.ContainedJobTerminated,
			because: "on Windows the kill must go through the job object: a console control event is signal routing an uncooperative child simply ignores");
		workerGone.Should().BeTrue(
			because: "the worker is a job member and must die with the job");
		descendantGone.Should().BeTrue(
			because: "job membership is inherited, so a descendant spawned before the assignment could only survive if the process had been created running rather than suspended");
	}

	private static IWorkerProcessSupervisor CreateSupervisor() {
		IFileSystem fileSystem = new System.IO.Abstractions.FileSystem();
		// A registry rooted in the scratch area, not in the developer's clio home: this test kills
		// processes, and it must never read a real host's worker records to decide what to kill.
		string registryRoot = Path.Combine(Path.GetTempPath(), $"clio-worker-registry-{Guid.NewGuid():N}");
		IStaleWorkerRegistry registry = new StaleWorkerRegistry(fileSystem,
			new InterprocessFileGate(fileSystem), registryRoot);
		IProcessContainment containment = OperatingSystem.IsWindows()
			? new WindowsJobObjectContainment()
			: new UnixProcessGroupContainment();
		return new WorkerProcessSupervisor(ConsoleLogger.Instance, new ProcessExecutor(ConsoleLogger.Instance),
			containment, new ClioExecutablePathProvider(fileSystem), registry, concurrencyCap: 2);
	}

	private static Process StartFixture(params string[] arguments) {
		ProcessStartInfo startInfo = new() {
			FileName = ResolveFixtureExecutable(),
			UseShellExecute = false,
			CreateNoWindow = true
		};
		foreach (string argument in arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		return Process.Start(startInfo)
			?? throw new InvalidOperationException("The process fixture did not start.");
	}

	private static string ResolveFixtureExecutable() {
		DirectoryInfo testDirectory = new(TestContext.CurrentContext.TestDirectory);
		string targetFramework = testDirectory.Name;
		string configuration = testDirectory.Parent?.Name
			?? throw new InvalidOperationException("The test configuration directory could not be resolved.");
		string repositoryRoot = Path.GetFullPath(Path.Combine(testDirectory.FullName, "..", "..", "..", ".."));
		string fixtureExecutable = Path.Combine(repositoryRoot, "clio.process.fixture", "bin", configuration,
			targetFramework, OperatingSystem.IsWindows() ? "git.exe" : "git");
		return File.Exists(fixtureExecutable)
			? fixtureExecutable
			: throw new FileNotFoundException("The process fixture was not built.", fixtureExecutable);
	}

	private static async Task<FixtureIdentity> ReadIdentityAsync(string path) {
		DateTime deadline = DateTime.UtcNow + IdentityWait;
		while (DateTime.UtcNow < deadline) {
			if (File.Exists(path)) {
				try {
					FixtureIdentity? identity =
						JsonSerializer.Deserialize<FixtureIdentity>(await File.ReadAllTextAsync(path));
					if (identity is not null) {
						return identity;
					}
				} catch (JsonException) {
					// The fixture is mid-write; try again.
				} catch (IOException) {
					// The fixture is mid-write; try again.
				}
			}
			await Task.Delay(50);
		}
		throw new TimeoutException($"The fixture did not publish its identity at '{path}'.");
	}

	// Existence is checked against the full identity, because a process id alone would let an unrelated
	// process that reused the number make a leaked orphan look like a clean kill.
	private static bool IsRunning(FixtureIdentity identity) {
		try {
			using Process process = Process.GetProcessById(identity.ProcessId);
			if (process.HasExited) {
				return false;
			}
			return process.StartTime.ToUniversalTime().Ticks == identity.StartUtcTicks;
		} catch (ArgumentException) {
			return false;
		} catch (InvalidOperationException) {
			return false;
		}
	}

	private static async Task<bool> WaitUntilGoneAsync(FixtureIdentity identity) {
		DateTime deadline = DateTime.UtcNow + DisappearanceWait;
		while (DateTime.UtcNow < deadline) {
			if (!IsRunning(identity)) {
				return true;
			}
			await Task.Delay(100);
		}
		return false;
	}

	private sealed record FixtureIdentity(int ProcessId, long StartUtcTicks, string ExecutablePath);
}
