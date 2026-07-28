using System;
using System.IO;
using System.Linq;
using System.Threading;
using ClioRing.Ipc;
using ClioRing.Models;
using ClioRing.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClioRing.ViewModels;

/// <summary>
	/// Settings sub-view-model for the Ring's clio connection: it defaults to the release clio, accepts
/// an explicit dev-clio path override (persisted to <c>app-settings.json</c>), and surfaces which clio is
/// actually connected using the negotiated handshake identity (name + version + resolved path). The
/// override applies on the next ring launch, consistent with the hotkey settings.
/// </summary>
public sealed partial class ClioSettingsViewModel : ViewModelBase {
	private const string ReleaseMode = "release";
	private const string DevelopmentMode = "development";
	private readonly IClioSettingsStore _store;
	private readonly IClioIpcClient? _client;
	private readonly ResolvedClioRuntime _runtime;
	private readonly SynchronizationContext? _uiContext;
	private bool _suppressRuntimePersistence;

	/// <summary>Creates the view-model bound to the persistence store and (optionally) the live IPC client.</summary>
	/// <param name="store">Where the dev-clio override is read from / written to.</param>
	/// <param name="client">The live clio IPC client whose handshake identity is displayed; null in design/tests.</param>
	/// <param name="runtime">The immutable runtime decision made during startup.</param>
	public ClioSettingsViewModel(IClioSettingsStore store, IClioIpcClient? client = null,
		ResolvedClioRuntime? runtime = null) {
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_client = client;
		_runtime = runtime ?? new ResolvedClioRuntime(ClioRuntimeMode.Release, ClioIpcSettings.Default);
		_uiContext = SynchronizationContext.Current;
		if (_client is not null) {
			_client.ConnectionChanged += OnConnectionChanged;
		}
		Refresh();
	}

	/// <summary>The development clio path the user is editing (a <c>clio.dll</c> or <c>clio.exe</c>).</summary>
	[ObservableProperty]
	private string _devClioPathInput = string.Empty;

	/// <summary>Whether a valid dev-clio override is currently persisted (so the ring is driving a dev build).</summary>
	[ObservableProperty]
	private bool _isDevOverrideActive;

	/// <summary>Friendly, jargon-free validation error for a bad override path (empty = no error).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasValidationError))]
	private string _validationMessage = string.Empty;

	/// <summary>Non-error status/confirmation line (e.g. "Saved. Restart the ring to connect."). Empty = none.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasStatusMessage))]
	private string _statusMessage = string.Empty;

	/// <summary>
	/// One-line identity of the connected clio, from the handshake: name + version, whether it is the normal
	/// or dev build, and the resolved path. Falls back to the configured target when not yet connected.
	/// </summary>
	[ObservableProperty]
	private string _connectedClioIdentity = string.Empty;

	/// <summary>Whether the current override differs from the running connection (a relaunch is needed to apply it).</summary>
	[ObservableProperty]
	private bool _restartRequired;

	/// <summary>Whether the clio child used by this Ring session is a development/custom runtime.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsReleaseRuntimeRunning))]
	private bool _isDevelopmentRuntimeRunning;

	/// <summary>Whether Development is selected for the next Ring launch.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanChangeRuntimeSelection))]
	private bool _isDevelopmentSelected;

	/// <summary>Connected clio name/version shown in the main runtime notice.</summary>
	[ObservableProperty]
	private string _runtimeSummary = "Installed dotnet tool";

	/// <summary>Resolved child command/path shown in the main runtime notice.</summary>
	[ObservableProperty]
	private string _runtimeTarget = ClioIpcSettings.Default.Command;

	/// <summary>Whether the selected runtime differs from the child running in this session.</summary>
	[ObservableProperty]
	private bool _runtimeSelectionRestartRequired;

	/// <summary>Whether the Development side of the runtime switch has a valid saved target.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanChangeRuntimeSelection))]
	private bool _hasDevelopmentTarget;

	/// <summary>Actionable startup warning when the persisted runtime choice could not be used safely.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasRuntimeConfigurationWarning))]
	private string _runtimeConfigurationWarning = string.Empty;

	/// <summary>Whether the running clio child is the released dotnet tool.</summary>
	public bool IsReleaseRuntimeRunning => !IsDevelopmentRuntimeRunning;

	/// <summary>The path to the config file that owns these settings (shown in the panel).</summary>
	public string SettingsPath => _store.SettingsPath;

	/// <summary>Whether a validation error is showing.</summary>
	public bool HasValidationError => !string.IsNullOrEmpty(ValidationMessage);

	/// <summary>Whether a non-error status/confirmation line is showing.</summary>
	public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

	/// <summary>Whether the main runtime strip must show a startup configuration warning.</summary>
	public bool HasRuntimeConfigurationWarning => !string.IsNullOrEmpty(RuntimeConfigurationWarning);

	/// <summary>Whether the selector can switch to a valid target or clear an invalid Development request.</summary>
	public bool CanChangeRuntimeSelection => HasDevelopmentTarget || IsDevelopmentSelected;

	/// <summary>Re-reads the persisted override and refreshes the connected-clio identity from the live client.</summary>
	public void Refresh() {
		string? persisted = _store.ReadDevClioPath();
		DevClioPathInput = persisted ?? string.Empty;
		StatusMessage = string.Empty;

		// A persisted-but-invalid override (e.g. hand-edited into the file) is ignored at launch; say so
		// rather than falling back to Release silently.
		DevClioValidation validation = DevClioLaunch.Validate(persisted);
		ValidationMessage = validation.IsValid ? string.Empty : validation.Message;

		UpdateIdentity(persisted);
		UpdateRuntimeState();
	}

	partial void OnIsDevelopmentSelectedChanged(bool value) {
		if (_suppressRuntimePersistence) {
			return;
		}
		if (value && !HasDevelopmentTarget) {
			_suppressRuntimePersistence = true;
			IsDevelopmentSelected = false;
			_suppressRuntimePersistence = false;
			StatusMessage = "Configure a development clio target in Settings first.";
			return;
		}
		try {
			_store.SaveRuntimeMode(value ? DevelopmentMode : ReleaseMode);
			RuntimeSelectionRestartRequired = value != IsDevelopmentRuntimeRunning;
			ValidationMessage = string.Empty;
			if (!value) {
				RuntimeConfigurationWarning = string.Empty;
			}
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
			_suppressRuntimePersistence = true;
			IsDevelopmentSelected = !value;
			_suppressRuntimePersistence = false;
			ValidationMessage = "Ring settings could not be updated. The existing file was left unchanged.";
		}
	}

	/// <summary>
	/// Validates and persists the dev-clio override. A blank path clears the override (reverts to the
	/// saved development target). An invalid path is NOT persisted and surfaces a friendly message.
	/// </summary>
	[RelayCommand]
	private void SaveDevClioOverride() {
		string? input = string.IsNullOrWhiteSpace(DevClioPathInput) ? null : DevClioPathInput.Trim();
		DevClioValidation validation = DevClioLaunch.Validate(input);
		if (!validation.IsValid) {
			ValidationMessage = validation.Message;
			StatusMessage = string.Empty;
			return;
		}

		ValidationMessage = string.Empty;
		try {
			_store.SaveDevClioPath(input);
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
			ValidationMessage = "Ring settings could not be updated. The existing file was left unchanged.";
			StatusMessage = string.Empty;
			return;
		}
		StatusMessage = input is null
			? "Development path cleared. Select Release before restarting Ring."
			: "Development path saved. Select Development before restarting Ring.";
		UpdateIdentity(input);
		UpdateRuntimeState();
	}

	/// <summary>Clears the development path without changing the selected runtime mode.</summary>
	[RelayCommand]
	private void ClearDevClioOverride() {
		DevClioPathInput = string.Empty;
		SaveDevClioOverride();
	}

	// Recomputes IsDevOverrideActive + the identity line + the restart hint from the persisted override and
	// the live client's handshake/target.
	private void UpdateIdentity(string? persistedOverride) {
		bool isDevOverride = !string.IsNullOrWhiteSpace(persistedOverride)
			&& DevClioLaunch.Validate(persistedOverride).IsValid;
		IsDevOverrideActive = isDevOverride;

		ClioServerHandshake? handshake = _client?.Handshake;
		string liveTarget = _client?.TargetPath ?? string.Empty;

		// The override applies at launch; if a valid override is configured but the running connection is
		// pointed elsewhere, tell the user a relaunch is needed rather than showing a stale label.
		string resolvedTarget = _runtime.Mode == ClioRuntimeMode.Development && isDevOverride
			? persistedOverride!.Trim()
			: liveTarget;
		RestartRequired = _client is { IsConnected: true }
			&& !string.IsNullOrEmpty(liveTarget)
			&& !string.Equals(liveTarget, resolvedTarget, StringComparison.OrdinalIgnoreCase);

		string displayTarget = string.IsNullOrEmpty(liveTarget) ? resolvedTarget : liveTarget;
		SetConnectedIdentity(handshake, displayTarget, _runtime.Mode == ClioRuntimeMode.Development);
	}

	private void UpdateRuntimeState() {
		string target = _client?.TargetPath ?? ResolveTargetPath(_runtime.LaunchSettings);
		IsDevelopmentRuntimeRunning = _runtime.Mode == ClioRuntimeMode.Development;
		RuntimeTarget = string.IsNullOrWhiteSpace(target) ? _runtime.LaunchSettings.Command : target;
		HasDevelopmentTarget = _store.HasDevelopmentTarget() || IsDevelopmentRuntimeRunning;
		ClioServerHandshake? handshake = _client?.Handshake;
		RuntimeSummary = handshake is null
			? IsDevelopmentRuntimeRunning
				? "Ring is using a development clio build"
				: "Installed dotnet tool"
			: $"{handshake.ServerName} {handshake.ServerVersion}";
		string? persistedMode = _store.ReadRuntimeMode();
		RuntimeConfigurationWarning = _runtime.ConfigurationWarning is not null
			&& !string.Equals(persistedMode, ReleaseMode, StringComparison.OrdinalIgnoreCase)
			&& !_store.HasDevelopmentTarget()
				? _runtime.ConfigurationWarning
				: string.Empty;
		_suppressRuntimePersistence = true;
		IsDevelopmentSelected = persistedMode is not null
			? string.Equals(persistedMode, DevelopmentMode, StringComparison.OrdinalIgnoreCase)
			: _runtime.RequestedMode.HasValue
				? _runtime.RequestedMode == ClioRuntimeMode.Development
				: HasDevelopmentTarget && IsDevelopmentRuntimeRunning;
		_suppressRuntimePersistence = false;
		RuntimeSelectionRestartRequired = IsDevelopmentSelected != IsDevelopmentRuntimeRunning;
	}

	private static string ResolveTargetPath(ClioIpcSettings settings) =>
		settings.Args.FirstOrDefault(argument => argument.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
		?? settings.Command;

	private void OnConnectionChanged(object? sender, EventArgs e) {
		if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext)) {
			RefreshConnectionIdentity();
			return;
		}
		_uiContext.Post(_ => RefreshConnectionIdentity(), null);
	}

	/// <summary>Refreshes displayed identity after the lazy clio handshake changes.</summary>
	public void RefreshConnectionIdentity() {
		UpdateIdentity(_store.ReadDevClioPath());
		UpdateRuntimeState();
	}

	/// <summary>Populates the runtime notice without starting a clio process (screenshot/design seam).</summary>
	/// <param name="developmentRunning">Whether the rendered session represents a development runtime.</param>
	/// <param name="summary">The representative clio name/version.</param>
	/// <param name="target">The representative command or path.</param>
	/// <param name="configurationWarning">Optional startup warning to render.</param>
	/// <param name="developmentSelected">Optional next-launch selector state when it differs from the running mode.</param>
	public void DesignSetRuntime(bool developmentRunning, string summary, string target,
		string? configurationWarning = null, bool? developmentSelected = null) {
		IsDevelopmentRuntimeRunning = developmentRunning;
		HasDevelopmentTarget = true;
		_suppressRuntimePersistence = true;
		IsDevelopmentSelected = developmentSelected ?? developmentRunning;
		_suppressRuntimePersistence = false;
		RuntimeSummary = summary;
		RuntimeTarget = target;
		RuntimeConfigurationWarning = configurationWarning ?? string.Empty;
		RuntimeSelectionRestartRequired = false;
	}

	/// <summary>
	/// Builds the human-readable connected-clio identity line from the handshake identity (never a
	/// hardcoded label): "<c>name version - dev build - path</c>", or a configured-target form when the
	/// handshake is absent. Pure and directly testable.
	/// </summary>
	/// <param name="handshake">The negotiated handshake, or null when not connected.</param>
	/// <param name="targetPath">The resolved clio path to display.</param>
	/// <param name="isDevOverride">Whether the dev-clio override is the active source.</param>
	public void SetConnectedIdentity(ClioServerHandshake? handshake, string? targetPath, bool isDevOverride) {
		string source = isDevOverride ? "development build" : "release build";
		string path = string.IsNullOrWhiteSpace(targetPath) ? "unknown path" : targetPath!;
		ConnectedClioIdentity = handshake is null
			? $"clio - {source} - {path}"
			: $"{handshake.ServerName} {handshake.ServerVersion} - {source} - {path}";
	}
}
