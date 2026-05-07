using System;
using System.Threading.Tasks;

namespace Clio.Common;

/// <summary>
/// Schedules a fire-and-forget action so the caller doesn't block on long-running side effects
/// (e.g. cache rebuilds after a successful save). Production implementation runs on the thread
/// pool; test implementations can run synchronously to keep behaviour assertions deterministic.
/// </summary>
internal interface IBackgroundTaskRunner {
	void Run(Action action);
}

internal sealed class TaskRunBackgroundTaskRunner(ILogger logger) : IBackgroundTaskRunner {
	public void Run(Action action) {
		ArgumentNullException.ThrowIfNull(action);
		Task.Run(() => {
			try {
				action();
			} catch (Exception ex) {
				logger.WriteWarning($"Background task failed: {ex}");
			}
		});
	}
}

/// <summary>
/// Synchronous implementation that runs the action on the calling thread. Intended for tests
/// or call sites that explicitly need deterministic, in-line execution.
/// </summary>
internal sealed class SynchronousBackgroundTaskRunner : IBackgroundTaskRunner {
	public void Run(Action action) {
		ArgumentNullException.ThrowIfNull(action);
		action();
	}
}
