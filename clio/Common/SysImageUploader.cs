using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;

namespace Clio.Common;

/// <inheritdoc cref="ISysImageUploader" />
public sealed class SysImageUploader : ISysImageUploader {

	/// <summary>
	/// Upper bound on an uploaded image payload. Mirrors the Binary sys-setting cap
	/// (<see cref="SysSettingsManager.MaxBinaryValueBytes"/>) so the two branding upload paths
	/// (logos and the shell background) enforce one consistent size policy.
	/// </summary>
	internal const long MaxImageBytes = SysSettingsManager.MaxBinaryValueBytes;

	/// <summary>
	/// The raster and vector formats the Appearance page accepts, mapped to their mime types. SVG is
	/// deliberately supported (PR #928 decision): users can already upload SVGs through the platform
	/// UI, and SysImage assets are rendered via <c>img</c> tags, where embedded SVG script does not
	/// execute — so clio does not restrict the format more than the platform itself does.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> MimeTypesByExtension =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			[".png"] = "image/png",
			[".jpg"] = "image/jpeg",
			[".jpeg"] = "image/jpeg",
			[".gif"] = "image/gif",
			[".bmp"] = "image/bmp",
			[".webp"] = "image/webp",
			[".svg"] = "image/svg+xml"
		};

	private const string CsrfCookieName = "BPMCSRF";

	/// <summary>
	/// Name of the dedicated <see cref="IHttpClientFactory"/> client for the image upload and its
	/// verification read. Configured like the auth client (no cookie jar, no auto-redirect — cookies
	/// are attached manually and a login-page redirect must surface as a raw 3xx), but with a larger
	/// timeout: a cold IIS site can take well over the auth client's 30-second budget to spin up.
	/// </summary>
	internal const string HttpClientName = "sys-image-upload";

	private readonly EnvironmentSettings _environmentSettings;
	private readonly ICreatioAuthClient _authClient;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly Clio.Common.IFileSystem _fileSystem;

	/// <summary>
	/// Initializes the uploader for the environment carried by <paramref name="environmentSettings"/>.
	/// </summary>
	public SysImageUploader(EnvironmentSettings environmentSettings, ICreatioAuthClient authClient,
		IHttpClientFactory httpClientFactory, Clio.Common.IFileSystem fileSystem) {
		_environmentSettings = environmentSettings;
		_authClient = authClient;
		_httpClientFactory = httpClientFactory;
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	public async Task<SysImageUploadResult> UploadAsync(string filePath,
		CancellationToken cancellationToken = default) {
		if (string.IsNullOrWhiteSpace(filePath)) {
			return SysImageUploadResult.Failure("A path to the image file is required.");
		}
		if (!_fileSystem.ExistsFile(filePath)) {
			return SysImageUploadResult.Failure($"File not found: '{filePath}'.");
		}
		string extension = System.IO.Path.GetExtension(filePath);
		if (!MimeTypesByExtension.TryGetValue(extension ?? string.Empty, out string mimeType)) {
			return SysImageUploadResult.Failure(
				$"Unsupported image extension '{extension}'. Supported: " +
				string.Join(", ", MimeTypesByExtension.Keys.OrderBy(k => k, StringComparer.Ordinal)) + ".");
		}
		long fileSize = _fileSystem.GetFileSize(filePath);
		if (fileSize == 0) {
			return SysImageUploadResult.Failure($"File is empty: '{filePath}'.");
		}
		if (fileSize > MaxImageBytes) {
			return SysImageUploadResult.Failure(
				$"File exceeds the {MaxImageBytes:N0}-byte limit: '{filePath}' ({fileSize:N0} bytes).");
		}
		byte[] payload = _fileSystem.ReadAllBytes(filePath);
		// Re-check after the read: a file that grew between the size probe and the read must not
		// slip past the cap (same bounded-read discipline as the Binary sys-setting upload path).
		if (payload.LongLength == 0 || payload.LongLength > MaxImageBytes) {
			return SysImageUploadResult.Failure(
				$"File changed while reading and no longer fits the {MaxImageBytes:N0}-byte limit: '{filePath}' ({payload.LongLength:N0} bytes).");
		}
		try {
			StorageStateResult session = await _authClient.LoginAsync(_environmentSettings, cancellationToken)
				.ConfigureAwait(false);
			return await UploadAuthenticatedAsync(session, filePath, payload, mimeType, cancellationToken)
				.ConfigureAwait(false);
		} catch (CreatioAuthenticationException ex) {
			return SysImageUploadResult.Failure(ex.Message);
		} catch (HttpRequestException ex) {
			return SysImageUploadResult.Failure($"Image upload failed: {ex.Message}");
		} catch (TaskCanceledException) {
			cancellationToken.ThrowIfCancellationRequested();
			return SysImageUploadResult.Failure("Image upload timed out.");
		}
	}

	private async Task<SysImageUploadResult> UploadAuthenticatedAsync(StorageStateResult session,
		string filePath, byte[] payload, string mimeType, CancellationToken cancellationToken) {
		string cookieHeader = string.Join("; ", session.Cookies.Select(c => $"{c.Name}={c.Value}"));
		string csrfToken = session.Cookies
			.FirstOrDefault(c => c.Name.Equals(CsrfCookieName, StringComparison.OrdinalIgnoreCase))?.Value;
		if (string.IsNullOrEmpty(csrfToken)) {
			return SysImageUploadResult.Failure(
				$"The login response carried no {CsrfCookieName} cookie; cannot call the image API.");
		}
		Guid imageId = Guid.NewGuid();
		string fileName = System.IO.Path.GetFileName(filePath);
		HttpClient http = _httpClientFactory.CreateClient(HttpClientName);

		using HttpRequestMessage upload = new(HttpMethod.Post, BuildUploadUrl(imageId, payload.LongLength, mimeType));
		upload.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
		upload.Headers.TryAddWithoutValidation(CsrfCookieName, csrfToken);
		upload.Content = new ByteArrayContent(payload);
		upload.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
		// The File API range end is zero-indexed and inclusive: a 123-byte file is "bytes 0-122/123".
		upload.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, payload.LongLength - 1, payload.LongLength);
		upload.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") {
			FileName = fileName
		};

		using HttpResponseMessage uploadResponse = await http.SendAsync(upload, cancellationToken)
			.ConfigureAwait(false);
		string uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!uploadResponse.IsSuccessStatusCode) {
			return SysImageUploadResult.Failure(
				$"Image upload failed: the image API returned HTTP {(int)uploadResponse.StatusCode}.");
		}
		if (TryReadUploadError(uploadBody, out string serverError)) {
			return SysImageUploadResult.Failure($"Image upload failed: {serverError}");
		}
		return await VerifyUploadAsync(http, cookieHeader, imageId, payload, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Builds the upload URL the platform Appearance page sends: the <c>fileapi&lt;epoch-ms&gt;</c>
	/// fragment is a cache buster, <c>fileId</c> becomes the created record's id, and the image API is
	/// served off the workspace base URL — under the <c>/0</c> alias on .NET Framework and at the site
	/// root on .NET Core (no <c>/rest/</c> segment on either runtime).
	/// </summary>
	private string BuildUploadUrl(Guid imageId, long totalFileLength, string mimeType) {
		return $"{BuildWorkspaceRoot()}/ImageAPIService/upload" +
			$"?fileapi{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" +
			$"&totalFileLength={totalFileLength}" +
			$"&fileId={imageId}" +
			$"&mimeType={Uri.EscapeDataString(mimeType)}";
	}

	private string BuildWorkspaceRoot() {
		string root = _environmentSettings.Uri?.TrimEnd('/') ?? string.Empty;
		return _environmentSettings.IsNetCore ? root : root + "/0";
	}

	/// <summary>
	/// Detects a rejection reported inside a 2xx body (<c>{"success":false,...}</c>, e.g. the
	/// file-security policy) so the server message is surfaced instead of a false success.
	/// </summary>
	private static bool TryReadUploadError(string responseBody, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return false;
		}
		try {
			JsonNode root = JsonNode.Parse(responseBody);
			JsonNode successNode = ReadCaseInsensitive(root, "success");
			if (successNode is null || successNode.GetValueKind() != JsonValueKind.False) {
				return false;
			}
			JsonNode errorInfo = ReadCaseInsensitive(root, "errorInfo");
			error = ReadCaseInsensitive(errorInfo, "message")?.GetValue<string>()
				?? ReadCaseInsensitive(errorInfo, "errorCode")?.GetValue<string>()
				?? "the image API reported success=false.";
			return true;
		} catch (JsonException) {
			// A non-JSON 2xx body (e.g. an HTML login page from an expired session) is not a
			// confirmed success either — the verification GET below is the authoritative check.
			return false;
		}
	}

	/// <summary>
	/// Reads a property accepting either casing: the service answers in camelCase but the casing is
	/// not a documented contract, and a PascalCase rejection message must not be dropped.
	/// </summary>
	private static JsonNode ReadCaseInsensitive(JsonNode node, string propertyName) {
		if (node is not JsonObject jsonObject) {
			return null;
		}
		foreach ((string key, JsonNode value) in jsonObject) {
			if (string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase)) {
				return value;
			}
		}
		return null;
	}

	/// <summary>
	/// Reads the image back through the read endpoint (the "hash" segment is literal) and requires the
	/// exact uploaded bytes: an expired session returns HTTP 200 with the login-page HTML, so a
	/// status-only check could report a false success — the byte comparison is the authoritative
	/// persistence proof.
	/// </summary>
	private async Task<SysImageUploadResult> VerifyUploadAsync(HttpClient http, string cookieHeader,
		Guid imageId, byte[] payload, CancellationToken cancellationToken) {
		string verifyUrl = $"{BuildWorkspaceRoot()}/img/entity/hash/SysImage/Data/{imageId}";
		using HttpRequestMessage verify = new(HttpMethod.Get, verifyUrl);
		verify.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
		using HttpResponseMessage verifyResponse = await http.SendAsync(verify, cancellationToken)
			.ConfigureAwait(false);
		if (!verifyResponse.IsSuccessStatusCode) {
			return SysImageUploadResult.Failure(
				$"Image upload could not be verified: reading the image back returned HTTP {(int)verifyResponse.StatusCode}.");
		}
		byte[] storedBytes = await verifyResponse.Content.ReadAsByteArrayAsync(cancellationToken)
			.ConfigureAwait(false);
		if (!storedBytes.AsSpan().SequenceEqual(payload)) {
			return SysImageUploadResult.Failure(
				"Image upload could not be verified: the image read back from the environment does not match the uploaded file.");
		}
		return SysImageUploadResult.Successful(imageId);
	}
}
