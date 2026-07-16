using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;
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
/// Applies a Creatio theme to the current user's profile by updating the virtual <c>SysUserProfile</c>
/// entity through the DataService (the same mechanism the Freedom UI profile page uses). The theme's
/// <c>Id</c> is written to the <c>Theme</c> column filtered by the current user's profile id; the Shell
/// maps that id to the theme's css class on the user's next page refresh. Requires the
/// <c>CanCustomizeBranding</c> license and the <c>CanChangeOwnTheme</c> system operation on the caller,
/// and the server-side <c>ChangeTheme</c> feature to be enabled.
/// </summary>
public class SetUserThemeCommand : RemoteCommand<SetUserThemeOptions>
{
	internal const string SysUserProfileSchemaName = "SysUserProfile";
	internal const string ThemeColumnName = "Theme";
	internal const string IdColumnName = "Id";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private readonly IServiceUrlBuilder _urlBuilder;
	private readonly ListThemesCommand _listThemesCommand;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetUserThemeCommand"/> class.
	/// </summary>
	public SetUserThemeCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder urlBuilder, ListThemesCommand listThemesCommand)
		: base(applicationClient, settings) {
		_urlBuilder = urlBuilder;
		_listThemesCommand = listThemesCommand;
	}

	/// <summary>
	/// Applies the requested theme (or resets the selection) to the current user's profile, verifying the
	/// write with a read-back so a silently-ignored change (feature <c>ChangeTheme</c> disabled) is
	/// reported as a failure rather than a false success.
	/// </summary>
	/// <param name="options">Command options carrying the theme selector or reset flag and connection settings.</param>
	/// <param name="applied">On success, the theme applied to the profile (empty caption/class on reset).</param>
	/// <param name="errorMessage">On failure, the validation or server-provided message.</param>
	/// <returns><c>true</c> when the profile theme was applied and verified; otherwise <c>false</c>.</returns>
	public virtual bool TrySetUserTheme(SetUserThemeOptions options, out AppliedUserTheme applied,
		out string errorMessage) {
		applied = null;
		errorMessage = null;
		bool hasTheme = !string.IsNullOrWhiteSpace(options.Theme);
		if (options.Reset && hasTheme) {
			errorMessage = "Specify either a theme to apply or --reset, not both.";
			return false;
		}
		if (!options.Reset && !hasTheme) {
			errorMessage = "A theme to apply is required. Provide a theme id, css-class-name, or caption, or use --reset.";
			return false;
		}
		if (!TryResolveTargetTheme(options, out AppliedUserTheme target, out errorMessage)) {
			return false;
		}
		if (!TryGetCurrentProfileId(options, out string profileId, out errorMessage)) {
			return false;
		}
		// The profile stores the theme's Id; the Freedom UI Shell maps that Id to the theme's cssClassName
		// (the body class) on the next page load. Writing the cssClassName instead silently falls back to
		// the default theme, so the Id is the value that is persisted and verified.
		if (!TryUpdateProfileTheme(options, profileId, target.Id, out errorMessage)) {
			return false;
		}
		if (!TryVerifyApplied(options, target.Id, out errorMessage)) {
			return false;
		}
		applied = target;
		return true;
	}

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

	private bool TryResolveTargetTheme(SetUserThemeOptions options, out AppliedUserTheme target,
		out string errorMessage) {
		target = null;
		errorMessage = null;
		if (options.Reset) {
			target = new AppliedUserTheme(string.Empty, string.Empty, string.Empty);
			return true;
		}
		string selector = options.Theme.Trim();
		ListThemesOptions listOptions = new() {
			Environment = options.Environment,
			TimeOut = options.TimeOut,
			MaxAttempts = options.MaxAttempts,
			RetryDelay = options.RetryDelay
		};
		IReadOnlyList<ThemeDescriptor> themes;
		bool listed;
		try {
			listed = _listThemesCommand.TryGetAvailableThemes(listOptions, out themes, out errorMessage);
		} catch (Exception exception) {
			// Match the contextual error wording of the other network calls in this command
			// (profile read/write/verify) so a transport failure here reports which step failed
			// instead of escaping to the generic top-level handler.
			errorMessage = $"Failed to list available themes: {exception.Message}";
			return false;
		}
		if (!listed) {
			return false;
		}
		// Resolve tier by tier (id, then css-class-name, then caption). Captions and css class names are
		// not guaranteed unique in Creatio, so a tier that matches more than one theme is reported as an
		// ambiguity (with the candidate ids) rather than silently applying whichever the server listed
		// first — the id path stays unambiguous, so the guidance-recommended id selector is unaffected.
		if (!TryMatchThemeTier(themes, selector, theme => theme.Id, "id", out ThemeDescriptor match,
				out errorMessage)) {
			return false;
		}
		if (match is null && !TryMatchThemeTier(themes, selector, theme => theme.CssClassName,
				"css-class-name", out match, out errorMessage)) {
			return false;
		}
		if (match is null && !TryMatchThemeTier(themes, selector, theme => theme.Caption, "caption",
				out match, out errorMessage)) {
			return false;
		}
		if (match is null) {
			errorMessage = BuildUnknownThemeMessage(selector, themes);
			return false;
		}
		if (string.IsNullOrWhiteSpace(match.Id)) {
			errorMessage = $"Theme '{selector}' has no id and cannot be applied.";
			return false;
		}
		target = new AppliedUserTheme(
			string.IsNullOrWhiteSpace(match.Caption) ? match.Id : match.Caption,
			match.CssClassName ?? string.Empty,
			match.Id);
		return true;
	}

	// Matches themes at a single resolution tier (id / css-class-name / caption). Returns true with a null
	// match when nothing matches (the caller falls through to the next tier), true with the single match
	// when exactly one matches, and false with an ambiguity message listing the candidate ids when more
	// than one theme matches the selector at this tier.
	private static bool TryMatchThemeTier(IReadOnlyList<ThemeDescriptor> themes, string selector,
		Func<ThemeDescriptor, string> field, string tierName, out ThemeDescriptor match, out string errorMessage) {
		match = null;
		errorMessage = null;
		List<ThemeDescriptor> matches = themes
			.Where(theme => string.Equals(field(theme), selector, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (matches.Count == 0) {
			return true;
		}
		if (matches.Count > 1) {
			IEnumerable<string> candidates = matches.Select(theme =>
				$"'{theme.Caption}' (id '{theme.Id}', cssClassName '{theme.CssClassName}')");
			errorMessage = $"Theme '{selector}' matches more than one theme by {tierName}: " +
				$"{string.Join("; ", candidates)}. Specify the theme by its unique id instead.";
			return false;
		}
		match = matches[0];
		return true;
	}

	private static string BuildUnknownThemeMessage(string selector, IReadOnlyList<ThemeDescriptor> themes) {
		if (themes.Count == 0) {
			return $"Theme '{selector}' was not found and no custom themes are available on this environment. " +
				"Create one with create-theme, or use --reset to restore the environment default.";
		}
		IEnumerable<string> available = themes
			.Where(theme => !string.IsNullOrWhiteSpace(theme.Id))
			.Select(theme => $"'{theme.Caption}' (id '{theme.Id}', cssClassName '{theme.CssClassName}')");
		return $"Theme '{selector}' was not found. Available themes: {string.Join("; ", available)}. " +
			"Use --reset to restore the environment default.";
	}

	private bool TryGetCurrentProfileId(SetUserThemeOptions options, out string profileId, out string errorMessage) {
		profileId = null;
		errorMessage = null;
		// The virtual SysUserProfile entity always resolves to exactly one row — the current user's
		// profile — regardless of filters; its Id is the caller's SysAdminUnit id, used as the update filter.
		// Only the profile Id is needed here (used as the update filter); the current theme is read
		// separately during verification (TryReadCurrentTheme), so do not select the Theme column here.
		object query = SelectQueryHelper.BuildSelectQuery(
			SysUserProfileSchemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition(IdColumnName, IdColumnName)],
			[],
			1);
		SysUserProfileSelectResponse response;
		try {
			response = SelectQueryHelper.ExecuteSelectQuery<SysUserProfileSelectResponse>(
				ApplicationClient, _urlBuilder, query, options.TimeOut);
		} catch (Exception exception) {
			errorMessage = $"Failed to read the current user's profile: {exception.Message}";
			return false;
		}
		SysUserProfileRow row = response.Rows.FirstOrDefault();
		if (row is null || string.IsNullOrWhiteSpace(row.Id)) {
			errorMessage = "The current user's profile could not be resolved (SysUserProfile returned no row).";
			return false;
		}
		profileId = row.Id;
		return true;
	}

	private bool TryUpdateProfileTheme(SetUserThemeOptions options, string profileId, string themeId,
		out string errorMessage) {
		errorMessage = null;
		string requestData = JsonSerializer.Serialize(BuildUpdateBody(profileId, themeId));
		string response;
		try {
			response = ApplicationClient.ExecutePostRequest(
				_urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update), requestData,
				options.TimeOut, options.MaxAttempts, options.RetryDelay);
		} catch (Exception exception) {
			errorMessage = $"Failed to apply the theme: {exception.Message}";
			return false;
		}
		UpdateQueryResponse parsed;
		try {
			parsed = JsonSerializer.Deserialize<UpdateQueryResponse>(response, JsonOptions);
		} catch (Exception exception) {
			errorMessage = $"Could not parse the UpdateQuery response: {exception.Message}";
			return false;
		}
		if (parsed is null) {
			errorMessage = "UpdateQuery returned an empty response.";
			return false;
		}
		if (!parsed.Success) {
			errorMessage = DescribeUpdateFailure(parsed.ErrorInfo?.Message);
			return false;
		}
		return true;
	}

	private bool TryVerifyApplied(SetUserThemeOptions options, string expectedThemeId, out string errorMessage) {
		errorMessage = null;
		if (!TryReadCurrentTheme(options, out string actual, out errorMessage)) {
			return false;
		}
		string normalizedActual = actual ?? string.Empty;
		// Theme ids are case-insensitive identifiers; compare ignoring case so a server that normalizes
		// GUID casing on storage does not turn a successful write into a false "not applied" report.
		if (string.Equals(normalizedActual, expectedThemeId, StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		// UpdateQuery reported success but the value did not change: the server-side SysUserProfile
		// listener silently ignores the Theme write when the 'ChangeTheme' feature is disabled.
		// (Note: a reset whose profile was already empty cannot be distinguished from a silently-ignored
		// reset via read-back, but the observable end-state — an empty theme — matches the caller's intent.)
		errorMessage = options.Reset
			? "The profile theme was not cleared. Ensure the 'ChangeTheme' feature is enabled on the environment."
			: $"The profile theme was not applied (still '{normalizedActual}'). Ensure the 'ChangeTheme' feature " +
				"is enabled on the environment and that the CanCustomizeBranding license and CanChangeOwnTheme " +
				"operation are granted.";
		return false;
	}

	private bool TryReadCurrentTheme(SetUserThemeOptions options, out string theme, out string errorMessage) {
		theme = null;
		errorMessage = null;
		object query = SelectQueryHelper.BuildSelectQuery(
			SysUserProfileSchemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition(ThemeColumnName, ThemeColumnName)],
			[],
			1);
		try {
			SysUserProfileSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<SysUserProfileSelectResponse>(
				ApplicationClient, _urlBuilder, query, options.TimeOut);
			theme = response.Rows.FirstOrDefault()?.Theme ?? string.Empty;
			return true;
		} catch (Exception exception) {
			errorMessage = $"Failed to verify the applied theme: {exception.Message}";
			return false;
		}
	}

	private static string DescribeUpdateFailure(string serverMessage) {
		if (string.IsNullOrWhiteSpace(serverMessage)) {
			return "UpdateQuery failed without a message. Ensure the CanCustomizeBranding license and the " +
				"CanChangeOwnTheme operation are granted to the current user.";
		}
		if (serverMessage.Contains("CanCustomizeBranding", StringComparison.OrdinalIgnoreCase)) {
			return $"{serverMessage} — the CanCustomizeBranding license is required to change the theme.";
		}
		if (serverMessage.Contains("CanChangeOwnTheme", StringComparison.OrdinalIgnoreCase)) {
			return $"{serverMessage} — the CanChangeOwnTheme system operation is required to change the theme.";
		}
		return serverMessage;
	}

	private static object BuildUpdateBody(string profileId, string themeId) =>
		new {
			__type = "Terrasoft.Nui.ServiceModel.DataContract.UpdateQuery",
			operationType = 2,
			rootSchemaName = SysUserProfileSchemaName,
			isForceUpdate = false,
			columnValues = new {
				items = new Dictionary<string, object>(StringComparer.Ordinal) {
					[ThemeColumnName] = new {
						expressionType = 2,
						parameter = new {
							dataValueType = SelectQueryHelper.TextDataValueType,
							value = themeId
						}
					}
				}
			},
			filters = new {
				filterType = 6,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				logicalOperation = 0,
				items = new {
					primaryFilter = new {
						filterType = 1,
						comparisonType = 3,
						isEnabled = true,
						trimDateTimeParameterToDate = false,
						leftExpression = new {
							expressionType = 0,
							columnPath = IdColumnName
						},
						rightExpression = new {
							expressionType = 2,
							parameter = new {
								dataValueType = SelectQueryHelper.TextDataValueType,
								value = profileId
							}
						}
					}
				}
			},
			queryKind = 0
		};

	private sealed class SysUserProfileSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto
	{
		[JsonPropertyName("rows")]
		public List<SysUserProfileRow> Rows { get; set; } = [];
	}

	private sealed class SysUserProfileRow
	{
		[JsonPropertyName("Id")]
		public string Id { get; set; }

		[JsonPropertyName("Theme")]
		public string Theme { get; set; }
	}

	private sealed class UpdateQueryResponse
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public UpdateQueryErrorInfo ErrorInfo { get; set; }
	}

	private sealed class UpdateQueryErrorInfo
	{
		[JsonPropertyName("message")]
		public string Message { get; set; }
	}
}

/// <summary>
/// The theme applied to the current user's profile by the <c>set-user-theme</c> command.
/// </summary>
/// <param name="Caption">Human-readable theme caption; empty when the selection was reset.</param>
/// <param name="CssClassName">The theme's CSS class name (the Shell body class); reported for reference, empty when reset.</param>
/// <param name="Id">The theme id written to the profile's <c>Theme</c> column; empty when the selection was reset.</param>
public sealed record AppliedUserTheme(string Caption, string CssClassName, string Id);
