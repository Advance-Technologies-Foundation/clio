using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clio;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262 H-1: unit coverage for the interprocess file gate.
/// <para>
/// These tests run against the REAL file system over a temporary directory, not
/// <c>MockFileSystem</c>, on purpose: the whole primitive is the operating system's share-mode
/// enforcement on an open handle, and an in-memory file system does not implement it. A green test on
/// a mock would prove only that the code calls <c>File.Open</c>, which is not the property that keeps
/// two clio processes from corrupting one another's page baseline.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
[NonParallelizable]
public sealed class InterprocessFileGateTests {

	private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(400);
	private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan BlockedProbe = TimeSpan.FromMilliseconds(300);

	private string _root;
	private IFileSystem _fileSystem;

	[SetUp]
	public void SetUp() {
		_fileSystem = new System.IO.Abstractions.FileSystem();
		_root = Path.Combine(Path.GetTempPath(), $"clio-gate-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void TearDown() {
		try {
			if (Directory.Exists(_root)) {
				Directory.Delete(_root, recursive: true);
			}
		} catch (IOException) {
			// A leftover temp directory must never fail a test run.
		}
	}

	// Every test uses its OWN lock path: the gate's monitor table is static and process-wide by design,
	// so a shared path would make tests contend with each other rather than with their own arrangement.
	private string NewLockPath() => Path.Combine(_root, ".locks", $"{Guid.NewGuid():N}.lock");

	private InterprocessFileGate CreateGate(TimeSpan? timeout = null) =>
		new(_fileSystem, timeout ?? Generous);

	[Test]
	[Description("The container must resolve IInterprocessFileGate as a single shared instance — this is the one failure the rest of the suite cannot see, because every page collaborator accepts a null gate and silently degrades to an ungated write if the registration disappears.")]
	public void Container_ShouldResolveOneSharedFileGate_WhenTheHostIsComposed() {
		// Arrange
		IServiceProvider provider = new BindingsModule().Register(new EnvironmentSettings {
			Uri = "http://localhost/di-probe",
			Login = "Supervisor",
			Password = "Supervisor"
		});

		// Act
		IInterprocessFileGate first = provider.GetRequiredService<IInterprocessFileGate>();
		IInterprocessFileGate second = provider.GetRequiredService<IInterprocessFileGate>();

		// Assert
		first.Should().BeOfType<InterprocessFileGate>(
			because: "the page collaborators take the gate as an OPTIONAL constructor parameter that degrades to an ungated write when null, so a missing registration would silently reopen the race this story closes");
		second.Should().BeSameAs(first,
			because: "the explicit singleton registration must win over the assembly scan's transient, so the host has one gate and its lifetime says what it means");
	}

	[Test]
	[Description("Enter must create the sentinel's parent directory, so a caller never has to pre-create a .locks directory that only the gate knows about.")]
	public void Enter_ShouldCreateTheSentinelDirectory_WhenItDoesNotExist() {
		// Arrange
		string lockPath = NewLockPath();
		Directory.Exists(Path.GetDirectoryName(lockPath)!).Should().BeFalse(
			because: "the arrangement must start without the .locks directory for this test to mean anything");

		// Act
		CreateGate().Enter(lockPath, () => { });

		// Assert
		File.Exists(lockPath).Should().BeTrue(
			because: "the gate must materialise its own sentinel rather than requiring callers to prepare the lock tree");
	}

	[Test]
	[Description("Enter must serialise two concurrent callers: the second cannot begin until the first has finished, so a read-modify-write cannot interleave.")]
	public void Enter_ShouldSerialiseCallers_WhenTwoThreadsContendOnOneLock() {
		// Arrange
		string lockPath = NewLockPath();
		InterprocessFileGate gate = CreateGate();
		int concurrent = 0;
		int observedOverlap = 0;
		using ManualResetEventSlim firstInside = new(false);

		void Body() =>
			gate.Enter(lockPath, () => {
				int current = Interlocked.Increment(ref concurrent);
				if (current > 1) {
					Interlocked.Exchange(ref observedOverlap, 1);
				}
				firstInside.Set();
				Thread.Sleep(120);
				Interlocked.Decrement(ref concurrent);
			});

		// Act
		Task first = Task.Run(Body);
		firstInside.Wait(Generous).Should().BeTrue(because: "the first caller must reach the guarded region");
		Task second = Task.Run(Body);
		Task.WaitAll([first, second], Generous).Should().BeTrue(
			because: "both callers must eventually complete; neither may be dropped");

		// Assert
		observedOverlap.Should().Be(0,
			because: "two callers inside the guarded region at once is exactly the read-modify-write interleaving the gate exists to prevent");
	}

	[Test]
	[Description("Enter must throw TimeoutException — not a raw IOException — when ANOTHER PROCESS's exclusive handle on the sentinel outlives the timeout.")]
	public void Enter_ShouldThrowTimeout_WhenTheSentinelHandleIsHeldElsewhere() {
		// Arrange — an independent FileShare.None handle stands in for a second clio process. This is the
		// same arrangement a real second process produces: the OS does not distinguish them.
		string lockPath = NewLockPath();
		Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
		using FileStream foreignHolder = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		InterprocessFileGate gate = CreateGate(ShortTimeout);
		bool actionRan = false;

		// Act
		Action enter = () => gate.Enter(lockPath, () => actionRan = true);

		// Assert
		enter.Should().Throw<TimeoutException>(
			because: "an expired deadline must surface as one recognisable failure type; letting the raw IOException escape is the wart in the older in-repo lock helper this gate deliberately fixes")
			.WithMessage("*another clio process*",
				because: "the message must point the operator at the actual cause — a second clio holding the file");
		actionRan.Should().BeFalse(because: "guarded work must never run without the lock");
	}

	[Test]
	[Description("Enter must throw TimeoutException when another THREAD of this process holds the lock past the timeout, so the in-process monitor layer has the same failure contract as the file layer.")]
	public void Enter_ShouldThrowTimeout_WhenAnotherThreadHoldsTheLockPastTheDeadline() {
		// Arrange
		string lockPath = NewLockPath();
		InterprocessFileGate gate = CreateGate(ShortTimeout);
		using ManualResetEventSlim holderInside = new(false);
		using ManualResetEventSlim release = new(false);
		Task holder = Task.Run(() => gate.Enter(lockPath, () => {
			holderInside.Set();
			release.Wait(Generous);
		}));
		holderInside.Wait(Generous).Should().BeTrue(because: "the holder must be inside the guarded region first");

		// Act
		Action enter = () => gate.Enter(lockPath, () => { });

		try {
			// Assert
			enter.Should().Throw<TimeoutException>(
				because: "a caller must not wait forever behind a thread of its own process either — the wait is bounded on both layers");
		} finally {
			release.Set();
			holder.Wait(Generous);
		}
	}

	[Test]
	[Description("A nested Enter on the same lock from the same thread must pass straight through instead of self-deadlocking, because a gated read inside a gated read-modify-write is a normal composition.")]
	public void Enter_ShouldAdmitReentrantAcquisition_WhenNestedOnTheSameThread() {
		// Arrange
		string lockPath = NewLockPath();
		InterprocessFileGate gate = CreateGate(ShortTimeout);

		// Act
		bool innerRan = false;
		Action nested = () => gate.Enter(lockPath, () => gate.Enter(lockPath, () => innerRan = true));

		// Assert
		nested.Should().NotThrow(
			because: "sync-pages reads the existing baseline INSIDE its gated meta.json rewrite; a non-reentrant gate would deadlock that path or force the read out of the lock and reopen the lost-merge window");
		innerRan.Should().BeTrue(because: "the nested guarded work must actually execute");
	}

	[Test]
	[Description("Enter must release the lock when the guarded work throws, so one failed page operation cannot wedge every later one.")]
	public void Enter_ShouldReleaseTheLock_WhenTheGuardedActionThrows() {
		// Arrange
		string lockPath = NewLockPath();
		InterprocessFileGate gate = CreateGate(ShortTimeout);
		Action failing = () => gate.Enter<bool>(lockPath, () => throw new InvalidOperationException("boom"));
		failing.Should().Throw<InvalidOperationException>(
			because: "the gate must propagate the guarded work's own failure unchanged");

		// Act
		bool secondRan = false;
		Action second = () => gate.Enter(lockPath, () => secondRan = true);

		// Assert
		second.Should().NotThrow(
			because: "a lock leaked by a failed action would turn one transient page error into a permanently unusable schema");
		secondRan.Should().BeTrue(because: "the follow-up work must run");
	}

	[Test]
	[Description("A second, independent gate instance must contend with the first, so an accidental extra instance cannot become a second lock domain that defeats the exclusion.")]
	public void Enter_ShouldStillExclude_WhenTwoSeparateGateInstancesShareOneLockPath() {
		// Arrange
		string lockPath = NewLockPath();
		InterprocessFileGate holderGate = CreateGate();
		InterprocessFileGate otherGate = CreateGate();
		using ManualResetEventSlim holderInside = new(false);
		using ManualResetEventSlim release = new(false);
		Task holder = Task.Run(() => holderGate.Enter(lockPath, () => {
			holderInside.Set();
			release.Wait(Generous);
		}));
		holderInside.Wait(Generous).Should().BeTrue(because: "the holder must be inside the guarded region first");

		try {
			// Act
			Task<bool> contender = Task.Run(() => otherGate.Enter(lockPath, () => true));
			bool completedWhileHeld = contender.Wait(BlockedProbe);

			// Assert
			completedWhileHeld.Should().BeFalse(
				because: "exclusion is a property of the lock PATH, not of the object: a second instance must queue behind the first, or a stray registration would silently disable the gate");
			release.Set();
			contender.Wait(Generous).Should().BeTrue(
				because: "once the holder leaves, the queued caller must proceed rather than fail");
		} finally {
			release.Set();
			holder.Wait(Generous);
		}
	}
}
