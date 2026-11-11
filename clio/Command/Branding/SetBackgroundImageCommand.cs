using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Branding;

/// <summary>
/// Options for the <c>set-background-image</c> command.
/// </summary>
[Verb("set-background-image",
	HelpText = "Set an image as the environment's shell background and bind it into a package as data bindings")]
public class SetBackgroundImageOptions : RemoteCommandOptions {

	/// <summary>
	/// Id of an already-uploaded image to set as the background (printed by <c>upload-image</c>).
	/// Pass either this or <see cref="File"/>, not both.
	/// </summary>
	[Value(0, MetaName = "image-id", Required = false,
		HelpText = "Id of an already-uploaded image to set as the background (printed by upload-image). Pass either this or --file.")]
	public string ImageId { get; set; }

	/// <summary>
	/// Path to a local image file to upload and set as the background in one step.
	/// Pass either this or <see cref="ImageId"/>, not both.
	/// </summary>
	[Option("file", Required = false,
		HelpText = "Path to a local image file to upload and set as the background in one step. Pass either this or the image-id argument.")]
	public string File { get; set; }

	/// <summary>Package that receives the background data bindings.</summary>
	[Option("package", Required = false,
		HelpText = "Package that receives the background data bindings. When omitted, the package from the " +
			"environment's CurrentPackageId system setting is used.")]
	public string PackageName { get; set; }

	/// <summary>
	/// When true, leaves the <c>UsePanelIconBackground</c> feature untouched instead of turning it off.
	/// </summary>
	[Option("keep-icon-background", Required = false,
		HelpText = "Keep the panel icon background feature (UsePanelIconBackground) as is instead of turning " +
			"it off. While the feature is on, the panel's own icon background can hide the shell background.")]
	public bool KeepIconBackground { get; set; }
}

/// <summary>
/// Outcome of setting the shell background image.
/// </summary>
public sealed record SetBackgroundResult {

	/// <summary>Whether the background was set and bound.</summary>
	public bool Success { get; private init; }

	/// <summary>The id of the image the background points at; <see cref="Guid.Empty"/> on failure.</summary>
	public Guid ImageId { get; private init; }

	/// <summary>
	/// The package the background data was bound into; null only when the run never got as far as resolving one.
	/// Populated on a binding failure too, because the parts that landed before it are in that package.
	/// </summary>
	public string Package { get; private init; }

	/// <summary>
	/// Every non-fatal problem the caller should surface, in the order it arose: an apply-side caveat such as a
	/// failed <c>UsePanelIconBackground</c> turn-off, then each delivery gap (a missing row, a row refused by
	/// policy, a dropped binding). One channel rather than two, because a caveat and a gap are the same thing
	/// to the caller — the run succeeded but delivers less than it looks like it did.
	/// </summary>
	public IReadOnlyList<string> Warnings { get; private init; } = [];

	/// <summary>
	/// The parts of the background the package delivery confirmed it bound, in delivery order, as the stable
	/// tokens <c>image</c>, <c>gallery-membership</c>, <c>background-config</c>, and
	/// <c>panel-icon-off-state</c>. Empty means the package carries nothing from this run even though the
	/// background was applied — every part can be refused while the apply succeeds, and
	/// <see cref="Warnings"/> then names the reason for each.
	/// </summary>
	public IReadOnlyList<string> Bound { get; private init; } = [];

	/// <summary>The failure message; null on success.</summary>
	public string Error { get; private init; }

	/// <summary>Creates a success result carrying the applied image id, the bound package, and any warnings.</summary>
	public static SetBackgroundResult Successful(Guid imageId, string package, IReadOnlyList<string> warnings,
		IReadOnlyList<string> bound = null) {
		return new SetBackgroundResult {
			Success = true, ImageId = imageId, Package = package, Warnings = warnings ?? [],
			Bound = bound ?? []
		};
	}

	/// <summary>
	/// Creates a failure result carrying the diagnostic message and the caveats raised before it, which the
	/// failure must not swallow. <paramref name="package"/> names the delivery target once one was resolved, so
	/// a caller reading the structured result learns where the parts that landed went without parsing the
	/// message.
	/// </summary>
	public static SetBackgroundResult Failure(string error, IReadOnlyList<string> warnings = null,
		string package = null, IReadOnlyList<string> bound = null) {
		return new SetBackgroundResult {
			Success = false, Error = error, Warnings = warnings ?? [], Package = package, Bound = bound ?? []
		};
	}
}

/// <summary>
/// Sets an image as the environment's shell background: optionally uploads a local file first, makes
/// the image available in the background gallery, and points the background configuration at it. The
/// change applies to all users after a page refresh and replaces the currently configured background.
/// </summary>
public class SetBackgroundImageCommand : RemoteCommand<SetBackgroundImageOptions> {

	internal const string BackgroundConfigCode = "CrtBackgroundConfig";

	/// <summary>Error text shared by the CLI and MCP surfaces when both image sources are passed.</summary>
	internal const string BothSourcesError = "Pass either a file or an image-id, not both.";

	/// <summary>Error text shared by the CLI and MCP surfaces when no image source is passed.</summary>
	internal const string NoSourceError =
		"Pass a file to upload (file) or the id of an already-uploaded image (image-id).";

	private const string ShellBackgroundBindingSuffix = "ShellBackground";

	private const string BackgroundConfigPart = "background-config";

	private readonly ISysSettingsManager _sysSettingsManager;
	private readonly ISysImageUploader _sysImageUploader;
	private readonly IBrandingBackgroundService _backgroundService;
	private readonly IPackageDataBinder _packageDataBinder;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetBackgroundImageCommand"/> class.
	/// </summary>
	public SetBackgroundImageCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		ISysSettingsManager sysSettingsManager, ISysImageUploader sysImageUploader,
		IBrandingBackgroundService backgroundService, IPackageDataBinder packageDataBinder)
		: base(applicationClient, settings) {
		_sysSettingsManager = sysSettingsManager;
		_sysImageUploader = sysImageUploader;
		_backgroundService = backgroundService;
		_packageDataBinder = packageDataBinder;
	}

	/// <summary>
	/// Sets the image identified by <paramref name="options"/> as the shell background. Exactly one of
	/// <see cref="SetBackgroundImageOptions.File"/> (uploaded first, then applied) and
	/// <see cref="SetBackgroundImageOptions.ImageId"/> (an already-uploaded image) must be provided.
	/// </summary>
	/// <param name="options">Command options carrying the image source and connection settings.</param>
	/// <returns>The outcome, carrying the applied image id or a failure message.</returns>
	public virtual SetBackgroundResult SetBackground(SetBackgroundImageOptions options) {
		if (!TryResolveOrUploadImageId(options, out Guid imageId, out string sourceError)) {
			return SetBackgroundResult.Failure(sourceError);
		}
		if (!_backgroundService.TryAddImageToBackgroundGallery(imageId, out BackgroundGalleryImage image,
				out string galleryError)) {
			return SetBackgroundResult.Failure(galleryError);
		}
		string configJson = JsonSerializer.Serialize(new { imageId = image.ImageId.ToString(), mode = "Image" });
		//Issue #1378: the setting write no longer reports an unusable acknowledgement as `false` - a
		//gateway or WAF page now leaves the manager as NonJsonWriteResponseException. This command's
		//contract is a SetBackgroundResult, and the image really is in the gallery by this point, so the
		//partial state has to be reported with the same prefix the refusal already uses rather than
		//escaping as an exception that would lose it. The diagnosis is appended, not replaced: it is
		//locally composed prose with the server excerpt kept on ServerDetail (issue #1333).
		bool configWritten;
		try {
			configWritten = _sysSettingsManager.UpdateSysSetting(BackgroundConfigCode, configJson);
		} catch (NonJsonWriteResponseException nonJsonEx) {
			return SetBackgroundResult.Failure(
				$"The image is in the background gallery, but writing the {BackgroundConfigCode} setting "
				+ $"failed. {nonJsonEx.Message}");
		}
		if (!configWritten) {
			return SetBackgroundResult.Failure(
				$"The image is in the background gallery, but writing the {BackgroundConfigCode} setting failed.");
		}
		List<string> warnings = [];
		if (options.KeepIconBackground) {
			warnings.Add($"The {BrandingBackgroundService.PanelIconBackgroundFeatureCode} feature was left as is " +
				"(keep-icon-background); while it is on, the panel's icon background can hide the shell background.");
		} else if (!_backgroundService.TryTurnOffPanelIconBackground("The background image", out string warning)) {
			warnings.Add(warning);
		}
		return BindBackground(image, options.PackageName, warnings);
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(SetBackgroundImageOptions options) {
		SetBackgroundResult result = SetBackground(options);
		if (result.Success) {
			Logger.WriteInfo($"Image {result.ImageId} is set as the shell background. " +
				"Users see it after a page refresh.");
			Logger.WriteInfo(result.Bound.Count > 0
				? $"Background data bound into package '{result.Package}': {string.Join(", ", result.Bound)}."
				: $"No background data could be bound into package '{result.Package}'; the warnings name the " +
					"reason for each.");
			Logger.WriteWarnings(result.Warnings);
			return;
		}
		Logger.WriteWarnings(result.Warnings);
		CommandSuccess = false;
		Logger.WriteError(result.Error);
	}

	private SetBackgroundResult BindBackground(BackgroundGalleryImage image, string packageName,
		List<string> warnings) {
		List<string> bound = [];
		if (!_backgroundService.TryUsePackage(_packageDataBinder, packageName, out string package, out string error)
			|| !_backgroundService.TryBindGalleryImage(_packageDataBinder, image, ShellBackgroundBindingSuffix, bound,
				warnings, out error)
			|| !TryBindBackgroundConfig(bound, warnings, out error)
			|| !_backgroundService.TryBindPanelIconOffState(_packageDataBinder, bound, warnings, out error)) {
			string into = package is null ? string.Empty : $" into package '{package}'";
			string kept = bound.Count == 0
				? string.Empty
				: $" Already bound and left in place: {string.Join(", ", bound)}.";
			return SetBackgroundResult.Failure(
				$"The background was applied (image {image.ImageId}), but binding it{into} failed: " +
				$"{error}{kept}", warnings, package, bound);
		}
		return SetBackgroundResult.Successful(image.ImageId, package, warnings, bound);
	}

	private bool TryBindBackgroundConfig(List<string> bound, List<string> warnings, out string error) {
		error = null;
		try {
			BindBackgroundConfig(bound.Contains(BrandingBackgroundService.ImagePart))
				.RecordInto(BackgroundConfigPart, bound, warnings);
			return true;
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			error = exception.Message;
			return false;
		}
	}

	private PackageDataBindingOutcome BindBackgroundConfig(bool imageBound) {
		if (imageBound) {
			return _packageDataBinder.BindSysSettingsValue(BackgroundConfigCode);
		}
		IReadOnlyList<string> dropped = _packageDataBinder.RemoveSysSettingsValue(BackgroundConfigCode);
		return PackageDataBindingOutcome.Refused([
			$"{BackgroundConfigCode}: the image row was not bound, so the configuration naming it was not bound " +
			"either — a package that ships the configuration without the image installs a background the target " +
			"cannot render",
			.. dropped
		]);
	}

	private bool TryResolveOrUploadImageId(SetBackgroundImageOptions options, out Guid imageId, out string error) {
		imageId = Guid.Empty;
		error = null;
		bool hasFile = !string.IsNullOrWhiteSpace(options.File);
		bool hasImageId = !string.IsNullOrWhiteSpace(options.ImageId);
		if (hasFile && hasImageId) {
			error = BothSourcesError;
			return false;
		}
		if (!hasFile && !hasImageId) {
			error = NoSourceError;
			return false;
		}
		if (hasFile) {
			SysImageUploadResult uploadResult = _sysImageUploader.UploadAsync(options.File)
				.ConfigureAwait(false).GetAwaiter().GetResult();
			if (!uploadResult.Success) {
				error = uploadResult.Error;
				return false;
			}
			imageId = uploadResult.ImageId;
			return true;
		}
		if (!Guid.TryParse(options.ImageId, out imageId) || imageId == Guid.Empty) {
			error = $"image-id '{options.ImageId}' is not a valid id. Pass the id printed by upload-image.";
			return false;
		}
		return _backgroundService.TryConfirmUploadedImage(imageId, out error);
	}
}
