using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.CreatioModel;

namespace Clio.Common;

public interface ICompilationHistoryPoller {

	CompilationHistory GetBaseline();

	/// <summary>
	/// Executes a single query round for records newer than <paramref name="baseline"/>.
	/// </summary>
	/// <param name="baseline">Only records with a later <c>CreatedOn</c> are returned.</param>
	/// <returns>New records ordered by <c>CreatedOn</c> descending.</returns>
	List<CompilationHistory> PollOnce(DateTime baseline);

	/// <summary>
	/// Polls for records newer than <paramref name="baseline"/> until cancellation, reporting each new
	/// record once.
	/// </summary>
	/// <remarks>
	/// A failed round is TOLERATED: the environment is mid-compile and may briefly be unable to answer,
	/// and every consumer runs this on a background thread where an escaping exception would be fatal.
	/// Only a run of failures lasting longer than <see cref="CompilationPollingOptions.GiveUpWindow"/>
	/// gives up, and it does so by throwing so the caller can report it. Each tolerated round is logged
	/// as a warning, and the failing rounds are spaced out by the configured backoff.
	/// </remarks>
	/// <param name="baseline">Only records with a later <c>CreatedOn</c> are reported.</param>
	/// <param name="ct">Stops the poll.</param>
	/// <param name="onNewRecord">Invoked once per newly observed record.</param>
	/// <exception cref="InvalidOperationException">
	/// Rounds kept failing for longer than the give-up window; the last failure is the inner exception.
	/// </exception>
	void Poll(DateTime baseline, CancellationToken ct, Action<CompilationHistory> onNewRecord);

}

public class CompilationHistoryPoller : ICompilationHistoryPoller {

	private readonly IDataProvider _dataProvider;

	private readonly ILogger _logger;

	private readonly TimeProvider _timeProvider;

	private readonly ICancellableDelay _delay;

	private readonly CompilationPollingOptions _options;

	/// <param name="dataProvider">The provider each round queries through.</param>
	/// <param name="logger">Where a tolerated failed round is reported.</param>
	/// <param name="timeProvider">Measures how long the current run of failures has lasted.</param>
	/// <param name="delay">The wait between rounds, and the seam a test replaces to avoid sleeping.</param>
	/// <param name="options">
	/// The timing budget. <see langword="null"/> selects <see cref="CompilationPollingOptions.Default"/>,
	/// which is what every production caller uses; only a test passes anything else.
	/// </param>
	public CompilationHistoryPoller(IDataProvider dataProvider, ILogger logger, TimeProvider timeProvider,
		ICancellableDelay delay, CompilationPollingOptions options = null) {
		//Checked here rather than discovered later: every dependency below is first touched on a failed
		//round, on a background thread, where a NullReferenceException is the fault that ends the process
		//instead of the outage it was supposed to report.
		dataProvider.CheckArgumentNull(nameof(dataProvider));
		logger.CheckArgumentNull(nameof(logger));
		timeProvider.CheckArgumentNull(nameof(timeProvider));
		delay.CheckArgumentNull(nameof(delay));
		_dataProvider = dataProvider;
		_logger = logger;
		_timeProvider = timeProvider;
		_delay = delay;
		_options = options ?? CompilationPollingOptions.Default;
	}

	public CompilationHistory GetBaseline() {
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		return ctx.Models<CompilationHistory>()
			.OrderByDescending(x => x.CreatedOn)
			.Take(1)
			.FirstOrDefault();
	}

	public List<CompilationHistory> PollOnce(DateTime baseline) {
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		return ctx.Models<CompilationHistory>()
			.OrderByDescending(x => x.CreatedOn)
			.Where(x => x.CreatedOn > baseline)
			.ToList();
	}

	public void Poll(DateTime baseline, CancellationToken ct, Action<CompilationHistory> onNewRecord) {
		HashSet<Guid> seen = [];
		FailureStreak streak = new();
		while (!ct.IsCancellationRequested) {
			//ONE wait per iteration: the backoff REPLACES the normal interval after a failed round, it is
			//not added to it, so a failing environment is asked on the 1/2/5/5 cadence and a healthy one
			//stays on the ordinary one-second cadence.
			bool succeeded = TryPollOnce(baseline, ct, streak, out List<CompilationHistory> records);
			TimeSpan nextDelay = succeeded
				? _options.PollInterval
				: _options.BackoffFor(streak.FailedRounds);
			if (succeeded) {
				baseline = ReportNewRecords(records, seen, baseline, onNewRecord);
			}
			if (_delay.WaitOrCancelled(nextDelay, ct)) {
				break;
			}
		}
	}

	/// <summary>
	/// Runs one round, reporting whether it succeeded. A failed round within the give-up window returns
	/// <see langword="false"/>, which is NOT the same as a successful round that found nothing - hence a
	/// <see langword="bool"/> plus an <see langword="out"/> list rather than a null list (Sonar S1168).
	/// </summary>
	/// <remarks>
	/// A single failed round must NOT end the poll. Before ClassifyingDataProvider (issue #1371) an
	/// unreachable round came back as an empty list and the loop simply tried again; now it throws, and a
	/// compile that takes minutes cannot be abandoned because one OData read out of hundreds timed out or
	/// hit a restarting app tier. Only a SUSTAINED failure is real - that is what the window distinguishes,
	/// and it is a WINDOW rather than a round count because a count is only a duration when the gap
	/// between rounds is fixed, and the backoff makes it not (issue #1376).
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Rounds have been failing for longer than <see cref="CompilationPollingOptions.GiveUpWindow"/>; the
	/// last failure is the inner exception.
	/// </exception>
	private bool TryPollOnce(DateTime baseline, CancellationToken ct, FailureStreak streak,
		out List<CompilationHistory> records) {
		try {
			records = PollOnce(baseline);
			streak.Reset();
			return true;
		//An OperationCanceledException still escapes when the poll was NOT cancelled - that is someone
		//else's cancellation and is not this poll's to swallow - but one raised by OUR token is handled by
		//the guard below, exactly like any other fault a cancelled read produces.
		} catch (Exception exception)
			when (exception is not OperationCanceledException || ct.IsCancellationRequested) {
			records = null;
			//Cancellation checked BEFORE the round is counted or reported. A read that was in flight when
			//the compile finished usually comes back as an ordinary failure rather than as an
			//OperationCanceledException (issue #1377), so without this guard every normal `clio cc` would
			//end with a spurious "polling round failed, retrying" warning on the way out.
			if (ct.IsCancellationRequested) {
				return false;
			}
			TimeSpan elapsed = streak.RecordFailure(_timeProvider);
			if (elapsed >= _options.GiveUpWindow) {
				//The last failure is CHAINED, never interpolated into this message. Interpolating it made
				//the CLI renderer treat this wrapper's own text as redundant - DescribeOuterContext drops
				//a link whose message already contains the text below it - so the line lost the elapsed
				//time, the window and the round count, which is the whole reason this message exists.
				//Observed on a live stand while verifying issue #1376; the same trap is documented at
				//PackageBuilder.CompileWithPolling, which wraps this exception a third time.
				throw new InvalidOperationException(
					$"Compilation polling gave up after {elapsed.TotalSeconds:F0} s of rounds that all "
					+ $"failed (give-up window {_options.GiveUpWindow.TotalSeconds:F0} s, "
					+ $"{streak.FailedRounds} failed rounds)",
					exception);
			}
			//Once per tolerated round, not once per run: with the 1/2/5/5 backoff a 90-second window is
			//about 20 lines, which is a readable account of an outage rather than a flood, and the round
			//number plus the elapsed/window pair makes them obviously one incident. The round that gives
			//up is NOT logged here - it throws, and both consumers already report that fault.
			_logger.WriteWarning(
				$"Compilation polling round {streak.FailedRounds} failed "
				+ $"({elapsed.TotalSeconds:F0} s into a {_options.GiveUpWindow.TotalSeconds:F0} s "
				+ $"tolerance window); retrying in {_options.BackoffFor(streak.FailedRounds).TotalSeconds:F0} s. "
				+ $"Reason: {Describe(exception)}");
			return false;
		}
	}

	/// <summary>
	/// The one rendering allowed into a diagnostic: the readable message, scrubbed AND fenced.
	/// </summary>
	/// <remarks>
	/// <c>GetReadableMessageException</c> alone is not enough. It returns a
	/// <c>DataProviderFailureException</c>'s already-scrubbed ConsoleMessage - the convention both
	/// consumers of this poller follow - but for any other exception type it returns the raw
	/// <c>Message</c>, which for a transport fault routinely carries the full request URI.
	/// <para>
	/// <see cref="UntrustedText.Fenced"/> rather than <c>ForConsole</c>, which is what a logger line
	/// elsewhere in <c>Clio.Common</c> uses (<c>SysSettingsManager</c>). This line is different: it is
	/// written by a WARNING during <c>compile-creatio</c>, and <c>CompileCreatioTool</c> copies the
	/// captured log messages into the MCP result through <c>McpPassthroughRedaction.SanitizeAndRedact</c>,
	/// which scrubs secrets (and only on a passthrough request) but never fences. So without the fence
	/// here, server-authored prose - an outage is exactly when the answer is some gateway's or proxy's
	/// own page - reaches an agent's context as if clio had written it. The fence costs a terminal reader
	/// a marker; dropping it costs an agent the distinction between clio's words and the server's.
	/// </para>
	/// </remarks>
	private static string Describe(Exception exception) =>
		UntrustedText.Fenced(exception.GetReadableMessageException()) ?? "no detail reported";

	/// <summary>
	/// Reports every record not seen before and returns the advanced baseline. A real Creatio
	/// CompilationHistory table can return the same row again across rounds, so the seen-Id set - not the
	/// timestamp alone - is what keeps a record from being reported twice.
	/// </summary>
	private static DateTime ReportNewRecords(List<CompilationHistory> records, HashSet<Guid> seen,
		DateTime baseline, Action<CompilationHistory> onNewRecord) {
		foreach (CompilationHistory record in records) {
			if (seen.Add(record.Id)) {
				baseline = record.CreatedOn > baseline ? record.CreatedOn : baseline;
				onNewRecord(record);
			}
		}
		return baseline;
	}

	/// <summary>
	/// The current run of consecutive failed rounds: how many there have been and when it started. A
	/// plain mutable state holder local to one <see cref="Poll"/> call - not a service - which is why it
	/// is passed by reference instead of living in a field: two concurrent polls on one poller instance
	/// must not share a run.
	/// </summary>
	private sealed class FailureStreak {

		private long _startedAt;

		/// <summary>Number of consecutive rounds that have failed, 0 when the last round succeeded.</summary>
		internal int FailedRounds { get; private set; }

		/// <summary>Ends the current run; the next failure starts the window measurement over.</summary>
		internal void Reset() => FailedRounds = 0;

		/// <summary>
		/// Counts one more failed round and returns how long the current run has lasted. The FIRST
		/// failure of a run returns <see cref="TimeSpan.Zero"/>, so a lone failure can never give up.
		/// </summary>
		internal TimeSpan RecordFailure(TimeProvider timeProvider) {
			if (FailedRounds == 0) {
				_startedAt = timeProvider.GetTimestamp();
			}
			FailedRounds++;
			return timeProvider.GetElapsedTime(_startedAt);
		}

	}

}
