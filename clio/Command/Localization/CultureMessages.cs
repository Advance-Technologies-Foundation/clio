namespace Clio.Command.Localization;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Messages shared by every command that writes a value in a given culture (<c>localize-page</c>,
/// <c>update-app-section</c>, entity caption writes), so the refusal and the warning read the same everywhere.
/// </summary>
public static class CultureMessages {

	/// <summary>
	/// Refusal for a culture that is not a <c>SysCulture</c> row. <c>{0}</c> is the requested culture,
	/// <c>{1}</c> the comma-separated available cultures.
	/// </summary>
	public const string CultureAbsentMessageFormat =
		"Culture '{0}' is not available in this environment. Add it in the Languages section "
		+ "(System Designer → Languages) first. Available: {1}.";

	/// <summary>
	/// Warning for a culture that exists but is inactive. <c>{0}</c> is the canonical culture name. Activation alone
	/// is not enough: the UI does not load in the culture until a full configuration compile has run
	/// (docs/knowledge/platform/an-activated-culture-needs-a-full-configuration-compile.md).
	/// </summary>
	public const string CultureInactiveWarningFormat =
		"Culture '{0}' exists but is inactive; users cannot select it until it is activated in the Languages section. "
		+ "After activating it, run a full configuration compile (clio compile-configuration --all); until then the "
		+ "UI does not load in that culture.";

	/// <summary>
	/// Formats the refusal for a culture the environment does not have.
	/// </summary>
	/// <param name="requestedCulture">Culture name as the caller supplied it; it is trimmed.</param>
	/// <param name="available">The environment's cultures, listed in the order given.</param>
	/// <returns>The formatted message.</returns>
	public static string FormatCultureAbsent(string requestedCulture, IEnumerable<CreatioCulture> available) =>
		string.Format(CultureAbsentMessageFormat, requestedCulture?.Trim(),
			string.Join(", ", (available ?? []).Select(culture => culture.Name)));

	/// <summary>
	/// Formats the warning for an inactive culture.
	/// </summary>
	/// <param name="cultureName">Canonical culture name.</param>
	/// <returns>The formatted warning.</returns>
	public static string FormatCultureInactive(string cultureName) =>
		string.Format(CultureInactiveWarningFormat, cultureName);
}
