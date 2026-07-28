using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Theming;
using CommandLine;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>delete-theme</c> command.
/// </summary>
[Verb("delete-theme", HelpText = "Delete a custom Creatio theme from the target environment via the native ThemeService")]
[RequiresCreatioVersion(ThemeServiceRequirement.MinVersion)]
public class DeleteThemeOptions : RemoteCommandOptions
{
	/// <summary>Id of the theme to delete (required).</summary>
	[Option("id", Required = true, HelpText = "Id of the theme to delete")]
	public string Id { get; set; }
}

/// <summary>
/// Deletes a custom Creatio theme from the target environment by calling the native
/// <c>ThemeService.svc/DeleteTheme</c> endpoint. Requires the <c>CanCustomizeBranding</c> license and the
/// <c>CanManageThemes</c> system operation. Deleting an unknown id is reported as a failure (not idempotent).
/// </summary>
public class DeleteThemeCommand : RemoteCommand<DeleteThemeOptions>
{
	private readonly IServiceUrlBuilder _urlBuilder;

	/// <summary>
	/// Initializes a new instance of the <see cref="DeleteThemeCommand"/> class.
	/// </summary>
	public DeleteThemeCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder urlBuilder)
		: base(applicationClient, settings) {
		_urlBuilder = urlBuilder;
	}

	/// <inheritdoc />
	protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteTheme);

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(DeleteThemeOptions options) {
		if (!ThemeParameterValidator.TryValidateId(options.Id, out string error)) {
			CommandSuccess = false;
			Logger.WriteError(error);
			return;
		}
		base.ExecuteRemoteCommand(options);
	}

	/// <inheritdoc />
	protected override string GetRequestData(DeleteThemeOptions options) {
		return JsonSerializer.Serialize(new DeleteThemeRequest { Id = options.Id });
	}

	/// <inheritdoc />
	protected override void ProceedResponse(string response, DeleteThemeOptions options) {
		if (ThemeServiceResponseParser.TryGetFailure(response, out string message)) {
			CommandSuccess = false;
			Logger.WriteError(ThemeServiceResponseParser.DescribeFailure("DeleteTheme", message));
		}
	}

	private sealed record DeleteThemeRequest
	{
		[JsonPropertyName("id")]
		public string Id { get; init; }
	}
}
