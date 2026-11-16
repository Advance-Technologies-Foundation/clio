namespace Clio.Command.Theming;

/// <summary>
/// Shared Creatio platform version requirement of the theme management surface. Every command that
/// calls the native <c>ThemeService</c> declares this floor via
/// <see cref="Clio.Common.RequiresCreatioVersionAttribute"/> on its options class.
/// </summary>
public static class ThemeServiceRequirement
{
	/// <summary>Minimum Creatio core version that ships the native <c>ThemeService</c>.</summary>
	public const string MinVersion = "10.0.0";

	public const string BackgroundMinVersion = "10.2.96";

	public const string BackgroundHint = "A theme background needs Creatio " + BackgroundMinVersion +
		" or later; on this environment set the site background with set-background-image instead.";
}
