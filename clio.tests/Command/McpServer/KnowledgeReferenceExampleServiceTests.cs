using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeReferenceExampleServiceTests {
	private IKnowledgeBundleActivator _activator = null!;
	private ServiceProvider _container = null!;
	private IKnowledgeBundleRuntime _runtime = null!;
	private IKnowledgeReferenceExampleService _service = null!;

	[SetUp]
	public void SetUp() {
		_activator = Substitute.For<IKnowledgeBundleActivator>();
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		ServiceCollection services = new();
		services.AddSingleton(_activator);
		services.AddSingleton(_runtime);
		services.AddSingleton<IKnowledgeReferenceExampleParser, KnowledgeReferenceExampleParser>();
		services.AddSingleton<IKnowledgeReferenceExampleService, KnowledgeReferenceExampleService>();
		_container = services.BuildServiceProvider();
		_service = _container.GetRequiredService<IKnowledgeReferenceExampleService>();
	}

	[TearDown]
	public void TearDown() {
		_activator.ClearReceivedCalls();
		_runtime.ClearReceivedCalls();
		_container.Dispose();
	}

	[Test]
	[Description("Maps complete catalog metadata and verified bundle provenance without cloning a reference repository.")]
	public void List_ShouldMapMetadataAndProvenance_WithoutCloningReferenceRepository() {
		// Arrange
		_runtime.GetArticlesByRole(KnowledgeReferenceExampleService.ReferenceExampleRole).Returns([
			Article("creatio", "com.creatio.clio", 75, KnowledgeSourceParticipation.Authoritative,
				"kafka", "Kafka reference", "kafka", "published", "kafka-native", 'a')
		]);

		// Act
		KnowledgeReferenceExampleListResult result = _service.List(new(null, null, null, null));

		// Assert
		result.Success.Should().BeTrue(because: "the complete catalog document is valid");
		KnowledgeReferenceExample example = result.Examples.Should().ContainSingle(
			because: "the active catalog contains one reference example").Subject;
		example.SourceAlias.Should().Be("creatio", because: "agents need the trusted-source provenance");
		example.LibraryId.Should().Be("com.creatio.clio", because: "catalog identities are library scoped");
		example.SourcePriority.Should().Be(75, because: "source priority is part of conflict provenance");
		example.SourceParticipation.Should().Be("authoritative", because: "source participation must remain visible");
		example.BundleSequence.Should().Be(42, because: "agents need the exact catalog generation");
		example.BundleDigest.Should().Be("sha256:test-bundle", because: "the verified bundle digest identifies provenance");
		example.CatalogItemId.Should().Be("example-kafka", because: "the catalog item must retain its bundle identity");
		example.Id.Should().Be("kafka", because: "the stable example identity comes from the YAML document");
		example.PrimaryUseCase.Should().Be(new KnowledgeReferenceExampleUseCase("kafka", "Kafka integration"),
			because: "the primary use case drives example discovery");
		example.Source.Repository.Should().Be("https://github.com/example/kafka",
			because: "discovery returns coordinates that an agent may later choose to clone");
		example.Source.Revision.Should().Be(new string('a', 40),
			because: "reference coordinates must use an immutable revision");
		example.EntryPoints.Should().Contain("workspace", "workspace",
			because: "agents need the repository-relative entry point");
		example.SupportingCapabilities.Should().Equal(["app-lifecycle", "kafka-native"],
			because: "capabilities are normalized into deterministic order");
		example.Compatibility.Should().Be(new KnowledgeReferenceExampleCompatibility("validated", "Creatio 8.1+"),
			because: "compatibility constraints must be visible before pulling an example");
		example.Trust.Should().Be(new KnowledgeReferenceExampleTrust("ATF", "vetted"),
			because: "publisher trust is catalog metadata rather than an inferred property");
		example.Notes.Should().Equal(["Example only"], because: "publisher notes complete the example metadata");
		_activator.Received(1).EnsureActivated();
		_runtime.Received(1).GetArticlesByRole(KnowledgeReferenceExampleService.ReferenceExampleRole);
	}

	[Test]
	[Description("Returns every registered catalog item so agents can discover examples before pulling their leaf repositories.")]
	public void List_ShouldReturnAllExamples_WhenNoFilterIsProvided() {
		// Arrange
		_runtime.GetArticlesByRole(KnowledgeReferenceExampleService.ReferenceExampleRole).Returns([
			Article("creatio", "com.creatio.clio", 100, KnowledgeSourceParticipation.Authoritative,
				"kafka", "Kafka reference", "kafka", "published", "esq", 'a'),
			Article("partner", "com.partner.examples", 10, KnowledgeSourceParticipation.Supplement,
				"pubsub", "Google Pub/Sub reference", "google-pubsub", "draft", "atf", 'b')
		]);

		// Act
		KnowledgeReferenceExampleListResult result = _service.List(new(null, null, null, null));

		// Assert
		result.Examples.Select(example => example.Id).Should().Equal(["kafka", "google-pubsub"],
			because: "unfiltered discovery must surface examples from every active catalog in priority order");
		result.Diagnostics.Should().BeEmpty(because: "both catalog entries are valid");
	}

	[TestCase("partner", null, null, null, "google-pubsub")]
	[TestCase(null, "Pub/Sub", null, null, "google-pubsub")]
	[TestCase(null, null, "atf", null, "google-pubsub")]
	[TestCase(null, null, null, "published", "kafka")]
	[Description("Applies source, free-text, capability, and status filters independently without hiding unrelated unfiltered metadata.")]
	public void List_ShouldApplyRequestedFilter_WhenFilterIsProvided(
		string? source,
		string? search,
		string? capability,
		string? status,
		string expectedId) {
		// Arrange
		_runtime.GetArticlesByRole(KnowledgeReferenceExampleService.ReferenceExampleRole).Returns([
			Article("creatio", "com.creatio.clio", 100, KnowledgeSourceParticipation.Authoritative,
				"kafka", "Kafka reference", "kafka", "published", "esq", 'a'),
			Article("partner", "com.partner.examples", 10, KnowledgeSourceParticipation.Supplement,
				"pubsub", "Google Pub/Sub reference", "google-pubsub", "draft", "atf", 'b')
		]);

		// Act
		KnowledgeReferenceExampleListResult result = _service.List(new(source, search, capability, status));

		// Assert
		result.Examples.Should().ContainSingle(example => example.Id == expectedId,
			because: "each optional filter must narrow the discoverable catalog predictably");
	}

	[Test]
	[Description("Skips malformed catalog YAML and returns a stable diagnostic instead of failing the complete discovery request.")]
	public void List_ShouldReturnDiagnostic_WhenCatalogItemIsMalformed() {
		// Arrange
		KnowledgeRoleArticle malformed = RoleArticle(
			"creatio", "com.creatio.clio", 100, KnowledgeSourceParticipation.Authoritative,
			"bad-example", "schemaVersion: [not valid");
		_runtime.GetArticlesByRole(KnowledgeReferenceExampleService.ReferenceExampleRole).Returns([malformed]);

		// Act
		KnowledgeReferenceExampleListResult result = _service.List(new(null, null, null, null));

		// Assert
		result.Success.Should().BeFalse(because: "malformed publisher metadata must be reported to the caller");
		result.Examples.Should().BeEmpty(because: "invalid catalog content must never be surfaced as a trusted example");
		result.Diagnostics.Should().ContainSingle(message =>
			message.Contains("bad-example", StringComparison.Ordinal)
			&& message.Contains("invalid YAML", StringComparison.Ordinal),
			because: "the diagnostic must identify both the failing catalog item and parsing failure");
	}

	[Test]
	[Description("Reports activation diagnostics instead of presenting an unavailable catalog as an empty catalog.")]
	public void List_ShouldReturnDiagnostic_WhenCatalogActivationIsTemporarilyUnavailable() {
		// Arrange
		_activator.LastDiagnostic.Returns(
			"Git knowledge source 'creatio' is synchronizing; activation will retry on the next request.");
		_runtime.GetArticlesByRole(KnowledgeReferenceExampleService.ReferenceExampleRole).Returns([]);

		// Act
		KnowledgeReferenceExampleListResult result = _service.List(new(null, null, null, null));

		// Assert
		result.Success.Should().BeFalse(
			because: "a transient activation failure is not equivalent to an empty trusted catalog");
		result.Examples.Should().BeEmpty(
			because: "no catalog was activated for this request");
		result.Diagnostics.Should().ContainSingle(message => message.Contains("synchronizing", StringComparison.Ordinal),
			because: "the caller needs an actionable retry diagnostic");
	}

	[Test]
	[Description("Rejects terminal control characters in publisher-authored text before catalog metadata is displayed.")]
	public void List_ShouldRejectCatalogItem_WhenDisplayTextContainsControlCharacters() {
		// Arrange
		KnowledgeRoleArticle unsafeArticle = Article(
			"creatio", "com.creatio.clio", 100, KnowledgeSourceParticipation.Authoritative,
			"unsafe", "Unsafe\u001b[2J reference", "unsafe", "published", "esq", 'a');
		_runtime.GetArticlesByRole(KnowledgeReferenceExampleService.ReferenceExampleRole).Returns([unsafeArticle]);

		// Act
		KnowledgeReferenceExampleListResult result = _service.List(new(null, null, null, null));

		// Assert
		result.Success.Should().BeFalse(because: "publisher-authored control characters are unsafe for terminal output");
		result.Examples.Should().BeEmpty(because: "unsafe catalog metadata must never be presented as trusted content");
		result.Diagnostics.Should().ContainSingle(message => message.Contains("title", StringComparison.Ordinal),
			because: "the caller should receive a bounded validation diagnostic without rendering the unsafe title");
	}

	private static KnowledgeRoleArticle Article(
		string sourceAlias,
		string libraryId,
		int priority,
		KnowledgeSourceParticipation participation,
		string itemSuffix,
		string title,
		string primaryUseCase,
		string status,
		string capability,
		char revisionCharacter) => RoleArticle(
		sourceAlias,
		libraryId,
		priority,
		participation,
		$"example-{itemSuffix}",
		$$"""
		schemaVersion: 0
		id: {{primaryUseCase}}
		title: {{title}}
		status: {{status}}
		primaryUseCase:
		  id: {{primaryUseCase}}
		  summary: {{title.Replace(" reference", " integration", StringComparison.Ordinal)}}
		source:
		  repository: https://github.com/example/{{primaryUseCase}}
		  revision: {{new string(revisionCharacter, 40)}}
		  defaultBranch: main
		entryPoints:
		  workspace: workspace
		supportingCapabilities:
		  - {{capability}}
		  - app-lifecycle
		compatibility:
		  status: validated
		  details: Creatio 8.1+
		trust:
		  publisher: ATF
		  level: vetted
		notes:
		  - Example only
		""");

	private static KnowledgeRoleArticle RoleArticle(
		string sourceAlias,
		string libraryId,
		int priority,
		KnowledgeSourceParticipation participation,
		string itemId,
		string yaml) {
		KnowledgeArticle article = new(
			itemId,
			$"docs://knowledge/{libraryId}/{itemId}",
			yaml,
			libraryId,
			itemId,
			itemId,
			KnowledgeReferenceExampleService.ReferenceExampleRole,
			$"catalog/reference-examples/{itemId}.yaml");
		KnowledgeArticleProvenance provenance = new(
			sourceAlias,
			libraryId,
			itemId,
			itemId,
			42,
			"sha256:test-bundle",
			article.LocalPath);
		return new KnowledgeRoleArticle(article, provenance, priority, participation);
	}
}
