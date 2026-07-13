using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ClioRing.ViewModels;

namespace ClioRing.Views;

/// <summary>
/// EXPERIMENTAL proof window for the clio MCP-over-stdio IPC. Shows the negotiated handshake, a
/// searchable full-catalog list, and read-only env calls. Only created when the <c>Experiments.ClioIpc</c>
/// flag is enabled (via the tray menu). Not part of the normal ring surface.
/// </summary>
public partial class ClioIpcWindow : Window {
	/// <summary>Design-time constructor.</summary>
	public ClioIpcWindow() {
		InitializeComponent();
	}

	/// <summary>Creates the window bound to the supplied view-model.</summary>
	public ClioIpcWindow(ClioIpcViewModel viewModel) {
		InitializeComponent();
		DataContext = viewModel;
	}

	private void InitializeComponent() {
		AvaloniaXamlLoader.Load(this);
	}
}
