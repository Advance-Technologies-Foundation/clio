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
	private const string FixtureDirectory = "Command/McpServer/Fixtures/KnowledgeBundle";

	private ServiceProvider _container;
	private IKnowledgeBundleRuntime _runtime;

	[SetUp]
	public void SetUp() {
		string publicKey = File.ReadAllText(FixturePath("p1-test-public.pem"));
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
		KnowledgeArticleLookup result = _runtime.Find("esq-filters");

		// Assert
		result.Status.Should().Be(KnowledgeArticleLookupStatus.Unavailable,
			because: "cold start must be distinguishable from a missing article in an active bundle");
		result.Article.Should().BeNull(
			because: "unverified content must never be returned during cold start");
	}

	[Test]
	[Description("Activates the signed ESQ bundle and serves the frozen compiled-Clio payload byte-for-byte.")]
	public void Activate_Should_Serve_Frozen_Oracle_When_Candidate_Is_Valid() {
		// Arrange
		using MemoryStream candidate = ValidCandidate();
		string expected = File.ReadAllText(FixturePath("esq-filters.md"), new UTF8Encoding(false, true));

		// Act
		KnowledgeBundleActivationResult activation = _runtime.Activate(candidate);
		KnowledgeArticleLookup lookup = _runtime.Find("esq-filters");

		// Assert
		activation.Status.Should().Be(KnowledgeBundleActivationStatus.Activated,
			because: "the signed compatible conformance bundle should become active");
		lookup.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "an article in the active bundle should be returned as verified content");
		lookup.Article!.Uri.Should().Be("docs://mcp/guides/esq-filters",
			because: "stable resource identity must survive externalization");
		Encoding.UTF8.GetBytes(lookup.Article.Text).Should().Equal(Encoding.UTF8.GetBytes(expected),
			because: "external guidance must be byte-identical to the compiled runtime oracle");
	}

	[Test]
	[Description("Rejects a resource whose bytes no longer match the signed manifest and retains active guidance.")]
	public void Activate_Should_Retain_Active_Bundle_When_Resource_Is_Tampered() {
		// Arrange
		ActivateValid();
		using MemoryStream candidate = MutateCandidate(entries => {
			byte[] resource = entries["resources/esq-filters.md"];
			resource[0] ^= 0x01;
		});

		// Act
		KnowledgeBundleActivationResult result = _runtime.Activate(candidate);
		KnowledgeArticleLookup lookup = _runtime.Find("esq-filters");

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
		byte[] validBytes = File.ReadAllBytes(FixturePath("valid.zip"));
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
		using MemoryStream candidate = MutateCandidate(entries => entries.Remove("resources/esq-filters.md"));

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

	private static MemoryStream ValidCandidate() => new(File.ReadAllBytes(FixturePath("valid.zip")));

	private static MemoryStream MutateAndResign(Action<JsonObject> mutateManifest) => MutateCandidate(entries => {
		JsonObject manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
		mutateManifest(manifest);
		byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
		using ECDsa signingKey = ECDsa.Create();
		signingKey.ImportFromPem(File.ReadAllText(FixturePath("p1-test-private.pem")));
		entries["manifest.json"] = manifestBytes;
		entries["manifest.sig"] = signingKey.SignData(manifestBytes, HashAlgorithmName.SHA256);
	});

	private static MemoryStream MutateCandidate(Action<Dictionary<string, byte[]>> mutate) {
		Dictionary<string, byte[]> entries = ReadEntries(File.ReadAllBytes(FixturePath("valid.zip")));
		mutate(entries);
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

	private static string FixturePath(string name) => Path.Combine(
		TestContext.CurrentContext.TestDirectory,
		FixtureDirectory.Replace('/', Path.DirectorySeparatorChar),
		name);
}
