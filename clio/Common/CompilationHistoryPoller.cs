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

	void Poll(DateTime baseline, CancellationToken ct, Action<CompilationHistory> onNewRecord);

}

public class CompilationHistoryPoller : ICompilationHistoryPoller {

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
		while (!ct.IsCancellationRequested) {
			List<CompilationHistory> records = PollOnce(baseline);

			foreach (CompilationHistory record in records) {
				if (seen.Add(record.Id)) {
					baseline = record.CreatedOn > baseline ? record.CreatedOn : baseline;
					onNewRecord(record);
				}
			}

			if (ct.WaitHandle.WaitOne(1_000)) {
				break;
			}
		}
	}

}
