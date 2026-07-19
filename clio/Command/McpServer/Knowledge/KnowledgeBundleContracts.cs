using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeBundleRuntime {
	KnowledgeBundleValidationResult Validate(Stream candidate, string? expectedBundleVersion = null);

	KnowledgeBundleActivationResult Activate(Stream candidate, string? expectedBundleVersion = null);

	void Deactivate();

	KnowledgeArticleLookup Find(string name);

	ulong? ActiveSequence { get; }
}

internal sealed record KnowledgeBundleValidationResult(
	KnowledgeBundleActivationStatus Status,
	KnowledgeBundleRejectionCode RejectionCode,
	ulong? CandidateSequence,
	string? Diagnostic);

internal interface IKnowledgeBundleTrustStore {
	bool TryGetPublicKeyPem(string keyId, out string publicKeyPem);
}

internal sealed class EnvironmentKnowledgeBundleTrustStore : IKnowledgeBundleTrustStore {
	private const int MaxPublicKeyBytes = 16 * 1024;
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
		try {
			using FileStream input = File.OpenRead(publicKeyPath);
			if (input.Length == 0 || input.Length > MaxPublicKeyBytes) {
				return false;
			}
			using StreamReader reader = new(
				input,
				new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
				detectEncodingFromByteOrderMarks: false);
			publicKeyPem = reader.ReadToEnd();
			if (!TryReadSinglePublicKey(publicKeyPem, out byte[] subjectPublicKeyInfo)) {
				publicKeyPem = string.Empty;
				return false;
			}
			using ECDsa verifier = ECDsa.Create();
			verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
			return bytesRead == subjectPublicKeyInfo.Length;
		} catch (IOException) {
			return false;
		} catch (UnauthorizedAccessException) {
			return false;
		} catch (ArgumentException) {
			return false;
		} catch (NotSupportedException) {
			return false;
		} catch (CryptographicException) {
			return false;
		}
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

internal interface IKnowledgeBundleActivator {
	void EnsureActivated();

	string? LastDiagnostic { get; }
}

internal interface IKnowledgeBundlePackageClient {
	bool IsConfigured { get; }

	KnowledgeBundlePackageConfiguration? GetConfiguration();

	KnowledgeBundlePackageCatalogResult GetCatalog();

	KnowledgeBundlePackageDownloadResult DownloadNext(
		IReadOnlySet<string> rejectedPackageVersions,
		string? activePackageVersion,
		string? highestObservedPackageVersion,
		string? fallbackCeilingPackageVersion,
		string? catalogFingerprint,
		int? transportDeadlineMilliseconds = null);
}

internal sealed record KnowledgeBundlePackageConfiguration(string Source, string PackageId);

internal sealed record KnowledgeBundlePackageCatalogResult(
	bool IsAvailable,
	string? LatestVersion,
	string? Diagnostic = null);

internal enum KnowledgeBundlePackageDownloadStatus {
	NoCandidate,
	Rejected,
	Downloaded
}

internal sealed record KnowledgeBundlePackageDownloadResult(
	KnowledgeBundlePackageDownloadStatus Status,
	string? PackageVersion,
	byte[]? BundleBytes,
	string? CatalogFingerprint = null);

internal sealed record KnowledgeBundleNuGetOptions(int TransportDeadlineMilliseconds);

internal sealed record KnowledgeBundleActivationOptions(int FailureRetryMilliseconds);

internal sealed class EnvironmentKnowledgeBundleActivator : IKnowledgeBundleActivator {
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IKnowledgeInstallationStore _store;
	private readonly KnowledgeBundleActivationOptions _options;
	private readonly object _activationLock = new();
	private bool _hasObservedMarker;
	private string? _observedMarkerIdentity;
	private string? _failedMarkerIdentity;
	private long _retryFailedMarkerAfter;
	public string? LastDiagnostic { get; private set; }

	public EnvironmentKnowledgeBundleActivator(
		IKnowledgeBundleRuntime runtime,
		IKnowledgeInstallationStore store,
		KnowledgeBundleActivationOptions options) {
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegative(options.FailureRetryMilliseconds);
	}

	public void EnsureActivated() {
		lock (_activationLock) {
			KnowledgeCurrentState? current = _store.ReadCurrent(out string? markerDiagnostic);
			if (markerDiagnostic is not null) {
				LastDiagnostic = markerDiagnostic;
				_runtime.Deactivate();
				_hasObservedMarker = false;
				_observedMarkerIdentity = null;
				return;
			}
			if (current is null) {
				if (!_hasObservedMarker || _observedMarkerIdentity is not null) {
					_runtime.Deactivate();
				}
				_hasObservedMarker = true;
				_observedMarkerIdentity = null;
				LastDiagnostic = null;
				return;
			}
			string markerIdentity = Identity(current.Active);
			if (_hasObservedMarker
					&& string.Equals(markerIdentity, _observedMarkerIdentity, StringComparison.Ordinal)) {
				return;
			}
			if (string.Equals(markerIdentity, _failedMarkerIdentity, StringComparison.Ordinal)
					&& Environment.TickCount64 < _retryFailedMarkerAfter) {
				return;
			}
			if (TryActivate(current.Active)) {
				_observedMarkerIdentity = markerIdentity;
				_hasObservedMarker = true;
				LastDiagnostic = null;
				_failedMarkerIdentity = null;
				return;
			}
			string? activeDiagnostic = LastDiagnostic;
			if (_runtime.ActiveSequence is null && current.Previous is not null) {
				if (TryActivate(current.Previous)) {
					LastDiagnostic = activeDiagnostic;
					RecordFailedMarker(markerIdentity);
					return;
				}
			}
			RecordFailedMarker(markerIdentity);
		}
	}

	private void RecordFailedMarker(string markerIdentity) {
		_failedMarkerIdentity = markerIdentity;
		_retryFailedMarkerAfter = Environment.TickCount64 + _options.FailureRetryMilliseconds;
	}

	private bool TryActivate(KnowledgeVersionPointer pointer) {
		if (!_store.TryReadCandidate(pointer, out InstalledKnowledgeCandidate? candidate, out string? diagnostic)) {
			LastDiagnostic = diagnostic ?? "Installed knowledge candidate could not be read.";
			return false;
		}
		using MemoryStream stream = new(candidate!.BundleBytes, writable: false);
		KnowledgeBundleValidationResult validation = _runtime.Validate(stream, pointer.PackageVersion);
		if (validation.Status != KnowledgeBundleActivationStatus.Activated
				|| validation.CandidateSequence != pointer.Sequence) {
			LastDiagnostic = validation.Diagnostic
				?? "Installed knowledge candidate does not match the activation marker sequence.";
			return false;
		}
		stream.Position = 0;
		KnowledgeBundleActivationResult activation = _runtime.Activate(stream, pointer.PackageVersion);
		bool activated = activation.Status == KnowledgeBundleActivationStatus.Activated
			&& activation.CandidateSequence == pointer.Sequence;
		LastDiagnostic = activated
			? null
			: activation.Diagnostic ?? "Installed knowledge candidate was rejected.";
		return activated;
	}

	private static string Identity(KnowledgeVersionPointer pointer) =>
		$"{pointer.PackageVersion}:{pointer.Sequence}:{pointer.BundleDigest}";
}

internal sealed class NoOpKnowledgeBundleActivator : IKnowledgeBundleActivator {
	public string? LastDiagnostic => null;

	public void EnsureActivated() {
	}
}

internal sealed class UnavailableKnowledgeBundleRuntime : IKnowledgeBundleRuntime {
	public ulong? ActiveSequence => null;

	public KnowledgeBundleValidationResult Validate(Stream candidate, string? expectedBundleVersion = null) =>
		new(KnowledgeBundleActivationStatus.Rejected, KnowledgeBundleRejectionCode.Malformed, null,
			"No knowledge bundle runtime is configured.");

	public KnowledgeBundleActivationResult Activate(Stream candidate, string? expectedBundleVersion = null) =>
		throw new NotSupportedException();

	public void Deactivate() {
	}

	public KnowledgeArticleLookup Find(string name) =>
		new(KnowledgeArticleLookupStatus.Unavailable, null, null);
}

internal sealed record KnowledgeBundleClientCapabilities(
	Version ClioVersion,
	Version McpToolContractVersion,
	IReadOnlySet<string> Tools,
	IReadOnlyDictionary<string, string> GuidanceResources);

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
	Unavailable
}

internal sealed record KnowledgeArticle(string Name, string Uri, string Text);

internal sealed record KnowledgeArticleLookup(
	KnowledgeArticleLookupStatus Status,
	KnowledgeArticle? Article,
	ulong? ActiveSequence);
