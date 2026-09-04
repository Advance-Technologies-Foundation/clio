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
	/// Only a sustained run of failures gives up, and it does so by throwing so the caller can report it.
	/// </remarks>
	/// <param name="baseline">Only records with a later <c>CreatedOn</c> are reported.</param>
	/// <param name="ct">Stops the poll.</param>
	/// <param name="onNewRecord">Invoked once per newly observed record.</param>
	/// <exception cref="InvalidOperationException">
	/// Too many consecutive rounds failed; the last failure is the inner exception.
	/// </exception>
	void Poll(DateTime baseline, CancellationToken ct, Action<CompilationHistory> onNewRecord);

}

public class CompilationHistoryPoller : ICompilationHistoryPoller {

	/// <summary>
	/// Consecutive failed rounds tolerated before the poll gives up and surfaces the fault. One second
	/// apart, so this is roughly ten seconds of an environment that cannot answer at all - short enough
	/// that a genuinely dead stand is reported promptly, long enough that a restarting app tier or a
	/// single timed-out read does not abandon a compile that has been running for minutes.
	/// </summary>
	public const int MaxConsecutiveFailures = 10;

	/// <summary>
	/// Default gap between rounds, in milliseconds.
	/// </summary>
	public const int DefaultPollIntervalMilliseconds = 1_000;

	private readonly IDataProvider _dataProvider;

	private readonly int _pollIntervalMilliseconds;

	/// <param name="dataProvider">The provider each round queries through.</param>
	/// <param name="pollIntervalMilliseconds">
	/// Gap between rounds. Only a test passes anything but the default: at one second a run that has to
	/// exhaust the ten-round budget spends ten real seconds sleeping, which is why the budget boundary
	/// went untested. A near-zero interval makes those rounds run back to back.
	/// </param>
	public CompilationHistoryPoller(IDataProvider dataProvider,
		int pollIntervalMilliseconds = DefaultPollIntervalMilliseconds) {
		_dataProvider = dataProvider;
		_pollIntervalMilliseconds = pollIntervalMilliseconds;
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
		var seen = new HashSet<Guid>();
		int consecutiveFailures = 0;
		while (!ct.IsCancellationRequested) {
			if (TryPollOnce(baseline, ref consecutiveFailures,
					out List<CompilationHistory> records)) {
				baseline = ReportNewRecords(records, seen, baseline, onNewRecord);
			}
			if (ct.WaitHandle.WaitOne(_pollIntervalMilliseconds)) {
				break;
			}
		}
	}

	/// <summary>
	/// Runs one round, reporting whether it succeeded. A failed round within the budget returns
	/// <see langword="false"/>, which is NOT the same as a successful round that found nothing - hence a
	/// <see langword="bool"/> plus an <see langword="out"/> list rather than a null list (Sonar S1168).
	/// </summary>
	/// <remarks>
	/// A single failed round must NOT end the poll. Before ClassifyingDataProvider (issue #1371) an
	/// unreachable round came back as an empty list and the loop simply tried again; now it throws, and a
	/// compile that takes minutes cannot be abandoned because one OData read out of hundreds timed out or
	/// hit a restarting app tier. Only a SUSTAINED failure is real - that is what the budget distinguishes.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// <see cref="MaxConsecutiveFailures"/> rounds failed in a row; the last failure is the inner exception.
	/// </exception>
	private bool TryPollOnce(DateTime baseline, ref int consecutiveFailures,
		out List<CompilationHistory> records) {
		try {
			records = PollOnce(baseline);
			consecutiveFailures = 0;
			return true;
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			consecutiveFailures++;
			if (consecutiveFailures >= MaxConsecutiveFailures) {
				throw new InvalidOperationException(
					$"Compilation polling gave up after {MaxConsecutiveFailures} consecutive failed "
					+ $"rounds. Last failure: {exception.Message}", exception);
			}
			records = null;
			return false;
		}
	}

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

}
