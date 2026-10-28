using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for the <c>upload-file</c> command.
/// </summary>
[Verb("upload-file",
	HelpText = "Upload a local file into a file entity, attach it to a record and print the file id")]
public class UploadFileOptions : RemoteCommandOptions {

	/// <summary>Path to the local file to upload.</summary>
	[Value(0, MetaName = "file", Required = true, HelpText = "Path to the local file to upload.")]
	public string File { get; set; }

	/// <summary>File entity schema name, for example <c>ContactFile</c>.</summary>
	[Option("entity", Required = true,
		HelpText = "File entity schema name, for example ContactFile, AccountFile or KnowledgeBaseFile.")]
	public string Entity { get; set; }

	/// <summary>Lookup column of the file entity that points to the parent record.</summary>
	[Option("parent-column", Required = true,
		HelpText = "Lookup column of the file entity that points to the parent record, for example Contact.")]
	public string ParentColumn { get; set; }

	/// <summary>Id of the parent record.</summary>
	[Option("parent-id", Required = true, HelpText = "Id of the parent record to attach the file to.")]
	public string ParentId { get; set; }
}

/// <summary>
/// Uploads a local file into a file entity and attaches it to a record. A thin adapter over
/// <see cref="IRecordFileUploader"/>, which owns the upload and its verification.
/// </summary>
public class UploadFileCommand : RemoteCommand<UploadFileOptions> {

	private readonly IRecordFileUploader _uploader;

	/// <summary>
	/// Initializes a new instance of the <see cref="UploadFileCommand"/> class.
	/// </summary>
	public UploadFileCommand(EnvironmentSettings settings, IRecordFileUploader uploader)
		: base(settings) {
		_uploader = uploader;
	}

	/// <summary>
	/// Uploads the file described by <paramref name="options"/> by delegating to <see cref="IRecordFileUploader"/>.
	/// </summary>
	/// <param name="options">Command options carrying the file, the file entity and the parent record.</param>
	/// <returns>The upload outcome carrying the created file id or a failure message.</returns>
	public virtual RecordFileUploadResult UploadFile(UploadFileOptions options) {
		if (!Guid.TryParse(options.ParentId, out Guid parentId)) {
			return RecordFileUploadResult.Failure("parent-id must be the id (GUID) of an existing record.");
		}
		return _uploader.UploadAsync(
				new RecordFileUploadRequest(options.File, options.Entity, options.ParentColumn, parentId))
			.ConfigureAwait(false).GetAwaiter().GetResult();
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(UploadFileOptions options) {
		RecordFileUploadResult result = UploadFile(options);
		if (result.Success) {
			Logger.WriteInfo($"Uploaded '{options.File}' to {options.Entity}. fileId: {result.FileId}");
			return;
		}
		CommandSuccess = false;
		Logger.WriteError(result.Error);
	}
}
