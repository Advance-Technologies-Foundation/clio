using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for the <c>upload-image</c> command.
/// </summary>
[Verb("upload-image",
	HelpText = "Upload a local image to an environment and print its image id")]
public class UploadImageOptions : RemoteCommandOptions {

	/// <summary>
	/// Path to the local image file to upload (png, jpg/jpeg, gif, bmp, or webp).
	/// </summary>
	[Value(0, MetaName = "file", Required = true,
		HelpText = "Path to the local image file to upload (png, jpg, jpeg, gif, bmp, or webp).")]
	public string File { get; set; }
}

/// <summary>
/// Uploads a local image to the target environment and reports the created image id. A thin adapter
/// over <see cref="ISysImageUploader"/>, which owns the upload and its verification; the command maps
/// the CLI options onto the service and renders the user-facing result.
/// </summary>
public class UploadImageCommand : RemoteCommand<UploadImageOptions> {

	private readonly ISysImageUploader _uploader;

	/// <summary>
	/// Initializes a new instance of the <see cref="UploadImageCommand"/> class.
	/// </summary>
	public UploadImageCommand(EnvironmentSettings settings, ISysImageUploader uploader)
		: base(settings) {
		_uploader = uploader;
	}

	/// <summary>
	/// Uploads the image described by <paramref name="options"/> by delegating to
	/// <see cref="ISysImageUploader"/>. Virtual so the MCP tool can call the resolved command directly
	/// and read the structured result.
	/// </summary>
	/// <param name="options">Command options carrying the file path and connection settings.</param>
	/// <returns>The upload outcome carrying the created image id or a failure message.</returns>
	public virtual SysImageUploadResult UploadImage(UploadImageOptions options) =>
		_uploader.UploadAsync(options.File).ConfigureAwait(false).GetAwaiter().GetResult();

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(UploadImageOptions options) {
		SysImageUploadResult result = UploadImage(options);
		if (result.Success) {
			Logger.WriteInfo($"Uploaded '{options.File}'. imageId: {result.ImageId}");
			return;
		}
		CommandSuccess = false;
		Logger.WriteError(result.Error);
	}
}
