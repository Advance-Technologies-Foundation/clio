using System;

namespace Clio.Common;

/// <summary>
/// Tracks the health of a repeatedly-polled channel and the backoff delay to apply between
/// attempts, independent of what the polled data itself says.
/// </summary>
public interface IPollRetryPolicy {

	/// <summary>
	/// Call after a poll attempt that completed without throwing.
	/// </summary>
	void RecordSuccess(DateTime now);

	/// <summary>
	/// Call after a poll attempt that threw (network fault, IIS recycle mid-compile, etc.).
	/// </summary>
	void RecordFailure(DateTime now);

	/// <summary>
	/// Delay to wait before the next attempt, given the current consecutive-failure count.
	/// </summary>
	TimeSpan NextDelay { get; }

	int ConsecutiveFailures { get; }

	/// <summary>
	/// True only once at least one poll has succeeded and that success is still recent enough
	/// to trust — i.e. the channel is not merely "hasn't reported new data" but demonstrably alive.
	/// </summary>
	bool IsChannelHealthy(DateTime now);

}

public class PollRetryPolicy : IPollRetryPolicy {

	#region Constants

	internal static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
	internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

	// A channel that hasn't produced a single successful poll within this long is no longer
	// "maybe still compiling" - treat it as unreachable, so settle-detection based on data read
	// before the outage started is never trusted while the channel is in this state.
	internal static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

	#endregion

	#region Fields: Private

	private DateTime? _lastSuccessAt;
	private int _consecutiveFailures;

	#endregion

	#region Methods: Public

	public void RecordSuccess(DateTime now){
		_consecutiveFailures = 0;
		_lastSuccessAt = now;
	}

	public void RecordFailure(DateTime now){
		_consecutiveFailures++;
	}

	public TimeSpan NextDelay {
		get {
			double factor = Math.Pow(2, Math.Max(0, _consecutiveFailures - 1));
			double delayMs = Math.Min(MaxDelay.TotalMilliseconds, BaseDelay.TotalMilliseconds * factor);
			return TimeSpan.FromMilliseconds(delayMs);
		}
	}

	public int ConsecutiveFailures => _consecutiveFailures;

	public bool IsChannelHealthy(DateTime now) =>
		_lastSuccessAt is not null && now - _lastSuccessAt.Value <= StaleAfter;

	#endregion

}
