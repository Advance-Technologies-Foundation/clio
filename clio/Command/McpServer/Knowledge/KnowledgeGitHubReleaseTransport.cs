using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// Retrieves a signed knowledge bundle published as a GitHub Release asset.
/// </summary>
/// <remarks>
/// <para>
/// This transport never runs Git and never keeps a checkout: it reads one release through the GitHub
/// REST API, downloads exactly the declared asset, and hands the bytes to the bundle runtime, which
/// owns signature, contract, and sequence verification.
/// </para>
/// <para>
/// The GitHub-published asset digest proves the bytes survived transport intact. It is deliberately
/// not a substitute for the publisher signature: the digest is served by the same origin as the
/// asset, so only the detached manifest signature establishes who produced the content.
/// </para>
/// </remarks>
internal sealed class KnowledgeGitHubReleaseTransport : IKnowledgeArtifactTransport {
	internal const string HttpClientName = "knowledge-github-release";

	private const int MaxReleaseMetadataBytes = 1024 * 1024;
	private const int MaxAssetBytes = 40 * 1024 * 1024;
	private const int MaxAssetEntries = 512;
	private const int MaxRedirects = 3;
	private const int MaxEntityTagLength = 256;
	private const string ApiVersionHeader = "X-GitHub-Api-Version";
	private const string ApiVersion = "2022-11-28";
	private const string DigestPrefix = "sha256:";

	private static readonly Regex Sha256DigestPattern = new(
		"^[0-9a-f]{64}$",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	private static readonly Regex ReleaseTagPattern = new(
		"^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	/// <summary>
	/// The hosts GitHub is documented to redirect release-asset downloads to.
	/// </summary>
	/// <remarks>
	/// A redirect to anything else is refused rather than followed. When the configured API origin is
	/// a loopback address — the hermetic test topology — only that same origin is accepted, so a test
	/// server can never send Clio to a public host and a production source can never be pointed at
	/// localhost.
	/// </remarks>
	private static readonly IReadOnlySet<string> AllowedDownloadHosts = new HashSet<string>(
		StringComparer.OrdinalIgnoreCase) {
		"api.github.com",
		"github.com",
		"release-assets.githubusercontent.com",
		"objects.githubusercontent.com"
	};

	private static readonly IReadOnlySet<string> AllowedAssetContentTypes = new HashSet<string>(
		StringComparer.OrdinalIgnoreCase) {
		"application/zip",
		"application/x-zip-compressed",
		"application/octet-stream"
	};

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly KnowledgeGitHubReleaseOptions _options;

	public KnowledgeGitHubReleaseTransport(
		IHttpClientFactory httpClientFactory,
		KnowledgeGitHubReleaseOptions options) {
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.TransportDeadlineMilliseconds);
	}

	/// <inheritdoc/>
	public KnowledgeSourceType Type => KnowledgeSourceType.GitHubRelease;

	/// <inheritdoc/>
	/// <remarks>
	/// The revision is the release tag, which the producer keeps identical to the signed bundle's
	/// library version. Ordering it is transport bookkeeping for the candidate walk only — generation
	/// ordering belongs to the bundle's monotonic <c>sequence</c>, enforced by the runtime's
	/// sequence-replay rules, and a higher tag never authorizes a lower sequence.
	/// </remarks>
	public string? GreaterRevision(string? left, string? right) {
		if (!TryParseTag(left, out Version? leftVersion)) {
			return TryParseTag(right, out _) ? right : null;
		}
		if (!TryParseTag(right, out Version? rightVersion)) {
			return left;
		}
		return leftVersion.CompareTo(rightVersion) >= 0 ? left : right;
	}

	/// <inheritdoc/>
	public KnowledgeTransportResult Retrieve(KnowledgeTransportRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		KnowledgeSourceConfiguration source = KnowledgeSourceConfigurationValidator.ValidateAndClone(request.Source);
		if (source.Type != Type) {
			throw new ArgumentException("GitHub release transport received a non-GitHub-release source.",
				nameof(request));
		}
		int deadlineMilliseconds = Math.Min(
			request.TransportDeadlineMilliseconds ?? _options.TransportDeadlineMilliseconds,
			_options.TransportDeadlineMilliseconds);
		if (deadlineMilliseconds <= 0) {
			return NoCandidate(request.CatalogFingerprint);
		}
		// One absolute deadline spans metadata, redirects, download, and digest verification, so a slow
		// stage cannot borrow time from the next one.
		using CancellationTokenSource deadline = new(deadlineMilliseconds);
		try {
			return RetrieveCore(request, source, deadline.Token);
		} catch (Exception exception) when (exception is HttpRequestException
				or OperationCanceledException
				or IOException
				or InvalidDataException
				or JsonException
				or RegexMatchTimeoutException
				or ArgumentException
				or NotSupportedException
				or UriFormatException) {
			return Failed(Redact(exception.Message), request.CatalogFingerprint);
		}
	}

	private KnowledgeTransportResult RetrieveCore(
		KnowledgeTransportRequest request,
		KnowledgeSourceConfiguration source,
		CancellationToken cancellationToken) {
		Uri apiBase = new(source.Location, UriKind.Absolute);
		// Validation already guarantees these, but reading them through a guard keeps the URI builder
		// free of null-forgiving operators in a file where nullable warnings are off.
		string owner = source.RepositoryOwner ?? throw new ArgumentException(
			"A GitHub release source must declare a repository owner.", nameof(request));
		string repository = source.RepositoryName ?? throw new ArgumentException(
			"A GitHub release source must declare a repository name.", nameof(request));
		HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
		// A repair asks for one exact previously installed revision; ordinary discovery asks GitHub which
		// release is current. Neither trusts tag ordering to decide what may be activated.
		Uri metadataUri = request.ExactRevision is { Length: > 0 } exactRevision
			? BuildApiUri(apiBase, owner, repository, $"releases/tags/{Uri.EscapeDataString(exactRevision)}")
			: BuildApiUri(apiBase, owner, repository, "releases/latest");
		bool isDiscovery = request.ExactRevision is not { Length: > 0 };
		ReleaseMetadataResponse metadata = ReadReleaseMetadata(
			client,
			metadataUri,
			isDiscovery ? request.CatalogFingerprint : null,
			cancellationToken);
		if (metadata.NotModified) {
			return NoCandidate(metadata.EntityTag ?? request.CatalogFingerprint);
		}
		string fingerprint = metadata.EntityTag ?? request.CatalogFingerprint;
		SelectedReleaseAsset selection;
		try {
			selection = SelectAsset(metadata.Payload, source, apiBase);
		} catch (InvalidDataException exception) {
			// Metadata that does not describe the expected artifact is a publisher problem, not a
			// candidate to reject by revision: there is nothing to record as tried.
			return Failed(exception.Message, fingerprint);
		}
		if (request.RejectedRevisions.Contains(selection.Revision)
				|| string.Equals(selection.Revision, request.ActiveRevision, StringComparison.Ordinal)) {
			return NoCandidate(fingerprint);
		}
		byte[] assetBytes = DownloadAsset(client, selection, apiBase, cancellationToken);
		string actualDigest = Convert.ToHexString(SHA256.HashData(assetBytes)).ToLowerInvariant();
		if (!string.Equals(actualDigest, selection.Digest, StringComparison.Ordinal)) {
			return new KnowledgeTransportResult(
				KnowledgeTransportStatus.Rejected,
				selection.Revision,
				null,
				null,
				fingerprint,
				Diagnostic: "The downloaded release asset does not match the digest GitHub published for it.");
		}
		return new KnowledgeTransportResult(
			KnowledgeTransportStatus.Downloaded,
			selection.Revision,
			assetBytes,
			null,
			fingerprint,
			ResolvedTag: selection.Revision,
			Diagnostic: selection.Immutable
				? null
				: "The release is not marked immutable; its assets could still be replaced upstream.");
	}

	private static Uri BuildApiUri(Uri apiBase, string owner, string repository, string relativePath) =>
		new(KnowledgeTransportHttp.EnsureTrailingSlash(apiBase),
			$"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/{relativePath}");

	private static ReleaseMetadataResponse ReadReleaseMetadata(
		HttpClient client,
		Uri metadataUri,
		string? entityTag,
		CancellationToken cancellationToken) {
		using HttpRequestMessage request = new(HttpMethod.Get, metadataUri);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
		request.Headers.TryAddWithoutValidation(ApiVersionHeader, ApiVersion);
		if (IsUsableEntityTag(entityTag)) {
			request.Headers.TryAddWithoutValidation("If-None-Match", entityTag);
		}
		using HttpResponseMessage response = client.Send(
			request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		if (response.StatusCode == HttpStatusCode.NotModified) {
			return new ReleaseMetadataResponse(null, ReadEntityTag(response) ?? entityTag, NotModified: true);
		}
		if (IsRateLimited(response)) {
			// GitHub answers a throttled client with the same status as a forbidden one. Retrying either
			// case immediately would deepen the throttle, so the caller is told to stop for now.
			throw new HttpRequestException(
				"GitHub refused the release metadata request (rate limit or access); no retry was attempted.");
		}
		response.EnsureSuccessStatusCode();
		using JsonDocument document = JsonDocument.Parse(
			KnowledgeTransportHttp.ReadBounded(response.Content, MaxReleaseMetadataBytes, cancellationToken));
		return new ReleaseMetadataResponse(
			document.RootElement.Clone(),
			ReadEntityTag(response),
			NotModified: false);
	}

	/// <summary>
	/// Picks the one asset the source declares, refusing anything the contract does not describe exactly.
	/// </summary>
	private static SelectedReleaseAsset SelectAsset(
		JsonElement? payload,
		KnowledgeSourceConfiguration source,
		Uri apiBase) {
		if (payload is not { ValueKind: JsonValueKind.Object } release) {
			throw new InvalidDataException("The GitHub release response is not a release object.");
		}
		if (ReadBoolean(release, "draft") || ReadBoolean(release, "prerelease")) {
			throw new InvalidDataException("The selected GitHub release is a draft or prerelease.");
		}
		string tag = ReadString(release, "tag_name")
			?? throw new InvalidDataException("The GitHub release does not declare a tag.");
		if (!ReleaseTagPattern.IsMatch(tag)) {
			throw new InvalidDataException(
				"A knowledge release tag must be an exact MAJOR.MINOR.PATCH library version.");
		}
		JsonElement selected = SelectDeclaredAsset(release, source);
		return DescribeAsset(selected, release, tag, apiBase);
	}

	/// <summary>
	/// Returns the single asset the source declares, refusing a release that does not expose exactly one.
	/// </summary>
	private static JsonElement SelectDeclaredAsset(JsonElement release, KnowledgeSourceConfiguration source) {
		if (!release.TryGetProperty("assets", out JsonElement assets)
				|| assets.ValueKind != JsonValueKind.Array
				|| assets.GetArrayLength() > MaxAssetEntries) {
			throw new InvalidDataException("The GitHub release does not expose a bounded asset list.");
		}
		JsonElement[] matches = assets.EnumerateArray()
			.Where(asset => asset.ValueKind == JsonValueKind.Object
				&& string.Equals(ReadString(asset, "name"), source.AssetName, StringComparison.Ordinal))
			.ToArray();
		if (matches.Length != 1) {
			throw new InvalidDataException(
				$"The GitHub release must expose exactly one '{source.AssetName}' asset.");
		}
		return matches[0];
	}

	/// <summary>
	/// Validates the selected asset's state, type, size, digest, and download host.
	/// </summary>
	private static SelectedReleaseAsset DescribeAsset(
		JsonElement selected,
		JsonElement release,
		string tag,
		Uri apiBase) {
		if (!string.Equals(ReadString(selected, "state"), "uploaded", StringComparison.Ordinal)) {
			throw new InvalidDataException("The selected release asset is not in the uploaded state.");
		}
		string? contentType = ReadString(selected, "content_type");
		if (contentType is null || !AllowedAssetContentTypes.Contains(contentType)) {
			throw new InvalidDataException("The selected release asset does not declare an archive content type.");
		}
		if (!selected.TryGetProperty("size", out JsonElement size)
				|| size.ValueKind != JsonValueKind.Number
				|| !size.TryGetInt64(out long assetSize)
				|| assetSize <= 0
				|| assetSize > MaxAssetBytes) {
			throw new InvalidDataException(
				$"The selected release asset size is missing or exceeds the {MaxAssetBytes}-byte limit.");
		}
		string digest = ReadString(selected, "digest") is { } rawDigest
				&& rawDigest.StartsWith(DigestPrefix, StringComparison.Ordinal)
				&& Sha256DigestPattern.IsMatch(rawDigest[DigestPrefix.Length..])
			? rawDigest[DigestPrefix.Length..]
			: throw new InvalidDataException(
				"The selected release asset does not publish a well-formed SHA-256 digest.");
		Uri downloadUri = ReadAbsoluteUri(selected, "url")
			?? throw new InvalidDataException("The selected release asset does not declare a download URL.");
		if (!IsAllowedDownloadUri(downloadUri, apiBase)) {
			throw new InvalidDataException("The selected release asset download URL is not an allowed GitHub host.");
		}
		return new SelectedReleaseAsset(tag, downloadUri, digest, assetSize, ReadBoolean(release, "immutable"));
	}

	private static byte[] DownloadAsset(
		HttpClient client,
		SelectedReleaseAsset selection,
		Uri apiBase,
		CancellationToken cancellationToken) {
		Uri current = selection.DownloadUri;
		for (int hop = 0; hop <= MaxRedirects; hop++) {
			using HttpRequestMessage request = new(HttpMethod.Get, current);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
			request.Headers.TryAddWithoutValidation(ApiVersionHeader, ApiVersion);
			using HttpResponseMessage response = client.Send(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			if (IsRedirect(response)) {
				current = ResolveRedirect(current, response, apiBase);
				continue;
			}
			if (IsRateLimited(response)) {
				throw new HttpRequestException(
					"GitHub refused the release asset download (rate limit or access); no retry was attempted.");
			}
			response.EnsureSuccessStatusCode();
			if (response.Content.Headers.ContentLength is long length && length != selection.Size) {
				throw new InvalidDataException("The release asset transfer length differs from its published size.");
			}
			return KnowledgeTransportHttp.ReadBounded(response.Content, (int)selection.Size, cancellationToken);
		}
		throw new InvalidDataException("The release asset download exceeded the allowed number of redirects.");
	}

	private static Uri ResolveRedirect(Uri current, HttpResponseMessage response, Uri apiBase) {
		Uri? location = response.Headers.Location;
		if (location is null) {
			throw new InvalidDataException("A release asset redirect carried no location.");
		}
		Uri resolved = location.IsAbsoluteUri ? location : new Uri(current, location);
		if (!IsAllowedDownloadUri(resolved, apiBase)) {
			// Covers scheme downgrade, credentials in the URL, and any host outside the documented set.
			throw new InvalidDataException("A release asset redirect targeted a host or scheme that is not allowed.");
		}
		return resolved;
	}

	/// <summary>
	/// Reports whether a download target is acceptable for the configured API origin.
	/// </summary>
	/// <remarks>
	/// Loopback and public topologies are kept strictly separate. A loopback API origin — used by the
	/// hermetic tests — accepts only that exact origin, and a public origin accepts only the documented
	/// GitHub hosts over HTTPS.
	/// </remarks>
	private static bool IsAllowedDownloadUri(Uri candidate, Uri apiBase) {
		if (!string.IsNullOrEmpty(candidate.UserInfo)) {
			return false;
		}
		if (apiBase.IsLoopback) {
			return candidate.IsLoopback
				&& string.Equals(candidate.Scheme, apiBase.Scheme, StringComparison.OrdinalIgnoreCase)
				&& candidate.Port == apiBase.Port;
		}
		return candidate.Scheme == Uri.UriSchemeHttps && AllowedDownloadHosts.Contains(candidate.IdnHost);
	}

	private static bool IsRedirect(HttpResponseMessage response) =>
		response.StatusCode is HttpStatusCode.MovedPermanently
			or HttpStatusCode.Found
			or HttpStatusCode.SeeOther
			or HttpStatusCode.TemporaryRedirect
			or HttpStatusCode.PermanentRedirect;

	private static bool IsRateLimited(HttpResponseMessage response) =>
		response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests;

	private static string? ReadEntityTag(HttpResponseMessage response) {
		string? tag = response.Headers.ETag?.ToString();
		return IsUsableEntityTag(tag) ? tag : null;
	}

	private static bool IsUsableEntityTag(string? entityTag) =>
		!string.IsNullOrWhiteSpace(entityTag)
		&& entityTag.Length <= MaxEntityTagLength
		&& !entityTag.Any(char.IsControl);

	private static bool ReadBoolean(JsonElement element, string propertyName) =>
		element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.True;

	private static string? ReadString(JsonElement element, string propertyName) =>
		element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static Uri? ReadAbsoluteUri(JsonElement element, string propertyName) =>
		Uri.TryCreate(ReadString(element, propertyName), UriKind.Absolute, out Uri? uri) ? uri : null;

	private static bool TryParseTag(string? value, out Version version) {
		version = new Version(0, 0, 0);
		if (value is null || value.Length > 32 || !ReleaseTagPattern.IsMatch(value)) {
			return false;
		}
		string[] parts = value.Split('.');
		version = new Version(
			int.Parse(parts[0], CultureInfo.InvariantCulture),
			int.Parse(parts[1], CultureInfo.InvariantCulture),
			int.Parse(parts[2], CultureInfo.InvariantCulture));
		return true;
	}

	private static string Redact(string message) => SensitiveErrorTextRedactor.Redact(message);

	private static KnowledgeTransportResult NoCandidate(string? catalogFingerprint) =>
		new(KnowledgeTransportStatus.NoCandidate, null, null, null, catalogFingerprint);

	private static KnowledgeTransportResult Failed(string diagnostic, string? catalogFingerprint) =>
		new(KnowledgeTransportStatus.Failed, null, null, null, catalogFingerprint, Diagnostic: diagnostic);

	private sealed record ReleaseMetadataResponse(JsonElement? Payload, string? EntityTag, bool NotModified);

	private sealed record SelectedReleaseAsset(
		string Revision,
		Uri DownloadUri,
		string Digest,
		long Size,
		bool Immutable);
}
