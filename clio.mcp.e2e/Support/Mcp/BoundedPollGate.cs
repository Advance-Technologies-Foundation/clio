using System;
using System.Runtime.ExceptionServices;
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
	/// <param name="isTransientProbeFailure">
	/// Optional. When <paramref name="probeAsync"/> THROWS, this decides whether the exception is itself a
	/// "not ready yet" signal rather than an abort — for example an unparsable envelope while a mid-settle
	/// server answers with a body the caller's structured-result parser cannot parse yet, the same shape
	/// <c>ApplicationToolE2ETests.WaitForCanonicalMainEntityAsync</c> already treats as "keep polling" by
	/// catching <see cref="InvalidOperationException"/> around its own probe. When this returns
	/// <see langword="true"/> for a thrown exception, the poll records it as the last observed failure and
	/// tries again instead of letting the exception abort the whole poll. Defaults to <see langword="null"/>,
	/// which preserves the original behaviour: any exception from <paramref name="probeAsync"/> propagates
	/// immediately, uncaught.
	/// </param>
	/// <returns>The probe result that satisfied <paramref name="isSatisfied"/>, or the last probe result observed.</returns>
	/// <exception cref="Exception">
	/// Rethrown when the attempt budget is exhausted and every single attempt failed with an exception
	/// matched by <paramref name="isTransientProbeFailure"/> — nothing is swallowed; the last observed
	/// failure is surfaced instead of a synthetic "not satisfied" result with no probe result to show.
	/// </exception>
	internal static async Task<TResult> PollUntilAsync<TResult>(
		Func<CancellationToken, Task<TResult>> probeAsync,
		Func<TResult, bool> isSatisfied,
		int maxAttempts,
		TimeSpan pollInterval,
		CancellationToken cancellationToken,
		Func<Exception, bool>? isTransientProbeFailure = null) {
		ArgumentNullException.ThrowIfNull(probeAsync);
		ArgumentNullException.ThrowIfNull(isSatisfied);
		if (maxAttempts < 1) {
			throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");
		}

		bool hasResult = false;
		TResult last = default!;
		Exception? lastProbeFailure = null;

		for (int attempt = 1; attempt <= maxAttempts; attempt++) {
			if (attempt > 1) {
				cancellationToken.ThrowIfCancellationRequested();
				await Task.Delay(pollInterval, cancellationToken);
			}

			try {
				last = await probeAsync(cancellationToken);
				hasResult = true;
				lastProbeFailure = null;
			}
			catch (Exception probeException) when (isTransientProbeFailure is not null && isTransientProbeFailure(probeException)) {
				// A matching probe failure is a "not ready yet" signal, not an abort: remember it so it
				// can be surfaced (or rethrown below) instead of silently discarding it, then keep polling.
				lastProbeFailure = probeException;
				continue;
			}

			if (hasResult && isSatisfied(last)) {
				return last;
			}
		}

		if (!hasResult && lastProbeFailure is not null) {
			// The whole budget was exhausted with nothing but transient-looking probe failures and never
			// a single successful probe: rethrow the last one so it is surfaced as the real diagnostic,
			// rather than silently reporting "not satisfied" with no result to show for it.
			ExceptionDispatchInfo.Capture(lastProbeFailure).Throw();
		}

		return last;
	}
}
