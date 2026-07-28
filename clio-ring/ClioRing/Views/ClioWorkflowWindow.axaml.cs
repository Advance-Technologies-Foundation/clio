using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ClioRing.ViewModels;

namespace ClioRing.Views;

/// <summary>
/// EXPERIMENTAL clio workflow window: command actions against the selected environment (with typed
/// confirm for destructive ones) + the Deploy Creatio wizard launcher. Only created when the
/// <c>Experiments.ClioIpc</c> flag is enabled.
/// </summary>
public partial class ClioWorkflowWindow : Window {
	/// <summary>Design-time constructor.</summary>
	public ClioWorkflowWindow() {
		InitializeComponent();
	}

	/// <summary>Creates the window bound to the supplied view-model.</summary>
	public ClioWorkflowWindow(ClioWorkflowViewModel viewModel) {
		InitializeComponent();
		DataContext = viewModel;
	}

	private void InitializeComponent() {
		AvaloniaXamlLoader.Load(this);
	}
}
