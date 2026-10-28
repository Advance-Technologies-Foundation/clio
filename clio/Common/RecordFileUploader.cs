using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.BrowserSession;
using Creatio.Client;

namespace Clio.Common;

/// <inheritdoc cref="IRecordFileUploader" />
public sealed class RecordFileUploader : IRecordFileUploader {

	/// <summary>
	/// Upper bound on an uploaded file. The whole file is sent in one request, so the bound also limits the
	/// memory one call holds. It equals the default of Creatio's own <c>MaxFileSize</c> setting.
	/// </summary>
	internal const long MaxFileBytes = SysSettingsManager.MaxBinaryValueBytes;

	private const int LoginTimeout = 30_000;
	private const string DataColumnName = "Data";
	private const string DefaultMimeType = "application/octet-stream";

	/// <summary>
	/// Mime types for common attachment extensions. Any other extension is sent as
	/// <see cref="DefaultMimeType"/>: the file API stores the bytes as they are and does not check the mime
	/// type against the content.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> MimeTypesByExtension =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			[".txt"] = "text/plain",
			[".csv"] = "text/csv",
			[".json"] = "application/json",
			[".xml"] = "application/xml",
			[".pdf"] = "application/pdf",
			[".zip"] = "application/zip",
			[".doc"] = "application/msword",
			[".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
			[".xls"] = "application/vnd.ms-excel",
			[".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
			[".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
			[".png"] = "image/png",
			[".jpg"] = "image/jpeg",
			[".jpeg"] = "image/jpeg",
			[".gif"] = "image/gif",
			[".svg"] = "image/svg+xml"
		};

	private static readonly Regex SchemaNamePattern = new("^[A-Za-z_][A-Za-z0-9_]{0,127}$",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IFileSystem _fileSystem;

	/// <summary>Initializes the uploader for the active environment.</summary>
	public RecordFileUploader(EnvironmentSettings environmentSettings,
		IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder,
		IFileSystem fileSystem) {
		_environmentSettings = environmentSettings;
		_applicationClientFactory = applicationClientFactory;
		_serviceUrlBuilder = serviceUrlBuilder;
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	public async Task<RecordFileUploadResult> UploadAsync(RecordFileUploadRequest request,
		CancellationToken cancellationToken = default) {
		RecordFileUploadResult validationFailure = Validate(request);
		if (validationFailure is not null) {
			return validationFailure;
		}
		validationFailure = TryReadFile(request.FilePath, out byte[] payload);
		if (validationFailure is not null) {
			return validationFailure;
		}
		if (string.IsNullOrWhiteSpace(_environmentSettings.Login)
			|| string.IsNullOrWhiteSpace(_environmentSettings.Password)) {
			return RecordFileUploadResult.Failure(
				"File upload needs forms authentication: set the login and password of the environment in clio.");
		}
		Guid fileId = Guid.NewGuid();
		bool uploadSent = false;
		try {
			using IOwnedApplicationClient client = _applicationClientFactory.CreateFormsEnvironmentClient(
				_environmentSettings);
			using (HttpResponseMessage loginResponse = await client.LoginAsync(LoginTimeout, cancellationToken)
					.ConfigureAwait(false)) {
				if (!loginResponse.IsSuccessStatusCode) {
					return AuthenticationFailure();
				}
			}
			string fileName = System.IO.Path.GetFileName(request.FilePath);
			string mimeType = GetMimeType(fileName);
			uploadSent = true;
			using HttpResponseMessage uploadResponse = await client.UploadImageAsync(
					BuildUploadUrl(request, fileId, fileName, payload.LongLength, mimeType), payload, fileName,
					mimeType, cancellationToken: cancellationToken)
				.ConfigureAwait(false);
			return await ReadUploadOutcomeAsync(client, uploadResponse, request, fileId, payload, cancellationToken)
				.ConfigureAwait(false);
		} catch (CreatioAuthenticationException ex) {
			return MapTransportFailure(ex, uploadSent, fileId, cancellationToken);
		} catch (UnauthorizedAccessException ex) {
			return MapTransportFailure(ex, uploadSent, fileId, cancellationToken);
		} catch (HttpRequestException ex) {
			return MapTransportFailure(ex, uploadSent, fileId, cancellationToken);
		} catch (OperationCanceledException ex) {
			return MapTransportFailure(ex, uploadSent, fileId, cancellationToken);
		}
	}

	/// <summary>
	/// Reads the answer of the file API and, when it reports a stored file, verifies the stored bytes.
	/// </summary>
	private async Task<RecordFileUploadResult> ReadUploadOutcomeAsync(ICreatioApplicationClient client,
		HttpResponseMessage uploadResponse, RecordFileUploadRequest request, Guid fileId, byte[] payload,
		CancellationToken cancellationToken) {
		string uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken)
			.ConfigureAwait(false);
		if (uploadResponse.StatusCode == System.Net.HttpStatusCode.BadRequest && IsSecurityFilterRejection(uploadBody)) {
			return RecordFileUploadResult.Failure(
				"File upload failed: the file security policy of the environment rejected the file before it " +
				"was stored. Typical causes: the extension does not match the file content, or the extension " +
				"is not allowed.");
		}
		if (!uploadResponse.IsSuccessStatusCode) {
			return UnconfirmedFailure(
				$"the file API returned HTTP {(int)uploadResponse.StatusCode}", fileId);
		}
		RecordFileUploadResult rejection = ReadUploadResponse(uploadBody, request, fileId);
		if (rejection is not null) {
			return rejection;
		}
		return await VerifyUploadAsync(client, request.Entity, fileId, payload, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Maps a transport exception to a result. After the upload request was sent the outcome is unconfirmed,
	/// because the file can be stored even when the answer is lost.
	/// </summary>
	private static RecordFileUploadResult MapTransportFailure(Exception exception, bool uploadSent, Guid fileId,
		CancellationToken cancellationToken) {
		switch (exception) {
			case HttpRequestException: {
				string reason = SensitiveErrorTextRedactor.Redact(exception.Message);
				return uploadSent
					? UnconfirmedFailure($"the request failed ({reason})", fileId)
					: RecordFileUploadResult.Failure($"File upload failed: {reason}");
			}
			case OperationCanceledException:
				cancellationToken.ThrowIfCancellationRequested();
				return uploadSent
					? UnconfirmedFailure("the request timed out", fileId)
					: RecordFileUploadResult.Failure("File upload timed out before the file was sent.");
			default:
				return uploadSent ? UnconfirmedFailure("the session was rejected", fileId) : AuthenticationFailure();
		}
	}

	private static RecordFileUploadResult Validate(RecordFileUploadRequest request) {
		if (request is null) {
			return RecordFileUploadResult.Failure("An upload request is required.");
		}
		if (string.IsNullOrWhiteSpace(request.Entity) || !SchemaNamePattern.IsMatch(request.Entity)) {
			return RecordFileUploadResult.Failure(
				"entity must be the schema name of a file entity, for example ContactFile.");
		}
		if (string.IsNullOrWhiteSpace(request.ParentColumn) || !SchemaNamePattern.IsMatch(request.ParentColumn)) {
			return RecordFileUploadResult.Failure(
				"parent-column must be the lookup column of the file entity that points to the parent record, " +
				"for example Contact for ContactFile.");
		}
		if (request.ParentId == Guid.Empty) {
			return RecordFileUploadResult.Failure("parent-id must be the id of an existing record.");
		}
		return null;
	}

	private RecordFileUploadResult TryReadFile(string filePath, out byte[] payload) {
		payload = null;
		if (string.IsNullOrWhiteSpace(filePath)) {
			return RecordFileUploadResult.Failure("A path to the file is required.");
		}
		if (!_fileSystem.ExistsFile(filePath)) {
			return RecordFileUploadResult.Failure($"File not found: '{filePath}'.");
		}
		long fileSize = _fileSystem.GetFileSize(filePath);
		if (fileSize == 0) {
			return RecordFileUploadResult.Failure($"File is empty: '{filePath}'.");
		}
		if (fileSize > MaxFileBytes) {
			return RecordFileUploadResult.Failure(
				$"File exceeds the {MaxFileBytes:N0}-byte limit: '{filePath}' ({fileSize:N0} bytes).");
		}
		payload = _fileSystem.ReadAllBytes(filePath);
		// Re-check after the read: a file that grew between the size probe and the read must not pass the cap.
		if (payload.LongLength == 0 || payload.LongLength > MaxFileBytes) {
			return RecordFileUploadResult.Failure(
				$"File changed while reading and no longer fits the {MaxFileBytes:N0}-byte limit: '{filePath}'.");
		}
		return null;
	}

	private static string GetMimeType(string fileName) =>
		MimeTypesByExtension.TryGetValue(System.IO.Path.GetExtension(fileName) ?? string.Empty, out string mimeType)
			? mimeType
			: DefaultMimeType;

	private static RecordFileUploadResult AuthenticationFailure() =>
		RecordFileUploadResult.Failure(
			"Authentication failed before the upload: check the login and password of the environment in clio.");

	/// <summary>
	/// Failure after the file was sent but before the outcome was confirmed. The file record can exist, so
	/// the text gives its id and says to check it instead of uploading the file again.
	/// </summary>
	private static RecordFileUploadResult UnconfirmedFailure(string reason, Guid fileId) =>
		RecordFileUploadResult.Failure(
			$"File upload was not confirmed: {reason}. The file record {fileId} can exist; read it with " +
			"odata-read before you upload the file again.", fileId);

	/// <summary>
	/// Builds the chunked-upload URL: with a <c>Content-Range</c> header the file API reads every parameter
	/// from the query string. <c>fileId</c> becomes the created record's id.
	/// </summary>
	private string BuildUploadUrl(RecordFileUploadRequest request, Guid fileId, string fileName, long totalFileLength,
		string mimeType) =>
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.FileApiUploadFile) +
		$"?totalFileLength={totalFileLength}" +
		$"&fileId={fileId}" +
		$"&fileName={Uri.EscapeDataString(fileName)}" +
		$"&mimeType={Uri.EscapeDataString(mimeType)}" +
		$"&columnName={DataColumnName}" +
		$"&entitySchemaName={request.Entity}" +
		$"&parentColumnName={request.ParentColumn}" +
		$"&parentColumnValue={request.ParentId}";

	/// <summary>
	/// Reads the file API answer. Returns <see langword="null"/> when it reports success, otherwise the
	/// failure to return. The service answers HTTP 200 with <c>success:false</c> on a rejection, and its
	/// <c>errorInfo.message</c> carries a stack trace and database constraint names, so the server text is
	/// never copied: known rejections are described in clio's own words, anything else by its error code.
	/// </summary>
	private static RecordFileUploadResult ReadUploadResponse(string body, RecordFileUploadRequest request,
		Guid fileId) {
		JsonElement root;
		try {
			using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
			root = document.RootElement.Clone();
		} catch (JsonException) {
			return UnconfirmedFailure("the file API did not return JSON (a session or proxy problem)", fileId);
		}
		if (root.ValueKind != JsonValueKind.Object) {
			return UnconfirmedFailure("the file API returned an empty or unexpected answer", fileId);
		}
		JsonValueKind success = TryGetPropertyIgnoreCase(root, "success", out JsonElement successElement)
			? successElement.ValueKind
			: JsonValueKind.Undefined;
		if (success == JsonValueKind.True) {
			return null;
		}
		if (success != JsonValueKind.False) {
			return UnconfirmedFailure("the file API returned an unexpected answer", fileId);
		}
		string errorCode = null;
		string message = null;
		if (TryGetPropertyIgnoreCase(root, "errorInfo", out JsonElement errorInfo)
			&& errorInfo.ValueKind == JsonValueKind.Object) {
			errorCode = ReadString(errorInfo, "errorCode");
			message = ReadString(errorInfo, "message");
		}
		// A mapped rejection is raised before the insert (unknown schema or column) or by the insert itself
		// (missing parent), so nothing was stored and no file id is returned.
		string knownRejection = DescribeKnownRejection(errorCode, message, request);
		if (knownRejection is not null) {
			return RecordFileUploadResult.Failure("File upload failed: " + knownRejection);
		}
		// Any other error can come after the insert committed, so the file record can exist.
		string code = errorCode is not null && SchemaNamePattern.IsMatch(errorCode) ? $" ({errorCode})" : string.Empty;
		return UnconfirmedFailure(
			$"the file API reported an error{code}. The server text is not shown; check the environment log " +
			"for the cause", fileId);
	}

	/// <summary>
	/// Describes a rejection that is known to store nothing, in clio's own words. Returns
	/// <see langword="null"/> for any other error.
	/// </summary>
	private static string DescribeKnownRejection(string errorCode, string message, RecordFileUploadRequest request) {
		message ??= string.Empty;
		if (errorCode == "ItemNotFoundException") {
			if (message.StartsWith("Item with name", StringComparison.Ordinal)) {
				return $"the entity '{request.Entity}' does not exist on the environment.";
			}
			if (message.StartsWith("Collection item with name", StringComparison.Ordinal)) {
				return $"the column '{request.ParentColumn}' does not exist on '{request.Entity}'.";
			}
			if (message.StartsWith($"Value \"{DataColumnName}\" was not found", StringComparison.Ordinal)) {
				return $"'{request.Entity}' is not a file entity: it has no {DataColumnName} column.";
			}
		}
		if (errorCode == "DbOperationException"
			&& (message.StartsWith("23503", StringComparison.Ordinal)
				|| message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))) {
			return $"the parent record {request.ParentId} does not exist in the table that " +
				$"'{request.ParentColumn}' points to.";
		}
		return null;
	}

	/// <summary>
	/// Whether the body is the <c>{"error":"..."}</c> answer of the platform file security filter. The filter
	/// runs before the file API, so nothing is stored. Its text is not copied into the result.
	/// </summary>
	private static bool IsSecurityFilterRejection(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(body);
			return document.RootElement.ValueKind == JsonValueKind.Object
				&& ReadString(document.RootElement, "error") is not null;
		} catch (JsonException) {
			return false;
		}
	}

	private static string ReadString(JsonElement element, string propertyName) =>
		TryGetPropertyIgnoreCase(element, propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	/// <summary>
	/// Reads a property in any casing. The service answers in camelCase, but the casing is not a documented
	/// contract (the same rule as <see cref="SysImageUploader"/>).
	/// </summary>
	private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value) {
		value = element.EnumerateObject()
			.FirstOrDefault(candidate => string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
			.Value;
		return value.ValueKind != JsonValueKind.Undefined;
	}

	/// <summary>
	/// Reads the stored bytes back through OData and requires the exact uploaded bytes: this proves the
	/// record exists under <paramref name="fileId"/> and holds the file.
	/// </summary>
	private async Task<RecordFileUploadResult> VerifyUploadAsync(ICreatioApplicationClient client, string entity,
		Guid fileId, byte[] payload, CancellationToken cancellationToken) {
		string verifyUrl = _serviceUrlBuilder.Build($"odata/{entity}({fileId})/{DataColumnName}");
		using HttpResponseMessage verifyResponse = await client.ExecuteGetRequestAsync(
				verifyUrl, cancellationToken: cancellationToken)
			.ConfigureAwait(false);
		if (!verifyResponse.IsSuccessStatusCode) {
			return UnconfirmedFailure(
				$"reading the file back returned HTTP {(int)verifyResponse.StatusCode}", fileId);
		}
		byte[] storedBytes = await verifyResponse.Content.ReadAsByteArrayAsync(cancellationToken)
			.ConfigureAwait(false);
		if (!storedBytes.AsSpan().SequenceEqual(payload)) {
			return UnconfirmedFailure("the file read back from the environment does not match the local file", fileId);
		}
		return RecordFileUploadResult.Successful(fileId);
	}
}
