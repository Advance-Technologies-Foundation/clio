using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClioRing.Ipc;

/// <summary>Coordinates ordinary clio processes with the exclusive Release-tool update window.</summary>
public interface IClioProcessGate {
	/// <summary>Acquires a shared lease held for the lifetime of one clio process.</summary>
	Task<IAsyncDisposable> AcquireProcessLeaseAsync(CancellationToken cancellationToken = default);

	/// <summary>Acquires an exclusive lease after all clio process leases have drained.</summary>
	Task<IAsyncDisposable> AcquireUpdateLeaseAsync(CancellationToken cancellationToken = default);
}

/// <summary>Async shared-process/exclusive-update gate with update preference.</summary>
public sealed class ClioProcessGate : IClioProcessGate {
	private readonly object _sync = new();
	private readonly SemaphoreSlim _singleUpdater = new(1, 1);
	private TaskCompletionSource<bool> _changed = NewSignal();
	private int _activeProcesses;
	private bool _updatePending;

	/// <inheritdoc />
	public async Task<IAsyncDisposable> AcquireProcessLeaseAsync(
		CancellationToken cancellationToken = default) {
		while (true) {
			Task wait;
			lock (_sync) {
				if (!_updatePending) {
					_activeProcesses++;
					return new Lease(ReleaseProcess);
				}
				wait = _changed.Task;
			}
			await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task<IAsyncDisposable> AcquireUpdateLeaseAsync(
		CancellationToken cancellationToken = default) {
		await _singleUpdater.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			while (true) {
				Task wait;
				lock (_sync) {
					_updatePending = true;
					if (_activeProcesses == 0) {
						return new Lease(ReleaseUpdate);
					}
					wait = _changed.Task;
				}
				await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch {
			lock (_sync) {
				_updatePending = false;
				SignalChanged();
			}
			_singleUpdater.Release();
			throw;
		}
	}

	private void ReleaseProcess() {
		lock (_sync) {
			_activeProcesses--;
			SignalChanged();
		}
	}

	private void ReleaseUpdate() {
		lock (_sync) {
			_updatePending = false;
			SignalChanged();
		}
		_singleUpdater.Release();
	}

	private void SignalChanged() {
		TaskCompletionSource<bool> changed = _changed;
		_changed = NewSignal();
		changed.TrySetResult(true);
	}

	private static TaskCompletionSource<bool> NewSignal() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	private sealed class Lease(Action release) : IAsyncDisposable {
		private Action? _release = release;

		public ValueTask DisposeAsync() {
			Interlocked.Exchange(ref _release, null)?.Invoke();
			return ValueTask.CompletedTask;
		}
	}
}
