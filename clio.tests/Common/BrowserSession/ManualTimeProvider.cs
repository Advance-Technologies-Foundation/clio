using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// A clock that moves only when the test moves it, and fires the timers it passed - CancellationTokenSource timers,
/// Task.Delay and Task.WaitAsync on a TimeProvider included.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider {
	private readonly object _gate = new();
	private readonly List<ManualTimer> _timers = [];
	private DateTimeOffset _now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow() {
		lock (_gate) {
			return _now;
		}
	}

	public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period) {
		ManualTimer timer = new(this, callback, state);
		timer.Change(dueTime, period);
		lock (_gate) {
			_timers.Add(timer);
		}
		return timer;
	}

	public void Advance(TimeSpan delta) {
		List<ManualTimer> due;
		lock (_gate) {
			_now += delta;
			due = _timers.Where(timer => timer.DueAt <= _now).ToList();
			foreach (ManualTimer timer in due) {
				timer.DueAt = null;
			}
		}
		foreach (ManualTimer timer in due) {
			timer.Fire();
		}
	}

	private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object state) : ITimer {
		public DateTimeOffset? DueAt { get; set; }

		public void Fire() => callback(state);

		public bool Change(TimeSpan dueTime, TimeSpan period) {
			lock (owner._gate) {
				DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
			}
			return true;
		}

		public void Dispose() {
			lock (owner._gate) {
				DueAt = null;
				owner._timers.Remove(this);
			}
		}

		public ValueTask DisposeAsync() {
			Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
