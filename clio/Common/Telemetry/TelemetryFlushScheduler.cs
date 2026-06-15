using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clio.Common.Telemetry;

/// <summary>
/// Schedules opportunistic background telemetry flushes with single-flight semantics.
/// </summary>
public interface ITelemetryFlushScheduler
{
	/// <summary>
	/// Starts a fire-and-forget background flush; no-ops when a flush is already running.
	/// Never throws to the caller.
	/// </summary>
	void TryScheduleFlush();

	/// <summary>
	/// Awaits the in-flight flush (if any) up to the timeout. Called on MCP server exit so the
	/// fire-and-forget task is not killed by the runtime while an upload is in progress.
	/// </summary>
	Task DrainAsync(TimeSpan timeout);
}

/// <inheritdoc />
public sealed class TelemetryFlushScheduler : ITelemetryFlushScheduler
{
	/// <summary>
	/// Hard cap for a single background flush run.
	/// </summary>
	internal static readonly TimeSpan FlushRunTimeout = TimeSpan.FromMinutes(2);

	private readonly ITelemetryFlushService _flushService;
	private readonly ILogger<TelemetryFlushScheduler> _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private volatile Task _pending = Task.CompletedTask;

	/// <summary>
	/// Initializes a new instance of the <see cref="TelemetryFlushScheduler"/> class.
	/// </summary>
	public TelemetryFlushScheduler(ITelemetryFlushService flushService,
		ILogger<TelemetryFlushScheduler> logger = null)
	{
		_flushService = flushService ?? throw new ArgumentNullException(nameof(flushService));
		_logger = logger ?? NullLogger<TelemetryFlushScheduler>.Instance;
	}

	/// <inheritdoc />
	public void TryScheduleFlush()
	{
		// Acquire the gate before Task.Run so a skipped call never overwrites the tracked task.
		if (!_gate.Wait(0)) {
			return;
		}
		_pending = Task.Run(async () => {
			try {
				using CancellationTokenSource cts = new(FlushRunTimeout);
				await _flushService.FlushAsync(cts.Token).ConfigureAwait(false);
			} catch (Exception ex) {
				// Background telemetry work must never surface failures.
				_logger.LogDebug(ex, "telemetry-flush background run failed error={Error}", ex.Message);
			} finally {
				_gate.Release();
			}
		});
	}

	/// <inheritdoc />
	public async Task DrainAsync(TimeSpan timeout)
	{
		try {
			await _pending.WaitAsync(timeout).ConfigureAwait(false);
		} catch (Exception) {
			// Best effort: drain never blocks shutdown beyond the timeout or rethrows.
		}
	}
}
