using System;
using System.Collections.Generic;
using Clio.Command.Branding;
using Clio.Common;

namespace Clio.Command.Theming;

public sealed record PanelIconBackgroundOutcome(string State, IReadOnlyList<string> Warnings) {

	public static PanelIconBackgroundOutcome None { get; } = new(null, []);
}

public sealed record ThemeBackgroundBindingOutcome(string ImagePackage, IReadOnlyList<string> Warnings) {

	public static ThemeBackgroundBindingOutcome None { get; } = new(null, []);
}

public sealed record ThemeBackgroundOutcome(IReadOnlyList<string> Warnings, string ImagePackage = null,
	string PanelIconBackground = null) {

	public const string PanelIconOff = "off";

	public const string PanelIconKept = "kept";

	public static ThemeBackgroundOutcome None { get; } = new([]);

	public static ThemeBackgroundOutcome From(PanelIconBackgroundOutcome panelIcon,
		ThemeBackgroundBindingOutcome binding) {
		return new ThemeBackgroundOutcome([.. panelIcon.Warnings, .. binding.Warnings], binding.ImagePackage,
			panelIcon.State);
	}
}

public interface IThemeBackgroundWriter {

	bool TryAddImageToBackgroundGallery(ThemeBackground background, out BackgroundGalleryImage image,
		out string error);

	PanelIconBackgroundOutcome TurnOffPanelIconBackground(ThemeBackground background, bool keepIconBackground);

	ThemeBackgroundBindingOutcome BindBackgroundIntoThemePackage(ThemeBackground background,
		BackgroundGalleryImage image, string themeId, string packageName);
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

	public ThemeBackgroundBindingOutcome BindBackgroundIntoThemePackage(ThemeBackground background,
		BackgroundGalleryImage image, string themeId, string packageName) {
		if (background is null || background.RemovesBackground) {
			return ThemeBackgroundBindingOutcome.None;
		}
		List<string> bound = [];
		List<string> warnings = [];
		if (!TryUsePackage(packageName, out string package, out string error)
			|| !TryBindGalleryImage(image, themeId, bound, warnings, out bool imageBound, out error)
			|| !TryBindPanelIconOffState(bound, warnings, out error)) {
			warnings.Add(DescribeBindingFailure(package, bound, error));
			imageBound = bound.Contains(BrandingBackgroundService.ImagePart);
		}
		if (bound.Count > 0) {
			logger.WriteInfo($"Bound the background into package '{package}': {string.Join(", ", bound)}.");
		}
		return new ThemeBackgroundBindingOutcome(imageBound ? package : null,
			SensitiveErrorTextRedactor.RedactAll(warnings));
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

	private bool TryBindGalleryImage(BackgroundGalleryImage image, string themeId, List<string> bound,
		List<string> warnings, out bool imageBound, out string error) {
		imageBound = false;
		error = null;
		if (image is null) {
			return true;
		}
		GalleryImageBindingOutcome binding = backgroundService.BindGalleryImage(packageDataBinder, image,
			"ThemeBackground_" + Guid.Parse(themeId).ToString("N"));
		bound.AddRange(binding.Bound);
		warnings.AddRange(binding.Warnings);
		imageBound = binding.ImageBound;
		error = binding.Error;
		return error is null;
	}

	private bool TryBindPanelIconOffState(List<string> bound, List<string> warnings, out string error) {
		error = null;
		try {
			packageDataBinder.BindFeatureOffState(BrandingBackgroundService.PanelIconBackgroundFeatureCode)
				.RecordInto(BrandingBackgroundService.PanelIconOffStatePart, bound, warnings);
			return true;
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			error = exception.Message;
			return false;
		}
	}

	private static string DescribeBindingFailure(string package, IReadOnlyList<string> bound, string error) {
		string into = package is null ? "the theme's package" : $"package '{package}'";
		string kept = bound.Count == 0 ? string.Empty : $" Already bound and left in place: {string.Join(", ", bound)}.";
		return $"The theme background was applied, but binding it into {into} failed: {error}{kept}";
	}
}
