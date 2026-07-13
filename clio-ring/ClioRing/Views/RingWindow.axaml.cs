using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClioRing.Diagnostics;
using ClioRing.Interop;
using ClioRing.Models;
using ClioRing.Services;
using ClioRing.ViewModels;

namespace ClioRing.Views;

/// <summary>
/// Frameless, translucent host for the radial ring. It is the resident window summoned by the
/// global hotkey. It owns the hotkey registration and stamps the cold-start (process-start ->
/// first-paint) and warm (hotkey-received -> interactive) latency metrics.
/// </summary>
public partial class RingWindow : Window {
	private readonly RingViewModel _viewModel;
	private readonly LaunchOptions _options;
	private readonly HotkeyGesture _gesture;
	private readonly IWindowPlacementStore _placementStore;
	private readonly WindowsHotkey _hotkey = new();
	private readonly DispatcherTimer _saveDebounce;

	/// <summary>
	/// Optional extra step the interaction soak runs after its show/hide cycles (before the exit assert).
	/// Stories 8 + 9 wire this to open + render the guided Install form and the guided Uninstall flow (with
	/// their pipelines) so the soak exercises those windows too. Null when unset (e.g. design-time). Set once
	/// at startup.
	/// </summary>
	public static Func<Task>? SoakExtraStep { get; set; }

	private bool _initialized;
	private bool _coldStamped;
	private bool _benchActive;
	private bool _restoringPlacement;
	private int _benchSampleIndex;
	private TaskCompletionSource? _benchCycle;

	/// <summary>Design-time constructor.</summary>
	public RingWindow() : this(new RingViewModel(), LaunchOptions.Current, HotkeyGesture.Default, new WindowPlacementStore()) { }

	/// <summary>Primary constructor.</summary>
	public RingWindow(RingViewModel viewModel, LaunchOptions options, HotkeyGesture gesture, IWindowPlacementStore placementStore) {
		_viewModel = viewModel;
		_options = options;
		_gesture = gesture;
		_placementStore = placementStore;
		DataContext = viewModel;
		InitializeComponent();

		_saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
		_saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SavePlacement(); };
		PositionChanged += OnPositionChanged;
	}

	/// <summary>Shows, activates and briefly tops the ring (tray "Show ring" + second-launch signal).</summary>
	public void ShowRing() {
		_ = ShowRingAsync();
	}

	private async Task ShowRingAsync() {
		try {
			if (!IsVisible) {
				Show();
			}
			// clio environments can be added/removed while this resident process is alive (including by
			// the Ring's own guided deploy/uninstall). Refresh on every summon; the VM coalesces overlap.
			_ = LoadEnvironmentsGuardedAsync();

			Activate();
			Topmost = true;
			await Task.Delay(450).ConfigureAwait(true);
			Topmost = false;
		}
		catch (Exception ex) {
			StartupLog.Log($"ShowRing error: {ex.Message}");
		}
	}

	private void HideRing() {
		try {
			Hide();
		}
		catch (Exception ex) {
			StartupLog.Log($"HideRing error: {ex.Message}");
		}
	}

	private void InitializeComponent() {
		AvaloniaXamlLoader.Load(this);
	}

	/// <inheritdoc />
	protected override void OnOpened(EventArgs e) {
		base.OnOpened(e);

		// ONE-TIME init: OnOpened can fire again on a hide->show cycle. Hooking the hotkey or
		// re-loading environments more than once caused the WndProc self-recursion / redundant work.
		if (!_initialized) {
			_initialized = true;
			StartupLog.Log("ring window opened (first)");
			RestorePlacement();
			TryInstallHotkey();

			// Cold-start: stamp on the first rendered frame that includes the initial action set.
			RequestAnimationFrame(_ => OnFirstPaint());
		}

		// Show-on-launch (safe to repeat): do not depend on the hotkey for the window to first appear.
		ShowRing();
	}

	// ---------- Movable frameless window + persistence ----------

	/// <inheritdoc />
	protected override void OnPointerPressed(PointerPressedEventArgs e) {
		base.OnPointerPressed(e);

		// Drag the frameless window from any EMPTY background area (not nodes/hub/drawer/buttons),
		// and never while an overlay is up.
		if (e.Handled
			|| !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
			|| _viewModel.IsPickerOpen || _viewModel.HasPendingConfirm || _viewModel.ShowSettings) {
			return;
		}

		if (IsBackground(e.Source as Visual)) {
			BeginMoveDrag(e);
		}
	}

	private static bool IsBackground(Visual? source) {
		if (source is null) {
			return true;
		}

		foreach (Visual v in source.GetSelfAndVisualAncestors()) {
			switch (v) {
				case Button:
				case ListBox:
				case ListBoxItem:
				case TextBox:
				case ScrollViewer:
				case SelectableTextBlock:
					return false;
				case Border b when b.Classes.Contains("node"):
					return false;
			}
		}

		return true;
	}

	private void OnPositionChanged(object? sender, PixelPointEventArgs e) {
		if (_restoringPlacement || !_initialized) {
			return;
		}

		// Debounce: dragging fires many events; persist once movement settles.
		_saveDebounce.Stop();
		_saveDebounce.Start();
	}

	private void RestorePlacement() {
		WindowPlacement? placement = _placementStore.Load();
		if (placement is null || Screens is null) {
			return; // keep default CenterScreen
		}

		Screen? screen = Screens.All.FirstOrDefault(s => ScreenKey(s) == placement.ScreenKey)
			?? Screens.ScreenFromWindow(this)
			?? Screens.Primary;
		if (screen is null) {
			return;
		}

		_restoringPlacement = true;
		try {
			PixelRect wa = screen.WorkingArea;
			(int wpx, int hpx) = EstimatePixelSize(screen.Scaling);
			int x = Math.Clamp(placement.X, wa.X, Math.Max(wa.X, wa.X + wa.Width - wpx));
			int y = Math.Clamp(placement.Y, wa.Y, Math.Max(wa.Y, wa.Y + wa.Height - hpx));
			WindowStartupLocation = WindowStartupLocation.Manual;
			Position = new PixelPoint(x, y);
			StartupLog.Log($"window placement restored to {x},{y} on screen {placement.ScreenKey}");
		}
		finally {
			_restoringPlacement = false;
		}
	}

	private void SavePlacement() {
		try {
			if (Screens is null) {
				return;
			}

			Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
			if (screen is null) {
				return;
			}

			_placementStore.Save(new WindowPlacement {
				X = Position.X,
				Y = Position.Y,
				ScreenKey = ScreenKey(screen),
				Scaling = screen.Scaling
			});
		}
		catch (Exception ex) {
			StartupLog.Log($"SavePlacement skipped: {ex.Message}");
		}
	}

	/// <summary>Centres the ring on its current monitor and persists that position.</summary>
	public void CenterOnScreen() {
		if (Screens is null) {
			return;
		}

		Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
		if (screen is null) {
			return;
		}

		PixelRect wa = screen.WorkingArea;
		(int wpx, int hpx) = EstimatePixelSize(screen.Scaling);
		WindowStartupLocation = WindowStartupLocation.Manual;
		Position = new PixelPoint(wa.X + ((wa.Width - wpx) / 2), wa.Y + ((wa.Height - hpx) / 2));
		SavePlacement();
	}

	/// <summary>Forgets the saved position and re-centres.</summary>
	public void ResetPosition() {
		_placementStore.Clear();
		CenterOnScreen();
	}

	private (int Width, int Height) EstimatePixelSize(double scaling) {
		double w = ClientSize.Width > 1 ? ClientSize.Width : 532;
		double h = ClientSize.Height > 1 ? ClientSize.Height : 560;
		return ((int)(w * scaling), (int)(h * scaling));
	}

	private static string ScreenKey(Screen s) =>
		$"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}";

	private async Task LoadEnvironmentsGuardedAsync() {
		try {
			await _viewModel.LoadEnvironmentsAsync().ConfigureAwait(true);
		}
		catch (Exception ex) {
			StartupLog.Log($"env load error: {ex.Message}");
		}
	}

	/// <summary>Refreshes registered environments after an in-process deploy/uninstall completes.</summary>
	public void RefreshEnvironments() => _ = LoadEnvironmentsGuardedAsync();

	/// <inheritdoc />
	protected override void OnKeyDown(KeyEventArgs e) {
		// Esc is the explicit dismissal. The ring never auto-hides on transient focus loss
		// (an IDE/terminal focus-steal at launch used to make it vanish); it stays until the
		// user explicitly dismisses (Esc / hotkey toggle / tray) or the app is quit.
		if (e.Key == Key.Escape) {
			// Esc collapses an expanded drawer to the bar (output preserved) before hiding the ring.
			if (_viewModel.IsOutputOpen) {
				_viewModel.CollapseOutputCommand.Execute(null);
			}
			else {
				HideRing();
			}

			e.Handled = true;
		}

		base.OnKeyDown(e);
	}

	/// <inheritdoc />
	protected override void OnTextInput(TextInputEventArgs e) {
		// Start typing → open the env palette, but ONLY from the idle ring. Never steal keystrokes
		// when the palette/confirm/settings overlays are up, a run is streaming, or focus is in a
		// text field (search box, etc.).
		if (RingIsIdleForTyping() && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text![0])) {
			_viewModel.OpenPickerCommand.Execute(null);
			_viewModel.SearchQuery = e.Text!;
			e.Handled = true;
		}

		base.OnTextInput(e);
	}

	private bool RingIsIdleForTyping() {
		if (_viewModel.IsPickerOpen || _viewModel.HasPendingConfirm || _viewModel.ShowSettings || _viewModel.IsBusy) {
			return false;
		}

		// Not while focus is in an editable text field.
		return FocusManager?.GetFocusedElement() is not TextBox;
	}

	/// <inheritdoc />
	protected override void OnClosing(WindowClosingEventArgs e) {
		// Persist final position BEFORE the platform window is disposed (Screens is invalid in OnClosed).
		_saveDebounce.Stop();
		if (_initialized) {
			SavePlacement();
		}

		// Release the actions.json file watcher on shutdown.
		_viewModel.StopWatching();

		base.OnClosing(e);
	}

	/// <inheritdoc />
	protected override void OnClosed(EventArgs e) {
		_hotkey.Dispose();
		base.OnClosed(e);
	}

	private void TryInstallHotkey() {
		_viewModel.SetHotkeyInfo(_gesture.Display, AppSettingsReader.SettingsPath);
		try {
			if (TryGetPlatformHandle()?.Handle is { } handle && handle != IntPtr.Zero) {
				if (_hotkey.Install(handle, OnHotkeyReceived, _gesture)) {
					StartupLog.Log($"hotkey registered: {_gesture.Display}");
					_viewModel.SetHotkeyNotice(string.Empty);
				}
				else {
					// LOUD, non-silent: log with Win32 code + surface a non-modal on-screen notice.
					StartupLog.Log($"hotkey registration FAILED: {_gesture.Display} win32={_hotkey.LastError}");
					_viewModel.SetHotkeyNotice(
						$"Hotkey {_gesture.Display} is unavailable (already in use). Change it in Settings.");
				}
			}
			else {
				StartupLog.Log("hotkey skipped: no platform handle");
			}
		}
		catch (Exception ex) {
			StartupLog.Log($"hotkey install exception: {ex.Message}");
			_viewModel.SetHotkeyNotice($"Hotkey unavailable: {ex.Message}");
		}
	}

	private void OnFirstPaint() {
		if (_coldStamped) {
			return;
		}

		_coldStamped = true;
		if (_viewModel.InitialActionsPopulated) {
			double coldMs = Metrics.ElapsedMsSince(Metrics.ProcessStartTicks);
			Metrics.RecordColdStart(coldMs);
		}

		if (_options.HotReloadTest) {
			_ = RunHotReloadTestAsync();
		}
		else if (_options.PlaceX.HasValue && _options.PlaceY.HasValue) {
			_ = PlaceAndExitAsync(_options.PlaceX.Value, _options.PlaceY.Value);
		}
		else if (_options.SoakCycles > 0) {
			_ = RunSoakAsync(_options.SoakCycles);
		}
		else if (_options.Smoke) {
			_ = SmokeExitAsync();
		}
		else if (_options.BenchHotkeySamples > 0) {
			_ = RunHotkeyBenchmarkAsync(_options.BenchHotkeySamples);
		}
		else if (_options.AutoClio) {
			_ = RunAutoClioThenMaybeExitAsync();
		}
		else if (_options.ExitAfterPaint) {
			_ = ExitAfterDelayAsync();
		}
	}

	// Interaction soak: repeatedly exercise the exact paths that overflowed — env load, a clio run
	// through the adapter, and show/hide/tray/hotkey cycles. Asserts exactly ONE WndProc hook install
	// and no recursion (a StackOverflow would kill the process => non-zero/hard exit the harness sees).
	private async Task RunSoakAsync(int cycles) {
		try {
			await _viewModel.LoadEnvironmentsAsync().ConfigureAwait(true);

			// A real clio command through the adapter (the OnOpened+adapter combo that overflowed).
			await _viewModel.RunClioAsync(new ClioInvocation { Verb = "--version" }).ConfigureAwait(true);

			for (int i = 0; i < cycles; i++) {
				HideRing();                       // hide
				await Task.Delay(8).ConfigureAwait(true);
				Show();                           // tray "Show" equivalent
				Activate();
				await Task.Delay(8).ConfigureAwait(true);
				_hotkey.PostSyntheticHotkey();    // hotkey receipt path (would recurse if broken)
				await Task.Delay(8).ConfigureAwait(true);
			}

			// Stories 8 + 9: exercise the guided Install form and the guided Uninstall flow (with their
			// pipelines) in the same soak (a crash there fails the process, which headless screenshots
			// would otherwise mask).
			if (SoakExtraStep is not null) {
				await SoakExtraStep().ConfigureAwait(true);
				StartupLog.Log("soak: install form + uninstall flow + pipelines opened and closed");
			}

			int installs = WindowsHotkey.InstallCount;
			StartupLog.Log($"soak done: cycles={cycles} hookInstalls={installs}");

			if (installs == 1) {
				Environment.Exit(0);
			}
			else {
				StartupLog.Log($"soak FAILED: expected exactly 1 hook install, got {installs}");
				Environment.Exit(3);
			}
		}
		catch (Exception ex) {
			StartupLog.Log($"soak FAILED: {ex}");
			Environment.Exit(1);
		}
	}

	// Hot-reload harness: simulate an editor saving actions.json. Valid save must update the ring
	// live; invalid save must keep the last-good catalog + raise the notice. Restores the original.
	private async Task RunHotReloadTestAsync() {
		string path = System.IO.Path.Combine(AppContext.BaseDirectory, "actions.json");
		string original = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : string.Empty;
		bool validOk = false;
		bool invalidKeptLastGood = false;

		try {
			const string validCatalog =
				"{ \"$schema\": \"./actions.schema.json\", \"Actions\": [ { \"Id\": \"hot-test\", " +
				"\"Title\": \"Hot Reload OK\", \"ShortTitle\": \"HotOK\", \"Icon\": \"check\", " +
				"\"Kind\": \"ClioCommand\", \"ClioCommand\": { \"Verb\": \"--version\", \"Args\": [] }, " +
				"\"Parameters\": [], \"Risk\": \"None\" } ] }";
			const string invalidCatalog =
				"{ \"Actions\": [ { \"Id\": \"bad\", \"Title\": \"Bad\", \"Kind\": \"OpenUrl\" } ] }";

			// 1) VALID reload -> ring should show the new action, no error.
			System.IO.File.WriteAllText(path, validCatalog);
			await Task.Delay(1100).ConfigureAwait(true);
			int actionCount = _viewModel.Items.Count(i => i.Kind == RingItemKind.Action);
			bool hasHot = _viewModel.Items.Any(i => i.Kind == RingItemKind.Action && i.FullLabel == "Hot Reload OK");
			validOk = hasHot && actionCount == 1 && !_viewModel.HasCatalogError;
			StartupLog.Log($"hotreload valid: hasHot={hasHot} actions={actionCount} error={_viewModel.HasCatalogError} -> {validOk}");

			// 2) INVALID reload -> keep last-good (still the single hot action), raise the notice.
			System.IO.File.WriteAllText(path, invalidCatalog);
			await Task.Delay(1100).ConfigureAwait(true);
			int keptCount = _viewModel.Items.Count(i => i.Kind == RingItemKind.Action);
			invalidKeptLastGood = _viewModel.HasCatalogError && keptCount == 1;
			StartupLog.Log($"hotreload invalid: error={_viewModel.HasCatalogError} keptActions={keptCount} -> {invalidKeptLastGood}");
		}
		catch (Exception ex) {
			StartupLog.Log($"hotreload test FAILED: {ex}");
		}
		finally {
			if (original.Length > 0) {
				System.IO.File.WriteAllText(path, original);
				await Task.Delay(700).ConfigureAwait(true);
			}
		}

		StartupLog.Log($"hotreload test result: valid={validOk} invalidKeptLastGood={invalidKeptLastGood}");
		Environment.Exit(validOk && invalidKeptLastGood ? 0 : 4);
	}

	// Placement harness: move to (x,y) — deliberately allowed off-screen — let the debounced save
	// persist it, then exit. A subsequent normal launch must restore CLAMPED on-screen.
	private async Task PlaceAndExitAsync(int x, int y) {
		try {
			WindowStartupLocation = WindowStartupLocation.Manual;
			Position = new PixelPoint(x, y);
			await Task.Delay(900).ConfigureAwait(true); // > debounce interval so the save lands
			SavePlacement();
			StartupLog.Log($"place harness: requested {x},{y}");
			Environment.Exit(0);
		}
		catch (Exception ex) {
			StartupLog.Log($"place harness FAILED: {ex}");
			Environment.Exit(1);
		}
	}

	// Real-window smoke test: hold long enough for the entrance animation + env load to run,
	// then exit 0. If any of that path threw, the process would exit non-zero instead.
	private async Task SmokeExitAsync() {
		try {
			await Task.Delay(1200).ConfigureAwait(true);
			StartupLog.Log($"smoke ok (reduced-motion={_options.ReducedMotion})");
			Environment.Exit(0);
		}
		catch (Exception ex) {
			StartupLog.Log($"smoke FAILED: {ex}");
			Environment.Exit(1);
		}
	}

	/// <summary>Handles a WM_HOTKEY receipt (already stamped in the native thunk).</summary>
	private void OnHotkeyReceived() {
		if (!IsVisible) {
			Show();
			Activate();
			// Interactive == the ring is visible, laid out, and accepting input: the next frame.
			RequestAnimationFrame(_ => OnBecameInteractive());
		}
		else if (!_benchActive) {
			// Manual toggle: hide when already visible.
			Hide();
		}
		else {
			RequestAnimationFrame(_ => OnBecameInteractive());
		}
	}

	private void OnBecameInteractive() {
		double ms = Metrics.ElapsedMs(Metrics.HotkeyReceivedTicks, Stopwatch.GetTimestamp());
		if (_benchActive) {
			Metrics.RecordHotkey(_benchSampleIndex, ms);
			_benchCycle?.TrySetResult();
		}
		else {
			_viewModel.FocusCaption = $"summoned {ms:F0} ms";
		}
	}

	/// <summary>
	/// Runs N automated warm samples by hiding the window then posting a synthetic WM_HOTKEY,
	/// which drives the exact native receipt -> interactive path a real key press would.
	/// </summary>
	private async Task RunHotkeyBenchmarkAsync(int samples) {
		_benchActive = true;
		await Task.Delay(400).ConfigureAwait(true); // let cold paint settle

		for (int i = 1; i <= samples; i++) {
			Hide();
			await Task.Delay(70).ConfigureAwait(true);

			_benchSampleIndex = i;
			_benchCycle = new TaskCompletionSource();
			_hotkey.PostSyntheticHotkey();

			await _benchCycle.Task.ConfigureAwait(true);
			await Task.Delay(40).ConfigureAwait(true);
		}

		_benchActive = false;

		if (_options.AutoClio) {
			await RunAutoClioThenMaybeExitAsync().ConfigureAwait(true);
		}
		else {
			Environment.Exit(0);
		}
	}

	private async Task RunAutoClioThenMaybeExitAsync() {
		await _viewModel.RunClioAsync(new ClioInvocation { Verb = "--version" }).ConfigureAwait(true);
		if (_options.ExitAfterPaint || _options.BenchHotkeySamples > 0) {
			Environment.Exit(0);
		}
	}

	private async Task ExitAfterDelayAsync() {
		await Task.Delay(150).ConfigureAwait(true);
		Environment.Exit(0);
	}
}
