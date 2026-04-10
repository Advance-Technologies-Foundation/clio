namespace Clio.Common;

/// <summary>
/// Resolves the culture name of the authenticated Creatio user for the current environment.
/// </summary>
public interface IApplicationUserCultureProvider
{
	/// <summary>
	/// Returns the culture name (e.g. "en-US", "uk-UA") of the authenticated Creatio user.
	/// Falls back to "en-US" when the value cannot be retrieved.
	/// </summary>
	string GetUserCultureName();
}
