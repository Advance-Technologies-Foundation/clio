using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;
using Creatio.Client;

namespace Clio.Common;

/// <inheritdoc cref="ISysImageUploader" />
public sealed class SysImageUploader : ISysImageUploader {

	/// <summary>
	/// Upper bound on an uploaded image payload. Mirrors the Binary sys-setting cap
	/// (<see cref="SysSettingsManager.MaxBinaryValueBytes"/>) so the two branding upload paths
	/// (logos and the shell background) enforce one consistent size policy.
	/// </summary>
	internal const long MaxImageBytes = SysSettingsManager.MaxBinaryValueBytes;
	private const int LoginTimeout = 30_000;

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

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly Clio.Common.IFileSystem _fileSystem;
	// This field exists only to honor the obsolete public constructor for binary-compatible callers.
#pragma warning disable CS0618
	private readonly ICreatioAuthClient _legacyAuthClient;
#pragma warning restore CS0618

	/// <summary>
	/// Initializes the uploader for the active environment using a dedicated strict-TLS forms client.
	/// </summary>
	public SysImageUploader(EnvironmentSettings environmentSettings,
		IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder,
		Clio.Common.IFileSystem fileSystem) {
		_environmentSettings = environmentSettings;
		_applicationClientFactory = applicationClientFactory;
		_serviceUrlBuilder = serviceUrlBuilder;
		_fileSystem = fileSystem;
	}

	/// <summary>Retains the historical constructor while routing upload transport through CreatioClient.</summary>
	[Obsolete("Use the overload that accepts IApplicationClientFactory and IServiceUrlBuilder.")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Architecture", "CLIO001:Resolve behavior through DI",
		Justification = "The compatibility constructor must retain its historical public signature.")]
	public SysImageUploader(EnvironmentSettings environmentSettings, ICreatioAuthClient authClient,
		IHttpClientFactory httpClientFactory, Clio.Common.IFileSystem fileSystem)
		: this(environmentSettings, new ApplicationClientFactory(new NoReauthExecutor()),
			new ServiceUrlBuilder(environmentSettings), fileSystem) {
		ArgumentNullException.ThrowIfNull(authClient);
		ArgumentNullException.ThrowIfNull(httpClientFactory);
		_legacyAuthClient = authClient;
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
		if (string.IsNullOrWhiteSpace(_environmentSettings.Login)
			|| string.IsNullOrWhiteSpace(_environmentSettings.Password)) {
			return SysImageUploadResult.Failure(
				$"authentication failed for environment while uploading '{System.IO.Path.GetFileName(filePath)}' — " +
				"forms username and password are required in env config");
		}
		try {
			using IOwnedApplicationClient client = _applicationClientFactory.CreateFormsEnvironmentClient(
				_environmentSettings);
			if (_legacyAuthClient is not null) {
				StorageStateResult session = await _legacyAuthClient.LoginAsync(_environmentSettings, cancellationToken)
					.ConfigureAwait(false);
				if (!Uri.TryCreate(_environmentSettings.Uri, UriKind.Absolute, out Uri environmentUri)) {
					return SysImageUploadResult.Failure("Image upload failed: the environment URL is invalid.");
				}
				client.ImportSessionCookies(session.Cookies.Select(cookie => ToSessionCookie(cookie, environmentUri)));
			} else {
				using HttpResponseMessage loginResponse = await client.LoginAsync(LoginTimeout, cancellationToken)
					.ConfigureAwait(false);
				if (!loginResponse.IsSuccessStatusCode) {
					return SysImageUploadResult.Failure(
						$"authentication failed for environment while uploading '{System.IO.Path.GetFileName(filePath)}' — " +
						"check username and password in env config");
				}
			}
			return await UploadThroughClientAsync(client, filePath, payload, mimeType, cancellationToken)
				.ConfigureAwait(false);
		} catch (CreatioAuthenticationException) {
			return SysImageUploadResult.Failure(
				$"authentication failed for environment while uploading '{System.IO.Path.GetFileName(filePath)}' — " +
				"check username and password in env config");
		} catch (UnauthorizedAccessException) {
			return SysImageUploadResult.Failure(
				$"authentication failed for environment while uploading '{System.IO.Path.GetFileName(filePath)}' — " +
				"check username and password in env config");
		} catch (HttpRequestException ex) {
			return SysImageUploadResult.Failure($"Image upload failed: {ex.Message}");
		} catch (OperationCanceledException) {
			cancellationToken.ThrowIfCancellationRequested();
			return SysImageUploadResult.Failure("Image upload timed out.");
		}
	}

	private static CreatioSessionCookie ToSessionCookie(BrowserCookie cookie, Uri environmentUri) => new(
		cookie.Name,
		cookie.Value,
		string.IsNullOrEmpty(cookie.Domain) ? environmentUri.Host : cookie.Domain,
		string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
		cookie.HttpOnly,
		cookie.Secure,
		cookie.SameSite,
		cookie.Expires < 0
			? DateTime.MinValue
			: DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).UtcDateTime);

	private async Task<SysImageUploadResult> UploadThroughClientAsync(ICreatioApplicationClient client,
		string filePath, byte[] payload, string mimeType, CancellationToken cancellationToken) {
		Guid imageId = Guid.NewGuid();
		string fileName = System.IO.Path.GetFileName(filePath);

		using HttpResponseMessage uploadResponse = await client.UploadImageAsync(
			BuildUploadUrl(imageId, payload.LongLength, mimeType), payload, fileName, mimeType,
			cancellationToken: cancellationToken)
			.ConfigureAwait(false);
		string uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!uploadResponse.IsSuccessStatusCode) {
			string csrfHint = uploadResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized
				or System.Net.HttpStatusCode.Forbidden
				? " Verify the environment credentials and that its proxy preserves the Creatio CSRF cookie."
				: string.Empty;
			return SysImageUploadResult.Failure(
				$"Image upload failed: the image API returned HTTP {(int)uploadResponse.StatusCode}.{csrfHint}");
		}
		if (TryReadUploadError(uploadBody, out string serverError)) {
			return SysImageUploadResult.Failure($"Image upload failed: {serverError}");
		}
		return await VerifyUploadAsync(client, imageId, payload, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Builds the upload URL the platform Appearance page sends: the <c>fileapi&lt;epoch-ms&gt;</c>
	/// fragment is a cache buster, <c>fileId</c> becomes the created record's id, and the image API is
	/// served off the workspace base URL — under the <c>/0</c> alias on .NET Framework and at the site
	/// root on .NET Core (no <c>/rest/</c> segment on either runtime).
	/// </summary>
	private string BuildUploadUrl(Guid imageId, long totalFileLength, string mimeType) {
		return _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ImageApiUpload) +
			$"?fileapi{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" +
			$"&totalFileLength={totalFileLength}" +
			$"&fileId={imageId}" +
			$"&mimeType={Uri.EscapeDataString(mimeType)}";
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
			// The live API answers rejections in two shapes (both observed): {"error":"<reason>"} and
			// {"success":false,"errorInfo":{...}}. Missing either one turns a server rejection into a
			// misleading verification-mismatch error later.
			JsonNode plainError = ReadCaseInsensitive(root, "error");
			if (plainError is not null) {
				error = AsDisplayString(plainError) ?? "the image API reported an error.";
				return true;
			}
			JsonNode successNode = ReadCaseInsensitive(root, "success");
			if (successNode is null || successNode.GetValueKind() != JsonValueKind.False) {
				return false;
			}
			JsonNode errorInfo = ReadCaseInsensitive(root, "errorInfo");
			error = AsDisplayString(ReadCaseInsensitive(errorInfo, "message"))
				?? AsDisplayString(ReadCaseInsensitive(errorInfo, "errorCode"))
				?? "the image API reported success=false.";
			return true;
		} catch (JsonException) {
			// A non-JSON 2xx body (e.g. an HTML login page from an expired session) is not a
			// confirmed success either — the verification GET below is the authoritative check.
			return false;
		}
	}

	/// <summary>
	/// Renders a JSON value for a user-facing message regardless of its kind: a rejection reason can
	/// arrive as a string message or as a numeric error code, and <c>GetValue&lt;string&gt;()</c> on a
	/// non-string node would throw and lose the server's reason.
	/// </summary>
	private static string AsDisplayString(JsonNode node) {
		if (node is null) {
			return null;
		}
		return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
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
	private async Task<SysImageUploadResult> VerifyUploadAsync(ICreatioApplicationClient client, Guid imageId,
		byte[] payload, CancellationToken cancellationToken) {
		string verifyUrl = _serviceUrlBuilder.Build($"/img/entity/hash/SysImage/Data/{imageId}");
		using HttpResponseMessage verifyResponse = await client.ExecuteGetRequestAsync(
			verifyUrl, cancellationToken: cancellationToken)
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
