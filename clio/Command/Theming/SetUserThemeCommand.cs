using Clio.Common;
using CommandLine;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>set-user-theme</c> command.
/// </summary>
[Verb("set-user-theme", Aliases = ["apply-user-theme"],
	HelpText = "Apply a Creatio theme to the current (authenticated) user's profile on the target environment")]
[RequiresCreatioVersion(ThemeServiceRequirement.MinVersion)]
public class SetUserThemeOptions : RemoteCommandOptions
{
	/// <summary>
	/// Theme to apply, matched (case-insensitively, in order) against a theme's id, css class name, or
	/// caption (as reported by <c>list-themes</c>). Omitted when <see cref="Reset"/> is used.
	/// </summary>
	[Value(0, MetaName = "theme", Required = false,
		HelpText = "Theme to apply: id, css-class-name, or caption (case-insensitive), as reported by list-themes.")]
	public string Theme { get; set; }

	/// <summary>
	/// Clears the user's theme selection, restoring the environment default (the <c>DefaultTheme</c>
	/// system setting, or the built-in default). Mutually exclusive with <see cref="Theme"/>.
	/// </summary>
	[Option("reset", Required = false,
		HelpText = "Clear the user's theme, restoring the environment default (DefaultTheme). Mutually exclusive with the theme argument.")]
	public bool Reset { get; set; }
}

/// <summary>
/// Applies a Creatio theme to the current (authenticated) user's profile on the target environment, or
/// clears it with <c>--reset</c>. This command is a thin adapter over <see cref="IUserThemeApplier"/>,
/// which owns the apply/verify behavior (NFR-2 / Story 1); the command maps the CLI options onto the
/// service and renders the user-facing result.
/// </summary>
public class SetUserThemeCommand : RemoteCommand<SetUserThemeOptions>
{
	private readonly IUserThemeApplier _applier;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetUserThemeCommand"/> class.
	/// </summary>
	public SetUserThemeCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IUserThemeApplier applier)
		: base(applicationClient, settings) {
		_applier = applier;
	}

	/// <summary>
	/// Applies the requested theme (or resets the selection) to the current user's profile by delegating
	/// to <see cref="IUserThemeApplier"/>.
	/// </summary>
	/// <param name="options">Command options carrying the theme selector or reset flag and connection settings.</param>
	/// <param name="applied">On success, the theme applied to the profile (empty caption/class on reset).</param>
	/// <param name="errorMessage">On failure, the validation or server-provided message.</param>
	/// <returns><c>true</c> when the profile theme was applied and verified; otherwise <c>false</c>.</returns>
	public virtual bool TrySetUserTheme(SetUserThemeOptions options, out AppliedUserTheme applied,
		out string errorMessage) =>
		_applier.TrySetUserTheme(options, out applied, out errorMessage);

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(SetUserThemeOptions options) {
		if (TrySetUserTheme(options, out AppliedUserTheme applied, out string errorMessage)) {
			if (options.Reset) {
				Logger.WriteInfo("Cleared the user's theme; the environment default applies. Refresh the page to see the change.");
				return;
			}
			Logger.WriteInfo(
				$"Applied theme '{applied.Caption}' (id '{applied.Id}', cssClassName '{applied.CssClassName}') to the current user. Refresh the page to see it.");
			return;
		}
		CommandSuccess = false;
		Logger.WriteError(errorMessage);
	}
}
