using System;
using System.Diagnostics;

namespace Clio.Common;

/// <summary>
/// The time one call may take across ALL of its requests, for a caller whose answer is bounded by a deadline (an MCP
/// worker is killed at its budget, a read answer is dropped at the read deadline). A request's own timeout bounds that
/// one request only; a call that makes several requests in sequence needs this to keep the whole answer in time.
/// Applied through <see cref="CreatioRequestOptions.ForNextRequest"/>.
/// </summary>
public sealed class RequestDeadline {

	private readonly Func<TimeSpan> _elapsed;

	/// <summary>Starts a deadline of <paramref name="budget"/> from now.</summary>
	/// <param name="budget">The time the whole call may take.</param>
	public RequestDeadline(TimeSpan budget) : this(budget, Stopwatch.StartNew()) { }

	private RequestDeadline(TimeSpan budget, Stopwatch stopwatch) : this(budget, () => stopwatch.Elapsed) { }

	/// <summary>A deadline whose elapsed time is read from <paramref name="elapsed"/>; for tests.</summary>
	/// <param name="budget">The time the whole call may take.</param>
	/// <param name="elapsed">Returns the time spent so far.</param>
	internal RequestDeadline(TimeSpan budget, Func<TimeSpan> elapsed) {
		ArgumentNullException.ThrowIfNull(elapsed);
		Budget = budget;
		_elapsed = elapsed;
	}

	/// <summary>The time the whole call may take.</summary>
	public TimeSpan Budget { get; }

	/// <summary>The time left; never negative.</summary>
	public TimeSpan Remaining {
		get {
			TimeSpan remaining = Budget - _elapsed();
			return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
		}
	}

	/// <summary>Whether no time is left.</summary>
	public bool IsSpent => Remaining == TimeSpan.Zero;
}
