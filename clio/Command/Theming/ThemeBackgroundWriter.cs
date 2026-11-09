using System;
using System.Collections.Generic;
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

	bool TryConfirmImage(ThemeBackground background, out string error);

	ThemeBackgroundOutcome Complete(ThemeBackground background, bool keepIconBackground, string themeId,
		string packageName);
}

internal sealed class ThemeBackgroundWriter(IBrandingBackgroundService backgroundService,
	IPackageDataBinder packageDataBinder, ILogger logger) : IThemeBackgroundWriter {

	private const string PanelIconBackgroundFeatureCode = BrandingBackgroundService.PanelIconBackgroundFeatureCode;

	private static readonly IReadOnlyList<string> SysImageColumns = ["Id", "Name", "Data", "MimeType"];

	public bool TryConfirmImage(ThemeBackground background, out string error) {
		error = null;
		if (background?.ImageId is null) {
			return true;
		}
		Guid imageId = Guid.Parse(background.ImageId);
		if (!backgroundService.TryFindUploadedImage(imageId, out bool exists, out string checkError)) {
			error = $"Could not check image '{imageId}' in the environment, so nothing was changed: " +
				SensitiveErrorTextRedactor.Redact(checkError);
			return false;
		}
		if (!exists) {
			error = $"No uploaded image with id '{imageId}' was found in the environment, so nothing was changed. " +
				"Upload the file first with upload-image and pass the id it prints.";
		}
		return exists;
	}

	public ThemeBackgroundOutcome Complete(ThemeBackground background, bool keepIconBackground, string themeId,
		string packageName) {
		if (background is null || background.RemovesBackground) {
			return ThemeBackgroundOutcome.None;
		}
		List<string> warnings = [];
		string panelIconBackground = null;
		if (keepIconBackground) {
			logger.WriteInfo($"{PanelIconBackgroundFeatureCode} was left as it is (keep-icon-background).");
			panelIconBackground = ThemeBackgroundOutcome.PanelIconKept;
		} else if (backgroundService.TryTurnOffPanelIconBackground("The theme background", out string warning)) {
			logger.WriteInfo($"{PanelIconBackgroundFeatureCode} is off for All employees.");
			panelIconBackground = ThemeBackgroundOutcome.PanelIconOff;
		} else {
			warnings.Add(SensitiveErrorTextRedactor.Redact(warning));
		}
		if (background.ImageId is null) {
			return new ThemeBackgroundOutcome(warnings, PanelIconBackground: panelIconBackground);
		}
		Guid imageId = Guid.Parse(background.ImageId);
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
}
