using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeBundleRuntime {
	KnowledgeBundleValidationResult Validate(
		Stream candidate,
		string? expectedBundleVersion = null,
		string? expectedLibraryId = null);

	KnowledgeBundleActivationResult Activate(Stream candidate, string? expectedBundleVersion = null);

	KnowledgeBundleActivationResult ActivateLibrary(
		string sourceAlias,
		int priority,
		KnowledgeSourceParticipation participation,
		Stream candidate,
		string? expectedBundleVersion = null,
		string? expectedLibraryId = null,
		string? localRootPath = null);

	KnowledgeBundleActivationResult ActivateGitRepository(
		string sourceAlias,
		int priority,
		KnowledgeSourceParticipation participation,
		KnowledgeGitRepositorySnapshot snapshot);

	void Deactivate();

	void DeactivateLibrary(string sourceAlias);

	void SetTopicPins(IReadOnlyDictionary<string, string> topicPins);

	/// <summary>
	/// Resolves a name or URI against the active knowledge set.
	/// </summary>
	/// <param name="name">A namespaced URI, a legacy URI, a topic id, or an item id.</param>
	/// <param name="isEligible">
	/// An optional eligibility predicate applied before priority, pin, and ambiguity resolution.
	/// See <see cref="IKnowledgeResolver.Find"/> for the exact ordering guarantee.
	/// </param>
	/// <returns>The lookup outcome.</returns>
	KnowledgeArticleLookup Find(string name, Func<KnowledgeArticle, bool>? isEligible = null);

	/// <summary>
	/// Lists the resolvable guidance names.
	/// </summary>
	/// <param name="isEligible">The optional eligibility predicate; see <see cref="Find"/>.</param>
	/// <returns>The distinct, ordered set of names.</returns>
	IReadOnlyList<string> GetNames(Func<KnowledgeArticle, bool>? isEligible = null);

	IReadOnlyList<KnowledgeRoleArticle> GetArticlesByRole(string role);

	ulong? ActiveSequence { get; }

	/// <summary>
	/// An opaque token identifying the currently active knowledge set.
	/// </summary>
	/// <remarks>
	/// Callers that cache anything derived from the active set compare this token by reference and
	/// rebuild when it differs. It is the identity of the active set itself, which every mutation
	/// replaces wholesale, so a new write path cannot forget to invalidate dependent caches.
	/// </remarks>
	object SnapshotToken { get; }
}

internal sealed record KnowledgeBundleValidationResult(
	KnowledgeBundleActivationStatus Status,
	KnowledgeBundleRejectionCode RejectionCode,
	ulong? CandidateSequence,
	string? Diagnostic,
	string? CandidateLibraryId = null,
	string? CandidateLibraryVersion = null,
	string? BundleDigest = null,
	string? SourceCommit = null);

internal interface IKnowledgeBundleTrustStore {
	bool TryGetPublicKeyPem(string keyId, out string publicKeyPem);

	bool TryGetPublicKeyPem(string libraryId, string keyId, out string publicKeyPem);
}

/// <summary>
/// The environment-variable trust store, as its own contract.
/// </summary>
/// <remarks>
/// It is a distinct interface rather than a second <see cref="IKnowledgeBundleTrustStore"/>
/// registration so that the configured store can depend on the legacy store without the
/// registration becoming ambiguous, and so that tests can substitute one without the other.
/// </remarks>
internal interface IEnvironmentKnowledgeBundleTrustStore : IKnowledgeBundleTrustStore;

/// <summary>
/// The publisher trust Clio pins in its own binary for the built-in curated knowledge library.
/// </summary>
/// <remarks>
/// The built-in source has no operator-supplied key file: it is delivered as a signed GitHub Release
/// asset and its trust anchor ships with Clio. Keeping it behind its own contract lets the composed
/// store consult it before any configured material, so a settings entry can never substitute a key
/// for the built-in library.
/// </remarks>
internal interface IBuiltInKnowledgeBundleTrustStore : IKnowledgeBundleTrustStore;

/// <summary>
/// Serves the compiled-in P-256 public keys authorized to sign the built-in curated knowledge library.
/// </summary>
internal sealed class BuiltInKnowledgeBundleTrustStore : IBuiltInKnowledgeBundleTrustStore {

	/// <summary>
	/// The key identifier the producer stamps into the bundle manifest signature block.
	/// </summary>
	/// <remarks>
	/// Rotation is additive: publish the successor key here alongside the incumbent, ship that Clio
	/// release, and only then start signing with the successor. Removing a key is a separate, later
	/// change, once no supported Clio still needs to verify a bundle signed with it.
	/// </remarks>
	internal const string PrimaryKeyId = "clio-knowledge-2026-08";

	private const string PrimaryPublicKeyPem = """
		-----BEGIN PUBLIC KEY-----
		MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEC3j3ZzoiZ8ZwxCnsKt1+ep949n7P
		1rMyfF0ND3Amxam+UQIvtBbpfy7Q+c1Q+ZG2G4A0AkA3R3ttZ8sDMBI3Ng==
		-----END PUBLIC KEY-----
		""";

	private static readonly IReadOnlyDictionary<string, string> TrustedKeys =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			[PrimaryKeyId] = PrimaryPublicKeyPem
		};

	/// <summary>
	/// The library whose signatures this pinned trust covers.
	/// </summary>
	internal static string TrustedLibraryId => CuratedKnowledgeSourceDefaults.LibraryId;

	/// <summary>
	/// Always refuses: the library-agnostic overload cannot prove the caller is verifying the built-in
	/// library, and answering it would let any library reuse the pinned key by naming its key ID.
	/// </summary>
	public bool TryGetPublicKeyPem(string keyId, out string publicKeyPem) {
		publicKeyPem = string.Empty;
		return false;
	}

	/// <inheritdoc/>
	public bool TryGetPublicKeyPem(string libraryId, string keyId, out string publicKeyPem) {
		publicKeyPem = string.Empty;
		if (!string.Equals(libraryId, TrustedLibraryId, StringComparison.Ordinal)
				|| keyId is null
				|| !TrustedKeys.TryGetValue(keyId, out string? pem)) {
			return false;
		}
		publicKeyPem = pem;
		return true;
	}
}

internal interface IKnowledgeTrustFingerprintService {
	bool TryGetFingerprint(string trustedPublicKeyPath, out string fingerprint);
}

internal sealed class EnvironmentKnowledgeBundleTrustStore : IEnvironmentKnowledgeBundleTrustStore {
	private const int MaxPublicKeyBytes = 16 * 1024;
	private const string P256Oid = "1.2.840.10045.3.1.7";
	internal const string KeyIdVariable = "CLIO_KNOWLEDGE_TRUSTED_KEY_ID";
	internal const string PublicKeyPathVariable = "CLIO_KNOWLEDGE_TRUSTED_PUBLIC_KEY_PATH";

	public bool TryGetPublicKeyPem(string keyId, out string publicKeyPem) {
		publicKeyPem = string.Empty;
		string? trustedKeyId = Environment.GetEnvironmentVariable(KeyIdVariable);
		string? publicKeyPath = Environment.GetEnvironmentVariable(PublicKeyPathVariable);
		if (!string.Equals(keyId, trustedKeyId, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(publicKeyPath)
				|| !Path.IsPathFullyQualified(publicKeyPath)) {
			return false;
		}
		return TryReadPublicKeyFile(publicKeyPath, out publicKeyPem);
	}

	public bool TryGetPublicKeyPem(string libraryId, string keyId, out string publicKeyPem) {
		publicKeyPem = string.Empty;
		return false;
	}

	internal static bool TryReadPublicKeyFile(string publicKeyPath, out string publicKeyPem) =>
		TryReadPublicKeyFile(publicKeyPath, out publicKeyPem, out _);

	internal static bool TryReadPublicKeyFile(
		string publicKeyPath,
		out string publicKeyPem,
		out byte[] subjectPublicKeyInfo) {
		publicKeyPem = string.Empty;
		subjectPublicKeyInfo = [];
		try {
			if (!TryNormalizeLocalPublicKeyPath(publicKeyPath, requireExisting: true, out string normalizedPath)) {
				return false;
			}
			using FileStream input = new(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (input.Length == 0 || input.Length > MaxPublicKeyBytes) {
				return false;
			}
			using StreamReader reader = new(
				input,
				new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
				detectEncodingFromByteOrderMarks: false);
			publicKeyPem = reader.ReadToEnd();
			if (!TryReadSinglePublicKey(publicKeyPem, out subjectPublicKeyInfo)) {
				publicKeyPem = string.Empty;
				return false;
			}
			using ECDsa verifier = ECDsa.Create();
			verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
			ECParameters parameters = verifier.ExportParameters(includePrivateParameters: false);
			if (bytesRead != subjectPublicKeyInfo.Length
					|| !string.Equals(parameters.Curve.Oid.Value, P256Oid, StringComparison.Ordinal)) {
				publicKeyPem = string.Empty;
				subjectPublicKeyInfo = [];
				return false;
			}
			return true;
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or System.Security.SecurityException
				or ArgumentException
				or NotSupportedException
				or CryptographicException) {
			publicKeyPem = string.Empty;
			subjectPublicKeyInfo = [];
			return false;
		}
	}

	internal static bool TryNormalizeLocalPublicKeyPath(
		string? publicKeyPath,
		bool requireExisting,
		out string normalizedPath) {
		normalizedPath = string.Empty;
		try {
			if (string.IsNullOrWhiteSpace(publicKeyPath)) {
				return false;
			}
			string candidate = publicKeyPath.Trim();
			if (!Path.IsPathFullyQualified(candidate)
					|| (OperatingSystem.IsWindows() && candidate.StartsWith(@"\\", StringComparison.Ordinal))) {
				return false;
			}
			normalizedPath = Path.GetFullPath(candidate);
			if (OperatingSystem.IsWindows()) {
				string? root = Path.GetPathRoot(normalizedPath);
				if (string.IsNullOrEmpty(root)
						|| new DriveInfo(root).DriveType is DriveType.Network or DriveType.NoRootDirectory) {
					normalizedPath = string.Empty;
					return false;
				}
			}
			if (HasReparsePointInExistingAncestry(normalizedPath)) {
				normalizedPath = string.Empty;
				return false;
			}
			if (!requireExisting) {
				return true;
			}
			if (!File.Exists(normalizedPath)) {
				normalizedPath = string.Empty;
				return false;
			}
			FileAttributes attributes = File.GetAttributes(normalizedPath);
			return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
		} catch (Exception exception) when (exception is ArgumentException
				or IOException
				or NotSupportedException
				or System.Security.SecurityException
				or UnauthorizedAccessException) {
			normalizedPath = string.Empty;
			return false;
		}
	}

	private static bool HasReparsePointInExistingAncestry(string path) {
		string? current = path;
		while (!string.IsNullOrEmpty(current)) {
			if ((File.Exists(current) || Directory.Exists(current))
					&& (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) {
				return true;
			}
			current = Path.GetDirectoryName(current);
		}
		return false;
	}

	private static bool TryReadSinglePublicKey(string pem, out byte[] subjectPublicKeyInfo) {
		subjectPublicKeyInfo = [];
		if (!PemEncoding.TryFind(pem, out PemFields fields)
				|| !pem[fields.Label].Equals("PUBLIC KEY", StringComparison.Ordinal)
				|| !string.IsNullOrWhiteSpace(pem[..fields.Location.Start.Value])
				|| !string.IsNullOrWhiteSpace(pem[fields.Location.End.Value..])) {
			return false;
		}
		try {
			subjectPublicKeyInfo = Convert.FromBase64String(pem[fields.Base64Data]);
			return subjectPublicKeyInfo.Length > 0;
		} catch (FormatException) {
			return false;
		}
	}
}

internal sealed class KnowledgeTrustFingerprintService : IKnowledgeTrustFingerprintService {
	public bool TryGetFingerprint(string trustedPublicKeyPath, out string fingerprint) {
		fingerprint = string.Empty;
		if (!EnvironmentKnowledgeBundleTrustStore.TryReadPublicKeyFile(
				trustedPublicKeyPath,
				out _,
				out byte[] subjectPublicKeyInfo)) {
			return false;
		}
		fingerprint = Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo));
		return true;
	}
}

internal sealed class ConfiguredKnowledgeBundleTrustStore : IKnowledgeBundleTrustStore {
	private readonly IKnowledgeRuntimeConfigurationProvider _configurationProvider;
	private readonly IEnvironmentKnowledgeBundleTrustStore _legacyTrustStore;
	private readonly IBuiltInKnowledgeBundleTrustStore _builtInTrustStore;

	public ConfiguredKnowledgeBundleTrustStore(
		IKnowledgeRuntimeConfigurationProvider configurationProvider,
		IEnvironmentKnowledgeBundleTrustStore legacyTrustStore,
		IBuiltInKnowledgeBundleTrustStore builtInTrustStore) {
		_configurationProvider = configurationProvider
			?? throw new ArgumentNullException(nameof(configurationProvider));
		_legacyTrustStore = legacyTrustStore ?? throw new ArgumentNullException(nameof(legacyTrustStore));
		_builtInTrustStore = builtInTrustStore ?? throw new ArgumentNullException(nameof(builtInTrustStore));
	}

	public bool TryGetPublicKeyPem(string keyId, out string publicKeyPem) =>
		_legacyTrustStore.TryGetPublicKeyPem(keyId, out publicKeyPem);

	public bool TryGetPublicKeyPem(string libraryId, string keyId, out string publicKeyPem) {
		// Pinned trust is consulted first and is not overridable: a configured entry claiming the
		// built-in library must not be able to swap in its own signing key.
		if (_builtInTrustStore.TryGetPublicKeyPem(libraryId, keyId, out publicKeyPem)) {
			return true;
		}
		if (string.Equals(libraryId, BuiltInKnowledgeBundleTrustStore.TrustedLibraryId, StringComparison.Ordinal)) {
			publicKeyPem = string.Empty;
			return false;
		}
		publicKeyPem = string.Empty;
		try {
			KnowledgeSourceConfiguration? source = _configurationProvider.GetCurrent().Sources.Values
				.SingleOrDefault(candidate => string.Equals(
					candidate.LibraryId,
					libraryId,
					StringComparison.Ordinal));
			return source is not null
				&& string.Equals(source.TrustedKeyId, keyId, StringComparison.Ordinal)
				&& EnvironmentKnowledgeBundleTrustStore.TryReadPublicKeyFile(
					source.TrustedPublicKeyPath,
					out publicKeyPem);
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or ArgumentException
				or InvalidOperationException
				or Newtonsoft.Json.JsonException) {
			publicKeyPem = string.Empty;
			return false;
		}
	}
}

internal interface IKnowledgeBundleActivator {
	void EnsureActivated();

	string? LastDiagnostic { get; }
}

internal sealed record KnowledgeBundlePackageConfiguration(string Source, string PackageId);

internal sealed record KnowledgeBundlePackageCatalogResult(
	bool IsAvailable,
	string? LatestVersion,
	string? Diagnostic = null);

internal enum KnowledgeBundlePackageDownloadStatus {
	NoCandidate,
	Failed,
	Rejected,
	Downloaded
}

internal sealed record KnowledgeBundlePackageDownloadResult(
	KnowledgeBundlePackageDownloadStatus Status,
	string? PackageVersion,
	byte[]? BundleBytes,
	string? CatalogFingerprint = null,
	string? Diagnostic = null);

internal sealed record KnowledgeBundleNuGetOptions(int TransportDeadlineMilliseconds);

/// <summary>
/// Bounds one GitHub Release retrieval, covering metadata, redirects, download, and verification.
/// </summary>
/// <param name="TransportDeadlineMilliseconds">The absolute wall-clock ceiling for one retrieval.</param>
internal sealed record KnowledgeGitHubReleaseOptions(int TransportDeadlineMilliseconds);

internal sealed record KnowledgeBundleActivationOptions(int FailureRetryMilliseconds);

internal sealed class NoOpKnowledgeBundleActivator : IKnowledgeBundleActivator {
	public string? LastDiagnostic => null;

	public void EnsureActivated() {
	}
}

internal sealed class UnavailableKnowledgeBundleRuntime : IKnowledgeBundleRuntime {
	public ulong? ActiveSequence => null;

	public KnowledgeBundleValidationResult Validate(
		Stream candidate,
		string? expectedBundleVersion = null,
		string? expectedLibraryId = null) =>
		new(KnowledgeBundleActivationStatus.Rejected, KnowledgeBundleRejectionCode.Malformed, null,
			"No knowledge bundle runtime is configured.");

	public KnowledgeBundleActivationResult Activate(Stream candidate, string? expectedBundleVersion = null) =>
		throw new NotSupportedException();

	public KnowledgeBundleActivationResult ActivateLibrary(
		string sourceAlias,
		int priority,
		KnowledgeSourceParticipation participation,
		Stream candidate,
		string? expectedBundleVersion = null,
		string? expectedLibraryId = null,
		string? localRootPath = null) => throw new NotSupportedException();

	public KnowledgeBundleActivationResult ActivateGitRepository(
		string sourceAlias,
		int priority,
		KnowledgeSourceParticipation participation,
		KnowledgeGitRepositorySnapshot snapshot) => throw new NotSupportedException();

	public void Deactivate() {
	}

	public void DeactivateLibrary(string sourceAlias) {
	}

	public void SetTopicPins(IReadOnlyDictionary<string, string> topicPins) {
	}

	public KnowledgeArticleLookup Find(string name, Func<KnowledgeArticle, bool>? isEligible = null) =>
		new(KnowledgeArticleLookupStatus.Unavailable, null, null);

	public IReadOnlyList<string> GetNames(Func<KnowledgeArticle, bool>? isEligible = null) =>
		Array.Empty<string>();

	public IReadOnlyList<KnowledgeRoleArticle> GetArticlesByRole(string role) =>
		Array.Empty<KnowledgeRoleArticle>();

	// A single shared token: nothing ever activates here, so every caller's cache stays valid.
	public object SnapshotToken { get; } = new();
}

internal sealed record KnowledgeBundleClientCapabilities(
	Version ClioVersion,
	Version McpToolContractVersion,
	IReadOnlySet<string> Tools,
	// LOCAL DEV TOGGLE (off by default): when true, a Git knowledge bundle whose manifest omits the
	// explicit "sequence" field is accepted by synthesizing the sequence from libraryVersion. Sourced
	// from the 'knowledge-allow-unsequenced' feature flag in the clio config.
	bool AllowUnsequencedGitBundles = false);

internal enum KnowledgeBundleActivationStatus {
	Activated,
	Rejected
}

internal enum KnowledgeBundleRejectionCode {
	None,
	Malformed,
	UnsupportedContract,
	UntrustedKey,
	InvalidSignature,
	Incompatible,
	MissingCapability,
	InvalidContent,
	SequenceNotForward
}

internal sealed record KnowledgeBundleActivationResult(
	KnowledgeBundleActivationStatus Status,
	KnowledgeBundleRejectionCode RejectionCode,
	ulong? CandidateSequence,
	ulong? ActiveSequence,
	string? Diagnostic);

internal enum KnowledgeArticleLookupStatus {
	Active,
	NotFound,
	Unavailable,
	Ambiguous
}

internal sealed record KnowledgeArticle(
	string Name,
	string Uri,
	string Text,
	string LibraryId = "com.creatio.clio",
	string ItemId = "",
	string TopicId = "",
	string Role = "guidance",
	string? LocalPath = null,
	IReadOnlyList<string>? LegacyUris = null,
	string Title = "",
	string Description = "",
	string MediaType = "text/plain",
	IReadOnlyList<string>? RequiredFeatures = null) {
	internal const string DefaultRole = "guidance";
}

internal sealed record KnowledgeGuidanceDescriptor(
	string Name,
	string Title,
	string Description,
	string Uri,
	string MediaType);

internal sealed record KnowledgeArticleProvenance(
	string SourceAlias,
	string LibraryId,
	string LibraryVersion,
	string ItemId,
	string TopicId,
	ulong Sequence,
	string BundleDigest,
	string? LocalPath);

internal sealed record KnowledgeArticleLookup(
	KnowledgeArticleLookupStatus Status,
	KnowledgeArticle? Article,
	ulong? ActiveSequence,
	KnowledgeArticleProvenance? Provenance = null,
	string? Diagnostic = null);

internal sealed record KnowledgeRoleArticle(
	KnowledgeArticle Article,
	KnowledgeArticleProvenance Provenance,
	int Priority,
	KnowledgeSourceParticipation Participation);
