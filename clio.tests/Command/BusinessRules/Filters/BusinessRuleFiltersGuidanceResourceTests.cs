using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleFiltersGuidanceResourceTests {

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for the apply-static-filter friendly contract.")]
	public void BusinessRuleFiltersGuidanceResource_Should_Return_Canonical_Business_Rule_Filters_Guide() {
		// Arrange
		BusinessRuleFiltersGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the business-rule-filters guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/business-rule-filters",
			because: "the resource should expose a stable MCP URI for business-rule-filters guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the business-rule-filters guide should be discoverable as plain text");
		article.Text.Should().Contain("clio MCP business-rule filters guide",
			because: "the article should open with the canonical title for downstream contains-checks");
		article.Text.Should().Contain("apply-static-filter",
			because: "the guide should reference the action it documents");
		article.Text.Should().Contain("Inferred root schema rule",
			because: "the guide should publish the rootSchemaName inference contract");
		article.Text.Should().Contain("Lookup-record validation",
			because: "the guide should describe the create-time existence check that mirrors the creatio-ui lookup picker");
		article.Text.Should().Contain("filter.lookup-record-not-found",
			because: "the guide should call out the lookup-record-not-found error path");
		article.Text.Should().Contain("filter.items-not-allowed",
			because: "the guide should warn against sending items alongside targetAttribute / filter");
		article.Text.Should().Contain("backwardReferenceFilters",
			because: "the guide should document the backward-reference filter shape");
		article.Text.Should().Contain("EQUAL",
			because: "the guide should enumerate at least the EQUAL comparison token");
		article.Text.Should().Contain("CONTAIN",
			because: "the guide should enumerate the text-only CONTAIN token");
		article.Text.Should().Contain("IS_NULL",
			because: "the guide should enumerate the unary IS_NULL token");
		article.Text.Should().Contain("Intent decomposition",
			because: "the guide should teach LLM callers how to translate natural-language prompts into target + filters + backward references");
		article.Text.Should().Contain("Creatio domain vocabulary",
			because: "the guide should map domain nouns (Customer, Lead, Opportunity) to schemas and lookup values so the LLM stops conflating them");
		article.Text.Should().Contain("Worked examples",
			because: "the guide should include end-to-end natural-language -> payload mappings for the recurring prompt shapes");
		article.Text.Should().Contain("Customer",
			because: "the vocabulary section must call out that 'Customer' is an Account.Type value, not a schema name");
		article.Text.Should().Contain("[Lead:QualifiedAccount]",
			because: "the worked examples should include the canonical Account -> Lead backward reference path");
		article.Text.Should().Contain("NOT_EXISTS",
			because: "the guide should teach the 'without X' -> NOT_EXISTS mapping");
		article.Text.Should().Contain("Lookup vs forward-text comparison",
			because: "the guide must explicitly disambiguate EQUAL-on-Lookup-column from forward-into-text-column to keep the LLM off the wrong Type.Name pattern");
		article.Text.Should().Contain("\"columnPath\": \"Type\"",
			because: "the canonical example for 'Customer' must show the Lookup-column shape, not the forward Type.Name pattern");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes business-rule-filters so AI callers can retrieve filter authoring guidance by name.")]
	public void GuidanceCatalog_Should_Include_Business_Rule_Filters_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("business-rule-filters", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose business-rule-filters so get-guidance can return it by name");
		entry.Name.Should().Be("business-rule-filters",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Description.Should().Contain("apply-static-filter",
			because: "the catalog description should identify the action the guidance covers");
		entry.Article.Should().NotBeNull(
			because: "the catalog entry must carry the guidance text article");
		entry.Article.Uri.Should().Be("docs://mcp/guides/business-rule-filters",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog.GetNames returns business-rule-filters in the alphabetically sorted name list.")]
	public void GuidanceCatalog_GetNames_Should_Contain_Business_Rule_Filters() {
		// Act
		System.Collections.Generic.IReadOnlyList<string> names = GuidanceCatalog.GetNames();

		// Assert
		names.Should().Contain("business-rule-filters",
			because: "the new guidance article must surface in the catalog name list returned to MCP callers");
	}
}
