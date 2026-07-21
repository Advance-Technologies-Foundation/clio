using System;
using System.Threading;

namespace Clio.Common;

/// <summary>
/// Synchronous backoff delay abstraction for retry loops that run inside a lock (where <c>await</c> is
/// illegal). Injecting it lets tests substitute a zero-delay implementation so retry logic runs
/// instantly.
/// </summary>
public interface IRetryDelay {

	/// <summary>
	/// Blocks the current thread for the given duration before the next retry attempt.
	/// </summary>
	/// <param name="delay">The amount of time to wait. A non-positive value returns immediately.</param>
	void Wait(TimeSpan delay);
}

/// <summary>
/// Default <see cref="IRetryDelay"/> that blocks the calling thread via <see cref="Thread.Sleep(TimeSpan)"/>.
/// </summary>
public sealed class ThreadSleepRetryDelay : IRetryDelay {

	/// <summary>
	/// Shared process-wide instance. The type is stateless, so a single instance is safe to reuse and
	/// can be referenced without constructing a behavior class at call sites.
	/// </summary>
	public static readonly ThreadSleepRetryDelay Shared = new();

	/// <inheritdoc />
	public void Wait(TimeSpan delay) {
		if (delay > TimeSpan.Zero) {
			Thread.Sleep(delay);
		}
	}
}
