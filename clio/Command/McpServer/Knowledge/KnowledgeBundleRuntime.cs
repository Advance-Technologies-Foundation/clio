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
	private const string LegacyContractVersion = "0.1.0";
	private const string MultiSourceContractVersion = "1.0.0";
	private const string LegacyLibraryId = "com.creatio.clio";
	private const string LegacySourceAlias = "creatio";
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
	private readonly IKnowledgeResolver _resolver;
	private ActiveKnowledgeSet _active = ActiveKnowledgeSet.Empty;

	public KnowledgeBundleRuntime(
		IKnowledgeBundleTrustStore trustStore,
		KnowledgeBundleClientCapabilities capabilities,
		IKnowledgeResolver resolver) {
		_trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
		_capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
		_resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
	}

	public ulong? ActiveSequence {
		get {
			ActiveKnowledgeSet active = Volatile.Read(ref _active);
			return active.Libraries.Count == 0 ? null : active.Libraries.Max(library => library.Sequence);
		}
	}

	public KnowledgeBundleValidationResult Validate(
		Stream candidate,
		string? expectedBundleVersion = null,
		string? expectedLibraryId = null) {
		ArgumentNullException.ThrowIfNull(candidate);
		try {
			PreparedKnowledgeBundle prepared = Prepare(candidate, expectedBundleVersion, expectedLibraryId);
			return new KnowledgeBundleValidationResult(
				KnowledgeBundleActivationStatus.Activated,
				KnowledgeBundleRejectionCode.None,
				prepared.Sequence,
				null,
				prepared.LibraryId,
				prepared.LibraryVersion,
				prepared.BundleDigest,
				prepared.SourceCommit);
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
		return ActivateLibraryCore(
			LegacySourceAlias,
			priority: 100,
			KnowledgeSourceParticipation.Authoritative,
			candidate,
			expectedBundleVersion,
			LegacyLibraryId,
			localRootPath: null,
			requireMultiSourceContract: false);
	}

	public KnowledgeBundleActivationResult ActivateLibrary(
		string sourceAlias,
		int priority,
		KnowledgeSourceParticipation participation,
		Stream candidate,
		string? expectedBundleVersion = null,
		string? expectedLibraryId = null,
		string? localRootPath = null) => ActivateLibraryCore(
			sourceAlias,
			priority,
			participation,
			candidate,
			expectedBundleVersion,
			expectedLibraryId,
			localRootPath,
			requireMultiSourceContract: expectedLibraryId is not null);

	private KnowledgeBundleActivationResult ActivateLibraryCore(
		string sourceAlias,
		int priority,
		KnowledgeSourceParticipation participation,
		Stream candidate,
		string? expectedBundleVersion,
		string? expectedLibraryId,
		string? localRootPath,
		bool requireMultiSourceContract) {
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);
		ArgumentNullException.ThrowIfNull(candidate);
		lock (_activationLock) {
			try {
				PreparedKnowledgeBundle prepared = Prepare(
					candidate,
					expectedBundleVersion,
					requireMultiSourceContract ? expectedLibraryId : null);
				if (expectedLibraryId is not null
						&& !string.Equals(prepared.LibraryId, expectedLibraryId, StringComparison.Ordinal)) {
					return Rejected(
						KnowledgeBundleRejectionCode.InvalidContent,
						prepared.Sequence,
						$"Candidate library '{prepared.LibraryId}' does not match configured library '{expectedLibraryId}'.");
				}
				ActiveKnowledgeSet active = Volatile.Read(ref _active);
				KnowledgeLibrarySnapshot? current = active.Libraries.SingleOrDefault(library =>
					string.Equals(library.SourceAlias, sourceAlias, StringComparison.OrdinalIgnoreCase));
				if (current is not null
						&& expectedLibraryId is not null
						&& localRootPath is not null
						&& prepared.Sequence == current.Sequence
						&& string.Equals(prepared.BundleDigest, current.BundleDigest, StringComparison.Ordinal)) {
					KnowledgeLibrarySnapshot refreshed = current with {
						Priority = priority,
						Participation = participation,
						Articles = prepared.Articles.Select(article => localRootPath is null
							? article
							: article with { LocalPath = Path.GetFullPath(Path.Combine(
								localRootPath,
								article.LocalPath ?? string.Empty)) })
							.ToArray()
					};
					KnowledgeLibrarySnapshot[] refreshedLibraries = active.Libraries
						.Select(library => string.Equals(
							library.SourceAlias,
							sourceAlias,
							StringComparison.OrdinalIgnoreCase)
							? refreshed
							: library)
						.ToArray();
					Interlocked.Exchange(ref _active, active with { Libraries = refreshedLibraries });
					return new KnowledgeBundleActivationResult(
						KnowledgeBundleActivationStatus.Activated,
						KnowledgeBundleRejectionCode.None,
						prepared.Sequence,
						prepared.Sequence,
						null);
				}
				if (current is not null && prepared.Sequence <= current.Sequence) {
					return Rejected(
						KnowledgeBundleRejectionCode.SequenceNotForward,
						prepared.Sequence,
						$"Candidate sequence {prepared.Sequence} must be greater than active sequence {current.Sequence} for source '{sourceAlias}'.");
				}
				KnowledgeLibrarySnapshot activated = new(
					sourceAlias,
					prepared.LibraryId,
					priority,
					participation,
					prepared.Sequence,
					prepared.BundleDigest,
					prepared.Articles.Select(article => localRootPath is null
						? article
						: article with { LocalPath = Path.GetFullPath(Path.Combine(localRootPath, article.LocalPath ?? string.Empty)) })
						.ToArray());
				KnowledgeLibrarySnapshot[] nextLibraries = active.Libraries
					.Where(library => !string.Equals(library.SourceAlias, sourceAlias, StringComparison.OrdinalIgnoreCase))
					.Append(activated)
					.ToArray();
				Interlocked.Exchange(ref _active, active with { Libraries = nextLibraries });
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

	public void Deactivate() => Interlocked.Exchange(ref _active, ActiveKnowledgeSet.Empty);

	public void DeactivateLibrary(string sourceAlias) {
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);
		lock (_activationLock) {
			ActiveKnowledgeSet active = Volatile.Read(ref _active);
			KnowledgeLibrarySnapshot[] libraries = active.Libraries
				.Where(library => !string.Equals(library.SourceAlias, sourceAlias, StringComparison.OrdinalIgnoreCase))
				.ToArray();
			Interlocked.Exchange(ref _active, active with { Libraries = libraries });
		}
	}

	public void SetTopicPins(IReadOnlyDictionary<string, string> topicPins) {
		ArgumentNullException.ThrowIfNull(topicPins);
		lock (_activationLock) {
			ActiveKnowledgeSet active = Volatile.Read(ref _active);
			Interlocked.Exchange(ref _active, active with {
				TopicPins = new Dictionary<string, string>(topicPins, StringComparer.Ordinal)
			});
		}
	}

	public KnowledgeArticleLookup Find(string name) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ActiveKnowledgeSet active = Volatile.Read(ref _active);
		if (active.Libraries.Count == 0) {
			return new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Unavailable, null, null);
		}
		return _resolver.Find(name, active.Libraries, active.TopicPins);
	}

	public IReadOnlyList<string> GetNames() {
		ActiveKnowledgeSet active = Volatile.Read(ref _active);
		return _resolver.GetNames(active.Libraries);
	}

	private PreparedKnowledgeBundle Prepare(
		Stream candidate,
		string? expectedBundleVersion,
		string? expectedLibraryId = null) {
		using MemoryStream boundedCandidate = ReadBoundedCandidate(candidate);
		string bundleDigest = Convert.ToHexString(SHA256.HashData(boundedCandidate.ToArray())).ToLowerInvariant();
		boundedCandidate.Position = 0;
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
		if (expectedLibraryId is not null && !IsMultiSource(manifest)) {
			throw Reject(
				KnowledgeBundleRejectionCode.UnsupportedContract,
				manifest.Sequence,
				"Configured knowledge sources require the multi-source bundle contract.");
		}
		if (expectedLibraryId is not null
				&& !string.Equals(manifest.LibraryId, expectedLibraryId, StringComparison.Ordinal)) {
			throw Reject(
				KnowledgeBundleRejectionCode.InvalidContent,
				manifest.Sequence,
				$"Candidate library '{manifest.LibraryId}' does not match configured library '{expectedLibraryId}'.");
		}
		VerifyManifestSignature(manifest, manifestBytes, entries);
		string libraryVersion = IsMultiSource(manifest) ? manifest.LibraryVersion! : manifest.BundleVersion!;
		if (expectedBundleVersion is not null
				&& !string.Equals(libraryVersion, expectedBundleVersion, StringComparison.Ordinal)) {
			throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
				"Signed bundle version does not match its immutable package version.");
		}
		ValidateCompatibility(manifest);
		ValidateRequirements(manifest);
		IReadOnlyDictionary<string, KnowledgeArticle> articles = ReadAndValidateResources(manifest, entries);
		return new PreparedKnowledgeBundle(
			IsMultiSource(manifest) ? manifest.LibraryId! : LegacyLibraryId,
			libraryVersion,
			manifest.Sequence,
			bundleDigest,
			manifest.Source.Commit,
			articles.Values.ToArray());
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
		bool legacy = string.Equals(manifest.ContractVersion, LegacyContractVersion, StringComparison.Ordinal)
			&& string.Equals(manifest.BundleSchemaVersion, LegacyContractVersion, StringComparison.Ordinal);
		bool multiSource = string.Equals(manifest.ContractVersion, MultiSourceContractVersion, StringComparison.Ordinal)
			&& string.Equals(manifest.BundleSchemaVersion, MultiSourceContractVersion, StringComparison.Ordinal);
		if (!legacy && !multiSource) {
			throw Reject(KnowledgeBundleRejectionCode.UnsupportedContract, manifest.Sequence,
				$"Only bundle contracts {LegacyContractVersion} and {MultiSourceContractVersion} are supported.");
		}
		if (manifest.Sequence == 0
				|| (legacy && string.IsNullOrWhiteSpace(manifest.BundleVersion))
				|| (multiSource && (string.IsNullOrWhiteSpace(manifest.LibraryId)
					|| string.IsNullOrWhiteSpace(manifest.LibraryVersion)))
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
				"Bundle manifest is missing required values.");
		}
		if (multiSource
				&& (manifest.Source.Commit.Length is not (40 or 64)
					|| manifest.Source.Commit.Any(character => !Uri.IsHexDigit(character)))) {
			throw Reject(KnowledgeBundleRejectionCode.Malformed, manifest.Sequence,
				"Bundle source commit must be a complete 40- or 64-character hexadecimal object ID.");
		}
	}

	private static bool IsMultiSource(KnowledgeBundleManifestDto manifest) =>
		string.Equals(manifest.ContractVersion, MultiSourceContractVersion, StringComparison.Ordinal);

	private void VerifyManifestSignature(
		KnowledgeBundleManifestDto manifest,
		byte[] manifestBytes,
		IReadOnlyDictionary<string, ZipArchiveEntry> entries) {
		string? publicKeyPem;
		bool trusted = IsMultiSource(manifest)
			? _trustStore.TryGetPublicKeyPem(manifest.LibraryId!, manifest.Signature.KeyId, out publicKeyPem)
			: _trustStore.TryGetPublicKeyPem(manifest.Signature.KeyId, out publicKeyPem);
		if (!trusted
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
				|| manifest.Requirements.ResourceUris is null
				|| (IsMultiSource(manifest)
					? manifest.Requirements.ItemIds is null
					: manifest.Requirements.GuidanceIds is null)
				|| manifest.Requirements.Tools.Any(tool => !_capabilities.Tools.Contains(tool))) {
			throw Reject(KnowledgeBundleRejectionCode.MissingCapability, manifest.Sequence,
				"Bundle requires an MCP tool capability that is not available.");
		}
		EnsureUnique(manifest.Requirements.Tools, "required tool", manifest.Sequence);
		EnsureUnique(
			IsMultiSource(manifest) ? manifest.Requirements.ItemIds! : manifest.Requirements.GuidanceIds!,
			"required item id",
			manifest.Sequence);
		EnsureUnique(manifest.Requirements.ResourceUris, "required resource URI", manifest.Sequence);
	}

	private IReadOnlyDictionary<string, KnowledgeArticle> ReadAndValidateResources(
		KnowledgeBundleManifestDto manifest,
		IReadOnlyDictionary<string, ZipArchiveEntry> entries) {
		EnsureUnique(manifest.Resources.Select(resource => ResourceId(resource, manifest)), "resource id", manifest.Sequence);
		EnsureUnique(manifest.Resources.Select(resource => resource.Uri), "resource URI", manifest.Sequence);
		EnsureUnique(manifest.Resources.Select(resource => resource.Path), "resource path", manifest.Sequence);
		if (IsMultiSource(manifest)) {
			EnsureUnique(
				manifest.Resources.Select(resource => $"{resource.TopicId}\0{resource.Role}"),
				"topic and role",
				manifest.Sequence);
			string[] legacyUris = manifest.Resources
				.SelectMany(resource => resource.LegacyUris ?? Array.Empty<string>())
				.ToArray();
			EnsureUnique(legacyUris, "legacy resource URI", manifest.Sequence);
			HashSet<string> canonicalUris = manifest.Resources
				.Select(resource => resource.Uri)
				.ToHashSet(StringComparer.Ordinal);
			if (legacyUris.Any(canonicalUris.Contains)) {
				throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
					"Legacy resource URIs must not collide with canonical resource URIs.");
			}
		}
		HashSet<string> ids = manifest.Resources
			.Select(resource => ResourceId(resource, manifest))
			.ToHashSet(StringComparer.Ordinal);
		HashSet<string> uris = manifest.Resources.Select(resource => resource.Uri).ToHashSet(StringComparer.Ordinal);
		IReadOnlyList<string> requiredIds = IsMultiSource(manifest)
			? manifest.Requirements.ItemIds!
			: manifest.Requirements.GuidanceIds!;
		if (!ids.SetEquals(requiredIds)
				|| !uris.SetEquals(manifest.Requirements.ResourceUris)
				|| (!IsMultiSource(manifest) && !ids.SetEquals(_capabilities.GuidanceResources.Keys))) {
			throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
				"Declared requirements and bundle resources must describe the same complete item set.");
		}

		HashSet<string> expectedEntries = new(StringComparer.Ordinal) { "manifest.json", "manifest.sig" };
		Dictionary<string, KnowledgeArticle> articles = new(StringComparer.Ordinal);
		long totalLength = 0;
		foreach (KnowledgeBundleResourceDto resource in manifest.Resources) {
			ValidateResourceDescriptor(manifest, resource);
			string itemId = ResourceId(resource, manifest);
			if (!IsMultiSource(manifest)
					&& (!_capabilities.GuidanceResources.TryGetValue(itemId, out string? expectedUri)
						|| !string.Equals(resource.Uri, expectedUri, StringComparison.Ordinal))) {
				throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
					$"Resource '{itemId}' does not match the stable Clio guidance catalog URI.");
			}
			expectedEntries.Add(resource.Path);
			byte[] bytes = ReadRequiredEntry(entries, resource.Path, MaxResourceBytes);
			totalLength = checked(totalLength + bytes.LongLength);
			if (totalLength > MaxBundleResourceBytes
					|| resource.Length != bytes.LongLength
					|| !string.Equals(resource.Digest,
						Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), StringComparison.Ordinal)) {
				throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
					$"Resource '{itemId}' failed length or digest validation.");
			}
			string text = StrictUtf8.GetString(bytes);
			string topicId = IsMultiSource(manifest) ? resource.TopicId! : itemId;
			string role = IsMultiSource(manifest) ? resource.Role! : KnowledgeArticle.DefaultRole;
			articles.Add(itemId, new KnowledgeArticle(
				itemId,
				resource.Uri,
				text,
				IsMultiSource(manifest) ? manifest.LibraryId! : LegacyLibraryId,
				itemId,
				topicId,
				role,
				resource.Path,
				resource.LegacyUris?.ToArray() ?? Array.Empty<string>()));
		}
		if (!expectedEntries.SetEquals(entries.Keys)) {
			throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
				"Bundle contains missing or unexpected entries.");
		}
		return articles;
	}

	private static void ValidateResourceDescriptor(
		KnowledgeBundleManifestDto manifest,
		KnowledgeBundleResourceDto resource) {
		string itemId = ResourceId(resource, manifest);
		bool multiSource = IsMultiSource(manifest);
		string canonicalUri = multiSource
			? $"{KnowledgeResolver.NamespacedUriPrefix}{Uri.EscapeDataString(manifest.LibraryId!)}/{Uri.EscapeDataString(itemId)}"
			: resource.Uri;
		if (string.IsNullOrWhiteSpace(itemId)
				|| (multiSource && (string.IsNullOrWhiteSpace(resource.TopicId)
					|| string.IsNullOrWhiteSpace(resource.Role)
					|| !string.Equals(resource.Uri, canonicalUri, StringComparison.Ordinal)))
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
			throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
				$"Resource '{itemId}' has an invalid descriptor.");
		}
		if (resource.LegacyUris is not null) {
			EnsureUnique(resource.LegacyUris, "legacy resource URI", manifest.Sequence);
			if (resource.LegacyUris.Any(uri => !uri.StartsWith("docs://", StringComparison.Ordinal))) {
				throw Reject(KnowledgeBundleRejectionCode.InvalidContent, manifest.Sequence,
					$"Resource '{itemId}' has an invalid legacy URI.");
			}
		}
	}

	private static string ResourceId(
		KnowledgeBundleResourceDto resource,
		KnowledgeBundleManifestDto manifest) =>
		IsMultiSource(manifest) ? resource.ItemId! : resource.Id!;

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
		string LibraryId,
		string LibraryVersion,
		ulong Sequence,
		string BundleDigest,
		string SourceCommit,
		IReadOnlyList<KnowledgeArticle> Articles);

	private sealed record ActiveKnowledgeSet(
		IReadOnlyList<KnowledgeLibrarySnapshot> Libraries,
		IReadOnlyDictionary<string, string> TopicPins) {
		internal static readonly ActiveKnowledgeSet Empty = new(
			Array.Empty<KnowledgeLibrarySnapshot>(),
			new Dictionary<string, string>(StringComparer.Ordinal));
	}

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
