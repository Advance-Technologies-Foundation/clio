using System.Collections.Generic;
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
		runtime.Find(Arg.Any<string>()).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Active,
			article,
			4));
		runtime.GetNames().Returns([article.ItemId, article.TopicId]);
		runtime.GetArticlesByRole("reference").Returns([]);
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
		runtime.Find(article.ItemId).Returns(new KnowledgeArticleLookup(
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
}
