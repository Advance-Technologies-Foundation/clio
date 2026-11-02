using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using CommandLine;

namespace Clio.Command.Branding;

[Verb("remove-background",
	HelpText = "Remove the background of a custom Creatio theme, so the theme shows the site background again")]
[RequiresCreatioVersion(ThemeServiceRequirement.BackgroundMinVersion)]
public class RemoveBackgroundOptions : RemoteCommandOptions
{
	[Option("theme", Required = true, HelpText = "Id (a GUID) of the custom theme whose background to remove")]
	public string Theme { get; set; }
}

public class RemoveBackgroundCommand : RemoteCommand<RemoveBackgroundOptions>
{
	private readonly IServiceUrlBuilder _urlBuilder;

	public RemoveBackgroundCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder urlBuilder)
		: base(applicationClient, settings) {
		_urlBuilder = urlBuilder;
	}

	protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.UpdateTheme);

	protected override void ExecuteRemoteCommand(RemoveBackgroundOptions options) {
		if (!ThemeParameterValidator.TryValidateId(options.Theme, out string error)) {
			CommandSuccess = false;
			Logger.WriteError(error);
			return;
		}
		base.ExecuteRemoteCommand(options);
	}

	protected override string GetRequestData(RemoveBackgroundOptions options) {
		return ThemeBackground.UpdateRequest(options.Theme, new ThemeBackground());
	}

	protected override void ProceedResponse(string response, RemoveBackgroundOptions options) {
		if (ThemeServiceResponseParser.TryGetWriteFailure(response, out string message)) {
			CommandSuccess = false;
			Logger.WriteError(ThemeServiceResponseParser.DescribeFailure("UpdateTheme", message));
			return;
		}
		Logger.WriteInfo($"Removed the background of theme '{options.Theme}'; its users see the site background " +
			"after a page refresh.");
	}
}
