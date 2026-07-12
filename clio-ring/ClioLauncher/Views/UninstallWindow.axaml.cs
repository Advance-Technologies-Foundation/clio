using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ClioLauncher.ViewModels;

namespace ClioLauncher.Views;

/// <summary>
/// Host window for the guided Creatio Uninstall flow reached from the main radial ring's "Uninstall Creatio"
/// action (story 9). Listing the local registered environments runs automatically once the window is shown —
/// skipped in non-interactive harness modes and when the view-model opts out (screenshot/soak seams).
/// Opening the window starts nothing: only the confirm's explicit Yes click can (ADR D8).
/// </summary>
public partial class UninstallWindow : Window {
	/// <summary>Design-time constructor.</summary>
	public UninstallWindow() {
		InitializeComponent();
	}

	/// <summary>Creates the window bound to the supplied guided-uninstall view-model.</summary>
	public UninstallWindow(UninstallFormViewModel viewModel) {
		InitializeComponent();
		DataContext = viewModel;
		Opened += OnOpened;
	}

	private void InitializeComponent() {
		AvaloniaXamlLoader.Load(this);
	}

	// Auto-list local environments once the window is shown. Fire-and-forget: the VM's load path marshals to
	// the UI thread and guards on IsBusy; any failure is surfaced by the VM in its Output. Skipped in harness
	// modes (soak/smoke) and when the VM opted out (screenshot seams), so no clio child is ever spawned there.
	private void OnOpened(object? sender, EventArgs e) {
		Opened -= OnOpened;
		if (DataContext is not UninstallFormViewModel vm
			|| !vm.AutoDiscoverOnOpen
			|| LaunchOptions.Current.IsHarnessMode) {
			return;
		}

		_ = Dispatcher.UIThread.InvokeAsync(async () => {
			try {
				await vm.InitializeAsync();
			}
			catch (Exception) {
				// The VM's own load try/catch reports errors via Output; this is a last-resort guard so a
				// fire-and-forget fault can never crash the UI thread.
			}
		});
	}
}
