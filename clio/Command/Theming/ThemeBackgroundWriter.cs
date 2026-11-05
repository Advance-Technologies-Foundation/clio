using System;
using System.Collections.Generic;
using Clio.Common;

namespace Clio.Command.Theming;

public interface IThemeBackgroundWriter {

	IReadOnlyList<string> Complete(ThemeBackgroundArguments background, string themeId, string packageName);
}

internal sealed class ThemeBackgroundWriter(IFeatureStateService featureState, IPackageDataBinder packageDataBinder)
	: IThemeBackgroundWriter {

	private const string PanelIconBackgroundFeatureCode = "UsePanelIconBackground";

	private static readonly IReadOnlyList<string> SysImageColumns = ["Id", "Name", "Data", "MimeType"];

	public IReadOnlyList<string> Complete(ThemeBackgroundArguments background, string themeId, string packageName) {
		List<string> warnings = [];
		if (background.SetsBackground && !background.KeepIconBackground) {
			try {
				featureState.SetFeatureState(PanelIconBackgroundFeatureCode, SysAdminUnitIds.AllEmployees, state: false);
			} catch (Exception exception) {
				warnings.Add($"The theme background was applied, but turning off the {PanelIconBackgroundFeatureCode} " +
					$"feature failed, so the panel may still hide it: {exception.Message}");
			}
		}
		if (!background.HasImageId) {
			return warnings;
		}
		try {
			packageDataBinder.UsePackage(packageName);
			warnings.AddRange(packageDataBinder.BindRow("SysImage", "ThemeBackground_" + Guid.Parse(themeId).ToString("N"),
				SysImageColumns, Guid.Parse(background.ImageId.Trim())).Warnings);
		} catch (Exception exception) {
			warnings.Add($"The theme background was applied, but binding image {background.ImageId.Trim()} into the " +
				$"theme's package failed: {exception.Message}");
		}
		return warnings;
	}
}
