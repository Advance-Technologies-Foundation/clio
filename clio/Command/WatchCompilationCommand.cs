using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Clio.Common;
using Clio.CreatioModel;
using CommandLine;

namespace Clio.Command;

#region Class: WatchCompilationOptions

[Verb("watch-compilation", Hidden = true, HelpText = "Observe Creatio compilation status without triggering a compile")]
[FeatureToggle("watch-compilation")]
public class WatchCompilationOptions : RemoteCommandOptions {

	[Option("give-up-after", Required = false, Default = 300,
		HelpText = "Seconds to wait for the compilation to settle before giving up (exit code 2). Default: 300 (5 minutes).")]
	public int GiveUpAfterSeconds { get; set; } = 300;

}

#endregion

#region Class: WatchCompilationCommand

/// <summary>
/// Observes Creatio's <see cref="CompilationHistory"/> table to report the status of a
/// compilation started outside clio (Studio UI, another process/user, or an IIS recycle after a
/// package install). Never triggers a compile itself.
/// </summary>
public class WatchCompilationCommand : RemoteCommand<WatchCompilationOptions> {

	#region Constants

	// Exit codes are a stable CI contract - never renumber these.
	private const int ExitSuccess = 0;
	private const int ExitCompilationFailed = 1;
	private const int ExitGaveUpWaiting = 2;
	private const int ExitStartupError = 3;

	// Cadence between poll rounds while the channel is healthy; mirrors CompilationHistoryPoller.Poll's own cadence.
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

	#endregion

	#region Fields: Private

	private readonly ICompilationHistoryPoller _compilationHistoryPoller;
	private readonly ICompilationSettleTracker _settleTracker;
	private readonly IPollRetryPolicy _retryPolicy;

	#endregion

	#region Constructors: Public

	public WatchCompilationCommand(ICompilationHistoryPoller compilationHistoryPoller,
		ICompilationSettleTracker settleTracker, IPollRetryPolicy retryPolicy,
		EnvironmentSettings settings, ILogger logger)
		: base(settings) {
		_compilationHistoryPoller = compilationHistoryPoller;
		_settleTracker = settleTracker;
		_retryPolicy = retryPolicy;
		Logger = logger;
		EnvironmentSettings = settings;
	}

	#endregion

	#region Methods: Public

	public override int Execute(WatchCompilationOptions options){
		Stopwatch sw = Stopwatch.StartNew();
		int giveUpAfterSeconds = Math.Max(0, options.GiveUpAfterSeconds);
		DateTime deadline = DateTime.UtcNow.AddSeconds(giveUpAfterSeconds);

		if (!TryGetBaseline(out CompilationHistory baseline)) {
			return ExitStartupError;
		}

		DateTime startedAt = DateTime.UtcNow;
		_settleTracker.SeedFromBaseline(baseline, startedAt);
		DateTime currentBaseline = baseline?.CreatedOn ?? startedAt;
		Logger.WriteInfo($"At: {DateTime.Now:HH:mm:ss} Watching compilation status on {EnvironmentSettings.Uri}...");

		int? giveUpExitCode = RunPollLoop(deadline, giveUpAfterSeconds, ref currentBaseline);
		return giveUpExitCode ?? ReportOutcome(sw);
	}

	#endregion

	#region Methods: Private

	private bool TryGetBaseline(out CompilationHistory baseline) {
		try {
			baseline = _compilationHistoryPoller.GetBaseline();
			return true;
		} catch (Exception e) {
			Logger.WriteError($"Could not read compilation history: {e.Message}");
			baseline = null;
			return false;
		}
	}

	/// <summary>
	/// Polls until either the settle tracker reports done on a healthy channel (returns
	/// <c>null</c>) or the deadline is exceeded (returns the give-up exit code).
	/// </summary>
	private int? RunPollLoop(DateTime deadline, int giveUpAfterSeconds, ref DateTime currentBaseline) {
		// PollOnce filters by "CreatedOn > baseline", but a real Creatio CompilationHistory table can
		// return the same row again across rounds (observed live: duplicate CreatedOn timestamps at
		// whole-second precision). Without this guard a repeated row keeps resetting the settle
		// tracker's last-activity clock forever and the watch never settles. Mirrors the same seen-Id
		// guard CompilationHistoryPoller.Poll already uses for exactly this reason.
		HashSet<Guid> seenRecordIds = [];

		while (true) {
			if (deadline - DateTime.UtcNow <= TimeSpan.Zero) {
				return GiveUp(giveUpAfterSeconds);
			}

			PollOnceAndObserve(seenRecordIds, ref currentBaseline);

			DateTime now = DateTime.UtcNow;
			if (_retryPolicy.IsChannelHealthy(now) && _settleTracker.IsSettled(now)) {
				return null;
			}

			TimeSpan remaining = deadline - DateTime.UtcNow;
			if (remaining <= TimeSpan.Zero) {
				return GiveUp(giveUpAfterSeconds);
			}
			TimeSpan sleepFor = _retryPolicy.ConsecutiveFailures > 0 ? _retryPolicy.NextDelay : PollInterval;
			Thread.Sleep(sleepFor < remaining ? sleepFor : remaining);
		}
	}

	private void PollOnceAndObserve(HashSet<Guid> seenRecordIds, ref DateTime currentBaseline) {
		try {
			foreach (CompilationHistory record in _compilationHistoryPoller.PollOnce(currentBaseline)) {
				if (record.CreatedOn > currentBaseline) {
					currentBaseline = record.CreatedOn;
				}
				if (!seenRecordIds.Add(record.Id)) {
					continue;
				}
				LogRecord(record);
				_settleTracker.Observe(record, DateTime.UtcNow);
			}
			_retryPolicy.RecordSuccess(DateTime.UtcNow);
		} catch (Exception e) {
			_retryPolicy.RecordFailure(DateTime.UtcNow);
			Logger.WriteWarning($"Poll attempt failed (attempt {_retryPolicy.ConsecutiveFailures}): {e.Message}. Retrying...");
		}
	}

	private int ReportOutcome(Stopwatch sw) {
		sw.Stop();
		CompilationSettleSnapshot snapshot = _settleTracker.Snapshot;
		if (snapshot.HasErrors) {
			Logger.WriteError($"Compilation finished with errors after {TimeOnly.FromTimeSpan(sw.Elapsed):HH:mm:ss}.");
			return ExitCompilationFailed;
		}
		if (snapshot.NewRecordCount > 0 && !snapshot.SawFinalMarker) {
			Logger.WriteWarning($"Compilation activity settled, but {CompilationSettleTracker.FinalMarkerProjectName} " +
				"was never observed - cannot confirm a full successful finish.");
			return ExitCompilationFailed;
		}

		Logger.WriteInfo($"At: {DateTime.Now:HH:mm:ss} Compilation settled successfully after {TimeOnly.FromTimeSpan(sw.Elapsed):HH:mm:ss}.");
		return ExitSuccess;
	}

	private int GiveUp(int giveUpAfterSeconds) {
		Logger.WriteWarning($"Gave up waiting after {giveUpAfterSeconds}s - compilation did not settle.");
		return ExitGaveUpWaiting;
	}

	private void LogRecord(CompilationHistory record){
		string decoratedDuration = record.DurationInSeconds switch {
			>= 10 => ConsoleLogger.WrapRed(record.DurationInSeconds),
			>= 5 => ConsoleLogger.WrapYellow(record.DurationInSeconds),
			var _ => record.DurationInSeconds.ToString("N0", CultureInfo.InvariantCulture)
		};
		bool isFinalMarker = string.Equals(record.ProjectName, CompilationSettleTracker.FinalMarkerProjectName,
			StringComparison.OrdinalIgnoreCase);
		string decoratedProjectName = isFinalMarker
			? ConsoleLogger.WrapBlue(record.ProjectName) + ConsoleLogger.WrapGreen(" <============")
			: record.ProjectName;

		if (string.Equals(record.ErrorsWarnings, "[]", StringComparison.OrdinalIgnoreCase)) {
			Logger.WriteInfo($"At: {record.CreatedOn:HH:mm:ss} after: {decoratedDuration} sec. {decoratedProjectName}");
		} else {
			Logger.WriteWarning($"At: {record.CreatedOn:HH:mm:ss} after: {decoratedDuration} sec. {decoratedProjectName} with: {record.ErrorsWarnings}");
		}
	}

	#endregion

}

#endregion
