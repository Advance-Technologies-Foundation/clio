using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common;

/// <summary>
/// Uploads a local image file into a Creatio environment's <c>SysImage</c> table through the platform
/// image API (<c>ImageAPIService/upload</c>) on an authenticated clio session. The <c>SysImage</c>
/// binary column cannot be written through the OData JSON surface (the stream stays empty), so this
/// is the supported programmatic write path for images such as the shell background.
/// </summary>
public interface ISysImageUploader {

	/// <summary>
	/// Uploads the file at <paramref name="filePath"/> and returns the outcome. On success the result
	/// carries the created <c>SysImage</c> record id; on failure it carries a user-facing error message
	/// (no exception is thrown for expected validation/transport failures).
	/// </summary>
	/// <param name="filePath">Path to the local image file to upload.</param>
	/// <param name="cancellationToken">Cancels the login and upload requests.</param>
	Task<SysImageUploadResult> UploadAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a <see cref="ISysImageUploader.UploadAsync"/> call.
/// </summary>
/// <param name="Success">Whether the image was uploaded and verified.</param>
/// <param name="ImageId">The created <c>SysImage</c> record id; <see cref="System.Guid.Empty"/> on failure.</param>
/// <param name="Error">The user-facing failure message; <c>null</c> on success.</param>
public sealed record SysImageUploadResult(bool Success, System.Guid ImageId, string Error) {

	/// <summary>Creates a success result carrying the created <c>SysImage</c> id.</summary>
	public static SysImageUploadResult Successful(System.Guid imageId) => new(true, imageId, null);

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static SysImageUploadResult Failure(string error) => new(false, System.Guid.Empty, error);
}
