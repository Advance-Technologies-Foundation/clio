using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Clio.Common;

/// <summary>
/// The timing budget <see cref="CompilationHistoryPoller.Poll"/> runs on: how often it asks, how long
/// it keeps asking while the environment cannot answer, and how far apart the retries are spaced while
/// that lasts.
/// </summary>
/// <remarks>
/// A data-only carrier, deliberately not a service: production uses <see cref="Default"/> everywhere and
/// only a test supplies anything else, so that the give-up boundary can be exercised without the test
/// spending the real budget asleep (issue #1376).
/// </remarks>
/// <param name="PollInterval">Gap between rounds while the environment is answering.</param>
/// <param name="GiveUpWindow">
/// How long a run of failures is tolerated, measured from the FIRST failure of the current run. A
/// successful round ends the run and the measurement starts over.
/// </param>
/// <param name="BackoffSteps">
/// Gap after the 1st, 2nd, 3rd … failed round. The last entry repeats for every further failure.
/// </param>
public sealed record CompilationPollingOptions(
	TimeSpan PollInterval,
	TimeSpan GiveUpWindow,
	IReadOnlyList<TimeSpan> BackoffSteps) {

	/// <summary>
	/// The production budget: ask once a second, and tolerate an environment that cannot answer at all
	/// for 90 seconds, backing off 1 s, 2 s, then 5 s between the failing rounds.
	/// </summary>
	/// <remarks>
	/// 90 seconds, not the ~10 that the previous fixed count of 10 one-second rounds amounted to. During
	/// a package compile the app tier routinely stops answering OData for much longer than ten seconds -
	/// the measured gaps on a live stand were ~18 s between the compile request and the first history row
	/// and ~31-33 s between two consecutive projects' rows, and on .NET Framework an IIS application-pool
	/// recycle adds its own outage on top - so the old budget abandoned the monitoring of a multi-minute
	/// compile over an ordinary pause. 60 s would be borderline against a recycle landing on top of an
	/// already slow project; 90 s covers it and still reports a genuinely dead stand inside two minutes.
	/// <para>
	/// The backoff keeps the cost of that longer window flat: 1/2/5/5 … yields roughly 21 rounds across
	/// the 90 seconds instead of the 90 an unbacked-off one-second cadence would fire at an environment
	/// that is already struggling.
	/// </para>
	/// </remarks>
	public static CompilationPollingOptions Default { get; } = new(
		PollInterval: TimeSpan.FromSeconds(1),
		GiveUpWindow: TimeSpan.FromSeconds(90),
		BackoffSteps: new ReadOnlyCollection<TimeSpan>([
			TimeSpan.FromSeconds(1),
			TimeSpan.FromSeconds(2),
			TimeSpan.FromSeconds(5)
		]));

	/// <summary>
	/// The gap to wait after the <paramref name="failedRoundNumber"/>-th consecutive failed round; the
	/// last configured step repeats once the sequence is exhausted.
	/// </summary>
	/// <param name="failedRoundNumber">1-based position of the round within the current failing run.</param>
	public TimeSpan BackoffFor(int failedRoundNumber) {
		if (failedRoundNumber <= 0 || BackoffSteps is null || BackoffSteps.Count == 0) {
			return PollInterval;
		}
		int index = Math.Min(failedRoundNumber, BackoffSteps.Count) - 1;
		return BackoffSteps[index];
	}

}
