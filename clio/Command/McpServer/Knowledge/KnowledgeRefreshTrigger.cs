using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// Refreshes installed knowledge from the publisher when guidance is read and the cached generation
/// has gone unverified for longer than the operator's autoupdate schedule allows.
/// </summary>
/// <remarks>
/// <para>
/// clio already updates knowledge on a schedule — <c>autoupdate.knowledge</c>, enabled by default —
/// but that check runs at the start of an ordinary CLI command and is deliberately skipped for the MCP
/// verbs, and a warm MCP start activates its cached generation without contacting the publisher. An
/// installation used only through MCP therefore never refreshed at all (ENG-99899).
/// </para>
/// <para>
/// The trigger is a READ, exactly as it is for the component registry that <c>get-component-info</c>
/// serves: a guidance lookup returns the currently active generation immediately and, when the cache is
/// due for verification, starts the refresh beside the answer rather than in front of it
/// (stale-while-revalidate). Nothing is polled, so a host nobody asks for guidance costs nothing, and
/// no request ever waits on the publisher.
/// </para>
/// <para>
/// Whether a refresh may happen at all is <see cref="ISettingsRepository.TryScheduleAutoupdate"/>'s
/// decision, not this class's: it answers false for a disabled policy and for one whose
/// <c>next-run</c> has not arrived, and advances and persists <c>next-run</c> as part of answering
/// true. So the operator's <c>enabled</c> flag and <c>frequency-minutes</c> govern this path exactly as
/// they govern the CLI path, and the persisted <c>next-run</c> keeps concurrent clio processes from each
/// running their own update. The probe interval below is only how often this process is willing to ASK
/// that question; it is not a second schedule.
/// </para>
/// <para>
/// Two gates sit ABOVE the schedule, and both exist because the guidance read surface serves the whole
/// PROCESS, not only an MCP host. A refresh runs only in a long-lived host —
/// <see cref="McpHostTransportKind.Stdio"/> or <see cref="McpHostTransportKind.Http"/>, and never in a
/// worker, which serves one call — and only while the <c>CLIO_NO_UPDATE_CHECK</c> opt-out is unset.
/// Anywhere else the stale-while-revalidate premise fails: a short-lived process (<c>clio config</c>
/// reads the <c>knowledge-feedback</c> article, so an ordinary CLI verb reaches this too) abandons the
/// refresh when it exits, yet <c>TryScheduleAutoupdate</c> has already advanced and persisted
/// <c>next-run</c> in order to allow it — so the abandoned attempt CONSUMES the window and pushes the
/// next one, CLI or host, a whole <c>frequency-minutes</c> out. That would make this change defeat
/// itself.
/// </para>
/// <para>
/// The refresh itself is cheap in the overwhelmingly common case. An artifact transport compares the
/// published revision against the active one and stops at <c>NoCandidate</c> — one conditional metadata
/// request, no download, no signature check, no generation switch — and that outcome is what records
/// the publisher-check stamp. The bundle is fetched only when the publisher really moved.
/// </para>
/// </remarks>
internal interface IKnowledgeRefreshTrigger {
	/// <summary>
	/// Starts a publisher refresh when this process is eligible and willing to ask the schedule again.
	/// </summary>
	/// <remarks>
	/// Safe to call on every guidance read: the common path is one clock comparison and one interlocked
	/// read, and it never blocks, throws, or contacts anything itself.
	/// </remarks>
	/// <returns>
	/// The refresh task when one was started, or <see langword="null"/> when nothing was due. Callers on
	/// the read path ignore it; it is returned so a test can await the refresh instead of polling for it,
	/// and can tell "no refresh was started" from "a refresh decided against updating".
	/// </returns>
	Task? TriggerIfDue();
}

/// <inheritdoc cref="IKnowledgeRefreshTrigger"/>
internal sealed class KnowledgeRefreshTrigger : IKnowledgeRefreshTrigger {
	/// <summary>
	/// How often this process is willing to consult the autoupdate schedule.
	/// </summary>
	/// <remarks>
	/// A damper, not a cadence. Guidance is read many times a minute, and asking the schedule reads —
	/// and, when it fires, writes — <c>appsettings.json</c> under a lock, which must not happen per
	/// lookup. Five minutes keeps the delay this adds on top of the operator's own
	/// <c>frequency-minutes</c> small even when they shorten it.
	/// </remarks>
	private const int ProbeIntervalMinutes = 5;

	private static readonly TimeSpan ProbeInterval = TimeSpan.FromMinutes(ProbeIntervalMinutes);

	private readonly ISettingsRepository _settingsRepository;
	private readonly IKnowledgeSourceManagementService _sourceManagementService;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Func<McpHostTransportKind> _hostTransportReader;
	private readonly Func<bool> _isWorkerProcessReader;
	private readonly Func<bool> _updateCheckSuppressedReader;

	// Ticks rather than a DateTimeOffset so the damper stamp is a single atomic value: TriggerIfDue runs
	// on arbitrary concurrent request threads, and a multi-field struct read without a barrier can be
	// observed torn. Both pieces of the damper state are then guarded the same way, so the clock-jump
	// branch can be edited later without re-deriving why an unsynchronised field was safe.
	private long _nextProbeAtUtcTicks;
	private int _probeInFlight;
	private int _failureReported;

	/// <summary>
	/// Initializes a new instance reading the ambient process state: the transport the host entry point
	/// declared, clio's worker-mode flag, and the update-check opt-out variable.
	/// </summary>
	public KnowledgeRefreshTrigger(
		ISettingsRepository settingsRepository,
		IKnowledgeSourceManagementService sourceManagementService,
		TimeProvider timeProvider,
		ILogger logger)
		: this(settingsRepository, sourceManagementService, timeProvider, logger,
			() => McpHostTransport.Current,
			() => McpWorkerEnvironment.IsWorkerProcess,
			UpdateCheckOptOut.IsSuppressed) {
	}

	/// <summary>
	/// Initializes a new instance over explicit readers, so a test can state a host transport, a
	/// worker-mode flag and an opt-out without mutating process-wide state that would leak across a
	/// parallel fixture. Mirrors <see cref="McpWorkerPathGate"/>, which answers the same class of
	/// question about this process through the same seam.
	/// </summary>
	internal KnowledgeRefreshTrigger(
		ISettingsRepository settingsRepository,
		IKnowledgeSourceManagementService sourceManagementService,
		TimeProvider timeProvider,
		ILogger logger,
		Func<McpHostTransportKind> hostTransportReader,
		Func<bool> isWorkerProcessReader,
		Func<bool> updateCheckSuppressedReader) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_sourceManagementService = sourceManagementService
			?? throw new ArgumentNullException(nameof(sourceManagementService));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_hostTransportReader = hostTransportReader ?? throw new ArgumentNullException(nameof(hostTransportReader));
		_isWorkerProcessReader = isWorkerProcessReader
			?? throw new ArgumentNullException(nameof(isWorkerProcessReader));
		_updateCheckSuppressedReader = updateCheckSuppressedReader
			?? throw new ArgumentNullException(nameof(updateCheckSuppressedReader));
	}

	/// <inheritdoc/>
	public Task? TriggerIfDue() {
		if (!IsEligibleProcess()) {
			return null;
		}
		DateTimeOffset now = _timeProvider.GetUtcNow();
		long nextProbeAtUtcTicks = Volatile.Read(ref _nextProbeAtUtcTicks);
		// A stamp further out than one interval can only come from a clock that jumped forward and back;
		// treating it as due beats suppressing every refresh until the clock catches up.
		if (now.UtcTicks < nextProbeAtUtcTicks
				&& nextProbeAtUtcTicks - now.UtcTicks <= ProbeInterval.Ticks) {
			return null;
		}
		if (Interlocked.CompareExchange(ref _probeInFlight, 1, 0) != 0) {
			return null;
		}
		// Set BEFORE the refresh starts, not in its continuation: a refresh that completes in
		// milliseconds would otherwise leave the next lookup free to start another one immediately.
		Volatile.Write(ref _nextProbeAtUtcTicks, (now + ProbeInterval).UtcTicks);
		return Task.Run(Refresh);
	}

	/// <summary>
	/// Whether this process may contact the publisher unattended at all.
	/// </summary>
	/// <remarks>
	/// Fail-closed on the transport, matching <see cref="McpHostTransportKind.Unknown"/>'s documented
	/// intent: an ordinary CLI verb that reaches the guidance source, a hand-built test container, or a
	/// future host that forgot to declare its transport all read as "not a host" and start nothing. Only
	/// a long-lived host outlives the refresh it starts.
	/// </remarks>
	private bool IsEligibleProcess() {
		if (_isWorkerProcessReader()) {
			// A worker serves one call and exits, so it abandons the refresh exactly as a CLI verb does.
			return false;
		}
		McpHostTransportKind transport = _hostTransportReader();
		if (transport != McpHostTransportKind.Stdio && transport != McpHostTransportKind.Http) {
			return false;
		}
		return !_updateCheckSuppressedReader();
	}

	/// <summary>
	/// Asks the autoupdate schedule whether a knowledge update is due and runs it when it is.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Runs off the read path on purpose. <see cref="ISettingsRepository.TryScheduleAutoupdate"/> takes
	/// the settings write lock, which waits up to thirty seconds on a contended
	/// <c>appsettings.json</c> — tolerable in a background refresh, never in a guidance answer.
	/// </para>
	/// <para>
	/// Non-throwing by construction: this runs unattended beside a serving transport, so a settings or
	/// transport fault must leave the active generation serving and let the next scheduled window try
	/// again.
	/// </para>
	/// <para>
	/// The FIRST failure of any kind is a warning, every later one a debug line. An operator with no
	/// network must not collect one warning per window for weeks, but a host that never refreshes needs
	/// at least one observable signal — the startup staleness warning fires once, before the failures,
	/// and <see cref="ILogger.WriteDebug"/> is a no-op without <c>--debug</c>, which a service task does
	/// not have. A refused candidate arrives here as <c>Success=false</c>, not as an exception, so the
	/// per-source diagnostics travel with that warning: they are the part that says WHICH source failed
	/// and why, and a rejected signature reads differently from an unreachable publisher. A settings
	/// A settings-write refusal (<see cref="SettingsShapeMismatchException"/>, raised when a member of
	/// <c>appsettings.json</c> cannot be bound so no write is allowed) is deliberately NOT special-cased
	/// here: the general branch already reports it with the exception's own message, and reproducing
	/// <c>Program.RunIfDue</c>'s once-per-process latch would put a write to the entry point's mutable
	/// static inside a knowledge component and duplicate a policy that must then be edited in two files.
	/// </para>
	/// </remarks>
	private void Refresh() {
		try {
			if (!_settingsRepository.TryScheduleAutoupdate(
					AutoUpdateTarget.Knowledge, _timeProvider.GetUtcNow())) {
				return;
			}
			// sourceAlias null on purpose: every enabled source, exactly what the CLI autoupdate path
			// updates. Refreshing only the built-in source would leave a partner or customer library
			// behind and give one setting two meanings.
			KnowledgeSourceBatchResult result = _sourceManagementService.Update(sourceAlias: null);
			if (result.Success) {
				// Reset the run, so an outage that starts after a healthy stretch is warned about again
				// rather than being swallowed by a latch the first bad day in the process consumed.
				Volatile.Write(ref _failureReported, 0);
				_logger.WriteDebug($"Knowledge refresh on guidance read: {result.Message}");
				return;
			}
			ReportFailure($"Knowledge refresh on guidance read failed: {result.Message}{DescribeFailures(result)}");
		} catch (Exception exception) when (exception is not OutOfMemoryException) {
			ReportFailure("Knowledge refresh on guidance read did not run: " + exception.Message);
		} finally {
			Volatile.Write(ref _probeInFlight, 0);
		}
	}

	/// <summary>
	/// Warns on the first refresh failure in this process and drops to a debug line afterwards.
	/// </summary>
	/// <remarks>
	/// The warning goes through <see cref="McpAdvisoryLog.Emit"/>, not straight to the logger:
	/// <see cref="ConsoleLogger"/> suppresses every console write in MCP server mode, so on the stdio
	/// transport a plain <c>WriteWarning</c> reaches no console at all and the signal this method exists
	/// to give would be lost. Emit mirrors the line to standard error — the channel hosts capture — and
	/// owns the redaction and the length bound, so the caller passes raw text.
	/// </remarks>
	private void ReportFailure(string message) {
		if (Interlocked.CompareExchange(ref _failureReported, 1, 0) == 0) {
			McpAdvisoryLog.Emit(
				_logger, message, isWarning: true, mirrorToStandardError: Program.IsMcpServerMode);
			return;
		}
		_logger.WriteDebug(message);
	}

	/// <summary>
	/// Appends the per-source diagnostics of a failed batch, already redacted by the service.
	/// </summary>
	private static string DescribeFailures(KnowledgeSourceBatchResult result) {
		string details = string.Join(
			"; ",
			result.Sources
				.Where(source => !source.Success)
				.Select(source => $"{source.SourceAlias}: {source.Status} - {source.Message}"));
		return string.IsNullOrEmpty(details) ? string.Empty : $" ({details})";
	}
}
