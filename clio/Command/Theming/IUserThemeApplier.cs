namespace Clio.Command.Theming;

/// <summary>
/// Applies a Creatio theme to the current (authenticated) user's profile, or clears it. This is the
/// behavior behind the <c>set-user-theme</c> command and MCP tool (NFR-2 / Story 1); the command is a
/// thin adapter over this interface.
/// </summary>
public interface IUserThemeApplier
{
	/// <summary>
	/// Applies the requested theme (or resets the selection) to the current user's profile, verifying the
	/// write with a read-back so a silently-ignored change (feature <c>ChangeTheme</c> disabled) is reported
	/// as a failure rather than a false success.
	/// </summary>
	/// <param name="options">Options carrying the theme selector or reset flag and the connection settings.</param>
	/// <param name="applied">On success, the theme applied to the profile (empty caption/class on reset).</param>
	/// <param name="errorMessage">On failure, the validation or server-provided message.</param>
	/// <returns><c>true</c> when the profile theme was applied and verified; otherwise <c>false</c>.</returns>
	bool TrySetUserTheme(SetUserThemeOptions options, out AppliedUserTheme applied, out string errorMessage);
}
