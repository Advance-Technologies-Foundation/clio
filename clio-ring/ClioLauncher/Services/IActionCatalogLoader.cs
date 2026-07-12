using ClioLauncher.Models;

namespace ClioLauncher.Services;

/// <summary>Loads and validates the declarative action catalog (<c>actions.json</c>).</summary>
public interface IActionCatalogLoader {
	/// <summary>
	/// Loads the catalog from <paramref name="path"/> (or the default location next to the
	/// executable when null). Throws <see cref="ActionCatalogException"/> on any missing file,
	/// JSON error, or kind/block mismatch — never returns a partial catalog.
	/// </summary>
	/// <param name="path">Optional explicit path to an <c>actions.json</c> file.</param>
	/// <returns>A fully validated <see cref="ActionCatalog"/>.</returns>
	ActionCatalog Load(string? path = null);
}
