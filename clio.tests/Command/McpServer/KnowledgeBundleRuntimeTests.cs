using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeBundleRuntimeTests {
	private const string TestArticleName = "guide-a";
	private const string TestArticleUri = "docs://mcp/guides/guide-a";
	private const string TestArticlePath = "resources/guide-a.md";
	private const string TestArticleText = "Synthetic signed test payload.\n";

	private ServiceProvider _container;
	private IKnowledgeBundleRuntime _runtime;
	private IKnowledgeBundleTrustStore _trustStore;
	private string _privateKeyPem;
	private byte[] _validCandidateBytes;

	[SetUp]
	public void SetUp() {
		using ECDsa testKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		_privateKeyPem = testKey.ExportPkcs8PrivateKeyPem();
		string publicKey = testKey.ExportSubjectPublicKeyInfoPem();
		_validCandidateBytes = BuildValidCandidate(testKey);
		_trustStore = Substitute.For<IKnowledgeBundleTrustStore>();
		_trustStore.TryGetPublicKeyPem("p1-test", out Arg.Any<string>())
			.Returns(callInfo => {
				callInfo[1] = publicKey;
				return true;
			});
		_trustStore.TryGetPublicKeyPem(Arg.Any<string>(), "p1-test", out Arg.Any<string>())
			.Returns(callInfo => {
				callInfo[2] = publicKey;
				return true;
			});
		ServiceCollection services = new();
		services.AddSingleton(_trustStore);
		services.AddSingleton(new KnowledgeBundleClientCapabilities(
			new Version(8, 1, 0, 86),
			new Version(1, 0, 0),
			new HashSet<string>(StringComparer.Ordinal) { "get-guidance" },
			new Dictionary<string, string>(StringComparer.Ordinal) { [TestArticleName] = TestArticleUri }));
		services.AddSingleton<IKnowledgeResolver, KnowledgeResolver>();
		services.AddSingleton<IKnowledgeBundleRuntime, KnowledgeBundleRuntime>();
		_container = services.BuildServiceProvider();
		_runtime = _container.GetRequiredService<IKnowledgeBundleRuntime>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
	}

	[Test]
	[Description("Reports typed unavailability before any verified knowledge bundle is active.")]
	public void Find_ShouldReportUnavailable_WhenRuntimeIsCold() {
		// Arrange

		// Act
		KnowledgeArticleLookup result = _runtime.Find(TestArticleName);

		// Assert
		result.Status.Should().Be(KnowledgeArticleLookupStatus.Unavailable,
			because: "cold start must be distinguishable from a missing article in an active bundle");
		result.Article.Should().BeNull(
			because: "unverified content must never be returned during cold start");
	}

	[Test]
	[Description("Activates a valid signed bundle and serves its exact payload bytes.")]
	public void Activate_ShouldServeExactPayload_WhenCandidateIsValid() {
		// Arrange
		using MemoryStream candidate = ValidCandidate();

		// Act
		KnowledgeBundleActivationResult activation = _runtime.Activate(candidate);
		KnowledgeArticleLookup lookup = _runtime.Find(TestArticleName);

		// Assert
		activation.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "the signed compatible conformance bundle should become active");
		lookup.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "an article in the active bundle should be returned as verified content");
		lookup.Article!.Uri.Should().Be(TestArticleUri,
			because: "stable resource identity must survive externalization");
		Encoding.UTF8.GetBytes(lookup.Article.Text).Should().Equal(Encoding.UTF8.GetBytes(TestArticleText),
			because: "the verified resource must be served without byte-changing transformations");
	}

	[Test]
	[Description("Rejects a legacy v0 bundle for a configured source before consulting legacy environment trust.")]
	public void Validate_ShouldRejectLegacyContractWithoutTrustLookup_WhenLibraryIsConfigured() {
		// Arrange
		using MemoryStream candidate = ValidCandidate();
		_trustStore.ClearReceivedCalls();

		// Act
		KnowledgeBundleValidationResult result = _runtime.Validate(
			candidate,
			expectedLibraryId: "com.example.partner");

		// Assert
		result.Status.Should().Be(KnowledgeBundleActivationStatus.Rejected,
			because: "configured multi-source lifecycle must accept only publisher-bound v1 manifests");
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.UnsupportedContract,
			because: "a valid legacy contract is still unsupported on the configured-source path");
		_trustStore.ReceivedCalls().Should().BeEmpty(
			because: "legacy key-only environment trust must not authorize a configured source candidate");
	}

	[Test]
	[Description("Activates a v1 publisher bundle and exposes canonical namespaced identity with provenance.")]
	public void ActivateLibrary_ShouldServeCanonicalItem_WhenV1BundleIsValid() {
		// Arrange
		using ECDsa signingKey = ECDsa.Create();
		signingKey.ImportFromPem(_privateKeyPem);
		using MemoryStream candidate = new(BuildV1Candidate(signingKey), writable: false);

		// Act
		KnowledgeBundleActivationResult activation = _runtime.ActivateLibrary(
			"partner",
			42,
			KnowledgeSourceParticipation.Authoritative,
			candidate,
			"2026.07.19.1",
			"com.example.partner",
			Path.GetFullPath(Path.Combine(Path.GetTempPath(), "knowledge", "partner")));
		KnowledgeArticleLookup lookup = _runtime.Find(
			"docs://knowledge/com.example.partner/guide-a");

		// Assert
		activation.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "a signed compatible v1 library generation should activate independently");
		lookup.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "the canonical namespaced route must resolve the exact publisher item");
		lookup.Provenance!.LibraryId.Should().Be("com.example.partner",
			because: "resolved guidance must disclose its stable publisher identity");
		lookup.Provenance.Sequence.Should().Be(2,
			because: "provenance must bind the article to its signed library generation");
	}

	[TestCase(40)]
	[TestCase(64)]
	[Description("V1 validation accepts only complete hexadecimal Git object identities at the supported SHA widths.")]
	public void Validate_ShouldAcceptCompleteCommit_WhenV1CommitUsesSupportedShaWidth(int commitLength) {
		// Arrange
		using MemoryStream candidate = MutateV1AndResign((manifest, _) =>
			manifest["source"]!["commit"] = new string('a', commitLength));

		// Act
		KnowledgeBundleValidationResult result = _runtime.Validate(
			candidate,
			expectedLibraryId: "com.example.partner");

		// Assert
		result.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: $"a complete {commitLength}-character hexadecimal object ID is immutable provenance");
	}

	[Test]
	[Description("V1 validation rejects two resources that claim the same logical topic and role even when their item identities differ.")]
	public void Validate_ShouldRejectDuplicateTopicRole_WhenV1ItemsDiffer() {
		// Arrange
		using MemoryStream candidate = MutateV1AndResign((manifest, entries) => {
			JsonObject duplicate = manifest["resources"]!.AsArray()[0]!.DeepClone().AsObject();
			duplicate["itemId"] = "guide-b";
			duplicate["uri"] = "docs://knowledge/com.example.partner/guide-b";
			duplicate["legacyUris"] = new JsonArray("docs://mcp/guides/guide-b");
			duplicate["path"] = "resources/guide-b.md";
			manifest["resources"]!.AsArray().Add(duplicate);
			manifest["requirements"]!["itemIds"]!.AsArray().Add("guide-b");
			manifest["requirements"]!["resourceUris"]!.AsArray().Add(
				"docs://knowledge/com.example.partner/guide-b");
			entries["resources/guide-b.md"] = entries[TestArticlePath];
		});

		// Act
		KnowledgeBundleValidationResult result = _runtime.Validate(
			candidate,
			expectedLibraryId: "com.example.partner");

		// Assert
		result.Status.Should().Be(KnowledgeBundleActivationStatus.Rejected,
			because: "topic resolution requires one deterministic item for each topic and role in a library");
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "duplicate logical declarations are a signed manifest contract violation");
		result.Diagnostic.Should().Contain("topic and role",
			because: "publishers need an actionable explanation of the conflicting identity pair");
	}

	[Test]
	[Description("Refreshes source policy for the identical installed v1 generation without treating it as a replay.")]
	public void ActivateLibrary_ShouldRefreshPolicy_WhenInstalledGenerationIdentityIsUnchanged() {
		// Arrange
		using ECDsa signingKey = ECDsa.Create();
		signingKey.ImportFromPem(_privateKeyPem);
		byte[] bundle = BuildV1Candidate(signingKey);
		string localRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "knowledge", "partner"));
		using MemoryStream first = new(bundle, writable: false);
		_runtime.ActivateLibrary(
			"partner", 42, KnowledgeSourceParticipation.Authoritative, first,
			"2026.07.19.1", "com.example.partner", localRoot);
		using MemoryStream refresh = new(bundle, writable: false);

		// Act
		KnowledgeBundleActivationResult result = _runtime.ActivateLibrary(
			"partner", 77, KnowledgeSourceParticipation.Supplement, refresh,
			"2026.07.19.1", "com.example.partner", localRoot);

		// Assert
		result.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "priority and participation changes must hot refresh without requiring a new signed generation");
		result.ActiveSequence.Should().Be(2,
			because: "an idempotent policy refresh must retain the verified generation identity");
	}

	[Test]
	[Description("Rejects a correctly signed bundle when its declared version does not match the immutable NuGet package version.")]
	public void Activate_ShouldRejectCandidate_WhenSignedBundleVersionDiffersFromPackageVersion() {
		// Arrange
		using MemoryStream candidate = ValidCandidate();

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate, "1.0.0");

		// Assert
		result.Status.Should().Be(KnowledgeBundleActivationStatus.Rejected,
			because: "a signed inner bundle cannot claim a different identity from its immutable package envelope");
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "package-to-bundle identity mismatch is verified content failure");
		result.ActiveSequence.Should().BeNull(
			because: "a mismatched cold candidate must not become active");
	}

	[Test]
	[Description("Rejects a resource whose bytes no longer match the signed manifest and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenResourceIsTampered() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateCandidate(entries => {
			byte[] resource = entries[TestArticlePath];
			resource[0] ^= 0x01;
		});

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);
		KnowledgeArticleLookup lookup = _runtime.Find(TestArticleName);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "resource bytes must match the signed manifest digest");
		lookup.ActiveSequence.Should().Be(1,
			because: "a rejected candidate must not replace the verified active bundle");
	}

	[Test]
	[Description("Rejects a damaged detached manifest signature and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenSignatureIsInvalid() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateCandidate(entries => entries["manifest.sig"][0] ^= 0x01);

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidSignature,
			because: "the exact manifest bytes must be authorized by the configured trusted key");
		result.ActiveSequence.Should().Be(1,
			because: "signature failure must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects a manifest signed under an unknown key id and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenKeyIsUntrusted() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest =>
			manifest["signature"]!["keyId"] = "unknown-key");

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.UntrustedKey,
			because: "a cryptographically valid signature has no authority without a configured trusted key id");
		result.ActiveSequence.Should().Be(1,
			because: "untrusted candidates must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects malformed trusted-key material as untrusted and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenTrustedKeyMaterialIsMalformed() {
		// Arrange
		ActivateValid();
		_trustStore.TryGetPublicKeyPem("p1-test", out Arg.Any<string>())
			.Returns(callInfo => {
				callInfo[1] = "not-a-public-key";
				return true;
			});
		using MemoryStream candidate = MutateAndResign(manifest => manifest["sequence"] = 2);

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.UntrustedKey,
			because: "malformed trust configuration must remain a typed trust rejection rather than escape activation");
		result.ActiveSequence.Should().Be(1,
			because: "invalid trust material must preserve the last-known-good bundle");
	}

	[Test]
	[Description("Rejects a correctly signed but incompatible bundle and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenCandidateIsIncompatible() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest =>
			manifest["compatibility"]!["clio"]!["max"] = "8.0.999");

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.Incompatible,
			because: "signature validity cannot override the declared client compatibility range");
		result.ActiveSequence.Should().Be(1,
			because: "incompatible candidates must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects an oversized signed compatibility version without evaluating its regular expression and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenCompatibilityVersionExceedsBound() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest =>
			manifest["compatibility"]!["clio"]!["min"] = new string('9', 500_000));

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.Incompatible,
			because: "untrusted version text must be rejected before bounded regular-expression evaluation");
		result.ActiveSequence.Should().Be(1,
			because: "an oversized signed compatibility value must preserve the last-known-good bundle");
	}

	[Test]
	[Description("Compares a four-part Clio assembly version to the bundle's exact three-part product range.")]
	public void Activate_ShouldNormalizeAssemblyRevision_WhenProductVersionRangeIsExact() {
		// Arrange
		using MemoryStream candidate = MutateAndResign(manifest => {
			manifest["compatibility"]!["clio"]!["min"] = "8.1.0";
			manifest["compatibility"]!["clio"]!["max"] = "8.1.0";
		});

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "the assembly revision is not part of the three-part Clio product compatibility contract");
		result.ActiveSequence.Should().Be(1,
			because: "the exact product-version match must publish the verified candidate");
	}

	[Test]
	[Description("Rejects a signed bundle using an unknown contract version and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenContractIsUnsupported() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest => manifest["contractVersion"] = "9.0.0");

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.UnsupportedContract,
			because: "a future contract must fail closed until this runtime explicitly implements it");
		result.ActiveSequence.Should().Be(1,
			because: "unsupported candidates must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects a signed bundle using an unknown schema version and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenSchemaIsUnsupported() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest => manifest["bundleSchemaVersion"] = "9.0.0");

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.UnsupportedContract,
			because: "a future schema must fail closed until this runtime explicitly implements it");
		result.ActiveSequence.Should().Be(1,
			because: "unsupported schema candidates must leave the last-known-good bundle active");
	}

	[Test]
	[Description("Rejects a signed manifest missing its issue timestamp and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenIssuedAtIsMissing() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest => manifest.Remove("issuedAt"));

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.Malformed,
			because: "every producer-declared v0 manifest field must be enforced by the consumer schema boundary");
		result.ActiveSequence.Should().Be(1,
			because: "a signed but incomplete manifest must preserve the last-known-good bundle");
	}

	[Test]
	[Description("Rejects malformed manifest JSON and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenManifestIsMalformed() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateCandidate(entries =>
			entries["manifest.json"] = Encoding.UTF8.GetBytes("{not-json"));

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.Malformed,
			because: "invalid JSON must be rejected rather than escaping the activation boundary");
		result.ActiveSequence.Should().Be(1,
			because: "malformed candidates must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects a signed resource descriptor with an unsupported media type and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenResourceDescriptorIsInvalid() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest =>
			manifest["resources"]![0]!["mediaType"] = "application/octet-stream");

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "v0 serves only strictly decoded text resources");
		result.ActiveSequence.Should().Be(1,
			because: "invalid descriptors must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects duplicate JSON property names even when the ambiguous manifest is correctly signed.")]
	public void Activate_ShouldRetainActiveBundle_WhenManifestHasDuplicateProperty() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateManifestBytesAndResign(manifestBytes => {
			string manifest = Encoding.UTF8.GetString(manifestBytes);
			return Encoding.UTF8.GetBytes(manifest.Replace(
				"\"sequence\":1,",
				"\"sequence\":1,\"sequence\":2,",
				StringComparison.Ordinal));
		});

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.Malformed,
			because: "signed content must have one unambiguous human and parser interpretation");
		result.ActiveSequence.Should().Be(1,
			because: "ambiguous signed manifests must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects a bundle that requires an unavailable MCP capability and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenRequiredToolIsMissing() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest =>
			manifest["requirements"]!["tools"]!.AsArray().Add("execute-esq"));

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.MissingCapability,
			because: "guidance must not activate when it depends on a tool contract the client lacks");
		result.ActiveSequence.Should().Be(1,
			because: "capability rejection must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects a self-consistent signed bundle that changes the stable catalog URI and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenResourceUriDiffersFromCatalog() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest => {
			const string mismatchedUri = "docs://synthetic/guides/wrong";
			manifest["requirements"]!["resourceUris"]![0] = mismatchedUri;
			manifest["resources"]![0]!["uri"] = mismatchedUri;
		});

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "a producer-signed URI cannot override Clio's stable resource identity");
		result.ActiveSequence.Should().Be(1,
			because: "catalog identity rejection must retain the last-known-good bundle");
	}

	[Test]
	[Description("Rejects a correctly signed bundle that omits one stable external guidance catalog entry.")]
	public void Activate_ShouldRejectCandidate_WhenStableCatalogResourceIsMissing() {
		// Arrange
		ServiceCollection services = new();
		services.AddSingleton(_trustStore);
		services.AddSingleton(new KnowledgeBundleClientCapabilities(
			new Version(8, 1, 0),
			new Version(1, 0, 0),
			new HashSet<string>(StringComparer.Ordinal) { "get-guidance" },
			new Dictionary<string, string>(StringComparer.Ordinal) {
				[TestArticleName] = TestArticleUri,
				["guide-b"] = "docs://mcp/guides/guide-b"
			}));
		services.AddSingleton<IKnowledgeResolver, KnowledgeResolver>();
		services.AddSingleton<IKnowledgeBundleRuntime, KnowledgeBundleRuntime>();
		using ServiceProvider partialCatalogContainer = services.BuildServiceProvider();
		IKnowledgeBundleRuntime runtime = partialCatalogContainer.GetRequiredService<IKnowledgeBundleRuntime>();
		using MemoryStream candidate = ValidCandidate();

		// Act
		KnowledgeBundleActivationResult result = runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "an active bundle must cover the complete stable external catalog atomically");
		runtime.ActiveSequence.Should().BeNull(
			because: "partial catalog coverage must not publish a degraded active snapshot");
	}

	[Test]
	[Description("Activates only increasing bundle sequences and rejects replay of equal or older candidates.")]
	public void Activate_ShouldAdvanceOnly_WhenSequenceIsGreater() {
		// Arrange
		ActivateValid();
		using MemoryStream sequenceTwo = MutateAndResign(manifest => manifest["sequence"] = 2);
		using MemoryStream replayTwo = MutateAndResign(manifest => manifest["sequence"] = 2);
		using MemoryStream replayOne = ValidCandidate();

		// Act
		KnowledgeBundleActivationResult advanced = _runtime.Activate(sequenceTwo);
		KnowledgeBundleActivationResult repeated = _runtime.Activate(replayTwo);
		KnowledgeBundleActivationResult replayed = _runtime.Activate(replayOne);

		// Assert
		advanced.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "a verified higher sequence is the only permitted forward transition");
		repeated.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.SequenceNotForward,
			because: "equal-sequence candidates must not replace the active bundle");
		replayed.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.SequenceNotForward,
			because: "older signed bundles must not roll the runtime back");
		replayed.ActiveSequence.Should().Be(2,
			because: "replay rejection must preserve the newest verified active sequence");
	}

	[Test]
	[Description("Publishes concurrent verified candidates atomically and retains the greatest sequence.")]
	public void Activate_ShouldPublishHighestSequenceAtomically_WhenCandidatesRace() {
		// Arrange
		ActivateValid();
		List<MemoryStream> candidates = Enumerable.Range(2, 7)
			.Select(sequence => MutateAndResign(manifest => manifest["sequence"] = sequence))
			.ToList();

		// Act
		Parallel.ForEach(candidates, candidate => _runtime.Activate(candidate));
		KnowledgeArticleLookup lookup = _runtime.Find(TestArticleName);

		// Assert
		_runtime.ActiveSequence.Should().Be(8,
			because: "serialized forward-only publication must converge on the greatest verified sequence");
		lookup.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "readers must observe a complete active bundle after concurrent publication");
		lookup.Article!.Text.Should().Be(TestArticleText,
			because: "atomic publication must never expose a partially prepared article set");
		foreach (MemoryStream candidate in candidates) {
			candidate.Dispose();
		}
	}

	[Test]
	[Description("Rejects a truncated candidate archive and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenArchiveIsTruncated() {
		// Arrange
		ActivateValid();
		byte[] validBytes = _validCandidateBytes;
		using MemoryStream candidate = new(validBytes[..(validBytes.Length / 2)]);

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.Status.Should().Be(KnowledgeBundleActivationStatus.Rejected,
			because: "a partial download must never become active");
		result.ActiveSequence.Should().Be(1,
			because: "truncated candidates must leave active guidance unchanged");
	}

	[Test]
	[Description("Rejects unexpected archive entries and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenArchiveHasUnexpectedEntry() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateCandidate(entries => entries.Add("unexpected.txt", [1, 2, 3]));

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "the signed manifest must describe the complete archive surface");
		result.ActiveSequence.Should().Be(1,
			because: "unexpected content must not replace active guidance");
	}

	[Test]
	[Description("Rejects a central directory with more entries than the v0 archive budget before activation.")]
	public void Activate_ShouldRetainActiveBundle_WhenArchiveEntryBudgetIsExceeded() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateCandidate(entries => {
			for (int index = 0; index < 1025; index++) {
				entries.Add($"unexpected/{index}.txt", []);
			}
		});

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "entry-flood archives must fail the bounded ZIP preflight");
		result.ActiveSequence.Should().Be(1,
			because: "archive-budget rejection must preserve the last-known-good bundle");
	}

	[Test]
	[Description("Rejects a ZIP whose end record understates the central-directory size before metadata enumeration.")]
	public void Activate_ShouldRetainActiveBundle_WhenCentralDirectorySizeIsUnderstated() {
		// Arrange
		ActivateValid();
		byte[] bytes = _validCandidateBytes.ToArray();
		int endRecordOffset = FindEndOfCentralDirectory(bytes);
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(endRecordOffset + 12, sizeof(uint)), 1);
		using MemoryStream candidate = new(bytes);

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "untrusted ZIP metadata must not bypass the bounded central-directory preflight");
		result.ActiveSequence.Should().Be(1,
			because: "malformed archive metadata must preserve the last-known-good bundle");
	}

	[Test]
	[Description("Rejects a non-seekable candidate that exceeds the compressed archive budget before ZIP parsing.")]
	public void Activate_ShouldRetainActiveBundle_WhenCompressedArchiveBudgetIsExceeded() {
		// Arrange
		ActivateValid();
		using Stream candidate = new FixedLengthNonSeekableStream(41L * 1024 * 1024);

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "oversized transport streams must be bounded before the ZIP parser sees them");
		result.ActiveSequence.Should().Be(1,
			because: "compressed-size rejection must preserve the last-known-good bundle");
	}

	[Test]
	[Description("Rejects a bundle with a declared resource entry missing and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenResourceEntryIsMissing() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateCandidate(entries => entries.Remove(TestArticlePath));

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "every resource declared by the signed manifest must be present");
		result.ActiveSequence.Should().Be(1,
			because: "missing content must not replace active guidance");
	}

	[Test]
	[Description("Rejects a signed manifest containing a traversal resource path and retains active guidance.")]
	public void Activate_ShouldRetainActiveBundle_WhenResourcePathTraverses() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateAndResign(manifest =>
			manifest["resources"]![0]!["path"] = "resources/../esq.md");

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);

		// Assert
		result.RejectionCode.Should().Be(KnowledgeBundleRejectionCode.InvalidContent,
			because: "signed metadata must still obey the consumer's safe extraction-path policy");
		result.ActiveSequence.Should().Be(1,
			because: "path validation failure must leave active guidance unchanged");
	}

	[Test]
	[Description("Distinguishes an unknown article in an active bundle from cold-start unavailability.")]
	public void Find_ShouldReportNotFound_WhenBundleIsActive() {
		// Arrange
		ActivateValid();

		// Act
		KnowledgeArticleLookup result = _runtime.Find("does-not-exist");

		// Assert
		result.Status.Should().Be(KnowledgeArticleLookupStatus.NotFound,
			because: "callers need to distinguish absent content from an unavailable knowledge runtime");
		result.ActiveSequence.Should().Be(1,
			because: "the diagnostic should identify the active bundle that was searched");
	}

	private void ActivateValid() {
		using MemoryStream candidate = ValidCandidate();
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);
		result.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "the test precondition requires a verified active bundle");
	}

	private MemoryStream ValidCandidate() => new(_validCandidateBytes.ToArray());

	private MemoryStream MutateAndResign(Action<JsonObject> mutateManifest) => MutateCandidate(entries => {
		JsonObject manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
		mutateManifest(manifest);
		byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
		using ECDsa signingKey = ECDsa.Create();
		signingKey.ImportFromPem(_privateKeyPem);
		entries["manifest.json"] = manifestBytes;
		entries["manifest.sig"] = signingKey.SignData(manifestBytes, HashAlgorithmName.SHA256);
	});

	private MemoryStream MutateV1AndResign(
		Action<JsonObject, Dictionary<string, byte[]>> mutateManifest) {
		using ECDsa signingKey = ECDsa.Create();
		signingKey.ImportFromPem(_privateKeyPem);
		Dictionary<string, byte[]> entries = ReadEntries(BuildV1Candidate(signingKey));
		JsonObject manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
		mutateManifest(manifest, entries);
		byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
		entries["manifest.json"] = manifestBytes;
		entries["manifest.sig"] = signingKey.SignData(manifestBytes, HashAlgorithmName.SHA256);
		return WriteArchive(entries);
	}

	private MemoryStream MutateManifestBytesAndResign(Func<byte[], byte[]> mutateManifest) =>
		MutateCandidate(entries => {
			byte[] manifestBytes = mutateManifest(entries["manifest.json"]);
			using ECDsa signingKey = ECDsa.Create();
			signingKey.ImportFromPem(_privateKeyPem);
			entries["manifest.json"] = manifestBytes;
			entries["manifest.sig"] = signingKey.SignData(manifestBytes, HashAlgorithmName.SHA256);
		});

	private MemoryStream MutateCandidate(Action<Dictionary<string, byte[]>> mutate) {
		Dictionary<string, byte[]> entries = ReadEntries(_validCandidateBytes);
		mutate(entries);
		return WriteArchive(entries);
	}

	private static byte[] BuildValidCandidate(ECDsa signingKey) {
		byte[] resourceBytes = new UTF8Encoding(false, true).GetBytes(TestArticleText);
		string digest = Convert.ToHexString(SHA256.HashData(resourceBytes)).ToLowerInvariant();
		byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new {
			contractVersion = "0.1.0",
			bundleSchemaVersion = "0.1.0",
			sequence = 1,
			bundleVersion = "1.0.0-test",
			issuedAt = "2026-07-18T00:00:00Z",
			source = new { repository = "synthetic-test", commit = "0123456" },
			compatibility = new {
				clio = new { min = "8.1.0", max = "8.1.999" },
				mcpToolContract = new { min = "1.0.0", max = "1.0.0" }
			},
			requirements = new {
				tools = new[] { "get-guidance" },
				guidanceIds = new[] { TestArticleName },
				resourceUris = new[] { TestArticleUri }
			},
			digestAlg = "SHA-256",
			signature = new { algorithm = "ECDSA-P256-SHA256", keyId = "p1-test" },
			resources = new[] {
				new {
					id = TestArticleName,
					uri = TestArticleUri,
					path = TestArticlePath,
					mediaType = "text/plain",
					length = resourceBytes.LongLength,
					digest
				}
			}
		});
		Dictionary<string, byte[]> entries = new(StringComparer.Ordinal) {
			["manifest.json"] = manifestBytes,
			["manifest.sig"] = signingKey.SignData(manifestBytes, HashAlgorithmName.SHA256),
			[TestArticlePath] = resourceBytes
		};
		using MemoryStream archive = WriteArchive(entries);
		return archive.ToArray();
	}

	private static byte[] BuildV1Candidate(ECDsa signingKey) {
		byte[] resourceBytes = new UTF8Encoding(false, true).GetBytes(TestArticleText);
		string digest = Convert.ToHexString(SHA256.HashData(resourceBytes)).ToLowerInvariant();
		string uri = "docs://knowledge/com.example.partner/guide-a";
		byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new {
			contractVersion = "1.0.0",
			bundleSchemaVersion = "1.0.0",
			libraryId = "com.example.partner",
			libraryVersion = "2026.07.19.1",
			sequence = 2,
			source = new { repository = "synthetic-partner", commit = "0123456789012345678901234567890123456789" },
			compatibility = new {
				clio = new { min = "8.1.0", max = "8.1.999" },
				mcpToolContract = new { min = "1.0.0", max = "1.0.0" }
			},
			requirements = new {
				tools = new[] { "get-guidance" },
				itemIds = new[] { TestArticleName },
				resourceUris = new[] { uri }
			},
			digestAlg = "SHA-256",
			signature = new { algorithm = "ECDSA-P256-SHA256", keyId = "p1-test" },
			resources = new[] {
				new {
					itemId = TestArticleName,
					topicId = "topic-a",
					role = "guidance",
					uri,
					legacyUris = new[] { TestArticleUri },
					path = TestArticlePath,
					mediaType = "text/plain",
					length = resourceBytes.LongLength,
					digest
				}
			}
		});
		Dictionary<string, byte[]> entries = new(StringComparer.Ordinal) {
			["manifest.json"] = manifestBytes,
			["manifest.sig"] = signingKey.SignData(manifestBytes, HashAlgorithmName.SHA256),
			[TestArticlePath] = resourceBytes
		};
		using MemoryStream archive = WriteArchive(entries);
		return archive.ToArray();
	}

	private static MemoryStream WriteArchive(IReadOnlyDictionary<string, byte[]> entries) {
		MemoryStream output = new();
		using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true)) {
			foreach ((string path, byte[] bytes) in entries) {
				ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
				using Stream stream = entry.Open();
				stream.Write(bytes);
			}
		}
		output.Position = 0;
		return output;
	}

	private static Dictionary<string, byte[]> ReadEntries(byte[] archiveBytes) {
		using MemoryStream input = new(archiveBytes);
		using ZipArchive archive = new(input, ZipArchiveMode.Read);
		return archive.Entries.ToDictionary(
			entry => entry.FullName,
			entry => {
				using Stream stream = entry.Open();
				using MemoryStream bytes = new();
				stream.CopyTo(bytes);
				return bytes.ToArray();
			},
			StringComparer.Ordinal);
	}

	private static int FindEndOfCentralDirectory(byte[] archiveBytes) {
		for (int index = archiveBytes.Length - 22; index >= 0; index--) {
			if (BinaryPrimitives.ReadUInt32LittleEndian(archiveBytes.AsSpan(index)) == 0x06054b50) {
				return index;
			}
		}
		throw new InvalidOperationException("Synthetic ZIP has no end-of-central-directory record.");
	}

	private sealed class FixedLengthNonSeekableStream(long length) : Stream {
		private long _remaining = length;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position {
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) {
			int read = (int)Math.Min(count, _remaining);
			if (read == 0) {
				return 0;
			}
			Array.Clear(buffer, offset, read);
			_remaining -= read;
			return read;
		}

		public override void Flush() {
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

}
