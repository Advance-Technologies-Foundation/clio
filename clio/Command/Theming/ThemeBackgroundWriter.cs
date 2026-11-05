using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.Theming;

public interface IThemeBackgroundWriter {

	bool TryConfirmImage(ThemeBackgroundArguments background, out string error);

	IReadOnlyList<string> Complete(ThemeBackgroundArguments background, string themeId, string packageName);
}

internal sealed class ThemeBackgroundWriter(IFeatureStateService featureState, IPackageDataBinder packageDataBinder,
	IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) : IThemeBackgroundWriter {

	private const string PanelIconBackgroundFeatureCode = "UsePanelIconBackground";

	private static readonly IReadOnlyList<string> SysImageColumns = ["Id", "Name", "Data", "MimeType"];

	public bool TryConfirmImage(ThemeBackgroundArguments background, out string error) {
		error = null;
		if (!background.HasImageId) {
			return true;
		}
		string imageId = background.ImageId.Trim();
		try {
			ImageRows found = SelectQueryHelper.ExecuteSelectQuery<ImageRows>(applicationClient, serviceUrlBuilder,
				SelectQueryHelper.BuildSelectQuery("SysImage", [new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
					[new SelectQueryHelper.SelectQueryFilterDefinition("Id", imageId, SelectQueryHelper.GuidDataValueType)], 1));
			if (found.Rows.Count > 0) {
				return true;
			}
			error = $"No uploaded image with id '{imageId}' was found in the environment, so nothing was changed. " +
				"Upload the file first with upload-image and pass the id it prints.";
		} catch (Exception exception) {
			error = $"Could not check image '{imageId}' in the environment, so nothing was changed: {exception.Message}";
		}
		return false;
	}

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

	private sealed class ImageRows : SelectQueryHelper.SelectQueryResponseBaseDto {

		[JsonPropertyName("rows")]
		public List<JsonElement> Rows { get; init; } = [];
	}
}
