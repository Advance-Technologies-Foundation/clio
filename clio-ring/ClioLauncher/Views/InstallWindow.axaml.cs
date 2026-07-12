using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClioLauncher.ViewModels;

namespace ClioLauncher.Views;

/// <summary>
/// Host window for the guided Creatio Install form reached from the main radial ring's "Deploy Creatio"
/// action (story 8). Discovery (builds, infrastructure, a free port) runs automatically once the window is
/// shown, mirroring the auto-load behaviour the previous wizard had — skipped in non-interactive harness
/// modes and when the view-model opts out (screenshot/soak seams).
/// </summary>
public partial class InstallWindow : Window {
	/// <summary>Design-time constructor.</summary>
	public InstallWindow() {
		InitializeComponent();
	}

	/// <summary>Creates the window bound to the supplied guided-install view-model.</summary>
	public InstallWindow(InstallFormViewModel viewModel) {
		InitializeComponent();
		DataContext = viewModel;
		Opened += OnOpened;
	}

	private void InitializeComponent() {
		AvaloniaXamlLoader.Load(this);
	}

	// Auto-run discovery once the window is shown. Fire-and-forget: the VM's LoadDefaults path marshals to
	// the UI thread and guards on IsBusy; any failure is surfaced by the VM in its Output. Skipped in harness
	// modes (soak/smoke) and when the VM opted out (screenshot seams), so no clio child is ever spawned there.
	private void OnOpened(object? sender, EventArgs e) {
		Opened -= OnOpened;
		if (DataContext is not InstallFormViewModel vm
			|| !vm.AutoDiscoverOnOpen
			|| LaunchOptions.Current.IsHarnessMode) {
			return;
		}

		_ = Dispatcher.UIThread.InvokeAsync(async () => {
			try {
				await vm.InitializeAsync();
			}
			catch (Exception) {
				// The VM's own LoadDefaults try/catch reports errors via Output; this is a last-resort guard
				// so a fire-and-forget fault can never crash the UI thread.
			}
		});
	}
}
