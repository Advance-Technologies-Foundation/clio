using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Branding;

/// <summary>
/// Options for the <c>set-logo</c> command. <see cref="Logo"/> brands every slot at once from one file;
/// each slot option brands that slot alone and overrides <see cref="Logo"/> for it. At least one image
/// option is required. Everything applied is also bound into <see cref="PackageName"/> as package data.
/// </summary>
[Verb("set-logo",
	HelpText = "Apply the product logos and the browser-tab favicon from local image files and bind them " +
		"into a package as data bindings")]
public class SetLogoOptions : RemoteCommandOptions {

	/// <summary>
	/// Local image file applied to every logo slot at once. A slot option (<see cref="LoginLogo"/>,
	/// <see cref="MenuLogo"/>, <see cref="ConfigurationLogo"/>, <see cref="DarkLogo"/>) wins for its own slot.
	/// </summary>
	[Option("logo", Required = false,
		HelpText = "Local image file applied to every logo slot at once. A slot option (--login-logo, " +
			"--menu-logo, --configuration-logo, --dark-logo) overrides it for that slot.")]
	public string Logo { get; set; }

	/// <summary>Local image file for the login-page logo.</summary>
	[Option("login-logo", Required = false,
		HelpText = "Local image file for the logo on the login page (LogoImage)")]
	public string LoginLogo { get; set; }

	/// <summary>Local image file for the main-menu logo.</summary>
	[Option("menu-logo", Required = false,
		HelpText = "Local image file for the main menu logo (MenuLogoImage)")]
	public string MenuLogo { get; set; }

	/// <summary>Local image file for the configuration-page logo.</summary>
	[Option("configuration-logo", Required = false,
		HelpText = "Local image file for the configuration page logo (ConfigurationPageLogoImage)")]
	public string ConfigurationLogo { get; set; }

	/// <summary>Local image file for the dark-surface logo (the Freedom UI top panel).</summary>
	[Option("dark-logo", Required = false,
		HelpText = "Local image file for the logo on the dark Freedom UI top panel (CrtAppToolbarLogo). " +
			"Pass the light variant of the logo here — a logo drawn for a white background is hard to read " +
			"on the dark panel.")]
	public string DarkLogo { get; set; }

	/// <summary>Local image file for the browser-tab icon.</summary>
	[Option("favicon", Required = false,
		HelpText = "Local image file for the browser-tab icon (FaviconImage). Pass a square icon; accepted " +
			"formats: " + SetLogoCommand.FaviconImageFormats + ". Not taken from --logo.")]
	public string Favicon { get; set; }

	/// <summary>Package that receives the branding data bindings.</summary>
	[Option("package", Required = false,
		HelpText = "Package that receives the branding data bindings. When omitted, the package from the " +
			"environment's CurrentPackageId system setting is used.")]
	public string PackageName { get; set; }
}

/// <summary>
/// Outcome of applying and binding the product logos.
/// </summary>
public sealed record SetLogoResult {

	/// <summary>
	/// Whether every requested image was applied and bound, and every setting an applied image depends on to
	/// take effect is on.
	/// </summary>
	public bool Success { get; private init; }

	/// <summary>Labels of the branding images that were applied, in request order.</summary>
	public IReadOnlyList<string> Applied { get; private init; } = [];

	/// <summary>
	/// Codes of the settings the package delivery confirmed it bound, in delivery order. Empty means the
	/// package carries nothing from this run even when <see cref="Applied"/> is not — every binding can be
	/// refused while every apply succeeds, and <see cref="Warnings"/> then names the reason for each.
	/// </summary>
	public IReadOnlyList<string> Bound { get; private init; } = [];

	/// <summary>
	/// The package the branding data was bound into; null only when the run never got as far as resolving one.
	/// Populated on a partial failure too, because the slots that did apply were bound into it.
	/// </summary>
	public string Package { get; private init; }

	/// <summary>
	/// Every non-fatal problem the caller should surface, in the order it arose: an apply-side caveat such as a
	/// failed splash-logo toggle, then each delivery gap (a slot without a value, a slot refused by policy, a
	/// dropped binding). One channel rather than two, because a caveat and a gap are the same thing to the
	/// caller — the run succeeded but delivers less than it looks like it did.
	/// </summary>
	public IReadOnlyList<string> Warnings { get; private init; } = [];

	/// <summary>The failure message; null on success.</summary>
	public string Error { get; private init; }

	/// <summary>Creates a success result carrying what was applied, the target package, and any warnings.</summary>
	public static SetLogoResult Successful(IReadOnlyList<string> applied, string package,
		IReadOnlyList<string> warnings, IReadOnlyList<string> bound = null) {
		return new SetLogoResult {
			Success = true, Applied = applied, Package = package, Warnings = warnings ?? [],
			Bound = bound ?? []
		};
	}

	/// <summary>
	/// Creates a failure result. <paramref name="applied"/> carries the slots that were already written
	/// before the failure, so the caller can see the partial state instead of assuming nothing changed;
	/// <paramref name="warnings"/> carries the caveats raised before it, which the failure must not swallow;
	/// <paramref name="package"/> names the delivery target once one was resolved, so a caller reading the
	/// structured result learns where the applied slots landed without parsing the message.
	/// </summary>
	public static SetLogoResult Failure(string error, IReadOnlyList<string> applied = null,
		IReadOnlyList<string> warnings = null, string package = null, IReadOnlyList<string> bound = null) {
		return new SetLogoResult {
			Success = false, Error = error, Applied = applied ?? [], Warnings = warnings ?? [], Package = package,
			Bound = bound ?? []
		};
	}
}

/// <summary>
/// Applies the product logos from local image files — one Binary sys-setting per slot — and binds the
/// applied values into a package as Creatio data bindings, so the logos ship with the package instead of
/// living only on this environment. Also sets <c>HideSplashScreenLogoImage</c> so the stock splash logo
/// does not flash during load, and applies the browser-tab favicon when one is passed. Re-running with a
/// new file refreshes both the environment and the packaged snapshot.
/// </summary>
public class SetLogoCommand : RemoteCommand<SetLogoOptions> {

	/// <summary>The login-page logo setting.</summary>
	internal const string LoginLogoCode = "LogoImage";

	/// <summary>The main-menu logo setting.</summary>
	internal const string MenuLogoCode = "MenuLogoImage";

	/// <summary>The configuration-page logo setting.</summary>
	internal const string ConfigurationLogoCode = "ConfigurationPageLogoImage";

	/// <summary>The dark-surface (Freedom UI top panel) logo setting.</summary>
	internal const string DarkLogoCode = "CrtAppToolbarLogo";

	/// <summary>The Boolean setting that suppresses the stock splash-screen logo.</summary>
	internal const string HideSplashLogoCode = "HideSplashScreenLogoImage";

	/// <summary>The Binary setting holding the browser-tab icon.</summary>
	internal const string FaviconCode = "FaviconImage";

	/// <summary>The Boolean gate; while it is off the platform ignores <see cref="FaviconCode"/>.</summary>
	internal const string UseFaviconCode = "UseFaviconFromSysSettings";

	/// <summary>How the favicon names itself in <see cref="SetLogoResult.Applied"/>, next to the slot labels.</summary>
	internal const string FaviconLabel = "favicon";

	/// <summary>Error text shared by the CLI and MCP surfaces when no image file is passed.</summary>
	internal const string NoImageError =
		"Pass at least one image file: logo (every slot at once), login-logo, menu-logo, configuration-logo, " +
		"dark-logo, or favicon.";

	/// <summary>Comma-separated list of the image file extensions the logo slots accept.</summary>
	internal const string LogoImageFormats = "png, jpg, jpeg, gif, bmp, webp, svg";

	/// <summary>Comma-separated list of the image file extensions the favicon accepts.</summary>
	internal const string FaviconImageFormats = LogoImageFormats + ", ico";

	private static readonly BrandingImageFormat LogoFormat = new(LogoImageFormats.Split(", "));

	private static readonly BrandingImageFormat FaviconFormat = new(FaviconImageFormats.Split(", "));

	private static readonly BrandingCompanion SplashSuppression = new(HideSplashLogoCode,
		$"The logos were applied, but setting {HideSplashLogoCode} failed, so the stock splash logo may still " +
		"flash during load",
		RequiredForEffect: false);

	private static readonly BrandingCompanion FaviconGate = new(UseFaviconCode,
		$"The favicon image was written, but turning {UseFaviconCode} on failed, so the platform ignores the " +
		"image and keeps the stock icon",
		RequiredForEffect: true);

	private readonly SysSettingsCommand _sysSettingsCommand;
	private readonly IPackageDataBinder _packageDataBinder;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetLogoCommand"/> class.
	/// </summary>
	public SetLogoCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		SysSettingsCommand sysSettingsCommand, IPackageDataBinder packageDataBinder, IFileSystem fileSystem)
		: base(applicationClient, settings) {
		_sysSettingsCommand = sysSettingsCommand;
		_packageDataBinder = packageDataBinder;
		_fileSystem = fileSystem;
	}

	/// <summary>
	/// Applies every image file in <paramref name="options"/> to its sys-setting, suppresses the stock splash
	/// logo, turns the favicon gate on when a favicon is passed, and delivers everything applied into the
	/// target package. At least one image option is required.
	/// </summary>
	/// <remarks>
	/// A refused image — one whose file is not a supported image format, or one the environment rejects —
	/// does not abandon the images that already succeeded: as long as one applied, the run still suppresses
	/// the splash logo and delivers what applied into the package, so the environment and the package never
	/// drift apart. The result is a failure naming every refused image, because the caller asked for more
	/// than the run produced — read <see cref="SetLogoResult.Applied"/> to see what did land before
	/// re-running with the refused images fixed.
	/// </remarks>
	/// <param name="options">Command options carrying the per-slot files, the favicon, target package, and connection settings.</param>
	/// <returns>The outcome, carrying what was applied, the bound package, and any warnings.</returns>
	public virtual SetLogoResult ApplyLogos(SetLogoOptions options) {
		var requestedImages = ResolveRequestedImages(options);
		if (requestedImages.Count == 0) {
			return SetLogoResult.Failure(NoImageError);
		}
		var missingFileError = FindMissingFileError(requestedImages);
		if (missingFileError is not null) {
			return SetLogoResult.Failure(missingFileError);
		}

		var (supportedImages, formatRefusals) = PartitionBySupportedFormat(requestedImages);
		var (appliedImages, applyRefusals) = ApplyImages(supportedImages, options.Environment);
		var refusedImages = formatRefusals.Concat(applyRefusals).ToList();
		if (appliedImages.Count == 0) {
			return SetLogoResult.Failure(DescribeRefusedImages(refusedImages));
		}
		var (companionCodes, companionFailures) = TurnCompanionsOn(appliedImages, options.Environment);
		var packageDelivery = BindAppliedImages(appliedImages, companionCodes, options.PackageName);
		return BuildResult(appliedImages, refusedImages, companionFailures, packageDelivery);
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(SetLogoOptions options) {
		var result = ApplyLogos(options);
		if (result.Applied.Count > 0) {
			var refreshNotice = result.Applied.Any(label => label != FaviconLabel)
				? " Users see the new branding after a page refresh."
				: string.Empty;
			Logger.WriteInfo($"Applied: {string.Join(", ", result.Applied)}.{refreshNotice}");
		}
		if (result.Applied.Contains(FaviconLabel)) {
			Logger.WriteInfo("A favicon change is never visible on an open session: users must sign out and back " +
				"in, and an already-open browser tab may keep the old icon until it is closed and reopened.");
		}
		if (result.Package is not null) {
			Logger.WriteInfo(result.Bound.Count > 0
				? $"Branding data bound into package '{result.Package}': {string.Join(", ", result.Bound)}."
				: $"No branding data could be bound into package '{result.Package}'.");
		}
		Logger.WriteWarnings(result.Warnings);
		if (!result.Success) {
			CommandSuccess = false;
			Logger.WriteError(result.Error);
		}
	}

	private string FindMissingFileError(IReadOnlyList<BrandingImage> requestedImages) {
		var missing = requestedImages
			.Where(image => !_fileSystem.ExistsFile(image.File))
			.ToList();
		if (missing.Count == 0) {
			return null;
		}
		var details = string.Join("; ",
			missing.Select(image => $"'{image.File}' (passed for {image.Label})"));
		return $"Files not found: {details}.";
	}

	private (List<BrandingImage> Applied, List<RefusedBrandingImage> Refused) ApplyImages(
		IReadOnlyList<BrandingImage> requestedImages, string environment) {
		var (applied, refused) = WriteSysSettings(requestedImages, image =>
			new UpdateSysSettingArgs(environment ?? string.Empty, image.Code, ValueFilePath: image.File));
		var refusedImages = refused
			.Select(refusal => new RefusedBrandingImage(refusal.Item, refusal.Error))
			.ToList();
		return (applied, refusedImages);
	}

	private (List<string> Codes, List<BrandingCompanionFailure> Failures) TurnCompanionsOn(
		IReadOnlyList<BrandingImage> appliedImages, string environment) {
		var companions = appliedImages
			.Select(image => image.Companion)
			.Where(companion => companion is not null)
			.Distinct();
		var (turnedOn, refused) = WriteSysSettings(companions, companion =>
			new UpdateSysSettingArgs(environment ?? string.Empty, companion.Code, Value: "true"));
		var codes = turnedOn
			.Select(companion => companion.Code)
			.ToList();
		var failures = new List<BrandingCompanionFailure>();
		foreach (var refusal in refused) {
			if (ReadCompanionIsOn(refusal.Item, environment)) {
				codes.Add(refusal.Item.Code);
			} else {
				failures.Add(new BrandingCompanionFailure(refusal.Item.RequiredForEffect,
					$"{refusal.Item.FailureMessage}: {refusal.Error}"));
			}
		}
		return (codes, failures);
	}

	private bool ReadCompanionIsOn(BrandingCompanion companion, string environment) {
		var current = _sysSettingsCommand.TryGetSysSetting(
			new GetSysSettingArgs(environment ?? string.Empty, companion.Code));
		return current.Success && bool.TryParse(current.Value, out var parsed) && parsed;
	}

	private PackageDelivery BindAppliedImages(IReadOnlyList<BrandingImage> appliedImages,
		IReadOnlyList<string> companionCodes, string packageName) {
		var warnings = new List<string>();
		var package = default(string);
		var boundCodes = new List<string>();
		try {
			package = _packageDataBinder.UsePackage(packageName);
			var codesToBind = appliedImages.Select(image => image.Code).Concat(companionCodes);
			foreach (var code in codesToBind) {
				var outcome = _packageDataBinder.BindSysSettingsValue(code);
				warnings.AddRange(outcome.Warnings);
				if (outcome.Bound) {
					boundCodes.Add(code);
				}
			}
			return new PackageDelivery(package, boundCodes, warnings, null);
		} catch (Exception exception) {
			return new PackageDelivery(package, boundCodes, warnings, exception);
		}
	}

	private (List<T> Written, List<(T Item, string Error)> Refused) WriteSysSettings<T>(
		IEnumerable<T> items, Func<T, UpdateSysSettingArgs> buildArgs) {
		var written = new List<T>();
		var refused = new List<(T Item, string Error)>();
		foreach (var item in items) {
			var update = _sysSettingsCommand.TryUpdateSysSetting(buildArgs(item));
			if (update.Success) {
				written.Add(item);
			} else {
				refused.Add((item, update.Error));
			}
		}
		return (written, refused);
	}

	private static (List<BrandingImage> Supported, List<RefusedBrandingImage> Refused) PartitionBySupportedFormat(
		IReadOnlyList<BrandingImage> requestedImages) {
		var supported = new List<BrandingImage>();
		var refused = new List<RefusedBrandingImage>();
		foreach (var image in requestedImages) {
			var formatError = FindUnsupportedFormatError(image);
			if (formatError is null) {
				supported.Add(image);
			} else {
				refused.Add(new RefusedBrandingImage(image, formatError));
			}
		}
		return (supported, refused);
	}

	private static string FindUnsupportedFormatError(BrandingImage image) {
		string extension = Path.GetExtension(image.File).TrimStart('.');
		if (extension.Length == 0) {
			return $"the file has no extension, so it cannot be verified as an image ({image.Label} accepts: {image.Format.DisplayList})";
		}
		if (!image.Format.Accepts(extension)) {
			return $"'.{extension}' is not a supported image format ({image.Label} accepts: {image.Format.DisplayList})";
		}
		return null;
	}

	private static IReadOnlyList<BrandingImage> ResolveRequestedImages(SetLogoOptions options) {
		var candidates = new BrandingImage[] {
			new("login-logo", LoginLogoCode, ResolveSlotFile(options.LoginLogo, options.Logo), LogoFormat,
				SplashSuppression),
			new("menu-logo", MenuLogoCode, ResolveSlotFile(options.MenuLogo, options.Logo), LogoFormat,
				SplashSuppression),
			new("configuration-logo", ConfigurationLogoCode,
				ResolveSlotFile(options.ConfigurationLogo, options.Logo), LogoFormat, SplashSuppression),
			new("dark-logo", DarkLogoCode, ResolveSlotFile(options.DarkLogo, options.Logo), LogoFormat,
				SplashSuppression),
			new(FaviconLabel, FaviconCode, options.Favicon, FaviconFormat, FaviconGate)
		};
		return candidates
			.Where(candidate => !string.IsNullOrWhiteSpace(candidate.File))
			.ToList();
	}

	private static string ResolveSlotFile(string slotOverride, string allSlotsFile) {
		return string.IsNullOrWhiteSpace(slotOverride) ? allSlotsFile : slotOverride;
	}

	private static SetLogoResult BuildResult(
		IReadOnlyList<BrandingImage> appliedImages,
		IReadOnlyList<RefusedBrandingImage> refusedImages,
		IReadOnlyList<BrandingCompanionFailure> companionFailures,
		PackageDelivery packageDelivery) {
		var appliedLabels = appliedImages.Select(image => image.Label).ToList();
		var warnings = companionFailures
			.Where(failure => !failure.Fatal)
			.Select(failure => failure.Message)
			.Concat(packageDelivery.Warnings)
			.ToList();
		var errors = new List<string>();
		if (refusedImages.Count > 0) {
			errors.Add(DescribeRefusedImages(refusedImages));
		}
		errors.AddRange(companionFailures
			.Where(failure => failure.Fatal)
			.Select(failure => failure.Message));
		if (packageDelivery.Failure is not null) {
			errors.Add(DescribeDeliveryFailure(packageDelivery));
		}
		if (errors.Count == 0) {
			return SetLogoResult.Successful(appliedLabels, packageDelivery.Package, warnings,
				packageDelivery.BoundCodes);
		}
		return SetLogoResult.Failure(string.Join(" ", errors),
			appliedLabels, warnings, packageDelivery.Package, packageDelivery.BoundCodes);
	}

	private static string DescribeDeliveryFailure(PackageDelivery packageDelivery) {
		var into = packageDelivery.Package is null
			? string.Empty
			: $" into package '{packageDelivery.Package}'";
		return $"The applied branding could not be bound{into}: {packageDelivery.Failure.Message}";
	}

	private static string DescribeRefusedImages(IReadOnlyList<RefusedBrandingImage> refusedImages) {
		var details = string.Join("; ", refusedImages.Select(refusal =>
			$"{refusal.Image.Label} ({refusal.Image.Code}) from '{refusal.Image.File}': {refusal.Error}"));
		return $"Applying {refusedImages.Count} image(s) failed: {details}.";
	}

	private sealed record BrandingCompanion(string Code, string FailureMessage, bool RequiredForEffect);

	private sealed record BrandingCompanionFailure(bool Fatal, string Message);

	private sealed record BrandingImageFormat(string[] Extensions) {

		public string DisplayList { get; } = string.Join(", ", Extensions);

		public bool Accepts(string extension) {
			return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
		}
	}

	private sealed record BrandingImage(
		string Label,
		string Code,
		string File,
		BrandingImageFormat Format,
		BrandingCompanion Companion = null);

	private sealed record RefusedBrandingImage(BrandingImage Image, string Error);

	private sealed record PackageDelivery(
		string Package,
		List<string> BoundCodes,
		List<string> Warnings,
		Exception Failure);
}
