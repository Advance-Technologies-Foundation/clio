using System.Collections.Generic;

namespace Clio.Command.Theming;

/// <summary>
/// Reads the custom-theme catalog of a Creatio environment. Abstracts the concrete
/// <see cref="ListThemesCommand"/> so behavior classes (for example <see cref="UserThemeApplier"/>)
/// can resolve a theme selector against the environment's themes without depending on the command type
/// directly (CLIO001 — behavior consumed through an interface).
/// </summary>
public interface IThemeCatalog
{
	/// <summary>
	/// Fetches the custom themes available on the target environment.
	/// </summary>
	/// <param name="options">Options carrying the connection, timeout, and retry settings.</param>
	/// <param name="themes">On success, the themes returned by the environment (possibly empty).</param>
	/// <param name="errorMessage">On failure, the server-provided message, if any.</param>
	/// <returns><c>true</c> when the catalog was read; <c>false</c> when the service reported a failure.</returns>
	bool TryGetAvailableThemes(ListThemesOptions options,
		out IReadOnlyList<ThemeDescriptor> themes, out string errorMessage);
}
