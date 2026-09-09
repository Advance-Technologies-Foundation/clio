using System;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CompilationCompletionDeciderTests {

	private static readonly DateTime Started = new(2026, 9, 9, 16, 0, 0, DateTimeKind.Utc);
	private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(90);
	private static readonly TimeSpan QuietFallback = TimeSpan.FromMinutes(5);

	private static CompilationCompletionState State(
		bool responseReceived = false,
		bool requestEnded = false,
		int newRecordCount = 0,
		bool reloadObserved = false,
		bool environmentReachable = true,
		bool environmentEverReachable = true,
		int? quietForSeconds = null,
		int secondsElapsed = 10) =>
		new(responseReceived, requestEnded, newRecordCount, reloadObserved, environmentReachable,
			environmentEverReachable,
			quietForSeconds is null ? null : Started.AddSeconds(secondsElapsed - quietForSeconds.Value),
			Started, Started.AddSeconds(secondsElapsed), StartupGrace, QuietFallback);

	[Test]
	[Description("A compile request that answered is the verdict, and it is checked before anything observed: a build that FAILS does not reload the runtime, so the server stays up and answers with the compiler diagnostics no other source carries.")]
	public void Decide_ShouldPreferTheResponse_WhenOneArrived() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(responseReceived: true, requestEnded: true, newRecordCount: 5);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.ResponseReceived,
			because: "the response is the most precise evidence there is and outranks the observed signals");
	}

	[Test]
	[Description("Activity followed by the environment going away and coming back is the end-of-build signature: a configuration build finishes by reloading the runtime.")]
	public void Decide_ShouldConfirm_WhenActivityWasFollowedByAReload() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(newRecordCount: 12, reloadObserved: true, environmentReachable: true, quietForSeconds: 10);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.ConfirmedByReload,
			because: "the reload is what distinguishes a finished build from a dropped connection");
	}

	[Test]
	[Description("A reload that has not finished yet is not completion: the verdict read that follows would be issued against an application still coming up.")]
	public void Decide_ShouldKeepWaiting_WhenTheEnvironmentHasNotComeBackYet() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(newRecordCount: 12, reloadObserved: true, environmentReachable: false);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.KeepWaiting,
			because: "the environment must be answering again before its verdict can be read");
	}

	[Test]
	[Description("THE FALSE-SUCCESS GUARD. An intermediary with an idle timeout resets the socket in the MIDDLE of a build; treating the ended request as completion would report the PREVIOUS build's verdict while this one is still running.")]
	public void Decide_ShouldKeepWaiting_WhenTheRequestEndedAfterActivityButNoReloadWasSeen() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(requestEnded: true, newRecordCount: 4, reloadObserved: false);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.KeepWaiting,
			because: "a dropped connection is not evidence that the build ended, and acting on it reports a stale verdict");
	}

	[Test]
	[Description("The reporter's case (issue #1422): the intermediary holds the socket open so the request never ends at all, and only the activity having stopped can conclude the build.")]
	public void Decide_ShouldInfer_WhenActivityStoppedAndTheRequestNeverEnded() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(newRecordCount: 9, quietForSeconds: 400, reloadObserved: false);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.InferredFromQuiet,
			because: "activity that started and then stopped for the whole quiet window is the only evidence left");
	}

	[Test]
	[Description("Quiet that has not yet outlasted the reload does NOT end the wait. The runtime reload lands about two minutes after the last compilation-history row, so concluding on a shorter gap reads the undated verdict before the build has actually finished.")]
	public void Decide_ShouldKeepWaiting_WhenActivityStoppedButNotForLongEnough() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(newRecordCount: 9, quietForSeconds: 60, secondsElapsed: 200);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.KeepWaiting,
			because: "a one-minute gap is shorter than the reload that ends a build, so it proves nothing yet");
	}

	[Test]
	[Description("A confirmed reload outranks the inferred quiet verdict, so an operation that has both reports the stronger evidence.")]
	public void Decide_ShouldPreferTheReload_OverTheQuietInference() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(newRecordCount: 9, quietForSeconds: 400, reloadObserved: true);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.ConfirmedByReload,
			because: "the reload is direct evidence and the quiet window is circumstantial");
	}

	[Test]
	[Description("A request that failed while the environment never built anything is a transport or authorization failure - an unreachable host, a login page, a 404 - and must be reported rather than waited out.")]
	public void Decide_ShouldReportTransportFailure_WhenNothingWasEverBuilt() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(requestEnded: true, newRecordCount: 0, secondsElapsed: 120);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.TransportFailure,
			because: "no compilation history after the grace period means the request never reached a building environment");
	}

	[Test]
	[Description("A host that silently drops packets - a wrong --uri, a firewall, a VPN that is down - never fails the request, so waiting for the request to end would mean waiting out the whole timeout. An environment that has NEVER answered a probe is the signal that ends it instead.")]
	public void Decide_ShouldReportTransportFailure_WhenTheEnvironmentNeverAnsweredAndTheRequestStillHangs() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(requestEnded: false, newRecordCount: 0,
			environmentReachable: false, environmentEverReachable: false, secondsElapsed: 120);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.TransportFailure,
			because: "an environment that has never answered is not building anything, and the hanging request will not say so");
	}

	[Test]
	[Description("An environment that IS answering but has written no history row yet is a slow build, not a broken host, so a still-hanging request must not be called a transport failure.")]
	public void Decide_ShouldKeepWaiting_WhenTheEnvironmentAnswersButHasNotBuiltYet() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(requestEnded: false, newRecordCount: 0,
			environmentReachable: true, environmentEverReachable: true, secondsElapsed: 120);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.KeepWaiting,
			because: "a reachable environment that has not written its first row may simply still be starting the build");
	}

	[Test]
	[Description("Inside the startup grace a silent environment is a build that has not written its first row yet - measured at ~18s on a warm stand - not a broken one.")]
	public void Decide_ShouldKeepWaiting_WhenTheRequestEndedInsideTheStartupGrace() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(requestEnded: true, newRecordCount: 0, secondsElapsed: 30);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.KeepWaiting,
			because: "a build that has not yet written its first history row must not be called a transport failure");
	}

	[Test]
	[Description("Quiet alone, with no activity ever observed, is not completion: it is an environment that has not started building.")]
	public void Decide_ShouldNotInfer_WhenNothingWasEverBuilt() {
		// Arrange
		ICompilationCompletionDecider decider = new CompilationCompletionDecider();
		CompilationCompletionState state = State(newRecordCount: 0, quietForSeconds: 400);

		// Act
		CompilationCompletionKind kind = decider.Decide(state);

		// Assert
		kind.Should().Be(CompilationCompletionKind.KeepWaiting,
			because: "the quiet window measures a gap between builds, and there was no build to have a gap in");
	}

}
