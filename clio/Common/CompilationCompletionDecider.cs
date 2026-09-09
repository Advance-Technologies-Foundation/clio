using System;

namespace Clio.Common;

/// <summary>
/// How a configuration build ended, as far as the caller can tell.
/// </summary>
public enum CompilationCompletionKind {

	/// <summary>Nothing decisive yet; keep observing.</summary>
	KeepWaiting,

	/// <summary>The compile request itself answered, so its payload is the verdict.</summary>
	ResponseReceived,

	/// <summary>Activity was observed and the runtime reload that ends a build was seen through it.</summary>
	ConfirmedByReload,

	/// <summary>Activity was observed and then stopped for longer than the quiet window, with no reload seen.</summary>
	InferredFromQuiet,

	/// <summary>The request failed without the environment ever starting to build.</summary>
	TransportFailure

}

/// <summary>
/// Everything the decision is made from, sampled at one instant.
/// </summary>
/// <param name="ResponseReceived">The compile request returned a response body.</param>
/// <param name="RequestEnded">The compile request is no longer in flight, for any reason.</param>
/// <param name="NewRecordCount">Compilation-history rows created since the build was started.</param>
/// <param name="ReloadObserved">The environment was seen to go unreachable and come back.</param>
/// <param name="EnvironmentReachable">The environment answered the most recent availability probe.</param>
/// <param name="EnvironmentEverReachable">The environment answered at least once since the build started.</param>
/// <param name="LastActivityAtUtc">When the last such row was observed; <see langword="null"/> when none was.</param>
/// <param name="StartedUtc">When the build was requested.</param>
/// <param name="NowUtc">The instant being decided at.</param>
/// <param name="StartupGrace">How long a build may take to write its first row before silence counts against it.</param>
/// <param name="QuietFallback">
/// How long activity must have been stopped before quiet ALONE ends the wait, with no reload seen.
/// </param>
public record CompilationCompletionState(
	bool ResponseReceived,
	bool RequestEnded,
	int NewRecordCount,
	bool ReloadObserved,
	bool EnvironmentReachable,
	bool EnvironmentEverReachable,
	DateTime? LastActivityAtUtc,
	DateTime StartedUtc,
	DateTime NowUtc,
	TimeSpan StartupGrace,
	TimeSpan QuietFallback);

/// <summary>
/// Decides when a configuration build has ended, from evidence about the environment rather than from
/// the compile request's response.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the response cannot be the rule.</b> A configuration build ends by reloading the application,
/// which tears down the very connection the response would travel on. Measured on a live 10.0.0.934
/// stand, six of six builds answered with zero response bytes and a connection reset; the successes clio
/// used to report came from an automatic retry that happened to arrive after the build, which on a full
/// rebuild meant running the whole rebuild again (three times, in the measured case).
/// </para>
/// <para>
/// <b>Why the reset is not the rule either.</b> An intermediary with an idle timeout - a load balancer
/// in front of a cloud instance - resets a long-lived socket in the MIDDLE of a build. Reading that as
/// completion would report the previous build's verdict while this one is still running, which is the
/// reported defect inverted. The reload is what separates the two: a finished build makes the
/// environment unreachable and then reachable again, and an intermediary's reset does not.
/// </para>
/// </remarks>
public interface ICompilationCompletionDecider {

	/// <summary>
	/// Decides whether the build has ended, and on what evidence.
	/// </summary>
	/// <param name="state">The evidence sampled at one instant.</param>
	/// <returns>The completion kind; <see cref="CompilationCompletionKind.KeepWaiting"/> when nothing is decisive yet.</returns>
	CompilationCompletionKind Decide(CompilationCompletionState state);

}

/// <inheritdoc cref="ICompilationCompletionDecider"/>
public class CompilationCompletionDecider : ICompilationCompletionDecider {

	/// <inheritdoc/>
	public CompilationCompletionKind Decide(CompilationCompletionState state) {
		state.CheckArgumentNull(nameof(state));

		// 1. The response is still the best evidence when it exists, and it is checked first because it is
		//    also the COMPILE-ERROR path: a build that fails does not reload the runtime, so the server
		//    stays up and answers - carrying the compiler diagnostics that no other source has.
		if (state.ResponseReceived) {
			return CompilationCompletionKind.ResponseReceived;
		}

		// 2. Confirmed: the environment built something, then went away and came back. ChannelHealthy is
		//    required as well so the verdict read that follows is issued against an application that is
		//    answering again, rather than into the middle of the reload.
		if (state.NewRecordCount > 0 && state.ReloadObserved && state.EnvironmentReachable) {
			return CompilationCompletionKind.ConfirmedByReload;
		}

		// 3. Inferred: activity started and has been stopped for longer than the fallback window without a
		//    reload being seen. This is the reporter's case (github.com/.../issues/1422), where an
		//    intermediary holds the socket open so the request never ends and nothing else terminates.
		//    It is reported as inferred rather than confirmed because a build could in principle still be
		//    between two very slow projects; the window is sized to make that unlikely, not impossible.
		//
		//    THE WINDOW MUST OUTLAST THE RELOAD, or this rule fires first on every ordinary build and
		//    rule 2 becomes unreachable. Measured on a live stand: the runtime reload lands about two
		//    minutes after the last compilation-history row, so a window sized for the gaps BETWEEN
		//    projects (45 s, which is what ICompilationSettleTracker is calibrated for) settles while the
		//    build is still finishing - and then reads an undated verdict that may still be the previous
		//    build's. That is the false success rule 3 exists to prevent, arrived at from the other side.
		if (state.NewRecordCount > 0 && state.LastActivityAtUtc is { } lastActivity
			&& state.NowUtc - lastActivity >= state.QuietFallback) {
			return CompilationCompletionKind.InferredFromQuiet;
		}

		// 4. Nothing was ever built, and the grace period for a slow first row has passed. Two shapes
		//    reach this, and BOTH have to, which is why it is not gated on the request ending alone:
		//      - the request failed outright (connection refused, a login page, a 404);
		//      - the request is still hanging because the host silently drops packets - a wrong --uri, a
		//        firewall, a VPN that is down. There the POST does not fail fast, so waiting for it would
		//        mean waiting out the whole timeout with nothing to show. What answers instead is the
		//        availability probe: an environment that has NEVER answered is not building anything.
		//    The grace keeps a genuine build from being called a failure in the window before it writes
		//    its first row, measured at ~18s on a warm stand and longer on a cold one.
		if (state.NewRecordCount == 0 && state.NowUtc - state.StartedUtc >= state.StartupGrace
			&& (state.RequestEnded || !state.EnvironmentEverReachable)) {
			return CompilationCompletionKind.TransportFailure;
		}

		// 5. Everything else keeps waiting - including a request that ended after activity WITHOUT a
		//    reload, which is an intermediary having dropped a build that is still running. Treating that
		//    as completion is the false success this ordering exists to prevent; it is left to rule 3,
		//    which needs the activity to actually stop first.
		return CompilationCompletionKind.KeepWaiting;
	}

}
