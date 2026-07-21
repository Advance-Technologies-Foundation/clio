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
	HelpText = "Set a previously uploaded image as the environment's shell background")]
public class SetBackgroundImageOptions : RemoteCommandOptions {

	/// <summary>
	/// Id of the uploaded image to set as the background (printed by <c>upload-image</c>).
	/// </summary>
	[Value(0, MetaName = "image-id", Required = true,
		HelpText = "Id of the uploaded image to set as the background (printed by upload-image).")]
	public string ImageId { get; set; }
}

/// <summary>
/// Outcome of setting the shell background image.
/// </summary>
public sealed record SetBackgroundResult {

	/// <summary>Whether the background was set.</summary>
	public bool Success { get; private init; }

	/// <summary>The failure message; null on success.</summary>
	public string Error { get; private init; }

	/// <summary>Creates a success result.</summary>
	public static SetBackgroundResult Successful() => new() { Success = true };

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static SetBackgroundResult Failure(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Sets a previously uploaded image as the environment's shell background: makes the image available
/// in the background gallery and points the background configuration at it. The change applies to all
/// users after a page refresh and replaces the currently configured background.
/// </summary>
public class SetBackgroundImageCommand : RemoteCommand<SetBackgroundImageOptions> {

	// Platform-seeded SysImageTag record named "shell_background"; carries the same id on every
	// installation. When an installation deviates, the tag id is re-resolved by name (see below).
	internal static readonly Guid ShellBackgroundTagId = new("273C2402-7CAE-456B-A9C4-067D2024F1A7");

	internal const string BackgroundConfigCode = "CrtBackgroundConfig";
	private const string ShellBackgroundTagName = "shell_background";

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ISysSettingsManager _sysSettingsManager;

	/// <summary>
	/// Initializes a new instance of the <see cref="SetBackgroundImageCommand"/> class.
	/// </summary>
	public SetBackgroundImageCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, ISysSettingsManager sysSettingsManager)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_sysSettingsManager = sysSettingsManager;
	}

	/// <summary>
	/// Sets the image identified by <paramref name="options"/> as the shell background. Virtual so the
	/// MCP tool can call the resolved command directly and read the structured result.
	/// </summary>
	/// <param name="options">Command options carrying the image id and connection settings.</param>
	/// <returns>The outcome, carrying a failure message when the background could not be set.</returns>
	public virtual SetBackgroundResult SetBackground(SetBackgroundImageOptions options) {
		if (!Guid.TryParse(options.ImageId, out Guid imageId) || imageId == Guid.Empty) {
			return SetBackgroundResult.Failure(
				$"image-id '{options.ImageId}' is not a valid id. Pass the id printed by upload-image.");
		}
		// Existence is probed with a filter query (not a keyed GET) so "absent" comes back as an empty
		// row set — cleanly distinguishable from a transport/server failure, which must not be reported
		// as "image not found".
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
		return SetBackgroundResult.Successful();
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(SetBackgroundImageOptions options) {
		SetBackgroundResult result = SetBackground(options);
		if (result.Success) {
			Logger.WriteInfo($"Image {options.ImageId} is set as the shell background. " +
				"Users see it after a page refresh.");
			return;
		}
		CommandSuccess = false;
		Logger.WriteError(result.Error);
	}

	// Makes the image available in the background gallery (idempotent: an already-registered image is
	// left as is). Returns null on success, otherwise the failure message.
	private string EnsureInBackgroundGallery(Guid imageId) {
		if (!TryEnsureForTag(imageId, ShellBackgroundTagId, out bool ensured, out string hardError)) {
			return hardError;
		}
		if (ensured) {
			return null;
		}
		// The seeded tag id can deviate on a customized installation — re-resolve it by name and retry.
		if (!TryQuerySingleId(
			$"{ODataKeyFormatter.CollectionPath("SysImageTag")}?$filter=Name eq '{ShellBackgroundTagName}'&$select=Id&$top=1",
			out string resolvedTagIdRaw, out string tagLookupError)) {
			return $"Could not resolve the background gallery tag: {tagLookupError}";
		}
		if (!Guid.TryParse(resolvedTagIdRaw, out Guid resolvedTagId)) {
			return "The background gallery tag was not found in the environment, so the image could not " +
				"be registered in the gallery.";
		}
		if (resolvedTagId != ShellBackgroundTagId
			&& TryEnsureForTag(imageId, resolvedTagId, out ensured, out hardError) && ensured) {
			return null;
		}
		return hardError ?? "Registering the image in the background gallery failed.";
	}

	// Ensures the image is registered under one gallery tag. Returns false with a hard error when the
	// gallery could not be read (a transport/server failure must abort, not blind-insert a duplicate
	// row); on a true return, "ensured" says whether the membership exists (already present or just
	// registered) — false means the insert was rejected and the caller may retry with another tag id.
	private bool TryEnsureForTag(Guid imageId, Guid tagId, out bool ensured, out string hardError) {
		ensured = false;
		// Lookup columns are filtered via navigation paths (Entity/Id, Tag/Id) — the flat EntityId/TagId
		// names exist only in payloads; a flat name in $filter fails with "Column by path ... not found"
		// (verified live, PR #928).
		if (!TryQuerySingleId(
			$"{ODataKeyFormatter.CollectionPath("SysImageInTag")}?$filter=Entity/Id eq {imageId} and Tag/Id eq {tagId}&$select=Id&$top=1",
			out string membershipId, out string readError)) {
			hardError = $"Could not check the background gallery: {readError}";
			return false;
		}
		hardError = null;
		if (membershipId is not null) {
			ensured = true;
			return true;
		}
		ensured = TryRegisterInGallery(imageId, tagId);
		return true;
	}

	private bool TryRegisterInGallery(Guid imageId, Guid tagId) {
		try {
			string url = _serviceUrlBuilder.Build(ODataKeyFormatter.CollectionPath("SysImageInTag"));
			string body = JsonSerializer.Serialize(new {
				EntityId = imageId.ToString(),
				TagId = tagId.ToString()
			});
			string response = ApplicationClient.ExecutePostRequest(url, body);
			if (string.IsNullOrWhiteSpace(response)) {
				return true;
			}
			using JsonDocument document = JsonDocument.Parse(response);
			return !ODataResponseError.TryDetect(document.RootElement, out _);
		} catch (JsonException) {
			// A non-JSON body on a successful POST still means the record was created.
			return true;
		} catch (Exception) {
			return false;
		}
	}

	// Runs an OData collection GET and reads the first row's Id. Returns false with an error when the
	// environment could not answer (transport failure, empty body, or a server error envelope); on a
	// true return, "id" is null when the row set is empty.
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
