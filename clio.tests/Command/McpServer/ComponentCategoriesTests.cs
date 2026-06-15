using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the controlled category taxonomy that Solution A owns (ENG-91571) and
/// Solution D's faceted discovery consumes.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentCategoriesTests {

	[Test]
	[Description("The controlled vocabulary exposes a stable ~5–15 category set so faceted discovery has a bounded, always-visible facet space (umbrella ADR Decision 1).")]
	public void All_ShouldExposeBoundedControlledVocabulary_WhenRead() {
		// Act
		var categories = ComponentCategories.All;

		// Assert
		categories.Should().NotBeEmpty(
			because: "Solution D's faceted discovery needs at least one facet to render");
		categories.Count.Should().BeInRange(5, 15,
			because: "the umbrella ADR pins the taxonomy at a controlled ~5–15 vocabulary, not an open-ended list");
		categories.Should().OnlyHaveUniqueItems(category => category.Id,
			because: "duplicate category ids would make the facet space ambiguous");
		categories.Should().AllSatisfy(category => category.Description.Should().NotBeNullOrWhiteSpace(
			because: "each category needs a one-line facet label for list-mode discovery"));
	}

	[Test]
	[Description("Category identifiers are kebab-case so they match the producer wire contract and the project's naming convention (ENG-91571).")]
	public void All_ShouldUseKebabCaseIds_WhenRead() {
		// Assert
		ComponentCategories.All.Should().AllSatisfy(category =>
			category.Id.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$",
				because: $"category id '{category.Id}' must be kebab-case to match the producer data and stay a stable wire token"));
	}

	[TestCase("media")]
	[TestCase("data-collection")]
	[TestCase("data-input")]
	[Description("IsKnown accepts members of the controlled vocabulary so taxonomy validation passes for producer-supplied categories (ENG-91571).")]
	public void IsKnown_ShouldReturnTrue_WhenCategoryIsInVocabulary(string categoryId) {
		// Assert
		ComponentCategories.IsKnown(categoryId).Should().BeTrue(
			because: $"'{categoryId}' is a member of the controlled taxonomy");
	}

	[TestCase("")]
	[TestCase(null)]
	[TestCase("Media")]
	[TestCase("not-a-category")]
	[Description("IsKnown rejects free-form, empty, null, and wrong-case values so taxonomy drift fails fast (ENG-91571).")]
	public void IsKnown_ShouldReturnFalse_WhenCategoryIsNotInVocabulary(string? categoryId) {
		// Assert
		ComponentCategories.IsKnown(categoryId).Should().BeFalse(
			because: $"'{categoryId}' is not a member of the controlled, case-sensitive taxonomy");
	}
}
