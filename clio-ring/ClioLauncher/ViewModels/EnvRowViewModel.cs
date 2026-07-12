using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClioLauncher.ViewModels;

/// <summary>
/// One row in the environment palette. Carries only non-sensitive metadata. An optional
/// <see cref="SectionLabel"/> renders a compact section header above the first row of each group
/// (PINNED / RECENT / results) so the flat list keeps keyboard navigation simple and AOT-clean.
/// </summary>
public partial class EnvRowViewModel : ViewModelBase {
	/// <summary>Environment name.</summary>
	public required string Name { get; init; }

	/// <summary>URL host (may be empty).</summary>
	public string Host { get; init; } = string.Empty;

	/// <summary>"Local" / "Cloud".</summary>
	public string Location { get; init; } = string.Empty;

	/// <summary>"NetCore" / "Framework".</summary>
	public string Framework { get; init; } = string.Empty;

	/// <summary>Section header shown above this row, or null.</summary>
	public string? SectionLabel { get; init; }

	/// <summary>Whether a section header should render above this row.</summary>
	public bool HasSection => SectionLabel is not null;

	/// <summary>Whether this environment is pinned (drives the star + ordering).</summary>
	[ObservableProperty]
	private bool _isPinned;

	/// <summary>Whether this row is the current keyboard highlight (drives the high-contrast band).</summary>
	[ObservableProperty]
	private bool _isHighlighted;

	/// <summary>Toggles the pin state for this environment (does not close the palette).</summary>
	public ICommand? TogglePinCommand { get; init; }
}
