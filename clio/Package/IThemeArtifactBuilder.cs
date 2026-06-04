namespace Clio.Package;

/// <summary>
/// Builds the identifiers and the <c>theme.json</c> / <c>theme.css</c> content for a theme.
/// </summary>
public interface IThemeArtifactBuilder
{
	/// <summary>
	/// Resolves the full identifier set from a CSS class name. When <paramref name="caption"/> is not
	/// supplied it is derived as Title Case of the class name;
	/// when <paramref name="id"/> is not supplied a new UUID is generated.
	/// </summary>
	/// <param name="cssClassName">Root CSS class name.</param>
	/// <param name="caption">Optional explicit caption; derived from the class name when omitted.</param>
	/// <param name="id">Optional explicit id; a UUID is generated when omitted.</param>
	/// <returns>The resolved <see cref="ThemeIdentifiers"/>.</returns>
	ThemeIdentifiers DeriveIdentifiers(string cssClassName, string caption = null, string id = null);

	/// <summary>
	/// Validates identifiers against the theme contract (id, caption, cssClassName patterns and
	/// length limits). Throws <see cref="System.ArgumentException"/> on the first violation.
	/// </summary>
	/// <param name="identifiers">The identifiers to validate.</param>
	void Validate(ThemeIdentifiers identifiers);

	/// <summary>
	/// Builds the <c>theme.json</c> content (exactly <c>{ id, caption, cssClassName }</c>).
	/// </summary>
	/// <param name="identifiers">The resolved identifiers.</param>
	/// <returns>The serialized <c>theme.json</c> text.</returns>
	string BuildThemeJson(ThemeIdentifiers identifiers);

	/// <summary>
	/// Builds the <c>theme.css</c> content from the canonical baseline template, scoped under
	/// <c>.&lt;cssClassName&gt;</c>.
	/// </summary>
	/// <param name="identifiers">The resolved identifiers.</param>
	/// <returns>The baseline <c>theme.css</c> text for this theme.</returns>
	string BuildThemeCss(ThemeIdentifiers identifiers);
}
