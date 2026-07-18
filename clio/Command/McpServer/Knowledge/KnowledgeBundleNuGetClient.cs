using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.McpServer.Knowledge;

internal sealed class KnowledgeBundleNuGetClient : IKnowledgeBundlePackageClient {
	internal const string HttpClientName = "knowledge-bundle-nuget";
	internal const string SourceVariable = "CLIO_KNOWLEDGE_NUGET_SOURCE";
	internal const string PackageIdVariable = "CLIO_KNOWLEDGE_NUGET_PACKAGE_ID";
	internal const string InnerBundlePath = "content/knowledge-bundle.zip";

	private const int MaxServiceIndexBytes = 1024 * 1024;
	private const int MaxVersionIndexBytes = 1024 * 1024;
	private const int MaxVersionEntries = 4096;
	private const int MaxPackageBytes = 48 * 1024 * 1024;
	private const int MaxInnerBundleBytes = 40 * 1024 * 1024;
	private const int MaxPackageEntries = 256;
	private const int MaxPackageCentralDirectoryBytes = 2 * 1024 * 1024;
	private const uint EndOfCentralDirectorySignature = 0x06054b50;

	private static readonly Regex PackageIdPattern = new(
		"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));
	private static readonly Regex StableVersionPattern = new(
		"^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly KnowledgeBundleNuGetOptions _options;

	public KnowledgeBundleNuGetClient(
		IHttpClientFactory httpClientFactory,
		KnowledgeBundleNuGetOptions options) {
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.TransportDeadlineMilliseconds);
	}

	public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SourceVariable));

	public KnowledgeBundlePackageDownloadResult DownloadNext(
		IReadOnlySet<string> rejectedPackageVersions,
		string? activePackageVersion,
		string? highestObservedPackageVersion,
		string? fallbackCeilingPackageVersion,
		string? catalogFingerprint) {
		ArgumentNullException.ThrowIfNull(rejectedPackageVersions);
		using CancellationTokenSource deadline = new(_options.TransportDeadlineMilliseconds);
		HttpClient client;
		Uri packageBaseAddress;
		string packageId;
		IReadOnlyList<StablePackageVersion> versions;
		try {
			if (!TryReadConfiguration(out Uri source, out packageId)) {
				return NoCandidate();
			}
			client = _httpClientFactory.CreateClient(HttpClientName);
			packageBaseAddress = DiscoverPackageBaseAddress(client, source, deadline.Token);
			versions = ReadVersions(client, packageBaseAddress, packageId, deadline.Token);
		} catch (Exception exception) when (exception is HttpRequestException
				or OperationCanceledException
				or IOException
				or InvalidDataException
				or JsonException
				or RegexMatchTimeoutException
				or ArgumentException
				or NotSupportedException) {
			return NoCandidate();
		}
		StablePackageVersion? floor = StablePackageVersion.TryParse(
			activePackageVersion,
			out StablePackageVersion parsedFloor)
			? parsedFloor
			: null;
		string currentCatalogFingerprint = ComputeCatalogFingerprint(versions);
		bool catalogChanged = catalogFingerprint is not null
			&& !string.Equals(catalogFingerprint, currentCatalogFingerprint, StringComparison.Ordinal);
		IEnumerable<StablePackageVersion> eligible = versions
			.Where(version => !rejectedPackageVersions.Contains(version.ToString()))
			.Where(version => floor is null || version.CompareTo(floor.Value) > 0);
		StablePackageVersion? highestObserved = StablePackageVersion.TryParse(
			catalogChanged ? null : highestObservedPackageVersion,
			out StablePackageVersion parsedHighestObserved)
			? parsedHighestObserved
			: null;
		StablePackageVersion? fallbackCeiling = StablePackageVersion.TryParse(
			catalogChanged ? null : fallbackCeilingPackageVersion,
			out StablePackageVersion parsedFallbackCeiling)
			? parsedFallbackCeiling
			: null;
		StablePackageVersion? latest = eligible
			.Where(version => highestObserved is null || version.CompareTo(highestObserved.Value) > 0)
			.Cast<StablePackageVersion?>()
			.Max();
		if (latest is null && fallbackCeiling is not null) {
			latest = eligible
				.Where(version => version.CompareTo(fallbackCeiling.Value) < 0)
				.Cast<StablePackageVersion?>()
				.Max();
		}
		if (latest is null) {
			return NoCandidate(currentCatalogFingerprint);
		}
		string selectedVersion = latest.Value.ToString();
		byte[] packageBytes;
		try {
			packageBytes = DownloadPackage(
				client,
				packageBaseAddress,
				packageId,
				selectedVersion,
				deadline.Token);
		} catch (Exception exception) when (exception is HttpRequestException
				or OperationCanceledException
				or IOException) {
			return NoCandidate(currentCatalogFingerprint);
		} catch (InvalidDataException) {
			return Rejected(selectedVersion, currentCatalogFingerprint);
		}
		try {
			byte[] bundleBytes = ExtractInnerBundle(packageBytes);
			return new KnowledgeBundlePackageDownloadResult(
				KnowledgeBundlePackageDownloadStatus.Downloaded,
				selectedVersion,
				bundleBytes,
				currentCatalogFingerprint);
		} catch (Exception exception) when (exception is InvalidDataException
				or IOException
				or ArgumentException
				or NotSupportedException) {
			return Rejected(selectedVersion, currentCatalogFingerprint);
		}
	}

	private static KnowledgeBundlePackageDownloadResult NoCandidate(string? catalogFingerprint = null) =>
		new(KnowledgeBundlePackageDownloadStatus.NoCandidate, null, null, catalogFingerprint);

	private static KnowledgeBundlePackageDownloadResult Rejected(
		string packageVersion,
		string catalogFingerprint) =>
		new(KnowledgeBundlePackageDownloadStatus.Rejected, packageVersion, null, catalogFingerprint);

	internal static bool IsVersionGreaterThan(string candidate, string floor) =>
		StablePackageVersion.TryParse(candidate, out StablePackageVersion candidateVersion)
		&& StablePackageVersion.TryParse(floor, out StablePackageVersion floorVersion)
		&& candidateVersion.CompareTo(floorVersion) > 0;

	internal static string? GreaterVersion(string? left, string? right) {
		if (!StablePackageVersion.TryParse(left, out StablePackageVersion leftVersion)) {
			return StablePackageVersion.TryParse(right, out _) ? right : null;
		}
		if (!StablePackageVersion.TryParse(right, out StablePackageVersion rightVersion)) {
			return left;
		}
		return leftVersion.CompareTo(rightVersion) >= 0 ? left : right;
	}

	private static string ComputeCatalogFingerprint(IEnumerable<StablePackageVersion> versions) {
		string canonicalVersions = string.Join(
			'\n',
			versions.OrderBy(version => version).Select(version => version.ToString()));
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalVersions)));
	}

	private static bool TryReadConfiguration(out Uri source, out string packageId) {
		string? sourceValue = Environment.GetEnvironmentVariable(SourceVariable);
		packageId = Environment.GetEnvironmentVariable(PackageIdVariable) ?? string.Empty;
		if (!Uri.TryCreate(sourceValue, UriKind.Absolute, out source!)
				|| !IsAllowedFeedUri(source)
				|| !string.IsNullOrEmpty(source.UserInfo)
				|| packageId.Length > 100
				|| !PackageIdPattern.IsMatch(packageId)) {
			return false;
		}
		return true;
	}

	private static Uri DiscoverPackageBaseAddress(
		HttpClient client,
		Uri source,
		CancellationToken cancellationToken) {
		using HttpRequestMessage request = new(HttpMethod.Get, source);
		using HttpResponseMessage response = client.Send(
			request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		response.EnsureSuccessStatusCode();
		using JsonDocument document = JsonDocument.Parse(
			ReadBounded(response.Content, MaxServiceIndexBytes, cancellationToken));
		if (!document.RootElement.TryGetProperty("resources", out JsonElement resources)
				|| resources.ValueKind != JsonValueKind.Array) {
			throw new InvalidDataException("NuGet service index has no resources array.");
		}
		foreach (JsonElement resource in resources.EnumerateArray()) {
			if (!resource.TryGetProperty("@id", out JsonElement id)
					|| id.ValueKind != JsonValueKind.String
					|| !resource.TryGetProperty("@type", out JsonElement type)
					|| !IsPackageBaseAddress(type)
					|| !Uri.TryCreate(id.GetString(), UriKind.Absolute, out Uri? baseAddress)
					|| !IsAllowedFeedUri(baseAddress)
					|| !IsSameOrigin(source, baseAddress)) {
				continue;
			}
			return EnsureTrailingSlash(baseAddress);
		}
		throw new InvalidDataException("NuGet service index has no PackageBaseAddress resource.");
	}

	private static bool IsPackageBaseAddress(JsonElement type) {
		if (type.ValueKind == JsonValueKind.String) {
			return type.GetString()?.StartsWith("PackageBaseAddress/", StringComparison.Ordinal) == true;
		}
		return type.ValueKind == JsonValueKind.Array
			&& type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String
				&& item.GetString()?.StartsWith("PackageBaseAddress/", StringComparison.Ordinal) == true);
	}

	private static IReadOnlyList<StablePackageVersion> ReadVersions(
		HttpClient client,
		Uri packageBaseAddress,
		string packageId,
		CancellationToken cancellationToken) {
		string normalizedId = packageId.ToLowerInvariant();
		Uri versionsUri = new(packageBaseAddress, $"{normalizedId}/index.json");
		using HttpRequestMessage request = new(HttpMethod.Get, versionsUri);
		using HttpResponseMessage response = client.Send(
			request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		response.EnsureSuccessStatusCode();
		using JsonDocument document = JsonDocument.Parse(
			ReadBounded(response.Content, MaxVersionIndexBytes, cancellationToken));
		if (!document.RootElement.TryGetProperty("versions", out JsonElement versions)
				|| versions.ValueKind != JsonValueKind.Array) {
			throw new InvalidDataException("NuGet flat-container index has no versions array.");
		}
		if (versions.GetArrayLength() > MaxVersionEntries) {
			throw new InvalidDataException("NuGet flat-container index contains too many versions.");
		}
		return versions.EnumerateArray()
			.Where(value => value.ValueKind == JsonValueKind.String)
			.Select(value => value.GetString())
			.Select(value => StablePackageVersion.TryParse(value, out StablePackageVersion version)
				? (StablePackageVersion?)version
				: null)
			.Where(version => version.HasValue)
			.Select(version => version!.Value)
			.Distinct()
			.ToArray();
	}

	private static byte[] DownloadPackage(
		HttpClient client,
		Uri packageBaseAddress,
		string packageId,
		string version,
		CancellationToken cancellationToken) {
		string normalizedId = packageId.ToLowerInvariant();
		Uri packageUri = new(
			packageBaseAddress,
			$"{normalizedId}/{version}/{normalizedId}.{version}.nupkg");
		using HttpRequestMessage request = new(HttpMethod.Get, packageUri);
		using HttpResponseMessage response = client.Send(
			request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		response.EnsureSuccessStatusCode();
		return ReadBounded(response.Content, MaxPackageBytes, cancellationToken);
	}

	private static byte[] ExtractInnerBundle(byte[] packageBytes) {
		ValidatePackageCentralDirectory(packageBytes);
		using MemoryStream input = new(packageBytes, writable: false);
		using ZipArchive package = new(input, ZipArchiveMode.Read);
		if (package.Entries.Count > MaxPackageEntries) {
			throw new InvalidDataException("NuGet package contains too many entries.");
		}
		ZipArchiveEntry[] matches = package.Entries
			.Where(entry => string.Equals(entry.FullName, InnerBundlePath, StringComparison.Ordinal))
			.ToArray();
		if (matches.Length != 1 || matches[0].Length <= 0 || matches[0].Length > MaxInnerBundleBytes) {
			throw new InvalidDataException("NuGet package must contain exactly one bounded knowledge bundle.");
		}
		byte[] bundleBytes = GC.AllocateUninitializedArray<byte>(checked((int)matches[0].Length));
		using Stream bundle = matches[0].Open();
		bundle.ReadExactly(bundleBytes);
		return bundleBytes;
	}

	private static void ValidatePackageCentralDirectory(byte[] packageBytes) {
		const int minimumRecordBytes = 22;
		int tailLength = Math.Min(packageBytes.Length, minimumRecordBytes + ushort.MaxValue);
		if (tailLength < minimumRecordBytes) {
			throw new InvalidDataException("NuGet package is missing its central directory.");
		}
		ReadOnlySpan<byte> tail = packageBytes.AsSpan(packageBytes.Length - tailLength);
		for (int index = tail.Length - minimumRecordBytes; index >= 0; index--) {
			ReadOnlySpan<byte> record = tail[index..];
			if (BinaryPrimitives.ReadUInt32LittleEndian(record) != EndOfCentralDirectorySignature) {
				continue;
			}
			ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
			if (index + minimumRecordBytes + commentLength != tail.Length) {
				continue;
			}
			ushort diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
			ushort directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
			ushort entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
			ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
			uint directorySize = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
			uint directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
			long recordOffset = packageBytes.Length - tailLength + index;
			if (diskNumber != 0
					|| directoryDisk != 0
					|| entriesOnDisk != totalEntries
					|| totalEntries == ushort.MaxValue
					|| directorySize == uint.MaxValue
					|| directoryOffset == uint.MaxValue
					|| totalEntries > MaxPackageEntries
					|| directorySize > MaxPackageCentralDirectoryBytes
					|| (long)directoryOffset + directorySize != recordOffset) {
				throw new InvalidDataException("NuGet package central directory exceeds supported bounds.");
			}
			return;
		}
		throw new InvalidDataException("NuGet package is missing its central directory.");
	}

	private static byte[] ReadBounded(
		HttpContent content,
		int maximumBytes,
		CancellationToken cancellationToken) {
		if (content.Headers.ContentLength is long length && (length < 0 || length > maximumBytes)) {
			throw new InvalidDataException($"HTTP content exceeds the {maximumBytes}-byte limit.");
		}
		using Stream stream = content.ReadAsStream(cancellationToken);
		int initialCapacity = content.Headers.ContentLength is long contentLength
			? checked((int)contentLength)
			: 0;
		return ReadBounded(stream, maximumBytes, initialCapacity, cancellationToken);
	}

	private static byte[] ReadBounded(
		Stream stream,
		int maximumBytes,
		int initialCapacity,
		CancellationToken cancellationToken) {
		using MemoryStream output = new(initialCapacity);
		byte[] buffer = new byte[81920];
		int read;
		while ((read = stream.ReadAsync(buffer, cancellationToken).AsTask().GetAwaiter().GetResult()) > 0) {
			if (output.Length + read > maximumBytes) {
				throw new InvalidDataException($"Content exceeds the {maximumBytes}-byte limit.");
			}
			output.Write(buffer, 0, read);
		}
		return output.Length == output.Capacity
			? output.GetBuffer()
			: output.ToArray();
	}

	private static Uri EnsureTrailingSlash(Uri uri) =>
		uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
			? uri
			: new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

	private static bool IsAllowedFeedUri(Uri uri) =>
		uri.Scheme == Uri.UriSchemeHttps
		|| (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

	private static bool IsSameOrigin(Uri source, Uri candidate) =>
		string.Equals(source.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
		&& string.Equals(source.IdnHost, candidate.IdnHost, StringComparison.OrdinalIgnoreCase)
		&& source.Port == candidate.Port;

	private readonly record struct StablePackageVersion(int Major, int Minor, int Patch)
		: IComparable<StablePackageVersion> {
		public int CompareTo(StablePackageVersion other) {
			int major = Major.CompareTo(other.Major);
			if (major != 0) {
				return major;
			}
			int minor = Minor.CompareTo(other.Minor);
			return minor != 0 ? minor : Patch.CompareTo(other.Patch);
		}

		public override string ToString() => $"{Major}.{Minor}.{Patch}";

		internal static bool TryParse(string? value, out StablePackageVersion version) {
			version = default;
			if (value is null || value.Length > 32 || !StableVersionPattern.IsMatch(value)) {
				return false;
			}
			string[] parts = value.Split('.');
			if (!int.TryParse(parts[0], out int major)
					|| !int.TryParse(parts[1], out int minor)
					|| !int.TryParse(parts[2], out int patch)) {
				return false;
			}
			version = new StablePackageVersion(major, minor, patch);
			return true;
		}
	}
}
