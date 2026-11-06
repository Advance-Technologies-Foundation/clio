using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Branding;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.Theming;

public sealed record ThemeBackgroundOutcome(IReadOnlyList<string> Warnings, string ImagePackage = null,
	string PanelIconBackground = null) {

	public const string PanelIconOff = "off";

	public const string PanelIconKept = "kept";

	public static ThemeBackgroundOutcome None { get; } = new([]);
}

public interface IThemeBackgroundWriter {

	bool TryConfirmImage(ThemeBackgroundArguments background, out string error);

	ThemeBackgroundOutcome Complete(ThemeBackgroundArguments background, string themeId, string packageName);
}

internal sealed class ThemeBackgroundWriter(IFeatureStateService featureState, IPackageDataBinder packageDataBinder,
	IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, ILogger logger) : IThemeBackgroundWriter {

	private const string PanelIconBackgroundFeatureCode = SetBackgroundImageCommand.PanelIconBackgroundFeatureCode;

	private static readonly IReadOnlyList<string> SysImageColumns = ["Id", "Name", "Data", "MimeType"];

	public bool TryConfirmImage(ThemeBackgroundArguments background, out string error) {
		error = null;
		if (!background.HasImageId) {
			return true;
		}
		string imageId = background.ImageGuid.ToString();
		try {
			ImageRows found = SelectQueryHelper.ExecuteSelectQuery<ImageRows>(applicationClient, serviceUrlBuilder,
				SelectQueryHelper.BuildSelectQuery("SysImage", [new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
					[new SelectQueryHelper.SelectQueryFilterDefinition("Id", imageId, SelectQueryHelper.GuidDataValueType)], 1));
			if (found.Rows.Count > 0) {
				return true;
			}
			error = $"No uploaded image with id '{imageId}' was found in the environment, so nothing was changed. " +
				"Upload the file first with upload-image and pass the id it prints.";
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			error = $"Could not check image '{imageId}' in the environment, so nothing was changed: " +
				SensitiveErrorTextRedactor.Redact(exception.Message);
		}
		return false;
	}

	public ThemeBackgroundOutcome Complete(ThemeBackgroundArguments background, string themeId, string packageName) {
		List<string> warnings = [];
		string panelIconBackground = null;
		if (background.SetsBackground && background.KeepIconBackground) {
			logger.WriteInfo($"{PanelIconBackgroundFeatureCode} was left as it is (keep-icon-background).");
			panelIconBackground = ThemeBackgroundOutcome.PanelIconKept;
		} else if (background.SetsBackground) {
			try {
				featureState.SetFeatureState(PanelIconBackgroundFeatureCode, SysAdminUnitIds.AllEmployees, state: false);
				logger.WriteInfo($"{PanelIconBackgroundFeatureCode} is off for All employees.");
				panelIconBackground = ThemeBackgroundOutcome.PanelIconOff;
			} catch (Exception exception) when (exception is not OperationCanceledException) {
				warnings.Add($"The theme background was applied, but turning off the {PanelIconBackgroundFeatureCode} " +
					$"feature failed, so the panel may still hide it: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
			}
		}
		if (!background.HasImageId) {
			return new ThemeBackgroundOutcome(warnings, PanelIconBackground: panelIconBackground);
		}
		Guid imageId = background.ImageGuid;
		string imagePackage = null;
		try {
			string boundPackage = packageDataBinder.UsePackage(packageName);
			PackageDataBindingOutcome outcome = packageDataBinder.BindRow("SysImage",
				"ThemeBackground_" + Guid.Parse(themeId).ToString("N"), SysImageColumns, imageId);
			warnings.AddRange(SensitiveErrorTextRedactor.RedactAll(outcome.Warnings));
			if (outcome.Bound) {
				logger.WriteInfo($"Bound the background image into package '{boundPackage}'.");
				imagePackage = boundPackage;
			}
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			warnings.Add($"The theme background was applied, but binding image {imageId} into the " +
				$"theme's package failed: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
		}
		return new ThemeBackgroundOutcome(warnings, imagePackage, panelIconBackground);
	}

	private sealed class ImageRows : SelectQueryHelper.SelectQueryResponseBaseDto {

		[JsonPropertyName("rows")]
		public List<JsonElement> Rows { get; init; } = [];
	}
}
