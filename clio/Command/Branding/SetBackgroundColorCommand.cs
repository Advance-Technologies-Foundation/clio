using System;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using CommandLine;

namespace Clio.Command.Branding;

[Verb("set-background-color", HelpText = "Set a solid color as the background of a custom Creatio theme")]
[RequiresCreatioVersion(ThemeServiceRequirement.BackgroundMinVersion)]
public class SetBackgroundColorOptions : RemoteCommandOptions
{
	[Value(0, MetaName = "color", Required = true,
		HelpText = "Hex color (#RGB, #RRGGBB or #RRGGBBAA) to use as the background")]
	public string Color { get; set; }

	[Option("theme", Required = true, HelpText = "Id (a GUID) of the custom theme to give the background")]
	public string Theme { get; set; }

	[Option("keep-icon-background", Required = false,
		HelpText = "Keep the panel icon background feature (UsePanelIconBackground) as is instead of turning " +
			"it off. While the feature is on, the panel's own icon background can hide the background.")]
	public bool KeepIconBackground { get; set; }
}

public class SetBackgroundColorCommand : RemoteCommand<SetBackgroundColorOptions>
{
	private readonly IServiceUrlBuilder _urlBuilder;
	private readonly IFeatureStateService _featureState;

	public SetBackgroundColorCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder urlBuilder, IFeatureStateService featureState)
		: base(applicationClient, settings) {
		_urlBuilder = urlBuilder;
		_featureState = featureState;
	}

	protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.UpdateTheme);

	protected override void ExecuteRemoteCommand(SetBackgroundColorOptions options) {
		if (!ThemeParameterValidator.TryValidateId(options.Theme, out string error)) {
			CommandSuccess = false;
			Logger.WriteError(error);
			return;
		}
		base.ExecuteRemoteCommand(options);
	}

	protected override string GetRequestData(SetBackgroundColorOptions options) {
		return ThemeBackground.UpdateRequest(options.Theme,
			new ThemeBackground { Mode = ThemeBackground.ColorMode, Color = options.Color?.Trim() });
	}

	protected override void ProceedResponse(string response, SetBackgroundColorOptions options) {
		if (ThemeServiceResponseParser.TryGetWriteFailure(response, out string message)) {
			CommandSuccess = false;
			Logger.WriteError(ThemeServiceResponseParser.DescribeFailure("UpdateTheme", message));
			return;
		}
		Logger.WriteInfo($"Color {options.Color?.Trim()} is set as the background of theme '{options.Theme}'. Users " +
			"of the theme see it after a page refresh.");
		if (options.KeepIconBackground) {
			return;
		}
		try {
			_featureState.SetFeatureState(SetBackgroundImageCommand.PanelIconBackgroundFeatureCode,
				SysAdminUnitIds.AllEmployees, state: false);
		}
		catch (Exception exception) {
			Logger.WriteWarning($"Turning off the {SetBackgroundImageCommand.PanelIconBackgroundFeatureCode} feature " +
				$"failed, so the panel may still hide the color: {exception.Message}");
		}
	}
}
