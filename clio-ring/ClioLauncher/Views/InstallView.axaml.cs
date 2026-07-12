using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClioLauncher.Views;

/// <summary>
/// The guided Creatio Install form (story 8): database + Redis source (Local/Rancher), build/ZIP, instance
/// name and a pre-selected editable free port, with the shared <see cref="DeployPipelineView"/> hosted
/// alongside it to render the internal preflight ("Check requirements") and the install stages. Bound to a
/// <see cref="ClioLauncher.ViewModels.InstallFormViewModel"/>. There is no dry-run control anywhere.
/// </summary>
public partial class InstallView : UserControl {
	/// <summary>Creates the guided Install form view.</summary>
	public InstallView() {
		InitializeComponent();
	}

	private void InitializeComponent() {
		AvaloniaXamlLoader.Load(this);
	}
}
