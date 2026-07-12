using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using ClioLauncher.Diagnostics;
using ClioLauncher.Ipc;
using ClioLauncher.Models;
using ClioLauncher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClioLauncher.ViewModels;

/// <summary>Terminal state of a run, surfaced as the drawer's end-state badge.</summary>
public enum RunOutcome {
	/// <summary>No run yet / drawer idle.</summary>
	None,

	/// <summary>A run is in progress.</summary>
	Running,

	/// <summary>The run exited 0.</summary>
	Success,

	/// <summary>The run exited non-zero.</summary>
	Failure,

	/// <summary>The run was cancelled (process tree killed).</summary>
	Canceled
}

/// <summary>Location filter applied in the environment palette.</summary>
public enum EnvFilter {
	/// <summary>No location filter.</summary>
	All,

	/// <summary>Local only.</summary>
	Local,

	/// <summary>Cloud/remote only.</summary>
	Cloud
}

/// <summary>
/// View-model for the radial ring. The OUTER orbit is the fixed action set (never moves). Environments
/// do NOT crowd the ring: the hub is a searchable palette selector, and only up to 6 pinned+MRU quick
/// chips appear on the inner orbit. The selected environment (persisted) is the command target.
/// </summary>
public partial class RingViewModel : ViewModelBase {
	private const double CanvasSize = 500;
	private const double Center = CanvasSize / 2.0;
	private const double InnerRadius = 82;   // quick-slot env chips
	private const double OuterRadius = 150;  // actions
	private const double LabelRadius = 190;  // outward action labels
	private const double LabelHalfWidth = 48;
	private const int MaxQuickSlots = 6;
	private const int MaxRecents = 8;

	private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#F97316"));
	private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#3FB950"));
	private static readonly IBrush FailureBrush = new SolidColorBrush(Color.Parse("#F85149"));
	private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.Parse("#8B97A5"));

	private readonly IClioAdapter _clio;
	private readonly IActionCatalogLoader _catalogLoader;
	private readonly IEnvStateStore _stateStore;
	private readonly IActionCatalogWatcher _catalogWatcher;
	private readonly IEnvironmentSettingsWatcher _environmentSettingsWatcher;
	private readonly StringBuilder _output = new();

	private readonly List<ClioEnvironment> _allEnvironments = new();
	private readonly string _channel;
	private EnvState _state;
	private CancellationTokenSource? _cts;
	private bool _environmentLoadInProgress;
	private bool _environmentReloadPending;

	/// <summary>All ring nodes (outer actions + up to 6 inner env quick-chips).</summary>
	public ObservableCollection<RingItemViewModel> Items { get; } = new();

	/// <summary>Rows currently shown in the environment palette (filtered + sectioned).</summary>
	public ObservableCollection<EnvRowViewModel> FilteredEnvironments { get; } = new();

	/// <summary>True while clio's registered-environment catalog is being refreshed.</summary>
	[ObservableProperty]
	private bool _isRefreshingEnvironments;

	/// <summary>Raised after node positions are (re)computed so the view can lay out the ring.</summary>
	public event Action? LayoutChanged;

	/// <summary>
	/// Raised when the user activates a <see cref="ActionKind.GuidedInstall"/> ring action (Deploy Creatio).
	/// The host opens the guided Install form window — the ring VM never runs the install itself.
	/// </summary>
	public event Action? GuidedInstallRequested;

	/// <summary>
	/// Raised when the user activates a <see cref="ActionKind.GuidedUninstall"/> ring action (Uninstall Creatio).
	/// The host opens the guided Uninstall flow window — the ring VM never runs the uninstall itself, and
	/// opening the flow does NOT start anything: only the user's explicit Yes click inside it can.
	/// </summary>
	public event Action? GuidedUninstallRequested;

	/// <summary>Fixed canvas edge length used by the view (constant across all states).</summary>
	public double Diameter => CanvasSize;

	/// <summary>Accumulated streamed output of the last/active run.</summary>
	[ObservableProperty]
	private string _outputText = string.Empty;

	/// <summary>Compact hub caption (resting hint).</summary>
	[ObservableProperty]
	private string _focusCaption = "ready";

	/// <summary>Loud, non-modal notice text (e.g. hotkey conflict). Empty = hidden.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasHotkeyNotice))]
	private string _hotkeyNotice = string.Empty;

	/// <summary>Whether the settings / hotkey panel is open.</summary>
	[ObservableProperty]
	private bool _showSettings;

	/// <summary>Human-readable current hotkey gesture (for the settings panel).</summary>
	[ObservableProperty]
	private string _hotkeyDisplay = string.Empty;

	/// <summary>Path to the config file that owns the hotkey (for the settings panel).</summary>
	[ObservableProperty]
	private string _configPath = string.Empty;

	/// <summary>Whether a non-modal notice is showing.</summary>
	public bool HasHotkeyNotice => !string.IsNullOrEmpty(HotkeyNotice);

	/// <summary>Load-time actions.json validation error (empty = valid). Surfaced loudly in-app.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasCatalogError))]
	private string _catalogError = string.Empty;

	/// <summary>Whether the action catalog failed to load/validate.</summary>
	public bool HasCatalogError => !string.IsNullOrEmpty(CatalogError);

	/// <summary>
	/// Notice shown when a DIFFERENT clio-ring build is already running from another install location
	/// (empty = none). Explains why another build may be visible so the user is never confused about
	/// which build they launched.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasCrossBuildNotice))]
	private string _crossBuildNotice = string.Empty;

	/// <summary>Whether a cross-build conflict notice is showing.</summary>
	public bool HasCrossBuildNotice => !string.IsNullOrEmpty(CrossBuildNotice);

	/// <summary>Design/screenshot seam: sets a catalog validation error.</summary>
	public void DesignSetCatalogError(string message) => CatalogError = message;

	/// <summary>Command header shown in the drawer, e.g. "clio get-info -e ve".</summary>
	[ObservableProperty]
	private string _commandHeader = string.Empty;

	/// <summary>Whether the output bottom-sheet is expanded.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsOutputCollapsed))]
	private bool _isOutputOpen;

	/// <summary>Terminal/interim run state driving the end-state badge.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(OutcomeLabel))]
	[NotifyPropertyChangedFor(nameof(OutcomeBrush))]
	[NotifyPropertyChangedFor(nameof(HasOutcomeBadge))]
	[NotifyPropertyChangedFor(nameof(HasOutput))]
	[NotifyPropertyChangedFor(nameof(IsOutputCollapsed))]
	private RunOutcome _outcome = RunOutcome.None;

	/// <summary>Whether a command has produced output that remains available (expandable).</summary>
	public bool HasOutput => Outcome != RunOutcome.None;

	/// <summary>Whether the compact output bar (collapsed drawer) should show.</summary>
	public bool IsOutputCollapsed => HasOutput && !IsOutputOpen;

	/// <summary>True while a clio child process is running.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(CancelCommand))]
	private bool _isBusy;

	/// <summary>Set once the initial action set is populated (drives the first-paint stamp).</summary>
	public bool InitialActionsPopulated { get; private set; }

	// ---- Environment palette state ----

	/// <summary>Whether the environment palette overlay is open.</summary>
	[ObservableProperty]
	private bool _isPickerOpen;

	/// <summary>Type-to-filter query (matched against name + host).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasQuery))]
	[NotifyPropertyChangedFor(nameof(NoMatchText))]
	private string _searchQuery = string.Empty;

	/// <summary>Whether the search query is non-empty (drives the clear affordance).</summary>
	public bool HasQuery => !string.IsNullOrEmpty(SearchQuery);

	/// <summary>Highlighted palette row index (keyboard navigation).</summary>
	[ObservableProperty]
	private int _highlightedIndex;

	/// <summary>Active location filter.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsFilterAll))]
	[NotifyPropertyChangedFor(nameof(IsFilterLocal))]
	[NotifyPropertyChangedFor(nameof(IsFilterCloud))]
	private EnvFilter _locationFilter = EnvFilter.All;

	/// <summary>True when the current filter/query yields no environments (drives the empty state).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(NoMatchText))]
	private bool _hasNoMatches;

	/// <summary>Empty-state message.</summary>
	public string NoMatchText => SearchQuery.Trim().Length > 0
		? $"No environments match “{SearchQuery.Trim()}”"
		: "No environments";

	/// <summary>Selected environment name (persisted; the command target).</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasSelectedEnvironment))]
	[NotifyPropertyChangedFor(nameof(SelectedEnvironmentHint))]
	[NotifyPropertyChangedFor(nameof(SelectedEnvironmentAccessibleName))]
	private string _selectedEnvironmentName = "—";

	/// <summary>Accessible name for the hub (AutomationProperties.Name).</summary>
	public string SelectedEnvironmentAccessibleName => HasSelectedEnvironment
		? $"Selected environment {SelectedEnvironmentName}, {SelectedEnvironmentHint}. Activate to change."
		: "No environment selected. Activate to choose one.";

	/// <summary>Whether an environment is currently selected.</summary>
	public bool HasSelectedEnvironment => _allEnvironments.Any(e => e.Name == SelectedEnvironmentName);

	/// <summary>Compact hint for the selected env (host · Local/Cloud · flavour).</summary>
	public string SelectedEnvironmentHint {
		get {
			ClioEnvironment? env = _allEnvironments.FirstOrDefault(e => e.Name == SelectedEnvironmentName);
			return env is null
				? "no environment selected"
				: $"{(env.Host.Length > 0 ? env.Host : "?")} · {env.LocationLabel} · {env.FrameworkLabel}";
		}
	}

	/// <summary>Whether the All location filter is active.</summary>
	public bool IsFilterAll => LocationFilter == EnvFilter.All;

	/// <summary>Whether the Local location filter is active.</summary>
	public bool IsFilterLocal => LocationFilter == EnvFilter.Local;

	/// <summary>Whether the Cloud location filter is active.</summary>
	public bool IsFilterCloud => LocationFilter == EnvFilter.Cloud;

	// ---- Confirm dialog ----

	/// <summary>A pending risky action awaiting confirmation, if any.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasPendingConfirm))]
	private RingItemViewModel? _pendingConfirmItem;

	/// <summary>Confirmation prompt text.</summary>
	[ObservableProperty]
	private string _confirmMessage = string.Empty;

	/// <summary>Explicit consequence line for the pending confirm (empty = none), shown prominently.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasConfirmConsequence))]
	private string _confirmConsequence = string.Empty;

	/// <summary>Whether the pending confirm has an explicit consequence line to show.</summary>
	public bool HasConfirmConsequence => !string.IsNullOrEmpty(ConfirmConsequence);

	/// <summary>Guard: Run stays disabled until this arms shortly after opening.</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
	private bool _confirmArmed;

	/// <summary>
	/// Text the user types into the typed-confirm box. When <see cref="RequiresTypedConfirm"/> is set,
	/// Run stays disabled until this matches <see cref="ConfirmExpected"/> exactly (case-sensitive).
	/// </summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
	private string _confirmInput = string.Empty;

	/// <summary>Whether the pending confirmation additionally requires typing the exact env name.</summary>
	[ObservableProperty]
	private bool _requiresTypedConfirm;

	/// <summary>
	/// The exact environment name the user must type to confirm (captured when the confirm opens).
	/// Empty when no typed confirm is pending.
	/// </summary>
	public string ConfirmExpected { get; private set; } = string.Empty;

	/// <summary>Whether a confirmation dialog is currently required.</summary>
	public bool HasPendingConfirm => PendingConfirmItem is not null;

	/// <summary>End-state badge label.</summary>
	public string OutcomeLabel => Outcome switch {
		RunOutcome.Running => "RUNNING",
		RunOutcome.Success => "SUCCESS",
		RunOutcome.Failure => "FAILED",
		RunOutcome.Canceled => "CANCELED",
		_ => string.Empty
	};

	/// <summary>End-state badge colour.</summary>
	public IBrush OutcomeBrush => Outcome switch {
		RunOutcome.Running => RunningBrush,
		RunOutcome.Success => SuccessBrush,
		RunOutcome.Failure => FailureBrush,
		RunOutcome.Canceled => NeutralBrush,
		_ => NeutralBrush
	};

	/// <summary>Whether an end-state badge should show.</summary>
	public bool HasOutcomeBadge => Outcome != RunOutcome.None;

	/// <summary>
	/// Settings sub-view-model for the clio connection (default vs dev-clio override + connected-clio
	/// identity). Bound by the settings panel; refreshed when the panel opens.
	/// </summary>
	public ClioSettingsViewModel ClioSettings { get; }

	/// <summary>Parameterless ctor for design-time / fallback.</summary>
	public RingViewModel() : this(new ClioAdapter(), new ActionCatalogLoader(), new InMemoryEnvStateStore(), new NullActionCatalogWatcher(), environmentSettingsWatcher: new NullEnvironmentSettingsWatcher()) { }

	/// <summary>Primary DI constructor. The IPC client and settings store are optional so lightweight
	/// design-time / test constructions keep working; DI supplies both at runtime.</summary>
	public RingViewModel(IClioAdapter clio, IActionCatalogLoader catalogLoader, IEnvStateStore stateStore, IActionCatalogWatcher catalogWatcher, IClioIpcClient? ipcClient = null, IClioSettingsStore? settingsStore = null, IEnvironmentSettingsWatcher? environmentSettingsWatcher = null) {
		_clio = clio;
		_catalogLoader = catalogLoader;
		_stateStore = stateStore;
		_catalogWatcher = catalogWatcher;
		ClioSettings = new ClioSettingsViewModel(settingsStore ?? new ClioSettingsStore(), ipcClient);
		_state = _stateStore.Load();
		_channel = ResolveChannel();
		SelectedEnvironmentName = string.IsNullOrEmpty(_state.Selected) ? "—" : _state.Selected!;
		LoadInitialActions();

		// Hot-reload: apply edits to actions.json live. Events arrive off the UI thread; marshal.
		_catalogWatcher.Reloaded += cat => Dispatcher.UIThread.Post(() => ApplyReloadedCatalog(cat));
		_catalogWatcher.ReloadFailed += msg => Dispatcher.UIThread.Post(() => OnCatalogReloadFailed(msg));
		_catalogWatcher.Start();

		_environmentSettingsWatcher = environmentSettingsWatcher ?? new NullEnvironmentSettingsWatcher();
		_environmentSettingsWatcher.Changed += () => Dispatcher.UIThread.Post(() => _ = LoadEnvironmentsAsync());
		_environmentSettingsWatcher.Start();
	}

	/// <summary>Applies a valid hot-reloaded catalog: swaps the outer action nodes, keeps env chips + selection.</summary>
	private void ApplyReloadedCatalog(ActionCatalog catalog) {
		for (int i = Items.Count - 1; i >= 0; i--) {
			if (Items[i].Kind == RingItemKind.Action) {
				Items.RemoveAt(i);
			}
		}

		int insertAt = 0;
		foreach (LauncherAction action in catalog.Actions) {
			Items.Insert(insertAt++, RingItemViewModel.ForAction(action, SelectCommand));
		}

		CatalogError = string.Empty;
		RebuildLayout();
	}

	private void OnCatalogReloadFailed(string message) {
		// Keep the last-good catalog running; surface the same validation notice.
		CatalogError = message;
	}

	/// <summary>Stops the actions.json watcher (called on window close / shutdown).</summary>
	public void StopWatching() {
		_catalogWatcher.Stop();
		_environmentSettingsWatcher.Stop();
	}

	private static string ResolveChannel() {
		string? channel = AppSettingsReader.TryRead()?.Channel;
		return string.IsNullOrWhiteSpace(channel) ? "dev" : channel!.Trim();
	}

	/// <summary>Compact build badge shown in the ring corner: channel · git hash · build time.</summary>
	public string BuildBadge => BuildInfo.Badge(_channel);

	/// <summary>Full build identity (tooltip / settings / clipboard), includes app location.</summary>
	public string BuildIdentity => BuildInfo.Describe(_channel);

	private void LoadInitialActions() {
		try {
			ActionCatalog catalog = _catalogLoader.Load();
			foreach (LauncherAction action in catalog.Actions) {
				Items.Add(RingItemViewModel.ForAction(action, SelectCommand));
			}

			CatalogError = string.Empty;
		}
		catch (ActionCatalogException ex) {
			// Clear, load-time error naming the offending action/field. Surfaced in the ring notice,
			// the Settings panel, the tray tooltip, and the startup log.
			CatalogError = ex.Message;
			FocusCaption = "actions.json error";
			StartupLog.Log($"actions.json load error: {ex.Message}");
		}

		RebuildLayout();
		InitialActionsPopulated = true;
	}

	/// <summary>Discovers clio environments, prunes stale persisted state, builds quick-slots.</summary>
	public async Task LoadEnvironmentsAsync() {
		if (_environmentLoadInProgress) {
			_environmentReloadPending = true;
			return;
		}
		do {
			_environmentReloadPending = false;
			_environmentLoadInProgress = true;
			IsRefreshingEnvironments = true;
			try {
				IReadOnlyList<ClioEnvironment> environments = await _clio.ListEnvironmentsAsync().ConfigureAwait(true);
				SetEnvironments(environments);
				FocusCaption = _allEnvironments.Count > 0 ? "ready" : "no environments";
			}
			catch (Exception ex) {
				FocusCaption = $"discovery failed: {ex.Message}";
			}
			finally {
				_environmentLoadInProgress = false;
			}
		} while (_environmentReloadPending);
		IsRefreshingEnvironments = false;
	}

	/// <summary>Manual environment-catalog refresh exposed in the environment picker.</summary>
	[RelayCommand]
	private Task RefreshEnvironmentsAsync() => LoadEnvironmentsAsync();

	/// <summary>Design/screenshot seam: sets the environment set without spawning clio.</summary>
	public void SetEnvironments(IReadOnlyList<ClioEnvironment> environments) {
		_allEnvironments.Clear();
		_allEnvironments.AddRange(environments);

		var known = new HashSet<string>(_allEnvironments.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

		// Drop persisted names that no longer exist (no ghosts, no crash).
		_state.Pinned = _state.Pinned.Where(known.Contains).ToList();
		_state.Recents = _state.Recents.Where(known.Contains).ToList();
		if (!string.IsNullOrEmpty(_state.Selected) && !known.Contains(_state.Selected!)) {
			_state.Selected = null;
		}

		// Ensure a valid selection: persisted -> first pinned -> first recent -> first env.
		if (string.IsNullOrEmpty(_state.Selected)) {
			_state.Selected = _state.Pinned.FirstOrDefault()
				?? _state.Recents.FirstOrDefault()
				?? _allEnvironments.FirstOrDefault()?.Name;
		}

		SelectedEnvironmentName = _state.Selected ?? "—";
		_stateStore.Save(_state);

		RebuildQuickSlots();
		RebuildFiltered();
		OnPropertyChanged(nameof(HasSelectedEnvironment));
		OnPropertyChanged(nameof(SelectedEnvironmentHint));
		OnPropertyChanged(nameof(SelectedEnvironmentAccessibleName));
	}

	private IEnumerable<string> QuickSlotNames() {
		var known = new HashSet<string>(_allEnvironments.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var slots = new List<string>();

		void TryAdd(string? name) {
			if (!string.IsNullOrEmpty(name) && known.Contains(name!) && seen.Add(name!) && slots.Count < MaxQuickSlots) {
				slots.Add(name!);
			}
		}

		// Preferred order: active first, then pinned, then MRU.
		TryAdd(_state.Selected);
		foreach (string name in _state.Pinned) {
			TryAdd(name);
		}

		foreach (string name in _state.Recents) {
			TryAdd(name);
		}

		// Fresh profile (no pins/MRU yet): fill DETERMINISTICALLY from the env list so the inner
		// orbit is never a set of empty holes. These aren't fake "recents" — just stable defaults
		// until real usage history exists.
		foreach (ClioEnvironment env in _allEnvironments) {
			TryAdd(env.Name);
		}

		return slots;
	}

	private void RebuildQuickSlots() {
		for (int i = Items.Count - 1; i >= 0; i--) {
			if (Items[i].Kind == RingItemKind.Environment) {
				Items.RemoveAt(i);
			}
		}

		foreach (string name in QuickSlotNames()) {
			RingItemViewModel chip = RingItemViewModel.ForEnvironment(name, SelectCommand);
			chip.Selected = string.Equals(name, _state.Selected, StringComparison.OrdinalIgnoreCase);
			Items.Add(chip);
		}

		RebuildLayout();
	}

	// ---------- Environment palette ----------

	/// <summary>Opens the palette (from the hub or start-typing).</summary>
	[RelayCommand]
	private void OpenPicker() {
		SearchQuery = string.Empty;
		LocationFilter = EnvFilter.All;
		RebuildFiltered();
		HighlightedIndex = 0;
		IsPickerOpen = true;
	}

	/// <summary>Closes the palette, returning to the ring.</summary>
	[RelayCommand]
	private void ClosePicker() {
		IsPickerOpen = false;
	}

	/// <summary>Clears the search query (leaving the palette open).</summary>
	[RelayCommand]
	private void ClearQuery() {
		SearchQuery = string.Empty;
	}

	/// <summary>Toggles the pin of the currently highlighted row (does NOT change the active selection).</summary>
	public void TogglePinHighlighted() {
		if (HighlightedIndex >= 0 && HighlightedIndex < FilteredEnvironments.Count) {
			TogglePin(FilteredEnvironments[HighlightedIndex].Name);
		}
	}

	/// <summary>Moves the palette highlight (keyboard up/down), clamped.</summary>
	public void MoveHighlight(int delta) {
		if (FilteredEnvironments.Count == 0) {
			return;
		}

		int next = HighlightedIndex + delta;
		HighlightedIndex = Math.Clamp(next, 0, FilteredEnvironments.Count - 1);
	}

	/// <summary>Selects the highlighted palette row (Enter).</summary>
	[RelayCommand]
	private void ConfirmHighlighted() {
		if (HighlightedIndex >= 0 && HighlightedIndex < FilteredEnvironments.Count) {
			PickEnvironment(FilteredEnvironments[HighlightedIndex].Name);
		}
	}

	/// <summary>Selects an environment by name and closes the palette (mouse click / Enter).</summary>
	[RelayCommand]
	private void PickEnvironment(string? name) {
		SelectByName(name);
		IsPickerOpen = false;
	}

	/// <summary>Pins/unpins an environment (keeps the palette open).</summary>
	[RelayCommand]
	private void TogglePin(string? name) {
		if (string.IsNullOrEmpty(name)) {
			return;
		}

		if (_state.Pinned.Remove(name!)) {
			// unpinned
		}
		else {
			_state.Pinned.Insert(0, name!);
		}

		_stateStore.Save(_state);
		RebuildFiltered();
		RebuildQuickSlots();
	}

	/// <summary>Sets the location filter ("all" / "local" / "cloud").</summary>
	[RelayCommand]
	private void SetFilter(string? which) {
		LocationFilter = which?.ToLowerInvariant() switch {
			"local" => EnvFilter.Local,
			"cloud" => EnvFilter.Cloud,
			_ => EnvFilter.All
		};
		RebuildFiltered();
		HighlightedIndex = 0;
	}

	partial void OnSearchQueryChanged(string value) {
		RebuildFiltered();
		HighlightedIndex = 0;
	}

	partial void OnHighlightedIndexChanged(int value) {
		UpdateHighlight();
	}

	private void UpdateHighlight() {
		for (int i = 0; i < FilteredEnvironments.Count; i++) {
			FilteredEnvironments[i].IsHighlighted = i == HighlightedIndex;
		}
	}

	private void SelectByName(string? name) {
		if (string.IsNullOrEmpty(name) || _allEnvironments.All(e => !string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))) {
			return;
		}

		_state.Selected = name;
		_state.Recents.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
		_state.Recents.Insert(0, name!);
		if (_state.Recents.Count > MaxRecents) {
			_state.Recents.RemoveRange(MaxRecents, _state.Recents.Count - MaxRecents);
		}

		_stateStore.Save(_state);
		SelectedEnvironmentName = name!;
		OnPropertyChanged(nameof(HasSelectedEnvironment));
		OnPropertyChanged(nameof(SelectedEnvironmentHint));
		RebuildQuickSlots();
	}

	private void RebuildFiltered() {
		FilteredEnvironments.Clear();

		string query = SearchQuery.Trim();
		var pinned = new HashSet<string>(_state.Pinned, StringComparer.OrdinalIgnoreCase);
		var recents = new HashSet<string>(_state.Recents, StringComparer.OrdinalIgnoreCase);

		bool Matches(ClioEnvironment e) {
			if (LocationFilter == EnvFilter.Local && e.Location != EnvLocation.Local) {
				return false;
			}

			if (LocationFilter == EnvFilter.Cloud && e.Location != EnvLocation.Cloud) {
				return false;
			}

			return query.Length == 0
				|| e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| e.Host.Contains(query, StringComparison.OrdinalIgnoreCase);
		}

		List<ClioEnvironment> matches = _allEnvironments.Where(Matches).ToList();

		List<ClioEnvironment> pinnedRows = _state.Pinned
			.Select(n => matches.FirstOrDefault(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase)))
			.Where(e => e is not null).Select(e => e!).ToList();

		List<ClioEnvironment> recentRows = _state.Recents
			.Select(n => matches.FirstOrDefault(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase)))
			.Where(e => e is not null && !pinned.Contains(e!.Name)).Select(e => e!).ToList();

		// Rank results by match quality: exact name > prefix name > substring name > host-only.
		List<ClioEnvironment> otherRows = matches
			.Where(e => !pinned.Contains(e.Name) && !recents.Contains(e.Name))
			.OrderBy(e => MatchRank(e, query))
			.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		AddSection(pinnedRows, "PINNED", pinned);
		AddSection(recentRows, "RECENT", pinned);
		AddSection(otherRows, query.Length > 0 ? "RESULTS" : "ALL", pinned);

		HasNoMatches = FilteredEnvironments.Count == 0;
		if (FilteredEnvironments.Count == 0) {
			HighlightedIndex = 0;
		}
		else if (HighlightedIndex >= FilteredEnvironments.Count) {
			HighlightedIndex = FilteredEnvironments.Count - 1;
		}
		else if (HighlightedIndex < 0) {
			HighlightedIndex = 0;
		}

		UpdateHighlight();
	}

	/// <summary>Ranks a match: 0 exact name, 1 prefix name, 2 substring name, 3 host-only.</summary>
	private static int MatchRank(ClioEnvironment e, string query) {
		if (query.Length == 0) {
			return 2;
		}

		if (e.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) {
			return 0;
		}

		if (e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) {
			return 1;
		}

		if (e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) {
			return 2;
		}

		return 3; // matched on host only
	}

	private void AddSection(List<ClioEnvironment> envs, string label, HashSet<string> pinned) {
		for (int i = 0; i < envs.Count; i++) {
			ClioEnvironment e = envs[i];
			FilteredEnvironments.Add(new EnvRowViewModel {
				Name = e.Name,
				Host = e.Host,
				Location = e.LocationLabel,
				Framework = e.FrameworkLabel,
				SectionLabel = i == 0 ? label : null,
				IsPinned = pinned.Contains(e.Name),
				TogglePinCommand = TogglePinCommand
			});
		}
	}

	// ---------- Design/screenshot seam ----------

	/// <summary>Design seam: freezes the drawer in a given outcome with canned output.</summary>
	public void DesignShowOutput(string commandHeader, RunOutcome outcome, string output) {
		CommandHeader = commandHeader;
		Outcome = outcome;
		OutputText = output;
		IsOutputOpen = true;
		IsBusy = outcome == RunOutcome.Running;
	}

	// ---------- Layout ----------

	private void RebuildLayout() {
		LayoutOrbit(RingOrbit.Inner, InnerRadius);
		LayoutOrbit(RingOrbit.Outer, OuterRadius);
		LayoutChanged?.Invoke();
	}

	private void LayoutOrbit(RingOrbit orbit, double radius) {
		var nodes = Items.Where(i => i.Orbit == orbit).ToList();
		int n = nodes.Count;
		for (int i = 0; i < n; i++) {
			double angle = (2.0 * Math.PI * i / n) - (Math.PI / 2.0);
			double cos = Math.Cos(angle);
			double sin = Math.Sin(angle);
			double half = nodes[i].NodeSize / 2.0;

			nodes[i].X = Center + (radius * cos) - half;
			nodes[i].Y = Center + (radius * sin) - half;
			nodes[i].LabelX = Center + (LabelRadius * cos) - LabelHalfWidth;
			nodes[i].LabelY = Center + (LabelRadius * sin) - 9;
		}
	}

	// ---------- Selection / run ----------

	/// <summary>Handles selection of a ring node (env chip = set target; action = run/confirm).</summary>
	[RelayCommand]
	private async Task SelectAsync(RingItemViewModel? item) {
		if (item is null || IsBusy) {
			return;
		}

		// Env quick-chip: just set the active target (no run).
		if (item.Kind == RingItemKind.Environment) {
			SelectByName(item.EnvironmentName);
			return;
		}

		if (item.Action is { } action) {
			// Listed-env guard (destructive only): a DESTRUCTIVE action that targets an env parameter must
			// have a currently-listed env selected. When the hub shows "—" (no selection), refuse to open
			// confirm and tell the user why, rather than risking an irreversible op against no/ambiguous
			// target. Non-destructive actions keep their prior behaviour (e.g. get-info -> --version).
			if (action.Risk != Risk.None && ActionTargetsEnvParameter(action) && !HasSelectedEnvironment) {
				FocusCaption = "Select an environment first.";
				return;
			}

			if (action.Risk != Risk.None) {
				OpenConfirm(item, action);
				return;
			}
		}

		await ExecuteItemAsync(item).ConfigureAwait(true);
	}

	/// <summary>Whether the action carries an Env parameter (mirrors <see cref="ResolveEnvParameter"/> applicability).</summary>
	private static bool ActionTargetsEnvParameter(LauncherAction action) =>
		action.Parameters.Any(p => p.ParameterType == ParameterType.Env);

	private void OpenConfirm(RingItemViewModel item, LauncherAction action) {
		string env = SelectedEnvironmentName;
		string lead = string.IsNullOrWhiteSpace(action.ConfirmText)
			? $"Run '{action.Title}' against '{env}'?"
			: action.ConfirmText!.Replace("{env}", env);

		// Append the RESOLVED target so the user sees exactly which endpoint is affected.
		ConfirmMessage = $"{lead}\n\nTarget: {env} · {SelectedEnvironmentHint}";
		RequiresTypedConfirm = action.RequireTypedConfirm;
		ConfirmExpected = env;
		ConfirmConsequence = action.ConsequenceText ?? string.Empty;
		ConfirmInput = string.Empty;
		PendingConfirmItem = item;
		ConfirmArmed = false;
		_ = ArmConfirmAsync();
	}

	private async Task ArmConfirmAsync() {
		await Task.Delay(450).ConfigureAwait(true);
		if (HasPendingConfirm) {
			ConfirmArmed = true;
		}
	}

	/// <summary>Confirms and runs the pending risky action (only once armed).</summary>
	[RelayCommand(CanExecute = nameof(CanConfirm))]
	private async Task ConfirmAsync() {
		RingItemViewModel? item = PendingConfirmItem;
		PendingConfirmItem = null;
		ConfirmMessage = string.Empty;
		ConfirmArmed = false;
		RequiresTypedConfirm = false;
		ConfirmExpected = string.Empty;
		ConfirmConsequence = string.Empty;
		ConfirmInput = string.Empty;
		if (item is not null) {
			await ExecuteItemAsync(item).ConfigureAwait(true);
		}
	}

	private bool CanConfirm() =>
		ConfirmArmed
		&& (!RequiresTypedConfirm || string.Equals(ConfirmInput.Trim(), ConfirmExpected, StringComparison.Ordinal));

	/// <summary>Dismisses a pending confirmation without running.</summary>
	[RelayCommand]
	private void DismissConfirm() {
		PendingConfirmItem = null;
		ConfirmMessage = string.Empty;
		ConfirmArmed = false;
		RequiresTypedConfirm = false;
		ConfirmExpected = string.Empty;
		ConfirmConsequence = string.Empty;
		ConfirmInput = string.Empty;
	}

	/// <summary>Collapses the drawer to the compact output bar (output is retained, not discarded).</summary>
	[RelayCommand]
	private void CollapseOutput() {
		IsOutputOpen = false;
	}

	/// <summary>Re-expands the SAME output (buffered lines preserved).</summary>
	[RelayCommand]
	private void ExpandOutput() {
		IsOutputOpen = true;
	}

	/// <summary>Clears the output entirely (removes the bar); next run replaces it anyway.</summary>
	[RelayCommand]
	private void ClearOutput() {
		IsOutputOpen = false;
		Outcome = RunOutcome.None;
		OutputText = string.Empty;
		CommandHeader = string.Empty;
	}

	/// <summary>Opens the settings / hotkey panel.</summary>
	[RelayCommand]
	private void OpenSettings() {
		// Re-read the persisted dev-clio override and the live handshake so the panel always shows the
		// current override + which clio is actually connected.
		ClioSettings.Refresh();
		ShowSettings = true;
	}

	/// <summary>Closes the settings / hotkey panel.</summary>
	[RelayCommand]
	private void CloseSettings() {
		ShowSettings = false;
	}

	/// <summary>Records the active hotkey info for the settings panel.</summary>
	public void SetHotkeyInfo(string display, string configPath) {
		HotkeyDisplay = display;
		ConfigPath = configPath;
	}

	/// <summary>Shows a loud, non-modal notice (e.g. hotkey conflict). Pass empty to clear.</summary>
	public void SetHotkeyNotice(string message) {
		HotkeyNotice = message;
	}

	private async Task ExecuteItemAsync(RingItemViewModel item) {
		LauncherAction action = item.Action!;
		switch (action.Kind) {
			case ActionKind.ClioCommand:
				ClioCommandSpec spec = action.ClioCommand!;
				await RunClioAsync(new ClioInvocation {
					Verb = spec.Verb,
					Args = spec.Args,
					EnvName = spec.EnvName ?? ResolveEnvParameter(action)
				}, item).ConfigureAwait(true);
				break;
			case ActionKind.OpenUrl:
				OpenShell(action.OpenUrl!.Url);
				break;
			case ActionKind.OpenPath:
				OpenShell(action.OpenPath!.Path);
				break;
			case ActionKind.GuidedInstall:
				// The ring is the sole initiator: opening the form does NOT start an install; the user's
				// explicit Install click inside the form is the only trigger for the real operation.
				GuidedInstallRequested?.Invoke();
				break;
			case ActionKind.GuidedUninstall:
				// The ring is the sole initiator: opening the flow does NOT start an uninstall; the user's
				// explicit Yes click inside the confirm is the only trigger for the real operation.
				GuidedUninstallRequested?.Invoke();
				break;
		}
	}

	private string? ResolveEnvParameter(LauncherAction action) {
		foreach (ParameterDescriptor p in action.Parameters) {
			if (p.ParameterType == ParameterType.Env) {
				return HasSelectedEnvironment ? SelectedEnvironmentName : null;
			}
		}

		return null;
	}

	/// <summary>Runs a clio invocation, streaming output into the drawer. Falls back to --version.</summary>
	public async Task RunClioAsync(ClioInvocation invocation, RingItemViewModel? activeItem = null) {
		if (IsBusy) {
			return;
		}

		if (invocation.Verb == "get-info" && string.IsNullOrWhiteSpace(invocation.EnvName)) {
			invocation = new ClioInvocation { Verb = "--version" };
		}

		ResetNodeStates();
		if (activeItem is not null) {
			activeItem.State = NodeState.Running;
		}

		_cts = new CancellationTokenSource();
		_output.Clear();
		OutputText = string.Empty;
		IsBusy = true;
		IsOutputOpen = true;
		Outcome = RunOutcome.Running;
		CommandHeader = "clio " + invocation.Verb
			+ (string.IsNullOrWhiteSpace(invocation.EnvName) ? string.Empty : $" -e {invocation.EnvName}");

		try {
			ClioRunResult result = await _clio.RunAsync(
				invocation,
				line => Dispatcher.UIThread.Post(() => AppendLine(FormatLine(line))),
				_cts.Token).ConfigureAwait(true);

			if (result.Cancelled) {
				Outcome = RunOutcome.Canceled;
				SetActiveState(activeItem, NodeState.Idle);
			}
			else if (result.ExitCode == 0) {
				Outcome = RunOutcome.Success;
				SetActiveState(activeItem, NodeState.Success);
			}
			else {
				Outcome = RunOutcome.Failure;
				SetActiveState(activeItem, NodeState.Failure);
			}
		}
		catch (OperationCanceledException) {
			Outcome = RunOutcome.Canceled;
			SetActiveState(activeItem, NodeState.Idle);
		}
		finally {
			IsBusy = false;
			_cts?.Dispose();
			_cts = null;
		}
	}

	private static void SetActiveState(RingItemViewModel? item, NodeState state) {
		if (item is not null) {
			item.State = state;
		}
	}

	private void ResetNodeStates() {
		foreach (RingItemViewModel item in Items) {
			item.State = NodeState.Idle;
		}
	}

	/// <summary>Cancels the active run and kills the child process tree.</summary>
	[RelayCommand(CanExecute = nameof(CanCancel))]
	private void Cancel() {
		_cts?.Cancel();
	}

	private bool CanCancel() => IsBusy;

	private static string FormatLine(ClioOutputLine line) =>
		line.Stream == ClioStream.Stderr ? $"! {line.Text}" : line.Text;

	private void AppendLine(string text) {
		_output.AppendLine(text);
		OutputText = _output.ToString();
	}

	private void OpenShell(string target) {
		try {
			Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
			FocusCaption = $"opened {target}";
		}
		catch (Exception ex) {
			FocusCaption = $"failed to open: {ex.Message}";
		}
	}
}
