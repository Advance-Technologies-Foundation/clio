using System;
using ClioRing.Ipc;
using ClioRing.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClioRing.ViewModels;

/// <summary>
/// Settings sub-view-model for the ring's clio connection: it defaults to the normal clio build, accepts
/// an explicit dev-clio path override (persisted to <c>app-settings.json</c>), and surfaces which clio is
/// actually connected using the negotiated handshake identity (name + version + resolved path). The
/// override applies on the next ring launch, consistent with the hotkey settings.
/// </summary>
public sealed partial class ClioSettingsViewModel : ViewModelBase {
	private readonly IClioSettingsStore _store;
	private readonly IClioIpcClient? _client;

	/// <summary>Creates the view-model bound to the persistence store and (optionally) the live IPC client.</summary>
	/// <param name="store">Where the dev-clio override is read from / written to.</param>
	/// <param name="client">The live clio IPC client whose handshake identity is displayed; null in design/tests.</param>
	public ClioSettingsViewModel(IClioSettingsStore store, IClioIpcClient? client = null) {
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_client = client;
		Refresh();
	}

	/// <summary>The dev-clio build path the user is editing (a <c>clio.dll</c> or <c>clio.exe</c>). Blank = normal clio.</summary>
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

	/// <summary>The path to the config file that owns these settings (shown in the panel).</summary>
	public string SettingsPath => _store.SettingsPath;

	/// <summary>Whether a validation error is showing.</summary>
	public bool HasValidationError => !string.IsNullOrEmpty(ValidationMessage);

	/// <summary>Whether a non-error status/confirmation line is showing.</summary>
	public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

	/// <summary>Re-reads the persisted override and refreshes the connected-clio identity from the live client.</summary>
	public void Refresh() {
		string? persisted = _store.ReadDevClioPath();
		DevClioPathInput = persisted ?? string.Empty;
		StatusMessage = string.Empty;

		// A persisted-but-invalid override (e.g. hand-edited into the file) is ignored at launch; say so
		// rather than falling back to the normal clio silently.
		DevClioValidation validation = DevClioLaunch.Validate(persisted);
		ValidationMessage = validation.IsValid ? string.Empty : validation.Message;

		UpdateIdentity(persisted);
	}

	/// <summary>
	/// Validates and persists the dev-clio override. A blank path clears the override (reverts to the
	/// normal clio). An invalid path is NOT persisted and surfaces a friendly message.
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
		_store.SaveDevClioPath(input);
		StatusMessage = input is null
			? "Cleared. Restart the ring to connect to the normal clio."
			: "Saved. Restart the ring to connect to the dev clio.";
		UpdateIdentity(input);
	}

	/// <summary>Clears the dev-clio override, reverting the ring to the normal clio on the next launch.</summary>
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
		string resolvedTarget = isDevOverride ? persistedOverride!.Trim() : liveTarget;
		RestartRequired = _client is { IsConnected: true }
			&& !string.IsNullOrEmpty(liveTarget)
			&& !string.Equals(liveTarget, resolvedTarget, StringComparison.OrdinalIgnoreCase);

		string displayTarget = string.IsNullOrEmpty(liveTarget) ? resolvedTarget : liveTarget;
		SetConnectedIdentity(handshake, displayTarget, isDevOverride);
	}

	/// <summary>
	/// Builds the human-readable connected-clio identity line from the handshake identity (never a
	/// hardcoded label): "<c>name version — dev build — path</c>", or a "not connected yet" form when the
	/// handshake is absent. Pure and directly testable.
	/// </summary>
	/// <param name="handshake">The negotiated handshake, or null when not connected.</param>
	/// <param name="targetPath">The resolved clio path to display.</param>
	/// <param name="isDevOverride">Whether the dev-clio override is the active source.</param>
	public void SetConnectedIdentity(ClioServerHandshake? handshake, string? targetPath, bool isDevOverride) {
		string source = isDevOverride ? "dev build" : "normal build";
		string path = string.IsNullOrWhiteSpace(targetPath) ? "unknown path" : targetPath!;
		ConnectedClioIdentity = handshake is null
			? $"{source} — {path} (not connected yet)"
			: $"{handshake.ServerName} {handshake.ServerVersion} — {source} — {path}";
	}
}
