using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// Runs the knowledge autoupdate schedule inside a long-running MCP host.
/// </summary>
/// <remarks>
/// <para>
/// clio already updates knowledge on a schedule — <c>autoupdate.knowledge</c>, enabled by default —
/// but that check runs at the start of an ordinary CLI command and is deliberately skipped for the MCP
/// verbs, and a warm MCP start activates its cached generation without contacting the publisher. An
/// installation used only through MCP therefore never refreshed at all (ENG-99899). This loop closes
/// that hole with the SAME schedule rather than a second policy: the host asks
/// <see cref="ISettingsRepository.TryScheduleAutoupdate"/> whether the knowledge update is due, so an
/// operator's <c>enabled</c> and <c>frequency-minutes</c> settings govern it exactly as they govern the
/// CLI path, and the persisted <c>next-run</c> keeps concurrent clio processes from each running their
/// own update.
/// </para>
/// <para>
/// A failed attempt is bounded by the SCHEDULE, not by the wake-up: <c>TryScheduleAutoupdate</c>
/// advances and persists <c>next-run</c> as part of answering true, and nothing rolls it back when the
/// update that follows fails, so the retry lands on the next scheduled window — about an hour on the
/// defaults, not five minutes. Because the 3-day staleness warning is emitted once, at startup, a host
/// that has been resident for weeks has no other signal, so a run of
/// <see cref="CuratedKnowledgeSourceDefaults.BackgroundRefreshFailuresBeforeWarning"/> consecutive
/// failures is reported as a warning here.
/// </para>
/// </remarks>
public interface ICuratedKnowledgeBackgroundRefresh {
	/// <summary>
	/// Starts the refresh loop on a background thread and returns immediately.
	/// </summary>
	/// <param name="cancellationToken">Ends the loop; the host cancels it at shutdown.</param>
	/// <returns>
	/// The task running the loop, which completes when the token is cancelled. The host does not await
	/// it; it is returned so a test can observe the loop ending instead of polling for it.
	/// </returns>
	Task Start(CancellationToken cancellationToken);
}

internal sealed class CuratedKnowledgeBackgroundRefresh(
	ISettingsRepository settingsRepository,
	IKnowledgeSourceManagementService sourceManagementService,
	ICancellableDelay delay,
	TimeProvider timeProvider,
	ILogger logger) : ICuratedKnowledgeBackgroundRefresh {
	private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(
		CuratedKnowledgeSourceDefaults.BackgroundRefreshStartDelaySeconds);
	private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(
		CuratedKnowledgeSourceDefaults.BackgroundRefreshPollIntervalMinutes);

	/// <summary>Consecutive failed refresh attempts; reset by the first success.</summary>
	private int _consecutiveFailures;

	public Task Start(CancellationToken cancellationToken) =>
		// CancellationToken.None on purpose: the token ends the LOOP from the inside, through the
		// delay and the checks below, so the returned task completes rather than faulting with a
		// cancellation nobody observes.
		// LongRunning on purpose too: the wait below BLOCKS its thread for the whole host lifetime, so
		// running it on the thread pool would hold a pool thread permanently and starve MCP work on a
		// host with few cores.
		Task.Factory.StartNew(
			() => Run(cancellationToken),
			CancellationToken.None,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Default);

	private void Run(CancellationToken cancellationToken) {
		try {
			// The first wait is what keeps this off the startup path: the transport is already serving
			// by the time the first publisher call can happen.
			if (delay.WaitOrCancelled(StartDelay, cancellationToken)) {
				return;
			}
			while (!cancellationToken.IsCancellationRequested) {
				RefreshOnce(cancellationToken);
				if (delay.WaitOrCancelled(PollInterval, cancellationToken)) {
					return;
				}
			}
		} catch (OperationCanceledException) {
			// Shutdown reached the wait itself; the loop ends the same way it does on a cancelled wait.
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			// The host discards this task, so a fault here would surface only as an unobserved exception
			// at collection time. Ending the loop quietly keeps the cached generation serving.
			logger.WriteDebug(
				"Background knowledge refresh stopped: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
		}
	}

	/// <summary>
	/// Runs the knowledge update when the shared autoupdate schedule says it is due.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The whole gate is <see cref="ISettingsRepository.TryScheduleAutoupdate"/>: it answers false for a
	/// disabled policy and for one whose <c>next-run</c> has not arrived, and it advances and persists
	/// <c>next-run</c> as part of answering true. Deciding anything here in addition would be a second
	/// policy an operator cannot see or configure.
	/// </para>
	/// <para>
	/// Non-throwing by construction: this runs unattended beside a serving transport, so a settings or
	/// transport fault must leave the cached generation serving and try again rather than tear anything
	/// down. Note that <see cref="IKnowledgeSourceManagementService.Update"/> reports a transport failure
	/// by RETURNING <c>Success: false</c>; it does not throw, so both shapes are counted as failures here.
	/// A single failure stays a debug line — the next scheduled window retries it — but a run of
	/// <see cref="CuratedKnowledgeSourceDefaults.BackgroundRefreshFailuresBeforeWarning"/> is warned once,
	/// because the schedule slot the failed attempt consumed is gone and the startup staleness warning
	/// cannot fire again inside a process that has been serving for weeks.
	/// </para>
	/// </remarks>
	/// <param name="cancellationToken">Stops the update operation at shutdown.</param>
	private void RefreshOnce(CancellationToken cancellationToken) {
		try {
			if (!settingsRepository.TryScheduleAutoupdate(
					AutoUpdateTarget.Knowledge, timeProvider.GetUtcNow())) {
				return;
			}
			// sourceAlias null on purpose: every enabled source, exactly what the CLI autoupdate path
			// updates. A host that refreshed only the built-in source would leave a partner or customer
			// library behind and give the same setting two meanings.
			KnowledgeSourceBatchResult result = sourceManagementService.Update(
				sourceAlias: null, cancellationToken);
			logger.WriteDebug($"Background knowledge refresh: {result.Message}");
			if (result.Success) {
				_consecutiveFailures = 0;
				return;
			}
			RecordFailure(result.Message);
		} catch (OperationCanceledException) {
			// Shutdown during the update; the cached generation keeps serving until the process ends.
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			string reason = SensitiveErrorTextRedactor.Redact(exception.Message);
			logger.WriteDebug("Background knowledge refresh did not run: " + reason);
			RecordFailure(reason);
		}
	}

	/// <summary>
	/// Counts a failed attempt and warns once when the run of failures stops looking transient.
	/// </summary>
	/// <remarks>
	/// The warning is emitted on exactly the Nth consecutive failure rather than on every one after it:
	/// a host that stays cut off for weeks is one operator-visible line, not one per wake-up, which is
	/// what kept the original implementation silent. A success resets the run, so a later outage is
	/// reported again.
	/// </remarks>
	/// <param name="reason">The failure text, already redacted for logging.</param>
	private void RecordFailure(string reason) {
		_consecutiveFailures++;
		if (_consecutiveFailures != CuratedKnowledgeSourceDefaults.BackgroundRefreshFailuresBeforeWarning) {
			return;
		}
		logger.WriteWarning(
			$"Knowledge autoupdate has failed {_consecutiveFailures} times in a row and the cached "
			+ "generation is no longer being refreshed; the next attempt is the next scheduled window "
			+ "(autoupdate.knowledge frequency-minutes, 60 by default). Last failure: "
			+ reason);
	}
}
