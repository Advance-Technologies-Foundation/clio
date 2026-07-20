using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using Clio.Command.McpServer.Knowledge;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeGitRepositoryReaderTests {
	private const string LibraryId = "com.example.knowledge";
	private const string ItemId = "sample-guide";
	private MockFileSystem _fileSystem = null!;
	private string _repositoryPath = null!;
	private ServiceProvider _services = null!;
	private IKnowledgeGitRepositoryReader _sut = null!;

	[SetUp]
	public void SetUp() {
		_fileSystem = TestFileSystem.MockFileSystem();
		_repositoryPath = TestFileSystem.GetRootedPath("knowledge-reader", Guid.NewGuid().ToString("N"));
		_fileSystem.AddDirectory(_repositoryPath);
		ServiceCollection services = new();
		services.AddSingleton<System.IO.Abstractions.IFileSystem>(_fileSystem);
		services.AddSingleton(new KnowledgeBundleClientCapabilities(
			new Version(8, 1, 0, 86),
			new Version(1, 1, 0),
			new HashSet<string>(StringComparer.Ordinal) { "get-guidance" }));
		services.AddSingleton<IKnowledgeGitRepositoryReader, KnowledgeGitRepositoryReader>();
		_services = services.BuildServiceProvider();
		_sut = _services.GetRequiredService<IKnowledgeGitRepositoryReader>();
	}

	[TearDown]
	public void TearDown() => _services.Dispose();

	[Test]
	[Description("A source manifest matching the producer contract is materialized from repository files.")]
	public void TryRead_ShouldMaterializeArticles_WhenRepositoryContractIsValid() {
		// Arrange
		JObject manifest = ValidManifest();
		WriteResource("guidance/sample.md", "Trusted sample guidance.\n");
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeTrue(because: "the repository satisfies the complete direct-Git producer contract");
		diagnostic.Should().BeNull(because: "valid repository content should not produce a rejection diagnostic");
		snapshot.Should().NotBeNull(because: "successful validation must return an immutable materialized snapshot");
		snapshot!.Articles.Should().ContainSingle(because: "the declared resource set contains exactly one article");
		snapshot.Articles[0].Text.Should().Be("Trusted sample guidance.\n",
			because: "the article must be decoded from the validated repository file");
		snapshot.Articles[0].LegacyUris.Should().ContainSingle()
			.Which.Should().Be("docs://mcp/guides/sample-guide",
				because: "legacy aliases are part of the validated routing contract");
	}

	[Test]
	[Description("A repository requiring an unavailable MCP tool is rejected before any content is published.")]
	public void TryRead_ShouldRejectRepository_WhenRequiredToolIsUnavailable() {
		// Arrange
		JObject manifest = ValidManifest();
		((JArray)manifest["requirements"]!["tools"]!).Add("missing-tool");
		WriteResource("guidance/sample.md", "content");
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "agents cannot safely consume guidance that depends on an unavailable tool");
		snapshot.Should().BeNull(because: "a missing runtime capability must fail before publication");
		diagnostic.Should().Contain("unavailable MCP tool", because: "operators need an actionable capability diagnostic");
	}

	[Test]
	[Description("A repository outside the current Clio or MCP contract version range is rejected.")]
	public void TryRead_ShouldRejectRepository_WhenCompatibilityRangeExcludesRuntime() {
		// Arrange
		JObject manifest = ValidManifest();
		manifest["compatibility"]!["clio"]!["min"] = "9.0.0";
		manifest["compatibility"]!["clio"]!["max"] = "9.9.9";
		WriteResource("guidance/sample.md", "content");
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "the producer excludes this Clio version from its supported range");
		snapshot.Should().BeNull(because: "incompatible knowledge must never enter the active snapshot");
		diagnostic.Should().Contain("compatibility ranges", because: "the rejection should identify the contract boundary");
	}

	[Test]
	[Description("Required item and URI declarations must exactly cover the resource catalog.")]
	public void TryRead_ShouldRejectRepository_WhenRequirementsDoNotMatchResources() {
		// Arrange
		JObject manifest = ValidManifest();
		((JArray)manifest["requirements"]!["itemIds"]!).Add("undeclared-resource");
		WriteResource("guidance/sample.md", "content");
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "requirements and resources are one exact producer-consumer inventory");
		snapshot.Should().BeNull(because: "an incomplete or inflated catalog cannot be published");
		diagnostic.Should().Contain("inconsistent", because: "the diagnostic should identify catalog inconsistency");
	}

	[Test]
	[Description("Unknown fields and duplicate JSON properties are rejected to preserve exact schema semantics.")]
	public void TryRead_ShouldRejectRepository_WhenManifestDoesNotUseExactSchema() {
		// Arrange
		WriteResource("guidance/sample.md", "content");
		string manifest = ValidManifest().ToString(Formatting.None);
		manifest = manifest.Replace("\"sequence\":1", "\"sequence\":1,\"sequence\":2", StringComparison.Ordinal);
		_fileSystem.AddFile(ManifestPath(), new MockFileData(manifest));

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "duplicate properties make the producer contract ambiguous");
		snapshot.Should().BeNull(because: "non-canonical JSON must be rejected before materialization");
		diagnostic.Should().Contain("duplicate JSON property", because: "the precise schema violation should be reported");
	}

	[Test]
	[Description("Properties outside the versioned producer schema are rejected rather than silently ignored.")]
	public void TryRead_ShouldRejectRepository_WhenManifestContainsUnknownProperty() {
		// Arrange
		JObject manifest = ValidManifest();
		manifest["resources"]![0]!["publisherHint"] = "not-in-v1";
		WriteResource("guidance/sample.md", "content");
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "the consumer and producer must share one exact versioned JSON contract");
		snapshot.Should().BeNull(because: "unknown semantics must not be guessed during publication");
		diagnostic.Should().Contain("publisherHint", because: "the unknown producer field should be named for remediation");
	}

	[TestCase("bundlePath", "content/sample.md", "invalid descriptor")]
	[TestCase("mediaType", "application/octet-stream", "invalid descriptor")]
	[TestCase("sourcePath", "guidance/../outside.md", "invalid descriptor")]
	[TestCase("title", "", "invalid descriptor")]
	[TestCase("title", " padded", "invalid descriptor")]
	[TestCase("title", "unsafe\u001btitle", "invalid descriptor")]
	[TestCase("description", "", "invalid descriptor")]
	[Description("Resource paths, media types, and discovery metadata must conform to the producer schema before files are opened.")]
	public void TryRead_ShouldRejectRepository_WhenResourceDescriptorViolatesSchema(
		string property,
		string value,
		string expectedDiagnostic) {
		// Arrange
		JObject manifest = ValidManifest();
		manifest["resources"]![0]![property] = value;
		WriteResource("guidance/sample.md", "content");
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "direct Git consumption must enforce the published resource schema");
		snapshot.Should().BeNull(because: "invalid descriptors must fail before repository file access");
		diagnostic.Should().Contain(expectedDiagnostic, because: "the producer contract violation should be identifiable");
	}

	[Test]
	[Description("Resource feature requirements use stable feature identifiers.")]
	public void TryRead_ShouldRejectRepository_WhenRequiredFeatureIsInvalid() {
		// Arrange
		JObject manifest = ValidManifest();
		manifest["resources"]![0]!["requiredFeatures"] = new JArray("Invalid Feature");
		WriteResource("guidance/sample.md", "content");
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(
			because: "feature-gated discovery must use stable settings keys shared with Clio feature toggles");
		snapshot.Should().BeNull(
			because: "invalid feature requirements must fail before repository content is published");
		diagnostic.Should().Contain("invalid descriptor",
			because: "the malformed feature declaration should be identifiable to the publisher");
	}

	[TestCase("guidance", "catalog/sample.md")]
	[TestCase("reference", "guidance/sample.md")]
	[TestCase("advisory", "guidance/sample.md")]
	[TestCase("capability", "advisories/sample.md")]
	[TestCase("reference-example", "capabilities/sample.md")]
	[Description("A resource role must use its corresponding canonical repository directory.")]
	public void TryRead_ShouldRejectRepository_WhenRoleUsesAnotherSourceRoot(string role, string sourcePath) {
		// Arrange
		JObject manifest = ValidManifest();
		manifest["resources"]![0]!["role"] = role;
		manifest["resources"]![0]!["sourcePath"] = sourcePath;
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "repository structure is part of the role contract");
		snapshot.Should().BeNull(because: "content from the wrong semantic directory must not become active");
		diagnostic.Should().Contain("invalid descriptor",
			because: "operators need a clear producer-contract rejection");
	}

	[Test]
	[Description("The sum of all repository resources is bounded independently of each file limit.")]
	public void TryRead_ShouldRejectRepository_WhenAggregateResourceLimitIsExceeded() {
		// Arrange
		JObject manifest = ValidManifest(resourceCount: 9);
		byte[] content = Enumerable.Repeat((byte)'a', 4 * 1024 * 1024).ToArray();
		for (int index = 0; index < 9; index++) {
			WriteResource($"guidance/sample-{index}.md", content);
		}
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "many individually valid files must not bypass the total content budget");
		snapshot.Should().BeNull(because: "oversized repository content cannot become an active generation");
		diagnostic.Should().Contain("total size limit", because: "operators should see which bound was exceeded");
	}

	[Test]
	[Description("Snapshot identity frames each resource so content cannot shift across resource boundaries.")]
	public void TryRead_ShouldProduceDifferentDigest_WhenResourceBoundaryContentChanges() {
		// Arrange
		JObject manifest = ValidManifest(resourceCount: 2);
		WriteManifest(manifest);
		WriteResource("guidance/sample-0.md", "ab");
		WriteResource("guidance/sample-1.md", "c");
		_sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? first, out _);
		WriteResource("guidance/sample-0.md", "a");
		WriteResource("guidance/sample-1.md", "bc");

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? second,
			out string? diagnostic);

		// Assert
		result.Should().BeTrue(because: "both resource layouts satisfy the repository contract");
		diagnostic.Should().BeNull(because: "valid framed content should materialize without a diagnostic");
		second!.ContentDigest.Should().NotBe(first!.ContentDigest,
			because: "the same concatenated bytes assigned to different articles represent different guidance");
	}

	[Test]
	[Description("Repository content reached through a reparse point is rejected before it can escape the trusted checkout.")]
	public void TryRead_ShouldRejectRepository_WhenResourceIsReparsePoint() {
		// Arrange
		JObject manifest = ValidManifest();
		string resourcePath = WriteResource("guidance/sample.md", "content");
		_fileSystem.File.SetAttributes(resourcePath, FileAttributes.ReparsePoint);
		WriteManifest(manifest);

		// Act
		bool result = _sut.TryRead(_repositoryPath, LibraryId, out KnowledgeGitRepositorySnapshot? snapshot,
			out string? diagnostic);

		// Assert
		result.Should().BeFalse(because: "a tracked-looking path must not redirect reads outside the checkout");
		snapshot.Should().BeNull(because: "reparse-point content is outside the direct-Git trust boundary");
		diagnostic.Should().Contain("reparse point", because: "the trust-boundary violation should be explicit");
	}

	private JObject ValidManifest(int resourceCount = 1) {
		JArray resources = [];
		JArray itemIds = [];
		JArray resourceUris = [];
		for (int index = 0; index < resourceCount; index++) {
			string itemId = resourceCount == 1 ? ItemId : $"sample-{index}";
			string uri = $"docs://knowledge/{LibraryId}/{itemId}";
			resources.Add(new JObject {
				["itemId"] = itemId,
				["topicId"] = $"example.{itemId}",
				["role"] = "guidance",
				["title"] = $"Synthetic {itemId}",
				["description"] = $"Synthetic discovery metadata for {itemId}.",
				["uri"] = uri,
				["legacyUris"] = new JArray($"docs://mcp/guides/{itemId}"),
				["sourcePath"] = resourceCount == 1 ? "guidance/sample.md" : $"guidance/sample-{index}.md",
				["bundlePath"] = $"resources/{itemId}.md",
				["mediaType"] = "text/markdown"
			});
			itemIds.Add(itemId);
			resourceUris.Add(uri);
		}
		return new JObject {
			["$schema"] = "./schemas/v1/knowledge-repository.schema.json",
			["contractVersion"] = "1.0.0",
			["bundleSchemaVersion"] = "1.0.0",
			["libraryId"] = LibraryId,
			["libraryVersion"] = "1.0.0",
			["sequence"] = 1,
			["compatibility"] = new JObject {
				["clio"] = new JObject { ["min"] = "8.1.0", ["max"] = "8.1.999" },
				["mcpToolContract"] = new JObject { ["min"] = "1.0.0", ["max"] = "1.1.999" }
			},
			["requirements"] = new JObject {
				["tools"] = new JArray("get-guidance"),
				["itemIds"] = itemIds,
				["resourceUris"] = resourceUris
			},
			["resources"] = resources
		};
	}

	private void WriteManifest(JObject manifest) =>
		_fileSystem.AddFile(ManifestPath(), new MockFileData(manifest.ToString(Formatting.None)));

	private string ManifestPath() => _fileSystem.Path.Combine(_repositoryPath, "bundle-source.json");

	private string WriteResource(string relativePath, string content) =>
		WriteResource(relativePath, Encoding.UTF8.GetBytes(content));

	private string WriteResource(string relativePath, byte[] content) {
		string path = _fileSystem.Path.Combine(_repositoryPath, relativePath.Replace('/', _fileSystem.Path.DirectorySeparatorChar));
		_fileSystem.AddFile(path, new MockFileData(content));
		return path;
	}
}
