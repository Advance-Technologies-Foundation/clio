using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Thread-safe <see cref="System.IProgress{T}"/> sink that records the message of every
/// <c>notifications/progress</c> the SDK delivers, so a test can assert the stage-marker text.
/// Shared across the MCP E2E tests that verify progress streaming (ENG-93087). Distinct from the
/// counting sink used by the keep-alive tests, which only tallies notification volume.
/// </summary>
internal sealed class MessageCollectingProgress : System.IProgress<ProgressNotificationValue> {
	private readonly List<string> _messages = new();
	private readonly object _gate = new();
	private TaskCompletionSource<bool> _messageObserved = CreateMessageObservedSignal();

	/// <summary>Snapshot of the messages observed so far, in delivery order.</summary>
	public IReadOnlyList<string> Messages {
		get {
			lock (_gate) {
				return _messages.ToArray();
			}
		}
	}

	/// <summary>Number of progress notifications observed so far.</summary>
	public int Count {
		get {
			lock (_gate) {
				return _messages.Count;
			}
		}
	}

	/// <inheritdoc />
	public void Report(ProgressNotificationValue value) {
		TaskCompletionSource<bool> signal;
		lock (_gate) {
			_messages.Add(value.Message ?? string.Empty);
			signal = _messageObserved;
			_messageObserved = CreateMessageObservedSignal();
		}

		signal.TrySetResult(true);
	}

	/// <summary>
	/// Waits for the collected messages to satisfy <paramref name="condition"/>, returning the snapshot
	/// that satisfied it. A completed <c>tools/call</c> does NOT guarantee the sink has observed every
	/// notification the server already sent: tool completion and notification dispatch use independent
	/// SDK continuations, so asserting on <see cref="Messages"/> the instant the call returns races the
	/// delivery of the last markers. This is the typed-sink counterpart of
	/// <c>McpServerSession.WaitForCapturedProgressAsync</c>.
	/// </summary>
	/// <param name="condition">Predicate over the message snapshot, evaluated on every new notification.</param>
	/// <param name="timeout">Bound on the wait; a final snapshot is evaluated at the boundary before failing.</param>
	/// <param name="cancellationToken">Cancels the wait.</param>
	/// <returns>The snapshot that satisfied <paramref name="condition"/>.</returns>
	/// <exception cref="TimeoutException">
	/// Thrown when <paramref name="condition"/> is still unsatisfied after <paramref name="timeout"/>,
	/// carrying every message observed so far so the failure names what actually arrived.
	/// </exception>
	public async Task<IReadOnlyList<string>> WaitForMessagesAsync(
		Func<IReadOnlyList<string>, bool> condition,
		TimeSpan timeout,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(condition);
		if (timeout <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Progress wait timeout must be positive.");
		}

		Stopwatch stopwatch = Stopwatch.StartNew();
		while (true) {
			cancellationToken.ThrowIfCancellationRequested();
			// Capture the signal BEFORE snapshotting, so a notification arriving between the snapshot and
			// the await is not lost: it completes the captured signal and the loop re-evaluates at once.
			Task messageObserved = GetMessageObservedSignalTask();
			IReadOnlyList<string> snapshot = Messages;
			if (condition(snapshot)) {
				return snapshot;
			}

			TimeSpan remaining = timeout - stopwatch.Elapsed;
			try {
				if (remaining <= TimeSpan.Zero) {
					throw new TimeoutException();
				}

				await messageObserved.WaitAsync(remaining, cancellationToken);
			}
			catch (TimeoutException) {
				// Re-check at the boundary before failing, so the helper itself cannot report a flake for a
				// notification that landed while the wait was expiring.
				IReadOnlyList<string> finalSnapshot = Messages;
				if (condition(finalSnapshot)) {
					return finalSnapshot;
				}

				throw new TimeoutException(BuildTimeoutMessage(timeout, finalSnapshot));
			}
		}
	}

	/// <summary>
	/// Waits until at least <paramref name="minimumCount"/> progress notifications have been observed.
	/// Convenience wrapper over <see cref="WaitForMessagesAsync"/> for the keep-alive assertions, which
	/// care about notification volume rather than marker text.
	/// </summary>
	public Task<IReadOnlyList<string>> WaitForCountAsync(
		int minimumCount,
		TimeSpan timeout,
		CancellationToken cancellationToken) {
		return WaitForMessagesAsync(messages => messages.Count >= minimumCount, timeout, cancellationToken);
	}

	private Task GetMessageObservedSignalTask() {
		lock (_gate) {
			return _messageObserved.Task;
		}
	}

	private static string BuildTimeoutMessage(TimeSpan timeout, IReadOnlyList<string> observed) {
		string rendered = observed.Count == 0
			? "<none>"
			: string.Join(Environment.NewLine, observed.Select((message, index) => $"  [{index}] {message}"));
		return $"No progress notification satisfying the expected condition arrived within {timeout}. "
			+ $"Observed {observed.Count} progress notification(s):{Environment.NewLine}{rendered}";
	}

	private static TaskCompletionSource<bool> CreateMessageObservedSignal() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);
}
