using System;
using System.Collections.Generic;
using Clio.Command.Branding;
using Clio.Common;

namespace Clio.Command.Theming;

public sealed record PanelIconBackgroundOutcome(string State, IReadOnlyList<string> Warnings) {

	public static PanelIconBackgroundOutcome None { get; } = new(null, []);
}

public sealed record ThemeImageBindingOutcome(string Package, IReadOnlyList<string> Warnings) {

	public static ThemeImageBindingOutcome None { get; } = new(null, []);
}

public sealed record ThemeBackgroundOutcome(IReadOnlyList<string> Warnings, string ImagePackage = null,
	string PanelIconBackground = null) {

	public const string PanelIconOff = "off";

	public const string PanelIconKept = "kept";

	public static ThemeBackgroundOutcome None { get; } = new([]);

	public static ThemeBackgroundOutcome From(PanelIconBackgroundOutcome panelIcon, ThemeImageBindingOutcome binding) {
		return new ThemeBackgroundOutcome([.. panelIcon.Warnings, .. binding.Warnings], binding.Package, panelIcon.State);
	}
}

public interface IThemeBackgroundWriter {

	bool TryAddImageToBackgroundGallery(ThemeBackground background, out BackgroundGalleryImage image,
		out string error);

	PanelIconBackgroundOutcome TurnOffPanelIconBackground(ThemeBackground background, bool keepIconBackground);

	ThemeImageBindingOutcome BindImageIntoThemePackage(BackgroundGalleryImage image, string themeId,
		string packageName);
}

internal sealed class ThemeBackgroundWriter(IBrandingBackgroundService backgroundService,
	IPackageDataBinder packageDataBinder, ILogger logger) : IThemeBackgroundWriter {

	public bool TryAddImageToBackgroundGallery(ThemeBackground background, out BackgroundGalleryImage image,
		out string error) {
		image = null;
		error = null;
		if (background?.ImageId is null) {
			return true;
		}
		Guid imageId = Guid.Parse(background.ImageId);
		if (backgroundService.TryConfirmUploadedImage(imageId, out string imageError)
			&& backgroundService.TryAddImageToBackgroundGallery(imageId, out image, out imageError)) {
			return true;
		}
		error = $"{SensitiveErrorTextRedactor.Redact(imageError)} The theme was not written.";
		return false;
	}

	public PanelIconBackgroundOutcome TurnOffPanelIconBackground(ThemeBackground background, bool keepIconBackground) {
		if (background is null || background.RemovesBackground) {
			return PanelIconBackgroundOutcome.None;
		}
		if (keepIconBackground) {
			logger.WriteInfo($"{BrandingBackgroundService.PanelIconBackgroundFeatureCode} was left as it is (keep-icon-background).");
			return new PanelIconBackgroundOutcome(ThemeBackgroundOutcome.PanelIconKept, []);
		}
		if (backgroundService.TryTurnOffPanelIconBackground("The theme background", out string warning)) {
			logger.WriteInfo($"{BrandingBackgroundService.PanelIconBackgroundFeatureCode} is off for All employees.");
			return new PanelIconBackgroundOutcome(ThemeBackgroundOutcome.PanelIconOff, []);
		}
		return new PanelIconBackgroundOutcome(null, [SensitiveErrorTextRedactor.Redact(warning)]);
	}

	public ThemeImageBindingOutcome BindImageIntoThemePackage(BackgroundGalleryImage image, string themeId,
		string packageName) {
		if (image is null) {
			return ThemeImageBindingOutcome.None;
		}
		if (!TryUsePackage(packageName, out string package, out string packageError)) {
			return new ThemeImageBindingOutcome(null, [DescribeBindingFailure(null, [], packageError)]);
		}
		GalleryImageBindingOutcome binding = backgroundService.BindGalleryImage(packageDataBinder, image,
			"ThemeBackground_" + Guid.Parse(themeId).ToString("N"));
		List<string> warnings = [.. SensitiveErrorTextRedactor.RedactAll(binding.Warnings)];
		if (binding.Error is not null) {
			warnings.Add(DescribeBindingFailure(package, binding.Bound, binding.Error));
		}
		if (binding.Bound.Count > 0) {
			logger.WriteInfo($"Bound the background into package '{package}': {string.Join(", ", binding.Bound)}.");
		}
		return new ThemeImageBindingOutcome(binding.ImageBound ? package : null, warnings);
	}

	private bool TryUsePackage(string packageName, out string package, out string error) {
		package = null;
		error = null;
		try {
			package = packageDataBinder.UsePackage(packageName);
			return true;
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			error = exception.Message;
			return false;
		}
	}

	private static string DescribeBindingFailure(string package, IReadOnlyList<string> bound, string error) {
		string into = package is null ? "the theme's package" : $"package '{package}'";
		string kept = bound.Count == 0 ? string.Empty : $" Already bound and left in place: {string.Join(", ", bound)}.";
		return $"The theme background was applied, but binding it into {into} failed: " +
			$"{SensitiveErrorTextRedactor.Redact(error)}{kept}";
	}
}
