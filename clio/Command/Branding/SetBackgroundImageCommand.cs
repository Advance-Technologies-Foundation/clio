using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
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

	internal static readonly Guid ShellBackgroundTagId = new("273C2402-7CAE-456B-A9C4-067D2024F1A7");

	internal const string BackgroundConfigCode = "CrtBackgroundConfig";

	/// <summary>The feature code that gates the panel's own icon background.</summary>
	internal const string PanelIconBackgroundFeatureCode = "UsePanelIconBackground";

	/// <summary>Error text shared by the CLI and MCP surfaces when both image sources are passed.</summary>
	internal const string BothSourcesError = "Pass either a file or an image-id, not both.";

	/// <summary>Error text shared by the CLI and MCP surfaces when no image source is passed.</summary>
	internal const string NoSourceError =
		"Pass a file to upload (file) or the id of an already-uploaded image (image-id).";

	private const string ShellBackgroundTagName = "shell_background";

	private const string ShellBackgroundBindingSuffix = "ShellBackground";

	private const string SysImageSchema = "SysImage";
	private const string SysImageInTagSchema = "SysImageInTag";

	private static readonly IReadOnlyList<string> SysImageColumns = ["Id", "Name", "Data", "MimeType"];
	private static readonly IReadOnlyList<string> SysImageInTagColumns = ["Id", "Entity", "Tag"];

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ISysSettingsManager _sysSettingsManager;
	private readonly ISysImageUploader _sysImageUploader;
	private readonly IFeatureStateService _featureState;
	private readonly IPackageDataBinder _packageDataBinder;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetBackgroundImageCommand"/> class.
	/// </summary>
	public SetBackgroundImageCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, ISysSettingsManager sysSettingsManager,
		ISysImageUploader sysImageUploader, IFeatureStateService featureState,
		IPackageDataBinder packageDataBinder)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_sysSettingsManager = sysSettingsManager;
		_sysImageUploader = sysImageUploader;
		_featureState = featureState;
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
		if (!TryEnsureInBackgroundGallery(imageId, out GalleryMembership membership, out string galleryError)) {
			return SetBackgroundResult.Failure(galleryError);
		}
		string configJson = JsonSerializer.Serialize(new { imageId = imageId.ToString(), mode = "Image" });
		if (!_sysSettingsManager.UpdateSysSetting(BackgroundConfigCode, configJson)) {
			return SetBackgroundResult.Failure(
				$"The image is in the background gallery, but writing the {BackgroundConfigCode} setting failed.");
		}
		List<string> warnings = [];
		if (options.KeepIconBackground) {
			warnings.Add($"The {PanelIconBackgroundFeatureCode} feature was left as is " +
				"(keep-icon-background); while it is on, the panel's icon background can hide the shell background.");
		} else {
			DisablePanelIconBackground(warnings);
		}
		return BindBackground(imageId, membership, options.PackageName, warnings);
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

	private SetBackgroundResult BindBackground(
		Guid imageId, GalleryMembership membership, string packageName, List<string> warnings) {
		string package = null;
		List<string> bound = [];
		try {
			package = _packageDataBinder.UsePackage(packageName);
			CreateBackgroundDataBindings(imageId, membership, bound, warnings);
			return SetBackgroundResult.Successful(imageId, package, warnings, bound);
		} catch (Exception exception) {
			string into = package is null ? string.Empty : $" into package '{package}'";
			string kept = bound.Count == 0
				? string.Empty
				: $" Already bound and left in place: {string.Join(", ", bound)}.";
			return SetBackgroundResult.Failure(
				$"The background was applied (image {imageId}), but binding it{into} failed: " +
				$"{exception.Message}{kept}", warnings, package, bound);
		}
	}

	private void CreateBackgroundDataBindings(
		Guid imageId, GalleryMembership membership, List<string> bound, List<string> warnings) {
		PackageDataBindingOutcome image = _packageDataBinder.BindRow(
			SysImageSchema, ShellBackgroundBindingSuffix, SysImageColumns, imageId);
		RecordOutcome(image, "image", bound, warnings);

		PackageDataBindingOutcome galleryMembership = BindGalleryMembership(image.Bound, membership);
		RecordOutcome(galleryMembership, "gallery-membership", bound, warnings);

		PackageDataBindingOutcome config = BindBackgroundConfig(image.Bound);
		RecordOutcome(config, "background-config", bound, warnings);

		PackageDataBindingOutcome featureOffState =
			_packageDataBinder.BindFeatureOffState(PanelIconBackgroundFeatureCode);
		RecordOutcome(featureOffState, "panel-icon-off-state", bound, warnings);
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

	private static void RecordOutcome(
		PackageDataBindingOutcome outcome, string part, List<string> bound, List<string> warnings) {
		warnings.AddRange(outcome.Warnings);
		if (outcome.Bound) {
			bound.Add(part);
		}
	}

	private PackageDataBindingOutcome BindGalleryMembership(bool imageBound, GalleryMembership membership) {
		string galleryFolderName = PackageDataBindingNames.For(SysImageInTagSchema, ShellBackgroundBindingSuffix);
		if (!imageBound) {
			IReadOnlyList<string> droppedWithImage =
				_packageDataBinder.RemoveBinding(galleryFolderName, SysImageInTagSchema);
			return PackageDataBindingOutcome.Refused([
				"background gallery membership: the image row was not bound, so the membership naming it was not " +
				"bound either — a package that ships the membership without the image installs a gallery entry " +
				"pointing at an image the target does not have",
				.. droppedWithImage
			]);
		}
		if (membership.TagId != ShellBackgroundTagId) {
			IReadOnlyList<string> droppedForTag =
				_packageDataBinder.RemoveBinding(galleryFolderName, SysImageInTagSchema);
			return PackageDataBindingOutcome.Refused([
				$"background gallery membership: this environment's {ShellBackgroundTagName} tag has a customized " +
				$"id ({membership.TagId}) that would not resolve on an install target, so the membership row was " +
				"not bound",
				.. droppedForTag
			]);
		}
		return _packageDataBinder.BindRow(
			SysImageInTagSchema, ShellBackgroundBindingSuffix, SysImageInTagColumns, membership.RowId);
	}

	private void DisablePanelIconBackground(List<string> warnings) {
		try {
			_featureState.SetFeatureState(
				PanelIconBackgroundFeatureCode, SysAdminUnitIds.AllEmployees, state: false);
		} catch (Exception exception) {
			warnings.Add(
				$"The background image was applied, but turning off the {PanelIconBackgroundFeatureCode} " +
				$"feature failed, so the panel may still hide it: {exception.Message}");
		}
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
		if (!TryQuerySingleId(
			$"{ODataKeyFormatter.CollectionPath(SysImageSchema)}?$filter=Id eq {imageId}&$select=Id&$top=1",
			out Guid? existingImageId, out string imageCheckError)) {
			error = $"Could not check the image in the environment: {imageCheckError}";
			return false;
		}
		if (existingImageId is null) {
			error = $"No uploaded image with id '{imageId}' was found in the environment. " +
				"Upload the file first with upload-image and pass the id it prints.";
			return false;
		}
		return true;
	}

	private bool TryEnsureInBackgroundGallery(Guid imageId, out GalleryMembership membership, out string error) {
		membership = null;
		error = null;
		GalleryProbe probe = EnsureMembershipForTag(imageId, ShellBackgroundTagId);
		if (probe.HardError is not null) {
			error = probe.HardError;
			return false;
		}
		if (probe.RowId is not null) {
			membership = new GalleryMembership(probe.RowId.Value, ShellBackgroundTagId);
			return true;
		}
		if (!TryQuerySingleId(
			$"{ODataKeyFormatter.CollectionPath("SysImageTag")}?$filter=Name eq '{ShellBackgroundTagName}'&$select=Id&$top=1",
			out Guid? resolvedTagId, out string tagLookupError)) {
			error = $"Could not resolve the background gallery tag: {tagLookupError}";
			return false;
		}
		if (resolvedTagId is null) {
			error = "The background gallery tag was not found in the environment, so the image could not " +
				"be registered in the gallery.";
			return false;
		}
		if (resolvedTagId.Value != ShellBackgroundTagId) {
			probe = EnsureMembershipForTag(imageId, resolvedTagId.Value);
			if (probe.HardError is not null) {
				error = probe.HardError;
				return false;
			}
			if (probe.RowId is not null) {
				membership = new GalleryMembership(probe.RowId.Value, resolvedTagId.Value);
				return true;
			}
		}
		error = "Registering the image in the background gallery failed."
			+ (probe.RegistrationError is null ? string.Empty : $" {probe.RegistrationError}");
		return false;
	}

	private GalleryProbe EnsureMembershipForTag(Guid imageId, Guid tagId) {
		string membershipUrl =
			$"{ODataKeyFormatter.CollectionPath(SysImageInTagSchema)}?$filter=Entity/Id eq {imageId} and Tag/Id eq {tagId}&$select=Id&$top=1";
		if (!TryQuerySingleId(membershipUrl, out Guid? membershipRowId, out string readError)) {
			return new GalleryProbe(null, $"Could not check the background gallery: {readError}", null);
		}
		if (membershipRowId is not null) {
			return new GalleryProbe(membershipRowId, null, null);
		}
		string registrationError = PostGalleryRegistration(imageId, tagId);
		if (!TryQuerySingleId(membershipUrl, out membershipRowId, out readError)) {
			return new GalleryProbe(null,
				$"Could not verify the background gallery registration: {readError}", registrationError);
		}
		return new GalleryProbe(membershipRowId, null, registrationError);
	}

	private string PostGalleryRegistration(Guid imageId, Guid tagId) {
		try {
			string url = _serviceUrlBuilder.Build(ODataKeyFormatter.CollectionPath(SysImageInTagSchema));
			string body = JsonSerializer.Serialize(new {
				EntityId = imageId.ToString(),
				TagId = tagId.ToString()
			});
			ApplicationClient.ExecutePostRequest(url, body);
			return null;
		} catch (Exception ex) {
			return ex.Message;
		}
	}

	private bool TryQuerySingleId(string relativeUrl, out Guid? id, out string error) {
		id = null;
		error = null;
		try {
			string response = ApplicationClient.ExecuteGetRequest(_serviceUrlBuilder.Build(relativeUrl));
			if (string.IsNullOrWhiteSpace(response)) {
				error = "the environment returned an empty response.";
				return false;
			}
			using JsonDocument document = JsonDocument.Parse(response);
			if (CreatioResponseError.TryDetect(document.RootElement, CreatioResponseContext.Service,
				out string serverError)) {
				error = serverError;
				return false;
			}
			if (!document.RootElement.TryGetProperty("value", out JsonElement rows)
				|| rows.ValueKind != JsonValueKind.Array) {
				error = "the environment returned an unexpected response.";
				return false;
			}
			if (rows.GetArrayLength() > 0
				&& rows[0].TryGetProperty("Id", out JsonElement idElement)
				&& idElement.ValueKind == JsonValueKind.String
				&& Guid.TryParse(idElement.GetString(), out Guid parsed)
				&& parsed != Guid.Empty) {
				id = parsed;
			}
			return true;
		} catch (JsonException) {
			error = "the environment returned a non-JSON response.";
			return false;
		} catch (Exception ex) {
			error = ex.Message;
			return false;
		}
	}

	private sealed record GalleryProbe(Guid? RowId, string HardError, string RegistrationError);

	private sealed record GalleryMembership(Guid RowId, Guid TagId);
}
