using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Branding;

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
		if (!ImageExists(imageId)) {
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

	private bool ImageExists(Guid imageId) {
		try {
			string url = _serviceUrlBuilder.Build($"odata/SysImage({imageId})?$select=Id");
			string response = ApplicationClient.ExecuteGetRequest(url);
			return !string.IsNullOrWhiteSpace(response) && !IsODataError(response);
		} catch (Exception) {
			return false;
		}
	}

	// Makes the image available in the background gallery (idempotent: an already-registered image is
	// left as is). Returns null on success, otherwise the failure message.
	private string EnsureInBackgroundGallery(Guid imageId) {
		Guid tagId = ShellBackgroundTagId;
		if (IsInGallery(imageId, tagId)) {
			return null;
		}
		if (TryRegisterInGallery(imageId, tagId)) {
			return null;
		}
		// The seeded tag id can deviate on a customized installation — re-resolve it by name and retry.
		Guid? resolvedTagId = FindBackgroundTagIdByName();
		if (resolvedTagId is null) {
			return "The background gallery tag was not found in the environment, so the image could not " +
				"be registered in the gallery.";
		}
		if (resolvedTagId.Value != tagId && (IsInGallery(imageId, resolvedTagId.Value)
			|| TryRegisterInGallery(imageId, resolvedTagId.Value))) {
			return null;
		}
		return "Registering the image in the background gallery failed.";
	}

	private bool IsInGallery(Guid imageId, Guid tagId) {
		try {
			string url = _serviceUrlBuilder.Build(
				$"odata/SysImageInTag?$filter=EntityId eq {imageId} and TagId eq {tagId}&$select=Id&$top=1");
			string response = ApplicationClient.ExecuteGetRequest(url);
			if (string.IsNullOrWhiteSpace(response) || IsODataError(response)) {
				return false;
			}
			JsonNode root = JsonNode.Parse(response);
			return root?["value"] is JsonArray rows && rows.Count > 0;
		} catch (Exception) {
			return false;
		}
	}

	private bool TryRegisterInGallery(Guid imageId, Guid tagId) {
		try {
			string url = _serviceUrlBuilder.Build("odata/SysImageInTag");
			string body = JsonSerializer.Serialize(new {
				EntityId = imageId.ToString(),
				TagId = tagId.ToString()
			});
			string response = ApplicationClient.ExecutePostRequest(url, body);
			return string.IsNullOrWhiteSpace(response) || !IsODataError(response);
		} catch (Exception) {
			return false;
		}
	}

	private Guid? FindBackgroundTagIdByName() {
		try {
			string url = _serviceUrlBuilder.Build(
				$"odata/SysImageTag?$filter=Name eq '{ShellBackgroundTagName}'&$select=Id&$top=1");
			string response = ApplicationClient.ExecuteGetRequest(url);
			if (string.IsNullOrWhiteSpace(response) || IsODataError(response)) {
				return null;
			}
			JsonNode root = JsonNode.Parse(response);
			if (root?["value"] is not JsonArray rows || rows.Count == 0) {
				return null;
			}
			string id = rows[0]?["Id"]?.GetValue<string>();
			return Guid.TryParse(id, out Guid tagId) ? tagId : null;
		} catch (Exception) {
			return null;
		}
	}

	private static bool IsODataError(string response) {
		try {
			JsonNode root = JsonNode.Parse(response);
			return root?["error"] is not null;
		} catch (JsonException) {
			// A non-JSON body is not a recognizable OData success payload.
			return true;
		}
	}
}
