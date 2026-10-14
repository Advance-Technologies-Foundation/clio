using System;

namespace Clio.Common;

/// <summary>
/// Common-layer carrier for the request timeout and retry settings used when calling native Creatio
/// services. A Common analog of the request-related part of <c>RemoteCommandOptions</c> so Common-layer
/// service clients do not depend on the Command layer. Defaults mirror <c>RemoteCommandOptions</c>.
/// </summary>
public sealed record CreatioRequestOptions
{
	/// <summary>Request timeout in milliseconds.</summary>
	public int TimeOut { get; init; } = 100_000;

	/// <summary>Maximum number of attempts.</summary>
	public int MaxAttempts { get; init; } = 3;

	/// <summary>Delay between attempts in seconds.</summary>
	public int RetryDelay { get; init; } = 1;

	/// <summary>
	/// The deadline every request of one call shares, or <see langword="null"/> when each request is bounded by
	/// <see cref="TimeOut"/> alone. Honoured by <see cref="ForNextRequest"/>, which
	/// <see cref="CreatioServiceClient"/> applies to every request it sends.
	/// </summary>
	public RequestDeadline Deadline { get; init; }

	/// <summary>
	/// The options for the next request: its timeout cut to what is left of <see cref="Deadline"/>, so the request
	/// cannot outlast the call. Without a deadline, these options as they are.
	/// </summary>
	/// <returns>The options for the next request.</returns>
	/// <exception cref="TimeoutException">The deadline is spent, so the request must not be sent.</exception>
	public CreatioRequestOptions ForNextRequest() {
		if (Deadline is null) {
			return this;
		}
		TimeSpan remaining = Deadline.Remaining;
		if (remaining == TimeSpan.Zero) {
			throw new TimeoutException(
				$"The call's time limit of {Deadline.Budget.TotalSeconds:0} s is spent; the request was not sent.");
		}
		int remainingMilliseconds = (int)Math.Ceiling(Math.Min(remaining.TotalMilliseconds, int.MaxValue));
		return this with { TimeOut = Math.Min(TimeOut, remainingMilliseconds) };
	}
}
