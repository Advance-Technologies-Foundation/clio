using System;
using System.Threading;

namespace Clio.Common;

/// <summary>
/// What a reload watcher has seen of the environment's availability.
/// </summary>
/// <param name="Reachable">Whether the last probe was answered.</param>
/// <param name="ReloadObserved">
/// Whether the environment was seen to stop answering and then answer again, after having answered
/// at least once - the signature of the runtime reload that ends a configuration build.
/// </param>
/// <param name="EverReachable">
/// Whether the environment answered at least once since watching started. One that never answered was
/// never reachable to begin with, which is a different problem from a build.
/// </param>
public record EnvironmentReloadSnapshot(bool Reachable, bool ReloadObserved, bool EverReachable);

/// <summary>
/// Watches an environment's availability so the runtime reload that ends a configuration build can be
/// observed.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because the reload is NOT visible on the compilation-history channel.</b> Measured on a
/// live stand across an application-pool recycle: the application stopped answering for 44 seconds,
/// while <c>CompilationHistoryPoller</c> reported not one failed read over the same window. A first
/// implementation that inferred the reload from history-poll failures therefore never once observed a
/// reload on a real build, and every compile fell through to the much slower quiet-window fallback.
/// </para>
/// <para>
/// <b>An outage must be seen SEVERAL TIMES before it counts.</b> A single failed probe is not a reload:
/// a transient 502 from a pegged application, or one request lost while the session is renewed, would
/// otherwise arm the signal, and the very next success would report a build as finished while it was
/// still running - reading the undated verdict of the PREVIOUS build. The measured outage lasts about
/// 44 seconds against a five-second cadence, so requiring several consecutive failures still detects a
/// real reload with room to spare.
/// </para>
/// </remarks>
public interface IEnvironmentReloadWatcher {

	/// <summary>Starts probing the environment, discarding anything observed by a previous run.</summary>
	/// <exception cref="InvalidOperationException">The watcher is already running.</exception>
	void Start();

	/// <summary>Stops probing and waits for the background probe to end.</summary>
	/// <remarks>Safe to call when the watcher was never started, and safe to call twice.</remarks>
	void Stop();

	/// <summary>Gets what has been observed so far. Safe to read while the watcher is running.</summary>
	EnvironmentReloadSnapshot Snapshot { get; }

}

/// <inheritdoc cref="IEnvironmentReloadWatcher"/>
public class EnvironmentReloadWatcher : IEnvironmentReloadWatcher {

	#region Constants: Internal

	/// <summary>Cadence between availability probes.</summary>
	/// <remarks>
	/// The measured outage is about 44 seconds, so this samples it several times over. It is not shorter
	/// because each probe is a real request against an application that is starting up, and there is
	/// nothing to gain from hurrying a signal that lasts most of a minute.
	/// </remarks>
	internal static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(5);

	/// <summary>Timeout for one probe.</summary>
	/// <remarks>
	/// SHORT, and actually honoured - which is why the probe does not go through
	/// <see cref="IApplicationClient"/>; see <see cref="IEnvironmentAvailabilityProbe"/>. While the
	/// application reloads a request hangs rather than failing fast, so a probe whose timeout is not
	/// enforced samples about once every hundred seconds and can span the whole outage in one sample.
	/// </remarks>
	internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

	/// <summary>How many consecutive failed probes count as the environment having gone down.</summary>
	/// <remarks>
	/// Three at a five-second cadence is about fifteen seconds, comfortably inside the measured 44-second
	/// reload, while a single transient failure no longer arms a signal whose consequence is reporting a
	/// still-running build as finished.
	/// </remarks>
	internal const int FailuresBeforeOutage = 3;

	#endregion

	#region Fields: Private

	private readonly IEnvironmentAvailabilityProbe _availabilityProbe;
	private readonly TimeSpan _probeInterval;

	private readonly object _stateLock = new();

	private CancellationTokenSource _cancellation;
	private Thread _thread;

	private bool _reachable;
	private bool _everReachable;
	private bool _outageStarted;
	private bool _reloadObserved;
	private int _consecutiveFailures;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="EnvironmentReloadWatcher"/> class.
	/// </summary>
	/// <param name="availabilityProbe">Probe that answers whether the application is responding.</param>
	public EnvironmentReloadWatcher(IEnvironmentAvailabilityProbe availabilityProbe)
		: this(availabilityProbe, ProbeInterval) { }

	/// <summary>
	/// Test seam taking an explicit probe cadence, so a test can drive the outage-and-recovery sequence
	/// without spending the production five seconds per probe on it.
	/// </summary>
	/// <param name="availabilityProbe">Probe that answers whether the application is responding.</param>
	/// <param name="probeInterval">Cadence between probes.</param>
	internal EnvironmentReloadWatcher(IEnvironmentAvailabilityProbe availabilityProbe,
		TimeSpan probeInterval) {
		availabilityProbe.CheckArgumentNull(nameof(availabilityProbe));
		_availabilityProbe = availabilityProbe;
		_probeInterval = probeInterval;
	}

	#endregion

	#region Properties: Public

	/// <inheritdoc/>
	public EnvironmentReloadSnapshot Snapshot {
		get {
			lock (_stateLock) {
				return new EnvironmentReloadSnapshot(_reachable, _reloadObserved, _everReachable);
			}
		}
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public void Start() {
		if (_thread is not null) {
			throw new InvalidOperationException(
				"The environment reload watcher is already running. Create one watcher per build.");
		}
		// Reset EVERY observation. A reused instance would otherwise start its second build already
		// believing a reload had happened, and the first completion check would report the previous
		// build's verdict without waiting for anything.
		lock (_stateLock) {
			_reachable = false;
			_everReachable = false;
			_outageStarted = false;
			_reloadObserved = false;
			_consecutiveFailures = 0;
		}
		_cancellation = new CancellationTokenSource();
		Thread thread = new(() => Run(_cancellation.Token)) { IsBackground = true };
		thread.Start();
		// Assigned only after a successful start, so a failed Thread.Start cannot leave Stop() joining a
		// thread that was never running.
		_thread = thread;
	}

	/// <inheritdoc/>
	public void Stop() {
		if (_thread is null) {
			return;
		}
		_cancellation.Cancel();
		// Join before disposing: the probe loop reads the token. The probe is cancellable and bounded, so
		// this cannot wait longer than one probe timeout.
		_thread.Join();
		_cancellation.Dispose();
		_cancellation = null;
		_thread = null;
	}

	#endregion

	#region Methods: Private

	private void Run(CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			Record(_availabilityProbe.IsReachable(ProbeTimeout, cancellationToken));
			if (cancellationToken.WaitHandle.WaitOne(_probeInterval)) {
				return;
			}
		}
	}

	private void Record(bool reachable) {
		lock (_stateLock) {
			_reachable = reachable;
			if (!reachable) {
				_consecutiveFailures++;
				// Only an environment that HAS answered can be observed to go down: without this a watcher
				// started against an environment that was unreachable from the outset would record a phantom
				// outage, and its first success would read as "the build finished". The consecutive-failure
				// threshold is the other half of that guard - see the type remarks.
				if (_everReachable && _consecutiveFailures >= FailuresBeforeOutage) {
					_outageStarted = true;
				}
				return;
			}
			_consecutiveFailures = 0;
			if (_outageStarted) {
				_reloadObserved = true;
				_outageStarted = false;
			}
			_everReachable = true;
		}
	}

	#endregion

}
