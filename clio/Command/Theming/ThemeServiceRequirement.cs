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
}
