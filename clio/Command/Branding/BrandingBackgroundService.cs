using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;

namespace Clio.Command.Branding;

public sealed record BackgroundGalleryImage(Guid ImageId, Guid MembershipId, Guid TagId);

public sealed record GalleryImageBindingOutcome(
	IReadOnlyList<string> Bound,
	IReadOnlyList<string> Warnings,
	string Error = null)
{
	public bool ImageBound {
		get {
			return Bound.Contains(BrandingBackgroundService.ImagePart);
		}
	}
}

public interface IBrandingBackgroundService
{
	bool TryConfirmUploadedImage(Guid imageId, out string error);

	bool TryAddImageToBackgroundGallery(Guid imageId, out BackgroundGalleryImage image, out string error);

	bool TryTurnOffPanelIconBackground(string appliedBackground, out string warning);

	bool TryUsePackage(IPackageDataBinder binder, string packageName, out string package, out string error);

	GalleryImageBindingOutcome BindGalleryImage(IPackageDataBinder binder, BackgroundGalleryImage image,
		string bindingSuffix);

	bool TryBindPanelIconOffState(IPackageDataBinder binder, List<string> bound, List<string> warnings,
		out string error);
}

internal sealed class BrandingBackgroundService(IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder, IFeatureStateService featureState) : IBrandingBackgroundService
{
	internal const string PanelIconBackgroundFeatureCode = "UsePanelIconBackground";

	internal const string ImagePart = "image";
	internal const string GalleryMembershipPart = "gallery-membership";
	internal const string PanelIconOffStatePart = "panel-icon-off-state";

	private const string ShellBackgroundTagName = "shell_background";

	private const string SysImageSchema = "SysImage";
	private const string SysImageInTagSchema = "SysImageInTag";

	internal static readonly Guid ShellBackgroundTagId = new("273C2402-7CAE-456B-A9C4-067D2024F1A7");

	private static readonly IReadOnlyList<string> SysImageColumns = ["Id", "Name", "Data", "MimeType"];
	private static readonly IReadOnlyList<string> SysImageInTagColumns = ["Id", "Entity", "Tag"];

	public bool TryConfirmUploadedImage(Guid imageId, out string error) {
		error = null;
		if (!TryQuerySingleId(
				$"{ODataKeyFormatter.CollectionPath(SysImageSchema)}?$filter=Id eq {imageId}&$select=Id&$top=1",
				out Guid? foundId, out string checkError)) {
			error = $"Could not check the image in the environment: {checkError}";
			return false;
		}
		if (foundId is null) {
			error = $"No uploaded image with id '{imageId}' was found in the environment. " +
				"Upload the file first with upload-image and pass the id it prints.";
			return false;
		}
		return true;
	}

	public bool TryAddImageToBackgroundGallery(Guid imageId, out BackgroundGalleryImage image, out string error) {
		image = null;
		error = null;
		GalleryProbe probe = EnsureMembershipForTag(imageId, ShellBackgroundTagId);
		if (probe.HardError is not null) {
			error = probe.HardError;
			return false;
		}
		if (probe.RowId is not null) {
			image = new BackgroundGalleryImage(imageId, probe.RowId.Value, ShellBackgroundTagId);
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
				image = new BackgroundGalleryImage(imageId, probe.RowId.Value, resolvedTagId.Value);
				return true;
			}
		}
		error = "Registering the image in the background gallery failed."
			+ (probe.RegistrationError is null ? string.Empty : $" {probe.RegistrationError}");
		return false;
	}

	public bool TryTurnOffPanelIconBackground(string appliedBackground, out string warning) {
		warning = null;
		try {
			featureState.SetFeatureState(PanelIconBackgroundFeatureCode, SysAdminUnitIds.AllEmployees, state: false);
			return true;
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			warning = $"{appliedBackground} was applied, but turning off the {PanelIconBackgroundFeatureCode} " +
				$"feature failed, so the panel may still hide it: {exception.Message}";
			return false;
		}
	}

	public bool TryUsePackage(IPackageDataBinder binder, string packageName, out string package, out string error) {
		package = null;
		error = null;
		try {
			package = binder.UsePackage(packageName);
			return true;
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			error = exception.Message;
			return false;
		}
	}

	public GalleryImageBindingOutcome BindGalleryImage(IPackageDataBinder binder, BackgroundGalleryImage image,
		string bindingSuffix) {
		List<string> bound = [];
		List<string> warnings = [];
		try {
			PackageDataBindingOutcome imageRow = binder.BindRow(SysImageSchema, bindingSuffix, SysImageColumns,
				image.ImageId);
			imageRow.RecordInto(ImagePart, bound, warnings);
			BindGalleryMembership(binder, imageRow.Bound, image, bindingSuffix)
				.RecordInto(GalleryMembershipPart, bound, warnings);
			return new GalleryImageBindingOutcome(bound, warnings);
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			return new GalleryImageBindingOutcome(bound, warnings, exception.Message);
		}
	}

	public bool TryBindPanelIconOffState(IPackageDataBinder binder, List<string> bound, List<string> warnings,
		out string error) {
		error = null;
		try {
			binder.BindFeatureOffState(PanelIconBackgroundFeatureCode).RecordInto(PanelIconOffStatePart, bound, warnings);
			return true;
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			error = exception.Message;
			return false;
		}
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
			string url = serviceUrlBuilder.Build(ODataKeyFormatter.CollectionPath(SysImageInTagSchema));
			string body = JsonSerializer.Serialize(new {
				EntityId = imageId.ToString(),
				TagId = tagId.ToString()
			});
			applicationClient.ExecutePostRequest(url, body);
			return null;
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			return exception.Message;
		}
	}

	private static PackageDataBindingOutcome BindGalleryMembership(IPackageDataBinder binder, bool imageBound,
		BackgroundGalleryImage image, string bindingSuffix) {
		string galleryFolderName = PackageDataBindingNames.For(SysImageInTagSchema, bindingSuffix);
		if (!imageBound) {
			IReadOnlyList<string> droppedWithImage = binder.RemoveBinding(galleryFolderName, SysImageInTagSchema);
			return PackageDataBindingOutcome.Refused([
				"background gallery membership: the image row was not bound, so the membership naming it was not " +
				"bound either — a package that ships the membership without the image installs a gallery entry " +
				"pointing at an image the target does not have",
				.. droppedWithImage
			]);
		}
		if (image.TagId != ShellBackgroundTagId) {
			IReadOnlyList<string> droppedForTag = binder.RemoveBinding(galleryFolderName, SysImageInTagSchema);
			return PackageDataBindingOutcome.Refused([
				$"background gallery membership: this environment's {ShellBackgroundTagName} tag has a customized " +
				$"id ({image.TagId}) that would not resolve on an install target, so the membership row was " +
				"not bound",
				.. droppedForTag
			]);
		}
		return binder.BindRow(SysImageInTagSchema, bindingSuffix, SysImageInTagColumns, image.MembershipId);
	}

	private bool TryQuerySingleId(string relativeUrl, out Guid? id, out string error) {
		id = null;
		error = null;
		try {
			string response = applicationClient.ExecuteGetRequest(serviceUrlBuilder.Build(relativeUrl));
			if (string.IsNullOrWhiteSpace(response)) {
				error = "the environment returned an empty response.";
				return false;
			}
			using JsonDocument document = JsonDocument.Parse(response);
			//Every caller hands this an ODataKeyFormatter.CollectionPath URL and expects the standard OData
			//"value" collection below, so the response has to be classified as OData. Under Service the
			//BaseResponse heuristics run over entity data, and an entity carrying a business Success:false
			//column was rejected as a service failure.
			if (CreatioResponseError.TryDetect(document.RootElement, CreatioResponseContext.ODataPayload,
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
		} catch (Exception exception) when (exception is not OperationCanceledException) {
			error = exception.Message;
			return false;
		}
	}

	private sealed record GalleryProbe(Guid? RowId, string HardError, string RegistrationError);
}
