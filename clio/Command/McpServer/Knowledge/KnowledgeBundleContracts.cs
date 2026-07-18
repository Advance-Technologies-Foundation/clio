using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeBundleRuntime {
	KnowledgeBundleActivationResult Activate(Stream candidate, string? expectedBundleVersion = null);

	KnowledgeArticleLookup Find(string name);

	ulong? ActiveSequence { get; }
}

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
}

internal interface IKnowledgeBundlePackageClient {
	bool IsConfigured { get; }

	KnowledgeBundlePackageDownloadResult DownloadNext(
		IReadOnlySet<string> rejectedPackageVersions,
		string? activePackageVersion,
		string? highestObservedPackageVersion,
		string? fallbackCeilingPackageVersion,
		string? catalogFingerprint);
}

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

internal sealed record KnowledgeBundleRenewalOptions(int CooldownMilliseconds);

internal sealed record KnowledgeBundleNuGetOptions(int TransportDeadlineMilliseconds);

internal sealed class EnvironmentKnowledgeBundleActivator : IKnowledgeBundleActivator {
	internal const string BundlePathVariable = "CLIO_KNOWLEDGE_BUNDLE_PATH";
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IKnowledgeBundlePackageClient _packageClient;
	private readonly KnowledgeBundleRenewalOptions _renewalOptions;
	private readonly object _activationLock = new();
	private readonly HashSet<string> _rejectedPackageVersions = new(StringComparer.Ordinal);
	private readonly Queue<string> _rejectedPackageVersionOrder = new();
	private int _pathAttempted;
	private int _renewalInProgress;
	private long _nextRenewalCheck;
	private string? _activePackageVersion;
	private string? _highestObservedPackageVersion;
	private string? _fallbackCeilingPackageVersion;
	private string? _catalogFingerprint;
	private const int MaxRejectedPackageVersions = 64;

	public EnvironmentKnowledgeBundleActivator(
		IKnowledgeBundleRuntime runtime,
		IKnowledgeBundlePackageClient packageClient,
		KnowledgeBundleRenewalOptions renewalOptions) {
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_packageClient = packageClient ?? throw new ArgumentNullException(nameof(packageClient));
		_renewalOptions = renewalOptions ?? throw new ArgumentNullException(nameof(renewalOptions));
		ArgumentOutOfRangeException.ThrowIfNegative(renewalOptions.CooldownMilliseconds);
	}

	public void EnsureActivated() {
		if (!_packageClient.IsConfigured) {
			TryActivateConfiguredPath();
			return;
		}
		if (_runtime.ActiveSequence is null) {
			TryActivateCold();
			return;
		}
		ScheduleRenewal();
	}

	private void TryActivateNextPackage() {
		KnowledgeBundlePackageDownloadResult download = _packageClient.DownloadNext(
			_rejectedPackageVersions,
			_activePackageVersion,
			_highestObservedPackageVersion,
			_fallbackCeilingPackageVersion,
			_catalogFingerprint);
		if (download.CatalogFingerprint is not null
				&& !string.Equals(
					_catalogFingerprint,
					download.CatalogFingerprint,
					StringComparison.Ordinal)) {
			_catalogFingerprint = download.CatalogFingerprint;
			_highestObservedPackageVersion = _activePackageVersion;
			_fallbackCeilingPackageVersion = null;
		}
		if (download.Status == KnowledgeBundlePackageDownloadStatus.Rejected
				&& download.PackageVersion is not null) {
			RecordRejectedVersionAndAdvanceScan(download.PackageVersion);
			return;
		}
		if (download.Status != KnowledgeBundlePackageDownloadStatus.Downloaded
				|| download.PackageVersion is null
				|| download.BundleBytes is null) {
			return;
		}
		using MemoryStream bundle = new(
			download.BundleBytes,
			index: 0,
			count: download.BundleBytes.Length,
			writable: false,
			publiclyVisible: true);
		KnowledgeBundleActivationResult activation = _runtime.Activate(bundle, download.PackageVersion);
		if (activation.Status == KnowledgeBundleActivationStatus.Activated) {
			_activePackageVersion = download.PackageVersion;
			_highestObservedPackageVersion = KnowledgeBundleNuGetClient.GreaterVersion(
				_highestObservedPackageVersion,
				download.PackageVersion);
			_fallbackCeilingPackageVersion = null;
			_rejectedPackageVersions.RemoveWhere(version =>
				!KnowledgeBundleNuGetClient.IsVersionGreaterThan(version, download.PackageVersion));
			PruneRejectedVersionOrder();
		} else {
			RecordRejectedVersionAndAdvanceScan(download.PackageVersion);
		}
	}

	private void RecordRejectedVersionAndAdvanceScan(string packageVersion) {
		_highestObservedPackageVersion = KnowledgeBundleNuGetClient.GreaterVersion(
			_highestObservedPackageVersion,
			packageVersion);
		_fallbackCeilingPackageVersion = packageVersion;
		RecordRejectedVersion(packageVersion);
	}

	private void RecordRejectedVersion(string packageVersion) {
		if (!_rejectedPackageVersions.Add(packageVersion)) {
			return;
		}
		_rejectedPackageVersionOrder.Enqueue(packageVersion);
		while (_rejectedPackageVersions.Count > MaxRejectedPackageVersions) {
			_rejectedPackageVersions.Remove(_rejectedPackageVersionOrder.Dequeue());
		}
	}

	private void PruneRejectedVersionOrder() {
		int count = _rejectedPackageVersionOrder.Count;
		for (int index = 0; index < count; index++) {
			string version = _rejectedPackageVersionOrder.Dequeue();
			if (_rejectedPackageVersions.Contains(version)) {
				_rejectedPackageVersionOrder.Enqueue(version);
			}
		}
	}

	private void TryActivateCold() {
		long now = Environment.TickCount64;
		if (now < Volatile.Read(ref _nextRenewalCheck)
				|| Interlocked.CompareExchange(ref _renewalInProgress, 1, 0) != 0) {
			return;
		}
		try {
			lock (_activationLock) {
				if (_runtime.ActiveSequence is null) {
					TryActivateNextPackage();
				}
			}
		} finally {
			Volatile.Write(ref _nextRenewalCheck,
				Environment.TickCount64 + _renewalOptions.CooldownMilliseconds);
			Volatile.Write(ref _renewalInProgress, 0);
		}
	}

	private void ScheduleRenewal() {
		long now = Environment.TickCount64;
		if (now < Volatile.Read(ref _nextRenewalCheck)
				|| Interlocked.CompareExchange(ref _renewalInProgress, 1, 0) != 0) {
			return;
		}
		Volatile.Write(ref _nextRenewalCheck, now + _renewalOptions.CooldownMilliseconds);
		_ = Task.Run(() => {
			try {
				lock (_activationLock) {
					TryActivateNextPackage();
				}
			} finally {
				Volatile.Write(ref _renewalInProgress, 0);
			}
		});
	}

	private void TryActivateConfiguredPath() {
		if (Interlocked.Exchange(ref _pathAttempted, 1) != 0) {
			return;
		}
		string? bundlePath = Environment.GetEnvironmentVariable(BundlePathVariable);
		if (string.IsNullOrWhiteSpace(bundlePath) || !Path.IsPathFullyQualified(bundlePath)) {
			return;
		}
		try {
			using FileStream candidate = File.OpenRead(bundlePath);
			_runtime.Activate(candidate);
		} catch (IOException) {
			// Strict lazy mode stays explicitly unavailable when the configured source cannot be read.
		} catch (UnauthorizedAccessException) {
			// Strict lazy mode stays explicitly unavailable when the configured source cannot be read.
		} catch (ArgumentException) {
			// Strict lazy mode stays explicitly unavailable when the configured source path is invalid.
		} catch (NotSupportedException) {
			// Strict lazy mode stays explicitly unavailable when the configured source path is unsupported.
		}
	}
}

internal sealed class NoOpKnowledgeBundleActivator : IKnowledgeBundleActivator {
	public void EnsureActivated() {
	}
}

internal sealed class UnavailableKnowledgeBundleRuntime : IKnowledgeBundleRuntime {
	public ulong? ActiveSequence => null;

	public KnowledgeBundleActivationResult Activate(Stream candidate, string? expectedBundleVersion = null) =>
		throw new NotSupportedException();

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
