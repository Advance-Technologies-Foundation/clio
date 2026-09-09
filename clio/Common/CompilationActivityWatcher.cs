using System;
using System.Collections.Generic;
using System.Threading;
using Clio.CreatioModel;

namespace Clio.Common;

/// <summary>
/// What a watcher has observed about a configuration build so far.
/// </summary>
/// <param name="NewRecordCount">Compilation-history rows created after the baseline.</param>
/// <param name="LastActivityAtUtc">When the last such row was observed, on the CLIENT clock; <see langword="null"/> when none was.</param>
/// <param name="HasErrors">Whether any observed row carried a non-warning diagnostic.</param>
public record CompilationActivitySnapshot(
	int NewRecordCount,
	DateTime? LastActivityAtUtc,
	bool HasErrors);

/// <summary>
/// Watches <see cref="CompilationHistory"/> for a configuration build that is running on the target
/// environment, surviving the environment becoming unreachable mid-build.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it must tolerate failure rather than simply poll.</b> A configuration build ends by reloading
/// the application, so the environment stops answering for a minute or two in the middle of the very
/// operation being watched. <c>CompilationHistoryPoller.Poll</c> has no exception handling of its own, so
/// a caller running it on a bare thread loses its watcher silently at exactly that moment and then waits
/// for progress that can no longer be reported.
/// </para>
/// <para>
/// <b>It does NOT detect the reload.</b> Measured on a live stand: across an application-pool recycle
/// this channel reported not one failed read while the application was demonstrably down for 44
/// seconds. Whether the environment restarted is <see cref="IEnvironmentReloadWatcher"/>'s question,
/// asked on a different endpoint; this watcher only survives the outage so progress reporting resumes.
/// </para>
/// </remarks>
public interface ICompilationActivityWatcher {

	/// <summary>
	/// Starts watching for rows created after <paramref name="baselineCreatedOn"/>.
	/// </summary>
	/// <param name="baselineCreatedOn">The server-clock cutoff; only later rows are reported.</param>
	/// <param name="onNewRecord">Called once per newly observed row, in observation order.</param>
	/// <param name="onPollFailed">
	/// Called with the consecutive-failure count and the failure, when a read fails.
	/// <see langword="null"/> to ignore them.
	/// </param>
	/// <remarks>
	/// Read failures are worth REPORTING to a user who asked to watch an environment, and worth
	/// IGNORING for a caller that triggered the build itself: there the outage is the expected middle
	/// of a successful compile, and warning about it on every poll would bury the progress output.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The watcher is already running.</exception>
	void Start(DateTime baselineCreatedOn, Action<CompilationHistory> onNewRecord,
		Action<int, Exception> onPollFailed = null);

	/// <summary>
	/// Stops watching and waits for the background poll to end.
	/// </summary>
	/// <remarks>Safe to call when the watcher was never started, and safe to call twice.</remarks>
	void Stop();

	/// <summary>Gets what has been observed so far. Safe to read while the watcher is running.</summary>
	CompilationActivitySnapshot Snapshot { get; }

}

/// <inheritdoc cref="ICompilationActivityWatcher"/>
public class CompilationActivityWatcher : ICompilationActivityWatcher {

	#region Constants: Internal

	/// <summary>Cadence between poll rounds while the channel is healthy.</summary>
	internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

	#endregion

	#region Fields: Private

	private readonly ICompilationHistoryPoller _compilationHistoryPoller;
	private readonly IPollRetryPolicy _retryPolicy;
	private readonly Func<DateTime> _utcNow;

	// A real CompilationHistory table can return the same row again across rounds (observed live:
	// duplicate CreatedOn at whole-second precision), so rows are de-duplicated by Id rather than by
	// the CreatedOn cutoff alone. Without it a repeated row keeps resetting the activity clock.
	private readonly HashSet<Guid> _seenRecordIds = [];

	private readonly object _stateLock = new();

	private CancellationTokenSource _cancellation;
	private Thread _thread;

	private int _newRecordCount;
	private DateTime? _lastActivityAtUtc;
	private bool _hasErrors;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="CompilationActivityWatcher"/> class.
	/// </summary>
	/// <param name="compilationHistoryPoller">Reads compilation-history rows from the environment.</param>
	/// <param name="retryPolicy">Backoff and channel-health policy across poll failures.</param>
	public CompilationActivityWatcher(ICompilationHistoryPoller compilationHistoryPoller,
		IPollRetryPolicy retryPolicy)
		: this(compilationHistoryPoller, retryPolicy, null) { }

	/// <summary>
	/// Test seam taking an explicit clock so activity and outage timing are assertable without waiting.
	/// </summary>
	/// <param name="compilationHistoryPoller">Reads compilation-history rows from the environment.</param>
	/// <param name="retryPolicy">Backoff and channel-health policy across poll failures.</param>
	/// <param name="utcNow">Clock used for activity and health timestamps.</param>
	internal CompilationActivityWatcher(ICompilationHistoryPoller compilationHistoryPoller,
		IPollRetryPolicy retryPolicy, Func<DateTime> utcNow) {
		compilationHistoryPoller.CheckArgumentNull(nameof(compilationHistoryPoller));
		retryPolicy.CheckArgumentNull(nameof(retryPolicy));
		_compilationHistoryPoller = compilationHistoryPoller;
		_retryPolicy = retryPolicy;
		_utcNow = utcNow ?? (() => DateTime.UtcNow);
	}

	#endregion

	#region Properties: Public

	/// <inheritdoc/>
	public CompilationActivitySnapshot Snapshot {
		get {
			lock (_stateLock) {
				return new CompilationActivitySnapshot(_newRecordCount, _lastActivityAtUtc, _hasErrors);
			}
		}
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public void Start(DateTime baselineCreatedOn, Action<CompilationHistory> onNewRecord,
		Action<int, Exception> onPollFailed = null) {
		onNewRecord.CheckArgumentNull(nameof(onNewRecord));
		if (_thread is not null) {
			throw new InvalidOperationException(
				"The compilation activity watcher is already running. Create one watcher per build.");
		}
		// Reset EVERY observation. A reused instance would otherwise begin its second build already
		// holding the first one's activity, and the completion rule would conclude on it immediately.
		lock (_stateLock) {
			_seenRecordIds.Clear();
			_newRecordCount = 0;
			_lastActivityAtUtc = null;
			_hasErrors = false;
		}
		_cancellation = new CancellationTokenSource();
		Thread thread = new(() => Run(baselineCreatedOn, onNewRecord, onPollFailed, _cancellation.Token)) {
			IsBackground = true
		};
		thread.Start();
		// Assigned only after a successful start, so a failed Thread.Start cannot leave Stop() joining a
		// thread that was never running.
		_thread = thread;
	}

	/// <inheritdoc/>
	public void Stop() {
		if (_thread is null) {
			return;
		}
		_cancellation.Cancel();
		// Join BEFORE disposing the token source: the poll loop reads the token, and disposing while it is
		// still in flight is the ObjectDisposedException CompileConfigurationCommand was already fixed for
		// once. The thread is bounded by the poll interval, so this cannot wait long.
		_thread.Join();
		_cancellation.Dispose();
		_cancellation = null;
		_thread = null;
	}

	#endregion

	#region Methods: Private

	private void Run(DateTime baselineCreatedOn, Action<CompilationHistory> onNewRecord,
		Action<int, Exception> onPollFailed, CancellationToken cancellationToken) {
		DateTime cutoff = baselineCreatedOn;
		while (!cancellationToken.IsCancellationRequested) {
			PollOnce(ref cutoff, onNewRecord, onPollFailed);
			TimeSpan delay;
			lock (_stateLock) {
				delay = _retryPolicy.ConsecutiveFailures > 0 ? _retryPolicy.NextDelay : PollInterval;
			}
			if (cancellationToken.WaitHandle.WaitOne(delay)) {
				return;
			}
		}
	}

	private void PollOnce(ref DateTime cutoff, Action<CompilationHistory> onNewRecord,
		Action<int, Exception> onPollFailed) {
		List<CompilationHistory> records;
		try {
			records = _compilationHistoryPoller.PollOnce(cutoff);
		} catch (Exception exception) {
			// Every read failure is treated alike on purpose. Mid-build the environment is expected to stop
			// answering while it reloads, and the shape of that failure (timeout, 503, a login page, a
			// disposed connection) says nothing the caller can act on differently. What matters is only that
			// the channel went down, which is recorded below and later read as the reload signature.
			// RecordFailure runs UNCONDITIONALLY, before the optional callback. Folding it into the
			// argument list of `onPollFailed?.Invoke(...)` does not evaluate it when the callback is null -
			// and the caller that passes null is compile-configuration, whose whole completion rule depends
			// on this outage being recorded.
			int consecutiveFailures = RecordFailure();
			onPollFailed?.Invoke(consecutiveFailures, exception);
			return;
		}
		foreach (CompilationHistory record in records) {
			if (record.CreatedOn > cutoff) {
				cutoff = record.CreatedOn;
			}
			if (!ShouldReport(record)) {
				continue;
			}
			onNewRecord(record);
		}
		RecordSuccess();
	}

	private bool ShouldReport(CompilationHistory record) {
		lock (_stateLock) {
			if (!_seenRecordIds.Add(record.Id)) {
				return false;
			}
			_newRecordCount++;
			_lastActivityAtUtc = _utcNow();
			if (CompilationDiagnostics.HasRealError(record.ErrorsWarnings)) {
				_hasErrors = true;
			}
			return true;
		}
	}

	// The retry policy is mutated here on the poll thread and read from the poll loop's backoff, so
	// every access goes through _stateLock. It is a plain state holder with no synchronisation of its
	// own, and its DateTime? field is not written atomically on every runtime.
	private int RecordFailure() {
		DateTime now = _utcNow();
		lock (_stateLock) {
			_retryPolicy.RecordFailure(now);
			return _retryPolicy.ConsecutiveFailures;
		}
	}

	private void RecordSuccess() {
		DateTime now = _utcNow();
		lock (_stateLock) {
			_retryPolicy.RecordSuccess(now);
		}
	}

	#endregion

}
