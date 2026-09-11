using System;
using System.Threading;

namespace Clio.Common;

/// <summary>
/// A wait that a <see cref="CancellationToken"/> can cut short. The single reason this is a service and
/// not a <c>Thread.Sleep</c> is that it is the seam a test replaces: the poll loop's timing can then be
/// exercised in full without the test spending the real durations asleep (issue #1376).
/// </summary>
public interface ICancellableDelay {

	/// <summary>
	/// Waits for <paramref name="duration"/> or until <paramref name="ct"/> is cancelled.
	/// </summary>
	/// <param name="duration">How long to wait; zero or less does not wait at all.</param>
	/// <param name="ct">The token whose cancellation ends the wait early.</param>
	/// <returns>
	/// <see langword="true"/> when the wait ended because of cancellation, <see langword="false"/> when
	/// the full duration elapsed.
	/// </returns>
	bool WaitOrCancelled(TimeSpan duration, CancellationToken ct);

}

/// <inheritdoc cref="ICancellableDelay"/>
public sealed class CancellableDelay : ICancellableDelay {

	/// <inheritdoc/>
	public bool WaitOrCancelled(TimeSpan duration, CancellationToken ct) =>
		duration <= TimeSpan.Zero ? ct.IsCancellationRequested : ct.WaitHandle.WaitOne(duration);

}
