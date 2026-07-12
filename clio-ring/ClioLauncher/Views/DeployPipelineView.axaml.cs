using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClioLauncher.Views;

/// <summary>
/// GitHub-Actions-style step-list view for a Creatio deploy/uninstall pipeline (story 7). Renders the
/// ordered steps built from the typed <c>manifest</c> event, each with a state glyph/colour, name,
/// duration, friendly message and an expandable technical-detail disclosure, plus a header showing the
/// overall run state, summary and the derived URL on success. Bound to a
/// <see cref="ClioLauncher.ViewModels.DeployPipelineViewModel"/>; a reusable control (not yet wired into
/// the main ring — that is stories 8/9).
/// </summary>
public partial class DeployPipelineView : UserControl {
	/// <summary>Creates the pipeline step-list view.</summary>
	public DeployPipelineView() {
		InitializeComponent();
	}

	private void InitializeComponent() {
		AvaloniaXamlLoader.Load(this);
	}
}
