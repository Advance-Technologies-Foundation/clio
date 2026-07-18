using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
	private string _privateKeyPem;
	private byte[] _validCandidateBytes;

	[SetUp]
	public void SetUp() {
		using ECDsa testKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		_privateKeyPem = testKey.ExportPkcs8PrivateKeyPem();
		string publicKey = testKey.ExportSubjectPublicKeyInfoPem();
		_validCandidateBytes = BuildValidCandidate(testKey);
		IKnowledgeBundleTrustStore trustStore = Substitute.For<IKnowledgeBundleTrustStore>();
		trustStore.TryGetPublicKeyPem("p1-test", out Arg.Any<string>())
			.Returns(callInfo => {
				callInfo[1] = publicKey;
				return true;
			});
		ServiceCollection services = new();
		services.AddSingleton(trustStore);
		services.AddSingleton(new KnowledgeBundleClientCapabilities(
			new Version(8, 1, 0),
			new Version(1, 0, 0),
			new HashSet<string>(StringComparer.Ordinal) { "get-guidance" }));
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
	public void Find_Should_Report_Unavailable_When_Runtime_Is_Cold() {
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
	public void Activate_Should_Serve_Exact_Payload_When_Candidate_Is_Valid() {
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
	[Description("Rejects a resource whose bytes no longer match the signed manifest and retains active guidance.")]
	public void Activate_Should_Retain_Active_Bundle_When_Resource_Is_Tampered() {
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
	public void Activate_Should_Retain_Active_Bundle_When_Signature_Is_Invalid() {
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
	public void Activate_Should_Retain_Active_Bundle_When_Key_Is_Untrusted() {
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
	[Description("Rejects a correctly signed but incompatible bundle and retains active guidance.")]
	public void Activate_Should_Retain_Active_Bundle_When_Candidate_Is_Incompatible() {
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
	[Description("Rejects a signed bundle using an unknown contract version and retains active guidance.")]
	public void Activate_Should_Retain_Active_Bundle_When_Contract_Is_Unsupported() {
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
	[Description("Rejects malformed manifest JSON and retains active guidance.")]
	public void Activate_Should_Retain_Active_Bundle_When_Manifest_Is_Malformed() {
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
	public void Activate_Should_Retain_Active_Bundle_When_Resource_Descriptor_Is_Invalid() {
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
	public void Activate_Should_Retain_Active_Bundle_When_Manifest_Has_Duplicate_Property() {
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
	public void Activate_Should_Retain_Active_Bundle_When_Required_Tool_Is_Missing() {
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
	[Description("Activates only increasing bundle sequences and rejects replay of equal or older candidates.")]
	public void Activate_Should_Advance_Only_When_Sequence_Is_Greater() {
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
	[Description("Rejects a truncated candidate archive and retains active guidance.")]
	public void Activate_Should_Retain_Active_Bundle_When_Archive_Is_Truncated() {
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
	public void Activate_Should_Retain_Active_Bundle_When_Archive_Has_Unexpected_Entry() {
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
	[Description("Rejects a bundle with a declared resource entry missing and retains active guidance.")]
	public void Activate_Should_Retain_Active_Bundle_When_Resource_Entry_Is_Missing() {
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
	public void Activate_Should_Retain_Active_Bundle_When_Resource_Path_Traverses() {
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
	public void Find_Should_Report_NotFound_When_Bundle_Is_Active() {
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

}
