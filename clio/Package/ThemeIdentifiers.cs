namespace Clio.Package;

/// <summary>
/// Resolved identifiers for a theme artifact.
/// </summary>
/// <param name="Id">Stable theme id stored in <c>theme.json</c> (UUID by default).</param>
/// <param name="Caption">Human-readable theme caption.</param>
/// <param name="CssClassName">Root CSS class the theme is scoped under; also the theme folder name.</param>
public sealed record ThemeIdentifiers(string Id, string Caption, string CssClassName);
