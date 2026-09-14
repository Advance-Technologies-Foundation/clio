using System;
using System.Threading;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class EnvironmentReloadWatcherTests {

	private const int WaitTimeoutMs = 10_000;

	private static readonly TimeSpan FastProbe = TimeSpan.FromMilliseconds(10);

	[Test]
	[Description("An environment that answered, then stopped answering for the whole outage threshold, then answered again is recorded as a reload - the signature of the runtime restart that ends a configuration build.")]
	public void Watcher_ShouldRecordAReload_WhenTheEnvironmentGoesAwayAndComesBack() {
		// Arrange
		IEnvironmentAvailabilityProbe probe = Substitute.For<IEnvironmentAvailabilityProbe>();
		int call = 0;
		probe.IsReachable(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(_ => {
			int current = Interlocked.Increment(ref call);
			return current == 1 || current > 1 + EnvironmentReloadWatcher.FailuresBeforeOutage;
		});
		IEnvironmentReloadWatcher watcher = new EnvironmentReloadWatcher(probe, FastProbe);

		// Act
		watcher.Start();
		bool reloadObserved = WaitFor(watcher, snapshot => snapshot.ReloadObserved);
		watcher.Stop();

		// Assert
		reloadObserved.Should().BeTrue(
			because: "an application that answered, went down for the whole threshold, and answered again has restarted");
	}

	[Test]
	[Description("THE FALSE-SUCCESS GUARD. A SINGLE failed probe is not a reload: one transient 502, or one request lost while the session is renewed, would otherwise arm the signal, and the next success would report a still-running build as finished from the PREVIOUS build's undated verdict.")]
	public void Watcher_ShouldNotRecordAReload_WhenOnlyOneProbeFails() {
		// Arrange
		IEnvironmentAvailabilityProbe probe = Substitute.For<IEnvironmentAvailabilityProbe>();
		int call = 0;
		probe.IsReachable(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(_ => Interlocked.Increment(ref call) != 2);
		IEnvironmentReloadWatcher watcher = new EnvironmentReloadWatcher(probe, FastProbe);

		// Act
		watcher.Start();
		// Wait well past the failing probe and its recovery before reading the verdict.
		WaitFor(watcher, _ => Volatile.Read(ref call) > 6);
		bool reloadObserved = watcher.Snapshot.ReloadObserved;
		watcher.Stop();

		// Assert
		reloadObserved.Should().BeFalse(
			because: $"an outage counts only after {EnvironmentReloadWatcher.FailuresBeforeOutage} consecutive failures, and one blip is not a restart");
	}

	[Test]
	[Description("An environment that was NEVER reachable must not report a reload: otherwise the first successful probe against a stand that was down at watch-start would read as 'the build finished'.")]
	public void Watcher_ShouldNotRecordAReload_WhenTheEnvironmentWasNeverReachable() {
		// Arrange
		IEnvironmentAvailabilityProbe probe = Substitute.For<IEnvironmentAvailabilityProbe>();
		int call = 0;
		probe.IsReachable(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(_ => Interlocked.Increment(ref call) > EnvironmentReloadWatcher.FailuresBeforeOutage);
		IEnvironmentReloadWatcher watcher = new EnvironmentReloadWatcher(probe, FastProbe);

		// Act
		watcher.Start();
		WaitFor(watcher, snapshot => snapshot.Reachable);
		bool reloadObserved = watcher.Snapshot.ReloadObserved;
		watcher.Stop();

		// Assert
		reloadObserved.Should().BeFalse(
			because: "an environment that had never answered cannot have been seen to go down and come back");
	}

	[Test]
	[Description("The probe is asked with the short timeout the watcher declares. It is the timeout that makes a five-second cadence real: on a path where the bound is not enforced a single sample can span the whole measured 44-second outage.")]
	public void Watcher_ShouldProbeWithTheShortTimeout() {
		// Arrange
		IEnvironmentAvailabilityProbe probe = Substitute.For<IEnvironmentAvailabilityProbe>();
		probe.IsReachable(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
		IEnvironmentReloadWatcher watcher = new EnvironmentReloadWatcher(probe, FastProbe);

		// Act
		watcher.Start();
		WaitFor(watcher, snapshot => snapshot.Reachable);
		watcher.Stop();

		// Assert
		probe.Received().IsReachable(EnvironmentReloadWatcher.ProbeTimeout, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("EverReachable stays false while the environment has never answered, which is what lets the completion rule call an unreachable host a transport failure instead of waiting out the whole timeout for a request that never fails.")]
	public void Watcher_ShouldReportNeverReachable_WhileTheEnvironmentIsDown() {
		// Arrange
		IEnvironmentAvailabilityProbe probe = Substitute.For<IEnvironmentAvailabilityProbe>();
		int call = 0;
		probe.IsReachable(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(_ => {
			Interlocked.Increment(ref call);
			return false;
		});
		IEnvironmentReloadWatcher watcher = new EnvironmentReloadWatcher(probe, FastProbe);

		// Act
		watcher.Start();
		bool probed = WaitFor(watcher, _ => Volatile.Read(ref call) > 0);
		EnvironmentReloadSnapshot snapshot = watcher.Snapshot;
		watcher.Stop();

		// Assert
		probed.Should().BeTrue(because: "the watcher must actually probe before its snapshot means anything");
		snapshot.Reachable.Should().BeFalse(because: "no probe has been answered");
		snapshot.EverReachable.Should().BeFalse(because: "the environment has never answered since watching started");
		snapshot.ReloadObserved.Should().BeFalse(because: "nothing has come back up");
	}

	[Test]
	[Description("A reused watcher starts from nothing. Without the reset a second build would begin already believing a reload had happened, and the very first completion check would report the previous build's verdict without waiting for anything.")]
	public void Watcher_ShouldDiscardPreviousObservations_WhenStartedAgain() {
		// Arrange
		IEnvironmentAvailabilityProbe probe = Substitute.For<IEnvironmentAvailabilityProbe>();
		int call = 0;
		probe.IsReachable(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(_ => {
			int current = Interlocked.Increment(ref call);
			return current == 1 || current > 1 + EnvironmentReloadWatcher.FailuresBeforeOutage;
		});
		IEnvironmentReloadWatcher watcher = new EnvironmentReloadWatcher(probe, FastProbe);
		watcher.Start();
		WaitFor(watcher, snapshot => snapshot.ReloadObserved);
		watcher.Stop();

		// Act
		probe.IsReachable(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
		watcher.Start();
		EnvironmentReloadSnapshot snapshot = watcher.Snapshot;
		watcher.Stop();

		// Assert
		snapshot.ReloadObserved.Should().BeFalse(
			because: "the reload observed during the previous build must not be carried into the next one");
	}

	[Test]
	[Description("Stop cancels and joins the probe thread BEFORE disposing the token source it reads, which is the ordering the earlier ObjectDisposedException fix established. A probe that only returns on cancellation proves the join actually waits.")]
	public void Watcher_ShouldStopWithoutObjectDisposedException_WhenAProbeIsInFlight() {
		// Arrange
		IEnvironmentAvailabilityProbe probe = Substitute.For<IEnvironmentAvailabilityProbe>();
		using ManualResetEventSlim probing = new(false);
		probe.IsReachable(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(callInfo => {
			probing.Set();
			// Blocks until Stop cancels, so Join has something real to wait for.
			callInfo.Arg<CancellationToken>().WaitHandle.WaitOne(WaitTimeoutMs);
			return false;
		});
		IEnvironmentReloadWatcher watcher = new EnvironmentReloadWatcher(probe, FastProbe);
		watcher.Start();
		probing.Wait(WaitTimeoutMs);

		// Act
		Action act = watcher.Stop;

		// Assert
		act.Should().NotThrow(
			because: "the token source must be disposed only after the thread that reads it has been joined");
	}

	[Test]
	[Description("Stop is safe on a watcher that was never started, so a caller unwinding from an early failure does not have to track whether it got that far.")]
	public void Watcher_ShouldTolerateStop_WhenItWasNeverStarted() {
		// Arrange
		IEnvironmentReloadWatcher watcher =
			new EnvironmentReloadWatcher(Substitute.For<IEnvironmentAvailabilityProbe>(), FastProbe);

		// Act
		Action act = watcher.Stop;

		// Assert
		act.Should().NotThrow(because: "an unstarted watcher has nothing to stop and no state to dispose");
	}

	private static bool WaitFor(IEnvironmentReloadWatcher watcher,
		Func<EnvironmentReloadSnapshot, bool> predicate) {
		DateTime deadline = DateTime.UtcNow.AddMilliseconds(WaitTimeoutMs);
		while (DateTime.UtcNow < deadline) {
			if (predicate(watcher.Snapshot)) {
				return true;
			}
			Thread.Sleep(25);
		}
		return false;
	}

}
