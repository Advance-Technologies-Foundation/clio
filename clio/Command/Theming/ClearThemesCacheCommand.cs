using Clio.Common;
using CommandLine;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>clear-themes-cache</c> command.
/// </summary>
[Verb("clear-themes-cache", Aliases = ["flush-themes"],
	HelpText = "Refresh the Creatio theme catalog cache")]
[RequiresCreatioVersion(ThemeServiceRequirement.MinVersion)]
public class ClearThemesCacheOptions : RemoteCommandOptions
{
}

/// <summary>
/// Refreshes the Creatio theme catalog cache on the target environment by calling the native
/// <c>ThemeService.svc/ClearThemesCache</c> endpoint. Requires the <c>CanCustomizeBranding</c> license
/// and the <c>CanManageThemes</c> system operation on the caller.
/// </summary>
public class ClearThemesCacheCommand : RemoteCommand<ClearThemesCacheOptions>
{
	private readonly IServiceUrlBuilder _urlBuilder;

	/// <summary>
	/// Initializes a new instance of the <see cref="ClearThemesCacheCommand"/> class.
	/// </summary>
	public ClearThemesCacheCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder urlBuilder)
		: base(applicationClient, settings) {
		_urlBuilder = urlBuilder;
	}

	/// <inheritdoc />
	protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearThemesCache);

	/// <inheritdoc />
	protected override void ProceedResponse(string response, ClearThemesCacheOptions options) {
		if (ThemeServiceResponseParser.TryGetFailure(response, out string message)) {
			CommandSuccess = false;
			Logger.WriteError(ThemeServiceResponseParser.DescribeFailure("ClearThemesCache", message));
		}
	}
}
