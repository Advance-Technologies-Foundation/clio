using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ClioRing.Diagnostics;
using ClioRing.Interop;
using ClioRing.Ipc;
using ClioRing.Services;
using ClioRing.ViewModels;
using ClioRing.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ClioRing;

public partial class App : Application {
	private ServiceProvider? _services;
	private RingWindow? _window;
	private TrayIcon? _tray;
	private ClioIpcWindow? _ipcWindow;
	private ClioWorkflowWindow? _workflowWindow;
	private InstallWindow? _installWindow;
	private UninstallWindow? _uninstallWindow;

	/// <summary>Returns whether a terminal stage outcome represents successful completion.</summary>
	public static bool IsSuccessfulRunOutcome(string? outcome) =>
		outcome is ClioStageEventContract.RunOutcomes.Success
			or ClioStageEventContract.RunOutcomes.SuccessWithWarnings;

	public override void Initialize() {
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted() {
		_services = Startup.BuildServiceProvider();

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
			// Stay resident: hiding the ring (or closing the window) must NOT quit the app.
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

			// Reduced-motion fallback applies before any node is built.
			RingView.ReducedMotion = LaunchOptions.Current.ReducedMotion;

			RingViewModel vm = _services.GetRequiredService<RingViewModel>();

			// Main-ring entry: the "Deploy Creatio" primary action opens the guided Install form window.
			vm.GuidedInstallRequested += ShowInstallWindow;

			// Main-ring entry: the "Uninstall Creatio" primary action opens the guided Uninstall flow window.
			vm.GuidedUninstallRequested += ShowUninstallWindow;

			// Interaction soak: also open + render the guided Install form AND the guided Uninstall flow with
			// their pipelines (design-populated, no live clio) so the soak exercises those windows, not just
			// the ring.
			Views.RingWindow.SoakExtraStep = async () => {
				await SoakInstallStepAsync();
				await SoakUninstallStepAsync();
			};

			HotkeyGesture gesture = HotkeyGesture.Parse(AppSettingsReader.TryRead()?.Hotkey) ?? HotkeyGesture.Default;
			StartupLog.Log($"resolved hotkey: {gesture.Display} (config {AppSettingsReader.SettingsPath})");

			// Cross-build notice: this build is primary (per-path single-instance), but if a DIFFERENT
			// clio-ring build is already running elsewhere, tell the user so they are never confused about
			// which build they are looking at. Skipped in non-interactive harness modes.
			if (!LaunchOptions.Current.IsHarnessMode) {
				CrossBuildInfo? other = SingleInstance.DetectOtherRunningBuild();
				if (other is not null) {
					string notice = $"{other.Describe()}. Quit it to run only this one.";
					vm.CrossBuildNotice = notice;
					StartupLog.Log($"cross-build conflict: this={AppContext.BaseDirectory} (id {SingleInstance.Id}) other={other.Describe()}");
				}
			}

			IWindowPlacementStore placementStore = _services.GetRequiredService<IWindowPlacementStore>();
			_window = new RingWindow(vm, LaunchOptions.Current, gesture, placementStore);
			desktop.MainWindow = _window;

			CreateTray(desktop, vm);
			SingleInstance.StartShowListener(
				() => Dispatcher.UIThread.Post(() => _window?.ShowRing()));
		}
		else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform) {
			singleViewPlatform.MainView = new RingView {
				DataContext = _services.GetRequiredService<RingViewModel>()
			};
		}

		base.OnFrameworkInitializationCompleted();
	}

	private void CreateTray(IClassicDesktopStyleApplicationLifetime desktop, RingViewModel vm) {
		if (_tray is not null) {
			return;
		}
		try {
			var showItem = new NativeMenuItem("Show ring");
			showItem.Click += (_, _) => _window?.ShowRing();

			var settingsItem = new NativeMenuItem("Settings / Hotkey");
			settingsItem.Click += (_, _) => {
				_window?.ShowRing();
				vm.ShowSettings = true;
			};

			var centerItem = new NativeMenuItem("Center Ring");
			centerItem.Click += (_, _) => {
				_window?.ShowRing();
				_window?.CenterOnScreen();
			};

			var resetItem = new NativeMenuItem("Reset Window Position");
			resetItem.Click += (_, _) => {
				_window?.ShowRing();
				_window?.ResetPosition();
			};

			// Story 10 (ADR D6): open the active logs/receipts folder in the OS file browser.
			var openLogsItem = new NativeMenuItem("Open logs");
			openLogsItem.Click += (_, _) => RingLog.OpenLogsFolder();

			var quitItem = new NativeMenuItem("Quit");
			quitItem.Click += (_, _) => {
				StartupLog.Log("shutdown reason=tray-quit");
				desktop.Shutdown();
			};

			var menu = new NativeMenu();
			menu.Add(showItem);
			menu.Add(settingsItem);
			menu.Add(centerItem);
			menu.Add(resetItem);
			menu.Add(openLogsItem);

			// EXPERIMENTAL clio IPC proof surface — only present when the flag is enabled. When off, the
			// tray behaves exactly as today (no extra entry, no IPC child ever spawned).
			if (Startup.IsClioIpcExperimentEnabled()) {
				var workflowItem = new NativeMenuItem("clio workflow (experimental)");
				workflowItem.Click += (_, _) => ShowWorkflowWindow();
				menu.Add(workflowItem);

				var ipcItem = new NativeMenuItem("clio tool catalog (experimental)");
				ipcItem.Click += (_, _) => ShowIpcProofWindow();
				menu.Add(ipcItem);
				StartupLog.Log("experiment ClioIpc=on -> tray entries added");
			}

			menu.Add(quitItem);

			string tooltip = vm.BuildIdentity;
			if (vm.HasCatalogError) {
				tooltip += $"\n⚠ actions.json error: {vm.CatalogError}";
			}
			if (vm.HasCrossBuildNotice) {
				tooltip += $"\n⚠ different build running: {vm.CrossBuildNotice}";
			}
			var tray = new TrayIcon {
				Icon = LoadTrayIcon(),
				ToolTipText = tooltip,
				Menu = menu,
				IsVisible = true
			};
			tray.Clicked += (_, _) => _window?.ShowRing();

			TrayIcon.SetIcons(this, new TrayIcons { tray });
			_tray = tray;
			StartupLog.Log("tray icon created");
		}
		catch (Exception ex) {
			StartupLog.Log($"tray creation failed: {ex.Message}");
		}
	}

	// Opens (or re-focuses) the experimental clio workflow window (command actions + deploy wizard).
	private void ShowWorkflowWindow() {
		if (_services is null) {
			return;
		}
		if (_workflowWindow is null) {
			ClioWorkflowViewModel vm = _services.GetRequiredService<ClioWorkflowViewModel>();
			vm.CatalogRequested += (_, _) => ShowIpcProofWindow();
			vm.DeployWizardRequested += (_, _) => ShowInstallWindow();
			_workflowWindow = new ClioWorkflowWindow(vm);
			_workflowWindow.Closed += (_, _) => _workflowWindow = null;
			_workflowWindow.Show();
		}
		else {
			_workflowWindow.Activate();
		}
		StartupLog.Log("clio workflow window shown");
	}

	// Opens (or re-focuses) the guided Creatio Install form. Shares the singleton IClioIpcClient; discovery
	// auto-runs on open. This is the main-ring "Deploy Creatio" destination (and the experimental tray path).
	private void ShowInstallWindow() {
		if (_services is null) {
			return;
		}
		if (_installWindow is null) {
			InstallFormViewModel vm = _services.GetRequiredService<InstallFormViewModel>();
			// Story 10: record every stage event the pipeline renders to the per-run NDJSON receipt.
			vm.Pipeline.StageEventObserved += _services.GetRequiredService<DeploymentReceipt>().Observe;
			vm.Pipeline.StageEventObserved += stageEvent => {
				if (IsSuccessfulRunOutcome(stageEvent.RunCompleted?.Outcome)) {
					_window?.RefreshEnvironments();
				}
			};
			_installWindow = new InstallWindow(vm);
			_installWindow.Closed += (_, _) => _installWindow = null;
			_installWindow.Show();
		}
		else {
			_installWindow.Activate();
		}
		StartupLog.Log("guided install form shown");
	}

	// Opens (or re-focuses) the guided Creatio Uninstall flow. Shares the singleton IClioIpcClient and the
	// ring's environment source; the local-env list runs on open. This is the main-ring "Uninstall Creatio"
	// destination. Opening it starts nothing — only the confirm's explicit Yes click can (ADR D8).
	private void ShowUninstallWindow() {
		if (_services is null) {
			return;
		}
		if (_uninstallWindow is null) {
			UninstallFormViewModel vm = _services.GetRequiredService<UninstallFormViewModel>();
			// Story 10: record every stage event the pipeline renders to the per-run NDJSON receipt.
			vm.Pipeline.StageEventObserved += _services.GetRequiredService<DeploymentReceipt>().Observe;
			vm.Pipeline.StageEventObserved += stageEvent => {
				if (IsSuccessfulRunOutcome(stageEvent.RunCompleted?.Outcome)) {
					_window?.RefreshEnvironments();
				}
			};
			_uninstallWindow = new UninstallWindow(vm);
			_uninstallWindow.Closed += (_, _) => _uninstallWindow = null;
			_uninstallWindow.Show();
		}
		else {
			_uninstallWindow.Activate();
		}
		StartupLog.Log("guided uninstall flow shown");
	}

	// Soak seam: open the guided Uninstall flow with a design-populated VM (sample local envs + open confirm +
	// sample pipeline, no live clio), hold a couple of frames so it actually renders, then close it. A crash
	// here fails the soak (non-zero exit), which headless screenshots would mask.
	private async System.Threading.Tasks.Task SoakUninstallStepAsync() {
		if (_services is null) {
			return;
		}
		UninstallFormViewModel vm = _services.GetRequiredService<UninstallFormViewModel>();
		vm.DesignPopulate(succeed: true);
		var window = new UninstallWindow(vm);
		try {
			window.Show();
			await System.Threading.Tasks.Task.Delay(120);
			// Re-drive the sample pipeline once more (the failure/AC-ERR render) to exercise both paths.
			vm.DesignPopulate(succeed: false);
			await System.Threading.Tasks.Task.Delay(120);
		}
		finally {
			window.Close();
		}
	}

	// Soak seam: open the guided Install form with a design-populated VM + sample pipeline (no live clio),
	// hold a couple of frames so it actually renders, then close it. A crash here fails the soak (non-zero
	// exit), which headless screenshots would mask.
	private async System.Threading.Tasks.Task SoakInstallStepAsync() {
		if (_services is null) {
			return;
		}
		InstallFormViewModel vm = _services.GetRequiredService<InstallFormViewModel>();
		vm.DesignPopulate(local: true);
		var window = new InstallWindow(vm);
		try {
			window.Show();
			await System.Threading.Tasks.Task.Delay(120);
			// Re-drive the sample pipeline once more to exercise the live render path under the soak.
			vm.Pipeline.DesignPopulate(succeed: true);
			await System.Threading.Tasks.Task.Delay(120);
		}
		finally {
			window.Close();
		}
	}

	// Opens (or re-focuses) the experimental clio IPC proof window. The view-model is resolved from DI,
	// so it shares the single long-lived IClioIpcClient (one clio child per app session).
	private void ShowIpcProofWindow() {
		if (_services is null) {
			return;
		}
		if (_ipcWindow is null) {
			ClioIpcViewModel vm = _services.GetRequiredService<ClioIpcViewModel>();
			_ipcWindow = new ClioIpcWindow(vm);
			_ipcWindow.Closed += (_, _) => _ipcWindow = null;
			_ipcWindow.Show();
		}
		else {
			_ipcWindow.Activate();
		}
		StartupLog.Log("clio IPC proof window shown");
	}

	private static WindowIcon LoadTrayIcon() {
		using var stream = AssetLoader.Open(new Uri("avares://ClioRing/Assets/clio-ring.ico"));
		return new WindowIcon(stream);
	}
}
