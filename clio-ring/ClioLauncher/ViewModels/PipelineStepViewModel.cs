using ClioLauncher.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClioLauncher.ViewModels;

/// <summary>
/// Render state of a single pipeline step, mirroring clio's stage <c>status</c> vocabulary (ADR D2).
/// The colour/glyph the view uses to encode the state is driven off this enum (plus <see cref="ClioLauncher.ViewModels.PipelineStepViewModel.SkipReason"/>
/// to distinguish the two kinds of skip), never per-step styling.
/// </summary>
public enum PipelineStepState {
	/// <summary>Declared by the manifest but not started yet (the only initial state — never fabricated as an event).</summary>
	Pending,

	/// <summary>The stage is currently running.</summary>
	Running,

	/// <summary>The stage completed successfully.</summary>
	Done,

	/// <summary>The stage failed.</summary>
	Failed,

	/// <summary>The stage was skipped (see <see cref="ClioLauncher.ViewModels.PipelineStepViewModel.SkipReason"/> for why).</summary>
	Skipped
}

/// <summary>
/// One row in the GitHub-Actions-style deploy/uninstall pipeline. Built once from a <c>manifest</c> entry
/// (all steps start <see cref="PipelineStepState.Pending"/>) and then transitioned by <c>stage</c> events.
/// This view-model is deliberately free of Avalonia visual types (brushes/geometry): the view resolves the
/// glyph from <see cref="IconKey"/> and the colour from the boolean state flags via style classes, so the
/// step is fully unit-testable without a rendering platform.
/// </summary>
public sealed partial class PipelineStepViewModel : ViewModelBase {
	/// <summary>Creates a pending step from a manifest entry.</summary>
	public PipelineStepViewModel(ClioStageManifestEntry entry) {
		StageId = entry.StageId;
		Name = entry.Name;
		Index = entry.Index;
		Conditional = entry.Conditional;
		_message = entry.Conditional ? "Runs only if applicable" : string.Empty;
	}

	/// <summary>Stable kebab-case stage key that correlates manifest and stage events.</summary>
	public string StageId { get; }

	/// <summary>Human-readable, user-language stage name shown as the step title.</summary>
	public string Name { get; }

	/// <summary>Zero-based position of this step within the manifest.</summary>
	public int Index { get; }

	/// <summary>True when the manifest flagged the stage as conditional (may end up skipped, not-applicable).</summary>
	public bool Conditional { get; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(StateLabel))]
	[NotifyPropertyChangedFor(nameof(IconKey))]
	[NotifyPropertyChangedFor(nameof(IsPending))]
	[NotifyPropertyChangedFor(nameof(IsRunning))]
	[NotifyPropertyChangedFor(nameof(IsDone))]
	[NotifyPropertyChangedFor(nameof(IsFailed))]
	[NotifyPropertyChangedFor(nameof(IsSkipped))]
	[NotifyPropertyChangedFor(nameof(IsSkippedNotApplicable))]
	[NotifyPropertyChangedFor(nameof(IsSkippedAfterFailure))]
	[NotifyPropertyChangedFor(nameof(IsSkippedNotSupported))]
	private PipelineStepState _state = PipelineStepState.Pending;

	[ObservableProperty]
	private string _message = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasTechnicalDetail))]
	private string? _detail;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasTechnicalDetail))]
	[NotifyPropertyChangedFor(nameof(HasErrorCode))]
	private string? _errorCode;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasDuration))]
	[NotifyPropertyChangedFor(nameof(DurationText))]
	private long? _durationMs;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSkippedNotApplicable))]
	[NotifyPropertyChangedFor(nameof(IsSkippedAfterFailure))]
	[NotifyPropertyChangedFor(nameof(IsSkippedNotSupported))]
	private string? _skipReason;

	/// <summary>True while the step is still awaiting execution.</summary>
	public bool IsPending => State == PipelineStepState.Pending;

	/// <summary>True while the step is executing.</summary>
	public bool IsRunning => State == PipelineStepState.Running;

	/// <summary>True once the step completed successfully.</summary>
	public bool IsDone => State == PipelineStepState.Done;

	/// <summary>True once the step failed.</summary>
	public bool IsFailed => State == PipelineStepState.Failed;

	/// <summary>True once the step was skipped (for any reason).</summary>
	public bool IsSkipped => State == PipelineStepState.Skipped;

	/// <summary>True when skipped because the stage was inert for the resolved inputs (distinct visual).</summary>
	public bool IsSkippedNotApplicable =>
		IsSkipped && SkipReason == ClioStageEventContract.SkipReasons.NotApplicable;

	/// <summary>True when skipped by the failure cascade (an earlier stage failed) — distinct from not-applicable.</summary>
	public bool IsSkippedAfterFailure =>
		IsSkipped && SkipReason == ClioStageEventContract.SkipReasons.AfterFailure;

	/// <summary>True when skipped because the stage is not supported.</summary>
	public bool IsSkippedNotSupported =>
		IsSkipped && SkipReason == ClioStageEventContract.SkipReasons.NotSupported;

	/// <summary>Whether a completion duration is known (shown once the step finishes).</summary>
	public bool HasDuration => DurationMs is > 0;

	/// <summary>Whether an error code is present (part of the technical disclosure).</summary>
	public bool HasErrorCode => !string.IsNullOrEmpty(ErrorCode);

	/// <summary>Whether there is any technical detail to reveal behind the expander (detail or error code).</summary>
	public bool HasTechnicalDetail => !string.IsNullOrEmpty(Detail) || !string.IsNullOrEmpty(ErrorCode);

	/// <summary>Human duration string (e.g. <c>42.1s</c> / <c>820ms</c>); empty when unknown.</summary>
	public string DurationText {
		get {
			if (DurationMs is not long ms || ms <= 0) {
				return string.Empty;
			}
			return ms >= 1000
				? (ms / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s"
				: ms.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms";
		}
	}

	/// <summary>Short state word for the row badge.</summary>
	public string StateLabel => State switch {
		PipelineStepState.Running => "RUNNING",
		PipelineStepState.Done => "DONE",
		PipelineStepState.Failed => "FAILED",
		PipelineStepState.Skipped => IsSkippedAfterFailure ? "SKIPPED (after failure)"
			: IsSkippedNotSupported ? "SKIPPED (not supported)"
			: "SKIPPED (not applicable)",
		_ => "PENDING"
	};

	/// <summary>Icon-family key the view resolves to a stroke geometry (state glyph).</summary>
	public string IconKey => State switch {
		PipelineStepState.Done => "check",
		PipelineStepState.Failed => "close",
		PipelineStepState.Running => "restart",
		_ => "dot"
	};

	/// <summary>Transitions the step to running, recording its friendly message.</summary>
	public void MarkRunning(string message) {
		State = PipelineStepState.Running;
		Message = message;
		SkipReason = null;
	}

	/// <summary>Transitions the step to done, recording duration + message + optional technical detail.</summary>
	public void MarkDone(string message, long? durationMs, string? detail) {
		DurationMs = durationMs;
		Message = message;
		Detail = detail;
		SkipReason = null;
		State = PipelineStepState.Done;
	}

	/// <summary>Transitions the step to failed, recording duration + message + technical detail + error code.</summary>
	public void MarkFailed(string message, long? durationMs, string? detail, string? errorCode) {
		DurationMs = durationMs;
		Message = message;
		Detail = detail;
		ErrorCode = errorCode;
		SkipReason = null;
		State = PipelineStepState.Failed;
	}

	/// <summary>Transitions the step to skipped, recording the reason (not-applicable / after-failure / not-supported).</summary>
	public void MarkSkipped(string skipReason, string message) {
		SkipReason = skipReason;
		Message = message;
		State = PipelineStepState.Skipped;
	}
}
