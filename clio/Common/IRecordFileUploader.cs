using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common;

/// <summary>
/// Uploads a local file into a Creatio file entity (for example <c>ContactFile</c> or
/// <c>KnowledgeBaseFile</c>) and links it to a parent record through the platform file API
/// (<c>FileApiService/UploadFile</c>). A file entity's <c>Data</c> column is an <c>Edm.Stream</c>
/// property that the OData JSON surface cannot write, so this is the supported programmatic path for
/// a file attached to a record.
/// </summary>
public interface IRecordFileUploader {

	/// <summary>
	/// Uploads the file described by <paramref name="request"/>, reads its bytes back and returns the
	/// outcome. Expected validation, server and transport failures are returned as a failure result, not
	/// thrown.
	/// </summary>
	/// <param name="request">The local file, the file entity and the parent record to link it to.</param>
	/// <param name="cancellationToken">Cancels the login, upload and readback requests.</param>
	Task<RecordFileUploadResult> UploadAsync(RecordFileUploadRequest request,
		CancellationToken cancellationToken = default);
}

/// <summary>Input of <see cref="IRecordFileUploader.UploadAsync"/>.</summary>
/// <param name="FilePath">Path to the local file.</param>
/// <param name="Entity">File entity schema name, for example <c>ContactFile</c>.</param>
/// <param name="ParentColumn">Lookup column of the file entity that points to the parent, for example <c>Contact</c>.</param>
/// <param name="ParentId">Id of the parent record.</param>
public sealed record RecordFileUploadRequest(string FilePath, string Entity, string ParentColumn, Guid ParentId);

/// <summary>Outcome of <see cref="IRecordFileUploader.UploadAsync"/>.</summary>
/// <param name="Success">Whether the file was uploaded and its bytes were read back unchanged.</param>
/// <param name="FileId">
/// Id of the created file record. Set on success, and also on a failure that happened after the file API
/// accepted the upload, so the caller can inspect the record instead of uploading the file again.
/// </param>
/// <param name="Error">User-facing failure text; <c>null</c> on success.</param>
public sealed record RecordFileUploadResult(bool Success, Guid? FileId, string Error) {

	/// <summary>Creates a success result carrying the created file record id.</summary>
	public static RecordFileUploadResult Successful(Guid fileId) => new(true, fileId, null);

	/// <summary>Creates a failure result; <paramref name="fileId"/> is set when the upload was accepted.</summary>
	public static RecordFileUploadResult Failure(string error, Guid? fileId = null) => new(false, fileId, error);
}
