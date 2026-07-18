using System;
using System.Collections.Generic;
using System.IO;
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
	internal const string KeyIdVariable = "CLIO_KNOWLEDGE_TRUSTED_KEY_ID";
	internal const string PublicKeyPathVariable = "CLIO_KNOWLEDGE_TRUSTED_PUBLIC_KEY_PATH";

	public bool TryGetPublicKeyPem(string keyId, out string publicKeyPem) {
		publicKeyPem = string.Empty;
		string? trustedKeyId = Environment.GetEnvironmentVariable(KeyIdVariable);
		string? publicKeyPath = Environment.GetEnvironmentVariable(PublicKeyPathVariable);
		if (!string.Equals(keyId, trustedKeyId, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(publicKeyPath)) {
			return false;
		}
		try {
			publicKeyPem = File.ReadAllText(publicKeyPath);
			return !string.IsNullOrWhiteSpace(publicKeyPem);
		} catch (IOException) {
			return false;
		} catch (UnauthorizedAccessException) {
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
		if (string.IsNullOrWhiteSpace(bundlePath)) {
			return;
		}
		try {
			using FileStream candidate = File.OpenRead(bundlePath);
			_runtime.Activate(candidate);
		} catch (IOException) {
			// Strict lazy mode stays explicitly unavailable when the configured source cannot be read.
		} catch (UnauthorizedAccessException) {
			// Strict lazy mode stays explicitly unavailable when the configured source cannot be read.
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
	IReadOnlySet<string> Tools);

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
