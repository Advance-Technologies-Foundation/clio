using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Generic bounded read-back poll: probes repeatedly until a caller-supplied condition is satisfied, or
/// a fixed attempt count is exhausted, whichever comes first.
/// </summary>
/// <remarks>
/// Extracted so the short read-after-write poll in
/// <c>PageSyncToolE2ETests.PageSyncTool_Should_Make_Completed_Write_Immediately_Observable</c> does not
/// duplicate the attempt-count/poll-interval shape <c>ApplicationToolE2ETests</c> already uses for its
/// <c>CanonicalMainEntityReadbackAttempts</c> readback poll — both are "probe, check, wait a fixed
/// interval, repeat" loops, just with different budgets for different eventual-consistency windows.
/// This helper does not classify errors the way <see cref="TransientPlatformConditionRetryGate"/> does:
/// it has no notion of "known transient condition", it simply keeps probing until the caller's own
/// <c>isSatisfied</c> predicate says the awaited state has been observed.
/// </remarks>
internal static class BoundedPollGate {
	/// <summary>
	/// Probes <paramref name="probeAsync"/> up to <paramref name="maxAttempts"/> times, waiting
	/// <paramref name="pollInterval"/> between attempts, and returns as soon as
	/// <paramref name="isSatisfied"/> accepts a probe result. If the budget is exhausted first, the last
	/// probe result is returned so the caller's own assertions decide pass/fail (and can report the
	/// budget that was not enough).
	/// </summary>
	/// <typeparam name="TResult">The type returned by one probe.</typeparam>
	/// <param name="probeAsync">Makes one probe attempt.</param>
	/// <param name="isSatisfied">Decides whether a probe result already proves what the caller is waiting for.</param>
	/// <param name="maxAttempts">Maximum number of probe attempts, at least 1.</param>
	/// <param name="pollInterval">Delay between attempts.</param>
	/// <param name="cancellationToken">Cancels the whole poll.</param>
	/// <returns>The probe result that satisfied <paramref name="isSatisfied"/>, or the last probe result observed.</returns>
	internal static async Task<TResult> PollUntilAsync<TResult>(
		Func<CancellationToken, Task<TResult>> probeAsync,
		Func<TResult, bool> isSatisfied,
		int maxAttempts,
		TimeSpan pollInterval,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(probeAsync);
		ArgumentNullException.ThrowIfNull(isSatisfied);
		if (maxAttempts < 1) {
			throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");
		}

		TResult last = await probeAsync(cancellationToken);
		for (int attempt = 1; attempt < maxAttempts; attempt++) {
			if (isSatisfied(last)) {
				return last;
			}

			cancellationToken.ThrowIfCancellationRequested();
			await Task.Delay(pollInterval, cancellationToken);
			last = await probeAsync(cancellationToken);
		}

		return last;
	}
}
