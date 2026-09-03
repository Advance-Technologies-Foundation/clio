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
	private const int MaxConsecutiveFailures = 10;

	private const int PollIntervalMilliseconds = 1_000;

	private readonly IDataProvider _dataProvider;

	public CompilationHistoryPoller(IDataProvider dataProvider) {
		_dataProvider = dataProvider;
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
			List<CompilationHistory> records;
			try {
				records = PollOnce(baseline);
				consecutiveFailures = 0;
			} catch (Exception exception) when (exception is not OperationCanceledException) {
				//A single failed round must NOT end the poll. Before ClassifyingDataProvider (issue
				//#1371) an unreachable round came back as an empty list and the loop simply tried
				//again; now it throws, and a compile that takes minutes cannot be abandoned because
				//one OData read out of hundreds timed out or hit a restarting app tier. Only a
				//SUSTAINED failure is real - that is what the budget below distinguishes.
				consecutiveFailures++;
				if (consecutiveFailures >= MaxConsecutiveFailures) {
					throw new InvalidOperationException(
						$"Compilation polling gave up after {MaxConsecutiveFailures} consecutive failed "
						+ $"rounds. Last failure: {exception.Message}", exception);
				}
				if (ct.WaitHandle.WaitOne(PollIntervalMilliseconds)) {
					break;
				}
				continue;
			}

			foreach (CompilationHistory record in records) {
				if (seen.Add(record.Id)) {
					baseline = record.CreatedOn > baseline ? record.CreatedOn : baseline;
					onNewRecord(record);
				}
			}

			if (ct.WaitHandle.WaitOne(PollIntervalMilliseconds)) {
				break;
			}
		}
	}

}
