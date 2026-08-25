using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeGuidanceSourceTests {
	[Test]
	[Description("Guidance that requires a disabled experimental feature is absent from lookup and discovery.")]
	public void FindAndCatalog_ShouldExcludeArticle_WhenRequiredFeatureIsDisabled() {
		// Arrange
		IKnowledgeBundleActivator activator = Substitute.For<IKnowledgeBundleActivator>();
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IFeatureToggleService features = Substitute.For<IFeatureToggleService>();
		KnowledgeArticle article = new(
			"process-modeling",
			"docs://knowledge/com.creatio.clio/process-modeling",
			"Synthetic process guidance.",
			ItemId: "process-modeling",
			TopicId: "creatio.process-modeling",
			Title: "Process modeling",
			Description: "Models Creatio processes.",
			RequiredFeatures: ["process-designer"]);
		// The substituted runtime honours the eligibility predicate the source supplies, exactly as the
		// real resolver does. That is the contract under test: the source must push feature gating
		// into resolution rather than filtering the winner afterwards.
		Func<KnowledgeArticle, bool>? resolverPredicate = null;
		runtime.Find(Arg.Any<string>(), Arg.Any<Func<KnowledgeArticle, bool>?>()).Returns(call => {
			resolverPredicate = call.ArgAt<Func<KnowledgeArticle, bool>?>(1);
			return resolverPredicate?.Invoke(article) == false
				? new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.NotFound, null, 4)
				: new KnowledgeArticleLookup(KnowledgeArticleLookupStatus.Active, article, 4);
		});
		runtime.GetNames(Arg.Any<Func<KnowledgeArticle, bool>?>()).Returns(call =>
			call.ArgAt<Func<KnowledgeArticle, bool>?>(0)?.Invoke(article) == false
				? []
				: new[] { article.ItemId, article.TopicId });
		runtime.GetArticlesByRole(Arg.Any<string>()).Returns([]);
		runtime.SnapshotToken.Returns(new object());
		features.IsFeatureEnabled("process-designer").Returns(false);
		KnowledgeGuidanceSource source = new(activator, runtime, features);

		// Act
		KnowledgeArticleLookup lookup = source.FindByName(article.ItemId);
		IReadOnlyList<KnowledgeGuidanceDescriptor> catalog = source.GetCatalog();

		// Assert
		lookup.Status.Should().Be(KnowledgeArticleLookupStatus.NotFound,
			because: "disabled experimental surfaces must not advertise guidance for tools the host does not expose");
		catalog.Should().BeEmpty(
			because: "resources/list must obey the same publisher-declared feature requirement as get-guidance");
		resolverPredicate.Should().NotBeNull(
			because: "eligibility must reach the resolver, not be applied to whatever the resolver already picked");
		resolverPredicate!(article).Should().BeFalse(
			because: "the predicate the resolver receives must reject the gated article before priority and pin selection");
		activator.Received(2).EnsureActivated();
	}

	[Test]
	[Description("Guidance that requires an enabled feature remains available under its stable item ID.")]
	public void FindByName_ShouldReturnArticle_WhenRequiredFeatureIsEnabled() {
		// Arrange
		IKnowledgeBundleActivator activator = Substitute.For<IKnowledgeBundleActivator>();
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IFeatureToggleService features = Substitute.For<IFeatureToggleService>();
		KnowledgeArticle article = new(
			"process-modeling",
			"docs://knowledge/com.creatio.clio/process-modeling",
			"Synthetic process guidance.",
			ItemId: "process-modeling",
			TopicId: "creatio.process-modeling",
			RequiredFeatures: ["process-designer"]);
		runtime.Find(article.ItemId, Arg.Any<Func<KnowledgeArticle, bool>?>()).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Active,
			article,
			4));
		features.IsFeatureEnabled("process-designer").Returns(true);
		KnowledgeGuidanceSource source = new(activator, runtime, features);

		// Act
		KnowledgeArticleLookup lookup = source.FindByName(article.ItemId);

		// Assert
		lookup.Status.Should().Be(KnowledgeArticleLookupStatus.Active,
			because: "enabling the matching feature should expose the publisher-owned guidance without reinstalling knowledge");
	}

	[Test]
	[Description("Reference fragments are discoverable as MCP resources without becoming bare get-guidance names.")]
	public void Catalog_ShouldIncludeReferenceWithoutGuidanceName_WhenReferenceRoleIsActive() {
		// Arrange
		IKnowledgeBundleActivator activator = Substitute.For<IKnowledgeBundleActivator>();
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IFeatureToggleService features = Substitute.For<IFeatureToggleService>();
		KnowledgeArticle article = new(
			"query-patterns",
			"docs://knowledge/com.creatio.clio/atf-repository-dev-query-patterns",
			"Reference content.",
			ItemId: "atf-repository-dev-query-patterns",
			TopicId: "creatio.atf-repository-dev.query-patterns",
			Role: "reference",
			Title: "ATF.Repository query patterns",
			Description: "Detailed query examples.");
		KnowledgeArticleProvenance provenance = new(
			"creatio-curated",
			"com.creatio.clio",
			"1.13.21",
			article.ItemId,
			article.TopicId,
			5,
			"digest",
			article.LocalPath);
		runtime.GetNames().Returns([]);
		runtime.GetArticlesByRole("reference").Returns([
			new KnowledgeRoleArticle(article, provenance, 100, KnowledgeSourceParticipation.Authoritative)
		]);
		KnowledgeGuidanceSource source = new(activator, runtime, features);

		// Act
		IReadOnlyList<string> guidanceNames = source.GetNames();
		IReadOnlyList<KnowledgeGuidanceDescriptor> catalog = source.GetCatalog();

		// Assert
		guidanceNames.Should().BeEmpty(
			because: "reference fragments must be loaded through resource URIs rather than the guide-name surface");
		catalog.Should().ContainSingle(item => item.Name == article.ItemId,
			because: "resources/list must still expose detailed publisher references to agents");
	}

	[Test]
	[Description("A maximum-sized catalog is resolved once per active snapshot and re-resolved when the snapshot changes.")]
	public void GetDiscoveryCatalog_ShouldResolveOncePerSnapshot_WhenPagedRepeatedly() {
		// Arrange
		const int catalogSize = 1024;
		const int pagesOverTheCatalog = 11;
		IKnowledgeBundleActivator activator = Substitute.For<IKnowledgeBundleActivator>();
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IFeatureToggleService features = Substitute.For<IFeatureToggleService>();
		KnowledgeArticle[] articles = Enumerable.Range(0, catalogSize)
			.Select(index => new KnowledgeArticle(
				$"topic-{index:D4}",
				$"docs://knowledge/com.example.partner/item-{index:D4}",
				"# body",
				ItemId: $"item-{index:D4}",
				TopicId: $"topic-{index:D4}"))
			.ToArray();
		Dictionary<string, KnowledgeArticle> byItemId = articles.ToDictionary(article => article.ItemId);
		int resolveCalls = 0;
		object firstSnapshot = new();
		runtime.SnapshotToken.Returns(_ => firstSnapshot);
		runtime.GetNames(Arg.Any<Func<KnowledgeArticle, bool>?>())
			.Returns(_ => articles.Select(article => article.ItemId).ToArray());
		runtime.Find(Arg.Any<string>(), Arg.Any<Func<KnowledgeArticle, bool>?>()).Returns(call => {
			resolveCalls++;
			return new KnowledgeArticleLookup(
				KnowledgeArticleLookupStatus.Active,
				byItemId[call.ArgAt<string>(0)],
				7);
		});
		runtime.GetArticlesByRole(Arg.Any<string>()).Returns([]);
		KnowledgeGuidanceSource source = new(activator, runtime, features);

		// Act
		IReadOnlyList<KnowledgeGuidanceDescriptor> firstPageView = source.GetDiscoveryCatalog();
		for (int page = 1; page < pagesOverTheCatalog; page++) {
			source.GetDiscoveryCatalog();
		}
		int callsBeforeSnapshotChange = resolveCalls;
		object secondSnapshot = new();
		runtime.SnapshotToken.Returns(_ => secondSnapshot);
		source.GetDiscoveryCatalog();

		// Assert
		firstPageView.Should().HaveCount(catalogSize,
			because: "every active article must remain discoverable through resource listing");
		firstPageView.Select(article => article.Uri).Should().BeInAscendingOrder(StringComparer.Ordinal,
			because: "cursor paging is offset-based, so the catalog order must be stable and deterministic");
		callsBeforeSnapshotChange.Should().Be(catalogSize,
			because: "enumerating every page must resolve the catalog once, not once per page");
		resolveCalls.Should().Be(catalogSize * 2,
			because: "a new active snapshot must invalidate the cached catalog so fresh content is served");
	}
}
