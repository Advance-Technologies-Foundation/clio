using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Clio.Command.McpServer.Knowledge;

internal sealed class KnowledgeBundleRuntime : IKnowledgeBundleRuntime {
	private const int MaxManifestBytes = 1024 * 1024;
	private const int MaxSignatureBytes = 1024;
	private const int MaxResourceBytes = 4 * 1024 * 1024;
	private const int MaxBundleResourceBytes = 32 * 1024 * 1024;
	private const int MaxArchiveBytes = 40 * 1024 * 1024;
	private const int MaxArchiveEntries = 1024;
	private const int MaxCentralDirectoryBytes = 2 * 1024 * 1024;
	private const uint EndOfCentralDirectorySignature = 0x06054b50;
	private const string ContractVersion = "0.1.0";
	private const string SignatureAlgorithm = "ECDSA-P256-SHA256";
	private const string DigestAlgorithm = "SHA-256";

	private static readonly Regex VersionPattern = new(
		"^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
		RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));
	private static readonly UTF8Encoding StrictUtf8 = new(false, true);

	private readonly object _activationLock = new();
	private readonly IKnowledgeBundleTrustStore _trustStore;
	private readonly KnowledgeBundleClientCapabilities _capabilities;
	private ActiveKnowledgeBundle? _active;

	public KnowledgeBundleRuntime(
		IKnowledgeBundleTrustStore trustStore,
		KnowledgeBundleClientCapabilities capabilities) {
		_trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
		_capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
	}

	public ulong? ActiveSequence => Volatile.Read(ref _active)?.Sequence;

	public KnowledgeBundleValidationResult Validate(Stream candidate, string? expectedBundleVersion = null) {
		ArgumentNullException.ThrowIfNull(candidate);
		try {
			PreparedKnowledgeBundle prepared = Prepare(candidate, expectedBundleVersion);
			return new KnowledgeBundleValidationResult(
				KnowledgeBundleActivationStatus.Activated,
				KnowledgeBundleRejectionCode.None,
				prepared.Sequence,
				null);
		} catch (KnowledgeBundleRejectedException exception) {
			return ValidationRejected(exception.Code, exception.CandidateSequence, exception.Message);
		} catch (Exception exception) when (exception is InvalidDataException
				or IOException
				or JsonException
				or CryptographicException
				or DecoderFallbackException
				or RegexMatchTimeoutException) {
			return ValidationRejected(KnowledgeBundleRejectionCode.Malformed, null, exception.Message);
		}
	}

	public KnowledgeBundleActivationResult Activate(Stream candidate, string? expectedBundleVersion = null) {
		ArgumentNullException.ThrowIfNull(candidate);
		lock (_activationLock) {
			try {
				PreparedKnowledgeBundle prepared = Prepare(candidate, expectedBundleVersion);
				ActiveKnowledgeBundle? active = Volatile.Read(ref _active);
				if (active is not null && prepared.Sequence <= active.Sequence) {
					return Rejected(
						KnowledgeBundleRejectionCode.SequenceNotForward,
						prepared.Sequence,
						$"Candidate sequence {prepared.Sequence} must be greater than active sequence {active.Sequence}.");
				}
				ActiveKnowledgeBundle activated = new(prepared.Sequence, prepared.Articles);
				Interlocked.Exchange(ref _active, activated);
				return new KnowledgeBundleActivationResult(
					KnowledgeBundleActivationStatus.Activated,
					KnowledgeBundleRejectionCode.None,
					prepared.Sequence,
					prepared.Sequence,
					null);
			} catch (KnowledgeBundleRejectedException exception) {
				return Rejected(exception.Code, exception.CandidateSequence, exception.Message);
			} catch (Exception exception) when (exception is InvalidDataException
					or IOException
					or JsonException
					or CryptographicException
					or DecoderFallbackException
					or RegexMatchTimeoutException) {
				return Rejected(KnowledgeBundleRejectionCode.Malformed, null, exception.Message);
			}
		}
	}

	public void Deactivate() => Interlocked.Exchange(ref _active, null);

	public KnowledgeArticleLookup Find(string name) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ActiveKnowledgeBundle? active = Volatile.Read(ref _active);
		if (active is null) {
			return new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Unavailable, null, null);
		}
		return active.Articles.TryGetValue(name, out KnowledgeArticle? article)
			? new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Active, article, active.Sequence)
			: new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.NotFound, null, active.Sequence);
	}

	private PreparedKnowledgeBundle Prepare(Stream candidate, string? expectedBundleVersion) {
		using MemoryStream boundedCandidate = ReadBoundedCandidate(candidate);
		ValidateCentralDirectory(boundedCandidate);
		using ZipArchive archive = new(boundedCandidate, ZipArchiveMode.Read, leaveOpen: true);
		Dictionary<string, ZipArchiveEntry> entries = ReadEntryIndex(archive);
		byte[] manifestBytes = ReadRequiredEntry(entries, "manifest.json", MaxManifestBytes);
		RejectDuplicateJsonProperties(manifestBytes);
		KnowledgeBundleManifestDto manifest = JsonSerializer.Deserialize(
			manifestBytes,
			KnowledgeBundleJsonContext.Default.KnowledgeBundleManifestDto)
			?? throw new KnowledgeBundleRejectedException(
				KnowledgeBundleRejectionCode.Malformed,
				null,
				"Bundle manifest is empty.");
		ValidateManifestEnvelope(manifest);
		VerifyManifestSignature(manifest, manifestBytes, entries);
		if (expectedBundleVersion is not null
				&& !string.Equals(manifest.BundleVersion, expectedBundleVersion, StringComparison.Ordinal)) {
			throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
				"Signed bundle version does not match its immutable package version.");
		}
		ValidateCompatibility(manifest);
		ValidateRequirements(manifest);
		IReadOnlyDictionary<string, KnowledgeArticle> articles = ReadAndValidateResources(manifest, entries);
		return new PreparedKnowledgeBundle(manifest.Sequence, articles);
	}

	private static MemoryStream ReadBoundedCandidate(Stream candidate) {
		int capacity = 0;
		if (candidate.CanSeek) {
			long remaining = candidate.Length - candidate.Position;
			if (remaining < 0 || remaining > MaxArchiveBytes) {
				throw new KnowledgeBundleRejectedException(
					KnowledgeBundleRejectionCode.InvalidContent,
					null,
					$"Bundle archive exceeds the {MaxArchiveBytes}-byte compressed size limit.");
			}
			capacity = checked((int)remaining);
			if (candidate is MemoryStream memory
					&& memory.TryGetBuffer(out ArraySegment<byte> segment)) {
				return new MemoryStream(
					segment.Array!,
					checked(segment.Offset + (int)candidate.Position),
					capacity,
					writable: false,
					publiclyVisible: true);
			}
		}
		MemoryStream output = new(capacity);
		byte[] buffer = new byte[81920];
		int read;
		while ((read = candidate.Read(buffer, 0, buffer.Length)) > 0) {
			if (output.Length + read > MaxArchiveBytes) {
				output.Dispose();
				throw new KnowledgeBundleRejectedException(
					KnowledgeBundleRejectionCode.InvalidContent,
					null,
					$"Bundle archive exceeds the {MaxArchiveBytes}-byte compressed size limit.");
			}
			output.Write(buffer, 0, read);
		}
		output.Position = 0;
		return output;
	}

	private static void ValidateCentralDirectory(MemoryStream candidate) {
		const int minimumRecordBytes = 22;
		int tailLength = checked((int)Math.Min(candidate.Length, minimumRecordBytes + ushort.MaxValue));
		if (tailLength < minimumRecordBytes) {
			throw new KnowledgeBundleRejectedException(
				KnowledgeBundleRejectionCode.InvalidContent, null, "Bundle archive is missing its central directory.");
		}
		byte[] tail = new byte[tailLength];
		candidate.Position = candidate.Length - tailLength;
		int offset = 0;
		while (offset < tail.Length) {
			int read = candidate.Read(tail, offset, tail.Length - offset);
			if (read == 0) {
				throw new KnowledgeBundleRejectedException(
					KnowledgeBundleRejectionCode.InvalidContent, null, "Bundle archive central directory is truncated.");
			}
			offset += read;
		}
		for (int index = tail.Length - minimumRecordBytes; index >= 0; index--) {
			ReadOnlySpan<byte> record = tail.AsSpan(index);
			if (BinaryPrimitives.ReadUInt32LittleEndian(record) != EndOfCentralDirectorySignature) {
				continue;
			}
			ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
			if (index + minimumRecordBytes + commentLength != tail.Length) {
				continue;
			}
			ushort diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
			ushort centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
			ushort entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
			ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
			uint centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
			uint centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
			long recordOffset = candidate.Length - tailLength + index;
			if (diskNumber != 0
					|| centralDirectoryDisk != 0
					|| entriesOnDisk != totalEntries
					|| totalEntries == ushort.MaxValue
					|| centralDirectorySize == uint.MaxValue
					|| centralDirectoryOffset == uint.MaxValue
					|| totalEntries > MaxArchiveEntries
					|| centralDirectorySize > MaxCentralDirectoryBytes
					|| (long)centralDirectoryOffset + centralDirectorySize != recordOffset) {
				throw new KnowledgeBundleRejectedException(
					KnowledgeBundleRejectionCode.InvalidContent,
					null,
					"Bundle archive central directory exceeds the supported v0 bounds.");
			}
			candidate.Position = 0;
			return;
		}
		throw new KnowledgeBundleRejectedException(
			KnowledgeBundleRejectionCode.InvalidContent, null, "Bundle archive is missing its central directory.");
	}

	private static void RejectDuplicateJsonProperties(ReadOnlySpan<byte> json) {
		Utf8JsonReader reader = new(json, new JsonReaderOptions {
			AllowTrailingCommas = false,
			CommentHandling = JsonCommentHandling.Disallow
		});
		Stack<HashSet<string>> objectProperties = new();
		while (reader.Read()) {
			switch (reader.TokenType) {
				case JsonTokenType.StartObject:
					objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
					break;
				case JsonTokenType.EndObject:
					objectProperties.Pop();
					break;
				case JsonTokenType.PropertyName:
					string propertyName = reader.GetString()!;
					if (!objectProperties.Peek().Add(propertyName)) {
						throw new KnowledgeBundleRejectedException(
							KnowledgeBundleRejectionCode.Malformed,
							null,
							$"Bundle manifest contains duplicate JSON property '{propertyName}'.");
					}
					break;
			}
		}
	}

	private static Dictionary<string, ZipArchiveEntry> ReadEntryIndex(ZipArchive archive) {
		if (archive.Entries.Count > MaxArchiveEntries) {
			throw new KnowledgeBundleRejectedException(
				KnowledgeBundleRejectionCode.InvalidContent,
				null,
				"Bundle contains too many archive entries.");
		}
		Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
		foreach (ZipArchiveEntry entry in archive.Entries) {
			if (string.IsNullOrWhiteSpace(entry.FullName)
					|| entry.FullName.EndsWith("/", StringComparison.Ordinal)
					|| entry.FullName.Contains('\\')
					|| !entries.TryAdd(entry.FullName, entry)) {
				throw new KnowledgeBundleRejectedException(
					KnowledgeBundleRejectionCode.InvalidContent,
					null,
					$"Bundle contains an invalid or duplicate entry '{entry.FullName}'.");
			}
		}
		return entries;
	}

	private static void ValidateManifestEnvelope(KnowledgeBundleManifestDto manifest) {
		if (!string.Equals(manifest.ContractVersion, ContractVersion, StringComparison.Ordinal)
				|| !string.Equals(manifest.BundleSchemaVersion, ContractVersion, StringComparison.Ordinal)) {
			throw Reject(KnowledgeBundleRejectionCode.UnsupportedContract, manifest.Sequence,
				$"Only bundle contract and schema {ContractVersion} are supported.");
		}
		if (manifest.Sequence == 0
				|| string.IsNullOrWhiteSpace(manifest.BundleVersion)
				|| manifest.IssuedAt == default
				|| manifest.Source is null
				|| string.IsNullOrWhiteSpace(manifest.Source.Repository)
				|| string.IsNullOrWhiteSpace(manifest.Source.Commit)
				|| manifest.Signature is null
				|| !string.Equals(manifest.Signature.Algorithm, SignatureAlgorithm, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(manifest.Signature.KeyId)
				|| !string.Equals(manifest.DigestAlg, DigestAlgorithm, StringComparison.Ordinal)
				|| manifest.Compatibility is null
				|| manifest.Requirements is null
				|| manifest.Resources is null
				|| manifest.Resources.Count == 0
				|| manifest.Resources.Any(resource => resource is null)) {
			throw Reject(KnowledgeBundleRejectionCode.Malformed, manifest.Sequence,
				"Bundle manifest is missing required v0 values.");
		}
	}

	private void VerifyManifestSignature(
		KnowledgeBundleManifestDto manifest,
		byte[] manifestBytes,
		IReadOnlyDictionary<string, ZipArchiveEntry> entries) {
		if (!_trustStore.TryGetPublicKeyPem(manifest.Signature.KeyId, out string? publicKeyPem)
				|| string.IsNullOrWhiteSpace(publicKeyPem)) {
			throw Reject(KnowledgeBundleRejectionCode.UntrustedKey, manifest.Sequence,
				$"Bundle signing key '{manifest.Signature.KeyId}' is not trusted.");
		}
		byte[] signature = ReadRequiredEntry(entries, "manifest.sig", MaxSignatureBytes);
		try {
			using ECDsa verifier = ECDsa.Create();
			verifier.ImportFromPem(publicKeyPem);
			ECParameters parameters = verifier.ExportParameters(includePrivateParameters: false);
			if (!string.Equals(parameters.Curve.Oid.Value, ECCurve.NamedCurves.nistP256.Oid.Value,
					StringComparison.Ordinal)
					|| !verifier.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256)) {
				throw Reject(KnowledgeBundleRejectionCode.InvalidSignature, manifest.Sequence,
					"Bundle manifest signature is invalid.");
			}
		} catch (Exception exception) when (exception is ArgumentException or CryptographicException) {
			throw Reject(KnowledgeBundleRejectionCode.UntrustedKey, manifest.Sequence,
				"Bundle trust material is not a supported ECDSA P-256 public key.");
		}
	}

	private void ValidateCompatibility(KnowledgeBundleManifestDto manifest) {
		if (!IsCompatible(manifest.Compatibility.Clio, _capabilities.ClioVersion)
				|| !IsCompatible(manifest.Compatibility.McpToolContract, _capabilities.McpToolContractVersion)) {
			throw Reject(KnowledgeBundleRejectionCode.Incompatible, manifest.Sequence,
				"Bundle compatibility ranges do not include this Clio runtime.");
		}
	}

	private void ValidateRequirements(KnowledgeBundleManifestDto manifest) {
		if (manifest.Requirements.Tools is null
				|| manifest.Requirements.GuidanceIds is null
				|| manifest.Requirements.ResourceUris is null
				|| manifest.Requirements.Tools.Any(tool => !_capabilities.Tools.Contains(tool))) {
			throw Reject(KnowledgeBundleRejectionCode.MissingCapability, manifest.Sequence,
				"Bundle requires an MCP tool capability that is not available.");
		}
		EnsureUnique(manifest.Requirements.Tools, "required tool", manifest.Sequence);
		EnsureUnique(manifest.Requirements.GuidanceIds, "required guidance id", manifest.Sequence);
		EnsureUnique(manifest.Requirements.ResourceUris, "required resource URI", manifest.Sequence);
	}

	private IReadOnlyDictionary<string, KnowledgeArticle> ReadAndValidateResources(
		KnowledgeBundleManifestDto manifest,
		IReadOnlyDictionary<string, ZipArchiveEntry> entries) {
		EnsureUnique(manifest.Resources.Select(resource => resource.Id), "resource id", manifest.Sequence);
		EnsureUnique(manifest.Resources.Select(resource => resource.Uri), "resource URI", manifest.Sequence);
		EnsureUnique(manifest.Resources.Select(resource => resource.Path), "resource path", manifest.Sequence);
		HashSet<string> ids = manifest.Resources.Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
		HashSet<string> uris = manifest.Resources.Select(resource => resource.Uri).ToHashSet(StringComparer.Ordinal);
		if (!ids.SetEquals(manifest.Requirements.GuidanceIds)
				|| !uris.SetEquals(manifest.Requirements.ResourceUris)
				|| !ids.SetEquals(_capabilities.GuidanceResources.Keys)) {
			throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
				"Declared requirements and bundle resources must exactly match the stable Clio guidance catalog.");
		}

		HashSet<string> expectedEntries = new(StringComparer.Ordinal) { "manifest.json", "manifest.sig" };
		Dictionary<string, KnowledgeArticle> articles = new(StringComparer.Ordinal);
		long totalLength = 0;
		foreach (KnowledgeBundleResourceDto resource in manifest.Resources) {
			ValidateResourceDescriptor(resource, manifest.Sequence);
			if (!_capabilities.GuidanceResources.TryGetValue(resource.Id, out string? expectedUri)
					|| !string.Equals(resource.Uri, expectedUri, StringComparison.Ordinal)) {
				throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
					$"Resource '{resource.Id}' does not match the stable Clio guidance catalog URI.");
			}
			expectedEntries.Add(resource.Path);
			byte[] bytes = ReadRequiredEntry(entries, resource.Path, MaxResourceBytes);
			totalLength = checked(totalLength + bytes.LongLength);
			if (totalLength > MaxBundleResourceBytes
					|| resource.Length != bytes.LongLength
					|| !string.Equals(resource.Digest,
						Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), StringComparison.Ordinal)) {
				throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
					$"Resource '{resource.Id}' failed length or digest validation.");
			}
			string text = StrictUtf8.GetString(bytes);
			articles.Add(resource.Id, new KnowledgeArticle(resource.Id, resource.Uri, text));
		}
		if (!expectedEntries.SetEquals(entries.Keys)) {
			throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
				"Bundle contains missing or unexpected entries.");
		}
		return articles;
	}

	private static void ValidateResourceDescriptor(KnowledgeBundleResourceDto resource, ulong sequence) {
		if (string.IsNullOrWhiteSpace(resource.Id)
				|| string.IsNullOrWhiteSpace(resource.Uri)
				|| !resource.Uri.StartsWith("docs://", StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(resource.Path)
				|| !resource.Path.StartsWith("resources/", StringComparison.Ordinal)
				|| resource.Path.Contains("..", StringComparison.Ordinal)
				|| resource.Path.Contains('\\')
				|| string.IsNullOrWhiteSpace(resource.MediaType)
				|| !resource.MediaType.StartsWith("text/", StringComparison.Ordinal)
				|| resource.Length < 0
				|| string.IsNullOrWhiteSpace(resource.Digest)
				|| resource.Digest.Length != 64
				|| resource.Digest.Any(character => !Uri.IsHexDigit(character))) {
			throw Reject(KnowledgeBundleRejectionCode.InvalidContent, sequence,
				$"Resource '{resource.Id}' has an invalid v0 descriptor.");
		}
	}

	private static bool IsCompatible(KnowledgeBundleVersionRangeDto range, Version current) {
		if (range is null
				|| !TryParseExactVersion(range.Min, out Version? min)
				|| !TryParseExactVersion(range.Max, out Version? max)
				|| min > max) {
			return false;
		}
		Version normalizedCurrent = new(
			current.Major,
			current.Minor,
			Math.Max(current.Build, 0));
		return normalizedCurrent >= min && normalizedCurrent <= max;
	}

	private static bool TryParseExactVersion(string value, out Version? version) {
		version = null;
		return value is not null
			&& value.Length <= 32
			&& VersionPattern.IsMatch(value)
			&& Version.TryParse(value, out version);
	}

	private static void EnsureUnique(IEnumerable<string> values, string label, ulong sequence) {
		HashSet<string> unique = new(StringComparer.Ordinal);
		foreach (string value in values) {
			if (string.IsNullOrWhiteSpace(value) || !unique.Add(value)) {
				throw Reject(KnowledgeBundleRejectionCode.InvalidContent, sequence,
					$"Every {label} must be non-empty and unique.");
			}
		}
	}

	private static byte[] ReadRequiredEntry(
		IReadOnlyDictionary<string, ZipArchiveEntry> entries,
		string path,
		int maximumBytes) {
		if (!entries.TryGetValue(path, out ZipArchiveEntry? entry) || entry.Length > maximumBytes) {
			throw new KnowledgeBundleRejectedException(
				KnowledgeBundleRejectionCode.InvalidContent,
				null,
				$"Required bundle entry '{path}' is missing or too large.");
		}
		using Stream input = entry.Open();
		using MemoryStream output = new(capacity: checked((int)entry.Length));
		byte[] buffer = new byte[81920];
		int read;
		while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
			if (output.Length + read > maximumBytes) {
				throw new KnowledgeBundleRejectedException(
					KnowledgeBundleRejectionCode.InvalidContent,
					null,
					$"Bundle entry '{path}' exceeds its size limit.");
			}
			output.Write(buffer, 0, read);
		}
		return output.ToArray();
	}

	private KnowledgeBundleActivationResult Rejected(
		KnowledgeBundleRejectionCode code,
		ulong? candidateSequence,
		string diagnostic) => new(
		KnowledgeBundleActivationStatus.Rejected,
		code,
		candidateSequence,
		ActiveSequence,
		diagnostic);

	private static KnowledgeBundleValidationResult ValidationRejected(
		KnowledgeBundleRejectionCode code,
		ulong? candidateSequence,
		string diagnostic) => new(
		KnowledgeBundleActivationStatus.Rejected,
		code,
		candidateSequence,
		diagnostic);

	private static KnowledgeBundleRejectedException Reject(
		KnowledgeBundleRejectionCode code,
		ulong? candidateSequence,
		string message) => new(code, candidateSequence, message);

	private sealed record PreparedKnowledgeBundle(
		ulong Sequence,
		IReadOnlyDictionary<string, KnowledgeArticle> Articles);

	private sealed record ActiveKnowledgeBundle(
		ulong Sequence,
		IReadOnlyDictionary<string, KnowledgeArticle> Articles);

	[SuppressMessage(
		"Design",
		"S3871:Exception types should be public",
		Justification = "This private exception is an internal non-escaping control-flow sentinel mapped to a typed activation result.")]
	private sealed class KnowledgeBundleRejectedException(
		KnowledgeBundleRejectionCode code,
		ulong? candidateSequence,
		string message) : Exception(message) {
		internal KnowledgeBundleRejectionCode Code { get; } = code;
		internal ulong? CandidateSequence { get; } = candidateSequence;
	}
}
