using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClioLauncher.Views;

/// <summary>
/// The guided Creatio Uninstall flow (story 9): a local-environment picker plus a simple "Are you sure?
/// Yes/No" confirm (no exact-name typing), with the shared <see cref="DeployPipelineView"/> hosted alongside
/// to render the uninstall stages. Bound to a <see cref="ClioLauncher.ViewModels.UninstallFormViewModel"/>.
/// Opening the flow starts nothing — only the confirm's Yes click can.
/// </summary>
public partial class UninstallView : UserControl {
	/// <summary>Creates the guided Uninstall flow view.</summary>
	public UninstallView() {
		InitializeComponent();
	}

	private void InitializeComponent() {
		AvaloniaXamlLoader.Load(this);
	}
}
