namespace Clio.Tests.Command;

using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageEntityDataSourceSeederTests {

	private const string Entity = "UsrDemoEntity";

	[Test]
	[Description("A template with a dataSources object but no primary data source gets a page-scoped PDS made primary, in the two-step form of stock record pages.")]
	public void BuildFromTemplateModelConfig_AddsPrimaryPds_WhenTemplateHasDataSourcesButNoPrimary() {
		// Arrange
		JsonObject modelConfig = JsonNode.Parse("""{"dataSources":{"AttachmentListDS":{"scope":"viewElement"}}}""")!.AsObject();

		// Act
		PageEntityDataSourceSeed seed = PageEntityDataSourceSeeder.BuildFromTemplateModelConfig(modelConfig, Entity);

		// Assert
		seed.DataSourceName.Should().Be("PDS", because: "PDS is the conventional primary data source name");
		seed.IsPrimary.Should().BeTrue(because: "record pages bind fields to the primary data source");
		seed.ModelConfigDiff.Should().HaveCount(2, because: "primaryDataSourceName and the data source are merged separately");
		seed.ModelConfigDiff[1]["path"].Should().BeEquivalentTo(new JArray("dataSources"),
			because: "the template's dataSources object exists, so merging into it keeps AttachmentListDS");
		seed.ModelConfigDiff[1]["values"]!["PDS"]!["scope"]!.ToString().Should().Be("page",
			because: "the primary data source of a record page is page-scoped");
	}

	[Test]
	[Description("A template with no dataSources object gets one root merge, because a merge whose path is missing is skipped by the runtime applier.")]
	public void BuildFromTemplateModelConfig_UsesRootMerge_WhenTemplateHasNoDataSources() {
		// Act
		PageEntityDataSourceSeed seed = PageEntityDataSourceSeeder.BuildFromTemplateModelConfig(new JsonObject(), Entity);

		// Assert
		seed.ModelConfigDiff.Should().HaveCount(1, because: "one root merge carries both the name and the data source");
		seed.ModelConfigDiff[0]["path"].Should().BeEquivalentTo(new JArray(), because: "the root always exists");
		seed.ModelConfigDiff[0]["values"]!["dataSources"]!["PDS"]!["config"]!["entitySchemaName"]!.ToString()
			.Should().Be(Entity, because: "PDS is bound to the requested entity");
	}

	[Test]
	[Description("A template that already names a primary data source gets that data source bound to the entity; no PDS is added.")]
	public void BuildFromTemplateModelConfig_BindsExistingPrimary_WhenTemplateNamesOne() {
		// Arrange
		JsonObject modelConfig = JsonNode.Parse(
			"""{"primaryDataSourceName":"MainDS","dataSources":{"MainDS":{"scope":"page","config":{}}}}""")!.AsObject();

		// Act
		PageEntityDataSourceSeed seed = PageEntityDataSourceSeeder.BuildFromTemplateModelConfig(modelConfig, Entity);

		// Assert
		seed.DataSourceName.Should().Be("MainDS", because: "the template's own primary data source is reused");
		seed.IsPrimary.Should().BeTrue(because: "it is the template's primary data source");
		seed.ModelConfigDiff.Should().ContainSingle(because: "only the entity of the existing data source changes");
		seed.ModelConfigDiff[0]["path"].Should().BeEquivalentTo(new JArray("dataSources", "MainDS", "config"),
			because: "the merge targets the existing data source's config");
	}

	[Test]
	[Description("The list page template declares a viewElement PDS without a primary data source; PDS is bound to the entity and not made primary.")]
	public void BuildFromTemplateModelConfig_BindsListPds_WithoutMakingItPrimary() {
		// Arrange
		JsonObject modelConfig = JsonNode.Parse("""{"dataSources":{"PDS":{"scope":"viewElement","config":{}}}}""")!.AsObject();

		// Act
		PageEntityDataSourceSeed seed = PageEntityDataSourceSeeder.BuildFromTemplateModelConfig(modelConfig, Entity);

		// Assert
		seed.DataSourceName.Should().Be("PDS", because: "the list data source is the one to bind");
		seed.IsPrimary.Should().BeFalse(because: "stock list pages have no primary data source");
		seed.ModelConfigDiff.ToString().Should().NotContain("primaryDataSourceName",
			because: "making the list source primary would change how the list page loads");
	}

	[Test]
	[Description("A template data source without a config object gets the entity through a merge on the data source itself, because a merge into a missing path is skipped.")]
	public void BuildFromTemplateModelConfig_MergesConfigIntoDataSource_WhenTemplateSourceHasNoConfig() {
		// Arrange
		JsonObject modelConfig = JsonNode.Parse("""{"dataSources":{"PDS":{"scope":"viewElement"}}}""")!.AsObject();

		// Act
		PageEntityDataSourceSeed seed = PageEntityDataSourceSeeder.BuildFromTemplateModelConfig(modelConfig, Entity);

		// Assert
		seed.ModelConfigDiff[0]["path"].Should().BeEquivalentTo(new JArray("dataSources", "PDS"),
			because: "the data source exists, its config does not");
		seed.ModelConfigDiff[0]["values"]!["config"]!["entitySchemaName"]!.ToString().Should().Be(Entity,
			because: "the config object is created by the merge with the entity inside it");
	}

	[Test]
	[Description("An unreadable template hierarchy binds no data source and says so instead of guessing a shape or failing page creation.")]
	public void BuildSeed_BindsNothingWithWarning_WhenHierarchyCannotBeRead() {
		// Arrange
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>())
			.Returns(_ => throw new System.InvalidOperationException("designer down"));
		var seeder = new PageEntityDataSourceSeeder(hierarchyClient, Substitute.For<IPageSchemaBodyParser>(),
			Substitute.For<IPageBundleBuilder>());

		// Act
		PageEntityDataSourceSeed seed = seeder.BuildSeed("tpl-uid", "pkg-uid", Entity);

		// Assert
		seed.ModelConfigDiff.Should().BeEmpty(
			because: "a blind primary PDS would turn the list template's viewElement PDS into a page-scoped primary source");
		seed.DataSourceName.Should().BeNull(because: "nothing was bound");
		seed.Warning.Should().Contain("designer down", because: "the caller must learn the template was not inspected");
	}

	[Test]
	[Description("An empty template hierarchy binds no data source and says so instead of adding a blind primary PDS.")]
	public void BuildSeed_BindsNothingWithWarning_WhenHierarchyIsEmpty() {
		// Arrange
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new List<PageDesignerHierarchySchema>());
		IPageBundleBuilder bundleBuilder = Substitute.For<IPageBundleBuilder>();
		var seeder = new PageEntityDataSourceSeeder(hierarchyClient, Substitute.For<IPageSchemaBodyParser>(),
			bundleBuilder);

		// Act
		PageEntityDataSourceSeed seed = seeder.BuildSeed("tpl-uid", "pkg-uid", Entity);

		// Assert
		seed.ModelConfigDiff.Should().BeEmpty(
			because: "with no template to inspect, a blind primary PDS could contradict the template's real shape");
		seed.DataSourceName.Should().BeNull(because: "nothing was bound");
		seed.Warning.Should().Contain("hierarchy is empty", because: "the caller must learn the template was not inspected");
		bundleBuilder.DidNotReceiveWithAnyArgs().Build(default!);
	}

	[Test]
	[Description("An unknown (null) template model config binds no data source and says so.")]
	public void BuildFromTemplateModelConfig_BindsNothingWithWarning_WhenModelConfigIsNull() {
		// Act
		PageEntityDataSourceSeed seed = PageEntityDataSourceSeeder.BuildFromTemplateModelConfig(null, Entity);

		// Assert
		seed.ModelConfigDiff.Should().BeEmpty(because: "null means the template shape is unknown");
		seed.DataSourceName.Should().BeNull(because: "nothing was bound");
		seed.Warning.Should().Contain(Entity, because: "the warning names the entity left unbound");
	}

	[Test]
	[Description("BuildSeed reads the template's merged model config through the hierarchy and bundle builder.")]
	public void BuildSeed_UsesTemplateBundleModelConfig() {
		// Arrange
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetParentSchemas("tpl-uid", "pkg-uid").Returns(new List<PageDesignerHierarchySchema> {
			new() { UId = "tpl-uid", Name = "ListPageV3Template", Body = "body" }
		});
		IPageSchemaBodyParser parser = Substitute.For<IPageSchemaBodyParser>();
		parser.Parse("body").Returns(new PageParsedSchemaBody());
		IPageBundleBuilder bundleBuilder = Substitute.For<IPageBundleBuilder>();
		bundleBuilder.Build(Arg.Any<IReadOnlyList<PageSchemaBundlePart>>()).Returns(new PageBundleInfo {
			ModelConfig = JsonNode.Parse("""{"dataSources":{"PDS":{"scope":"viewElement"}}}""")!.AsObject()
		});
		var seeder = new PageEntityDataSourceSeeder(hierarchyClient, parser, bundleBuilder);

		// Act
		PageEntityDataSourceSeed seed = seeder.BuildSeed("tpl-uid", "pkg-uid", Entity);

		// Assert
		seed.IsPrimary.Should().BeFalse(because: "the template's bundle declares a non-primary PDS");
		seed.Warning.Should().BeNull(because: "the template was read");
	}
}
