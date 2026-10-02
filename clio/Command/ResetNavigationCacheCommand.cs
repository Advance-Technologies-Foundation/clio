using System;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: ResetNavigationCacheOptions

/// <summary>
/// Command-line options for the <c>reset-navigation-cache</c> command.
/// </summary>
[Verb("reset-navigation-cache",
	HelpText = "Clear the menu cache (module structure, workplaces, sections) of clio's own Creatio session")]
public class ResetNavigationCacheOptions : RemoteCommandOptions
{
}

#endregion

#region Class: ResetNavigationCacheResponse

/// <summary>
/// Structured result of the <c>reset-navigation-cache</c> command.
/// </summary>
public sealed record ResetNavigationCacheResponse
{

	/// <summary>Gets whether the server confirmed that clio's session cache was cleared.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>Gets the failure detail; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>
	/// Gets how to refresh an open browser tab whose session still serves the old menu; present on success and
	/// on failure, because the reset never reaches a browser session.
	/// </summary>
	[JsonPropertyName("next-step")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string NextStep { get; init; }

}

#endregion

#region Class: ResetNavigationCacheCommand

/// <summary>
/// Clears the navigation cache of clio's own Creatio session through <see cref="INavigationCacheResetter"/>.
/// </summary>
public class ResetNavigationCacheCommand : RemoteCommand<ResetNavigationCacheOptions>
{

	#region Fields: Private

	private readonly INavigationCacheResetter _navigationCacheResetter;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="ResetNavigationCacheCommand"/> class.
	/// </summary>
	/// <param name="applicationClient">Client of the session whose cache is cleared.</param>
	/// <param name="environmentSettings">Resolved target environment settings.</param>
	/// <param name="navigationCacheResetter">Service that clears the session cache.</param>
	public ResetNavigationCacheCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings,
		INavigationCacheResetter navigationCacheResetter)
		: base(applicationClient, environmentSettings) {
		_navigationCacheResetter = navigationCacheResetter;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Clears the navigation cache of clio's session for the environment.
	/// </summary>
	/// <param name="options">Target environment.</param>
	/// <returns>
	/// <c>success</c> with the browser-session note, or <c>success=false</c> with the reason and the same note.
	/// </returns>
	public virtual ResetNavigationCacheResponse Reset(ResetNavigationCacheOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		string warning = _navigationCacheResetter.TryReset(ApplicationClient, EnvironmentSettings);
		return new ResetNavigationCacheResponse {
			Success = warning is null,
			Error = warning,
			NextStep = _navigationCacheResetter.BuildBrowserSessionNote(EnvironmentSettings)
		};
	}

	/// <summary>
	/// Executes the reset-navigation-cache command.
	/// </summary>
	/// <param name="options">Parsed command options.</param>
	/// <returns>0 when the server confirmed the reset; otherwise 1.</returns>
	public override int Execute(ResetNavigationCacheOptions options) {
		ResetNavigationCacheResponse response = Reset(options);
		if (response.Success) {
			Logger.WriteInfo("Navigation cache of clio's session cleared.");
		} else {
			Logger.WriteError(response.Error);
		}
		Logger.WriteInfo(response.NextStep);
		return response.Success ? 0 : 1;
	}

	#endregion

}

#endregion
