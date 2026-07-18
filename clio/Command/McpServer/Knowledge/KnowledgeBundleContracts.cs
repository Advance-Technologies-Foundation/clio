using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeBundleRuntime {
	KnowledgeBundleActivationResult Activate(Stream candidate);

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

internal sealed class EnvironmentKnowledgeBundleActivator : IKnowledgeBundleActivator {
	internal const string BundlePathVariable = "CLIO_KNOWLEDGE_BUNDLE_PATH";
	private readonly IKnowledgeBundleRuntime _runtime;
	private int _attempted;

	public EnvironmentKnowledgeBundleActivator(IKnowledgeBundleRuntime runtime) {
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
	}

	public void EnsureActivated() {
		if (Interlocked.Exchange(ref _attempted, 1) != 0) {
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

	public KnowledgeBundleActivationResult Activate(Stream candidate) => throw new NotSupportedException();

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
