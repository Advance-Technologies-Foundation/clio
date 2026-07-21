using System;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for the <c>set-background-image</c> command.
/// </summary>
[Verb("set-background-image",
	HelpText = "Set an image as the environment's shell background, from a local file or an uploaded image id")]
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
}

/// <summary>
/// Outcome of setting the shell background image.
/// </summary>
public sealed record SetBackgroundResult {

	/// <summary>Whether the background was set.</summary>
	public bool Success { get; private init; }

	/// <summary>The id of the image the background points at; <see cref="Guid.Empty"/> on failure.</summary>
	public Guid ImageId { get; private init; }

	/// <summary>The failure message; null on success.</summary>
	public string Error { get; private init; }

	/// <summary>Creates a success result carrying the applied image id.</summary>
	public static SetBackgroundResult Successful(Guid imageId) => new() { Success = true, ImageId = imageId };

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static SetBackgroundResult Failure(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Sets an image as the environment's shell background: optionally uploads a local file first, makes
/// the image available in the background gallery, and points the background configuration at it. The
/// change applies to all users after a page refresh and replaces the currently configured background.
/// </summary>
public class SetBackgroundImageCommand : RemoteCommand<SetBackgroundImageOptions> {

	internal static readonly Guid ShellBackgroundTagId = new("273C2402-7CAE-456B-A9C4-067D2024F1A7");

	internal const string BackgroundConfigCode = "CrtBackgroundConfig";
	private const string ShellBackgroundTagName = "shell_background";

	/// <summary>Error text shared by the CLI and MCP surfaces when both image sources are passed.</summary>
	internal const string BothSourcesError = "Pass either a file or an image-id, not both.";

	/// <summary>Error text shared by the CLI and MCP surfaces when no image source is passed.</summary>
	internal const string NoSourceError =
		"Pass a file to upload (file) or the id of an already-uploaded image (image-id).";

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ISysSettingsManager _sysSettingsManager;
	private readonly ISysImageUploader _sysImageUploader;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetBackgroundImageCommand"/> class.
	/// </summary>
	public SetBackgroundImageCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, ISysSettingsManager sysSettingsManager,
		ISysImageUploader sysImageUploader)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_sysSettingsManager = sysSettingsManager;
		_sysImageUploader = sysImageUploader;
	}

	/// <summary>
	/// Sets the image identified by <paramref name="options"/> as the shell background. Exactly one of
	/// <see cref="SetBackgroundImageOptions.File"/> (uploaded first, then applied) and
	/// <see cref="SetBackgroundImageOptions.ImageId"/> (an already-uploaded image) must be provided.
	/// </summary>
	/// <param name="options">Command options carrying the image source and connection settings.</param>
	/// <returns>The outcome, carrying the applied image id or a failure message.</returns>
	public virtual SetBackgroundResult SetBackground(SetBackgroundImageOptions options) {
		bool hasFile = !string.IsNullOrWhiteSpace(options.File);
		bool hasImageId = !string.IsNullOrWhiteSpace(options.ImageId);
		if (hasFile && hasImageId) {
			return SetBackgroundResult.Failure(BothSourcesError);
		}
		if (!hasFile && !hasImageId) {
			return SetBackgroundResult.Failure(NoSourceError);
		}
		Guid imageId;
		if (hasFile) {
			SysImageUploadResult uploadResult = _sysImageUploader.UploadAsync(options.File)
				.ConfigureAwait(false).GetAwaiter().GetResult();
			if (!uploadResult.Success) {
				return SetBackgroundResult.Failure(uploadResult.Error);
			}
			imageId = uploadResult.ImageId;
		} else {
			if (!Guid.TryParse(options.ImageId, out imageId) || imageId == Guid.Empty) {
				return SetBackgroundResult.Failure(
					$"image-id '{options.ImageId}' is not a valid id. Pass the id printed by upload-image.");
			}
			if (!TryQuerySingleId(
				$"{ODataKeyFormatter.CollectionPath("SysImage")}?$filter=Id eq {imageId}&$select=Id&$top=1",
				out string existingImageId, out string imageCheckError)) {
				return SetBackgroundResult.Failure(
					$"Could not check the image in the environment: {imageCheckError}");
			}
			if (existingImageId is null) {
				return SetBackgroundResult.Failure(
					$"No uploaded image with id '{imageId}' was found in the environment. " +
					"Upload the file first with upload-image and pass the id it prints.");
			}
		}
		string galleryError = EnsureInBackgroundGallery(imageId);
		if (galleryError is not null) {
			return SetBackgroundResult.Failure(galleryError);
		}
		string configJson = JsonSerializer.Serialize(new { imageId = imageId.ToString(), mode = "Image" });
		_sysSettingsManager.CreateSysSettingIfNotExists(BackgroundConfigCode, BackgroundConfigCode, "Text");
		if (!_sysSettingsManager.UpdateSysSetting(BackgroundConfigCode, configJson)) {
			return SetBackgroundResult.Failure(
				$"The image is in the background gallery, but writing the {BackgroundConfigCode} setting failed.");
		}
		return SetBackgroundResult.Successful(imageId);
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(SetBackgroundImageOptions options) {
		SetBackgroundResult result = SetBackground(options);
		if (result.Success) {
			Logger.WriteInfo($"Image {result.ImageId} is set as the shell background. " +
				"Users see it after a page refresh.");
			return;
		}
		CommandSuccess = false;
		Logger.WriteError(result.Error);
	}

	/// <summary>
	/// Makes the image available in the background gallery, first under the platform-seeded
	/// <c>shell_background</c> tag id and, when that insert is rejected (a customized installation can
	/// carry a different id), under the tag re-resolved by name. Idempotent: an already-registered
	/// image is left as is. Returns null on success, otherwise the failure message.
	/// </summary>
	private string EnsureInBackgroundGallery(Guid imageId) {
		if (!TryEnsureForTag(imageId, ShellBackgroundTagId, out bool ensured, out string hardError,
			out string registrationError)) {
			return hardError;
		}
		if (ensured) {
			return null;
		}
		if (!TryQuerySingleId(
			$"{ODataKeyFormatter.CollectionPath("SysImageTag")}?$filter=Name eq '{ShellBackgroundTagName}'&$select=Id&$top=1",
			out string resolvedTagIdRaw, out string tagLookupError)) {
			return $"Could not resolve the background gallery tag: {tagLookupError}";
		}
		if (!Guid.TryParse(resolvedTagIdRaw, out Guid resolvedTagId)) {
			return "The background gallery tag was not found in the environment, so the image could not " +
				"be registered in the gallery.";
		}
		if (resolvedTagId != ShellBackgroundTagId) {
			if (!TryEnsureForTag(imageId, resolvedTagId, out ensured, out hardError, out registrationError)) {
				return hardError;
			}
			if (ensured) {
				return null;
			}
		}
		return "Registering the image in the background gallery failed."
			+ (registrationError is null ? string.Empty : $" {registrationError}");
	}

	/// <summary>
	/// Ensures the image is registered under one gallery tag. The membership filter addresses the
	/// lookup columns via navigation paths (<c>Entity/Id</c>, <c>Tag/Id</c>) — flat column names fail
	/// on the platform with "Column by path ... not found". The insert's response body is never
	/// trusted as the outcome (a 2xx body can be a login page or an error envelope): after the POST
	/// the membership is read back, and only a confirmed row counts as registered — the same
	/// read-back-verification discipline as <see cref="SysImageUploader"/>. Returns false with a hard
	/// error when the gallery could not be read (an unanswered read must abort, not blind-insert a
	/// duplicate row); on a true return, <paramref name="ensured"/> is false when the registration did
	/// not materialize and the caller may retry with another tag id.
	/// </summary>
	private bool TryEnsureForTag(Guid imageId, Guid tagId, out bool ensured, out string hardError,
		out string registrationError) {
		ensured = false;
		registrationError = null;
		string membershipUrl =
			$"{ODataKeyFormatter.CollectionPath("SysImageInTag")}?$filter=Entity/Id eq {imageId} and Tag/Id eq {tagId}&$select=Id&$top=1";
		if (!TryQuerySingleId(membershipUrl, out string membershipId, out string readError)) {
			hardError = $"Could not check the background gallery: {readError}";
			return false;
		}
		hardError = null;
		if (membershipId is not null) {
			ensured = true;
			return true;
		}
		registrationError = PostGalleryRegistration(imageId, tagId);
		if (!TryQuerySingleId(membershipUrl, out membershipId, out readError)) {
			hardError = $"Could not verify the background gallery registration: {readError}";
			return false;
		}
		ensured = membershipId is not null;
		return true;
	}

	/// <summary>
	/// Sends the gallery-registration insert. The outcome is decided solely by the read-back in
	/// <see cref="TryEnsureForTag"/> (a 2xx body proves nothing), so the response body is not
	/// interpreted here; a transport failure is converted to a diagnostic message that the caller
	/// attaches to the final error when the registration does not materialize. Returns null when the
	/// request was sent without a transport error.
	/// </summary>
	private string PostGalleryRegistration(Guid imageId, Guid tagId) {
		try {
			string url = _serviceUrlBuilder.Build(ODataKeyFormatter.CollectionPath("SysImageInTag"));
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

	/// <summary>
	/// Runs an OData collection GET and reads the first row's <c>Id</c>. Returns false with an error
	/// when the environment could not answer (transport failure, empty body, or a server error
	/// envelope); on a true return, <paramref name="id"/> is null when the row set is empty — which is
	/// how "absent" is distinguished from "could not check".
	/// </summary>
	private bool TryQuerySingleId(string relativeUrl, out string id, out string error) {
		id = null;
		error = null;
		try {
			string response = ApplicationClient.ExecuteGetRequest(_serviceUrlBuilder.Build(relativeUrl));
			if (string.IsNullOrWhiteSpace(response)) {
				error = "the environment returned an empty response.";
				return false;
			}
			using JsonDocument document = JsonDocument.Parse(response);
			if (ODataResponseError.TryDetect(document.RootElement, out string serverError)) {
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
				&& idElement.ValueKind == JsonValueKind.String) {
				id = idElement.GetString();
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
}
