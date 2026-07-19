using System.Collections.Generic;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeResolverTests {
	private readonly IKnowledgeResolver _resolver = new KnowledgeResolver();

	[Test]
	[Description("An exact namespaced route selects only the addressed library and item.")]
	public void Find_ShouldResolveExactLibraryItem_WhenNamespacedUriIsUsed() {
		// Arrange
		KnowledgeLibrarySnapshot creatio = Library("creatio", "com.creatio.clio", 100,
			KnowledgeSourceParticipation.Authoritative, Article("esq", "creatio-esq"));
		KnowledgeLibrarySnapshot partner = Library("partner", "com.example.partner", 200,
			KnowledgeSourceParticipation.Authoritative, Article("esq", "partner-esq"));

		// Act
		KnowledgeArticleLookup result = _resolver.Find(
			"docs://knowledge/com.creatio.clio/creatio-esq",
			[creatio, partner],
			new Dictionary<string, string>());

		// Assert
		result.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "a valid exact route must resolve without logical-topic competition");
		result.Provenance!.LibraryId.Should().Be("com.creatio.clio",
			because: "exact namespaced lookup must not fall through to a higher-priority library");
	}

	[Test]
	[Description("An authoritative source wins a logical topic over a higher-priority supplement.")]
	public void Find_ShouldPreferAuthoritativeLibrary_WhenSupplementHasHigherPriority() {
		// Arrange
		KnowledgeLibrarySnapshot authoritative = Library("creatio", "com.creatio.clio", 10,
			KnowledgeSourceParticipation.Authoritative, Article("esq", "creatio-esq"));
		KnowledgeLibrarySnapshot supplement = Library("partner", "com.example.partner", 500,
			KnowledgeSourceParticipation.Supplement, Article("esq", "partner-esq"));

		// Act
		KnowledgeArticleLookup result = _resolver.Find("esq", [authoritative, supplement],
			new Dictionary<string, string>());

		// Assert
		result.Provenance!.LibraryId.Should().Be("com.creatio.clio",
			because: "supplemental content must not replace an eligible authoritative topic");
	}

	[Test]
	[Description("A topic pin overrides source priority for an eligible library.")]
	public void Find_ShouldHonorTopicPin_WhenPinnedLibraryIsEligible() {
		// Arrange
		KnowledgeLibrarySnapshot first = Library("first", "com.example.first", 100,
			KnowledgeSourceParticipation.Authoritative, Article("esq", "first-esq"));
		KnowledgeLibrarySnapshot second = Library("second", "com.example.second", 1,
			KnowledgeSourceParticipation.Authoritative, Article("esq", "second-esq"));
		Dictionary<string, string> pins = new() { ["esq"] = "com.example.second" };

		// Act
		KnowledgeArticleLookup result = _resolver.Find("esq", [first, second], pins);

		// Assert
		result.Provenance!.LibraryId.Should().Be("com.example.second",
			because: "an explicit operator pin is stronger than numeric priority");
	}

	[Test]
	[Description("Equal-priority authoritative libraries produce a visible ambiguity instead of order-dependent selection.")]
	public void Find_ShouldRejectAmbiguity_WhenEligiblePrioritiesTie() {
		// Arrange
		KnowledgeLibrarySnapshot first = Library("first", "com.example.first", 42,
			KnowledgeSourceParticipation.Authoritative, Article("esq", "first-esq"));
		KnowledgeLibrarySnapshot second = Library("second", "com.example.second", 42,
			KnowledgeSourceParticipation.Authoritative, Article("esq", "second-esq"));

		// Act
		KnowledgeArticleLookup result = _resolver.Find("esq", [second, first],
			new Dictionary<string, string>());

		// Assert
		result.Status.Should().Be(KnowledgeArticleLookupStatus.Ambiguous,
			because: "configuration order must never silently break a priority tie");
		result.Diagnostic.Should().Contain("topic pin",
			because: "the failure should tell the operator how to make selection deterministic");
	}

	[Test]
	[Description("Isolated libraries are reachable by exact route but excluded from logical topic resolution.")]
	public void Find_ShouldExcludeIsolatedLibrary_FromLogicalTopics() {
		// Arrange
		KnowledgeLibrarySnapshot isolated = Library("lab", "com.example.lab", 500,
			KnowledgeSourceParticipation.Isolated, Article("esq", "lab-esq"));

		// Act
		KnowledgeArticleLookup logical = _resolver.Find("esq", [isolated], new Dictionary<string, string>());
		KnowledgeArticleLookup exact = _resolver.Find("docs://knowledge/com.example.lab/lab-esq", [isolated],
			new Dictionary<string, string>());

		// Assert
		logical.Status.Should().Be(KnowledgeArticleLookupStatus.NotFound,
			because: "isolated libraries must not participate in logical-topic competition");
		exact.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "isolation still permits explicit namespaced access");
	}

	[Test]
	[Description("A signed legacy URI resolves directly to its canonical article while preserving publisher provenance.")]
	public void Find_ShouldResolveCanonicalArticle_WhenLegacyUriMatchesOneLibrary() {
		// Arrange
		const string legacyUri = "docs://mcp/guides/esq-filters";
		KnowledgeLibrarySnapshot library = Library(
			"creatio",
			"com.creatio.clio",
			100,
			KnowledgeSourceParticipation.Authoritative,
			Article("esq", "creatio-esq", legacyUri));

		// Act
		KnowledgeArticleLookup result = _resolver.Find(legacyUri, [library], new Dictionary<string, string>());

		// Assert
		result.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "signed legacy routes keep existing agent links working after namespacing");
		result.Article!.ItemId.Should().Be("creatio-esq",
			because: "the legacy route must resolve to the canonical signed item");
		result.Provenance!.LibraryId.Should().Be("com.creatio.clio",
			because: "legacy lookup must retain the actual publisher identity");
	}

	[Test]
	[Description("The same signed legacy URI in multiple active libraries is ambiguous and never resolved by priority.")]
	public void Find_ShouldRejectAmbiguity_WhenLegacyUriCollidesAcrossLibraries() {
		// Arrange
		const string legacyUri = "docs://mcp/guides/esq-filters";
		KnowledgeLibrarySnapshot first = Library(
			"creatio", "com.creatio.clio", 100, KnowledgeSourceParticipation.Authoritative,
			Article("esq", "creatio-esq", legacyUri));
		KnowledgeLibrarySnapshot second = Library(
			"partner", "com.example.partner", 1, KnowledgeSourceParticipation.Supplement,
			Article("partner-esq", "partner-esq", legacyUri));

		// Act
		KnowledgeArticleLookup result = _resolver.Find(
			legacyUri,
			[first, second],
			new Dictionary<string, string>());

		// Assert
		result.Status.Should().Be(KnowledgeArticleLookupStatus.Ambiguous,
			because: "a global compatibility URI cannot silently select one publisher by priority");
		result.Diagnostic.Should().Contain("Use a namespaced knowledge URI",
			because: "the operator needs a deterministic collision escape hatch");
	}

	private static KnowledgeLibrarySnapshot Library(
		string alias,
		string libraryId,
		int priority,
		KnowledgeSourceParticipation participation,
		KnowledgeArticle article) =>
		new(alias, libraryId, priority, participation, 7, "synthetic-digest", [article]);

	private static KnowledgeArticle Article(string topicId, string itemId, params string[] legacyUris) =>
		new(
			topicId,
			$"docs://knowledge/example/{itemId}",
			$"# {itemId}",
			ItemId: itemId,
			TopicId: topicId,
			LocalPath: $"resources/{itemId}.md",
			LegacyUris: legacyUris);
}
