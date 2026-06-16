using System.Linq;
using System.Reflection;
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
	[TestCase("   ")]
	[TestCase(null)]
	[TestCase("Media")]
	[TestCase("not-a-category")]
	[Description("IsKnown rejects free-form, empty, whitespace-only, null, and wrong-case values so taxonomy drift fails fast (ENG-91571).")]
	public void IsKnown_ShouldReturnFalse_WhenCategoryIsNotInVocabulary(string? categoryId) {
		// Assert
		ComponentCategories.IsKnown(categoryId).Should().BeFalse(
			because: $"'{categoryId}' is not a member of the controlled, case-sensitive taxonomy");
	}

	[Test]
	[Description("Every public taxonomy id constant must appear in All and every All row must have a backing constant, so a const added without an AllCategories row (or vice versa) fails fast instead of silently making IsKnown return false (ENG-91571).")]
	public void All_ShouldStayInLockstepWithPublicConstants_WhenComparedByReflection() {
		// Arrange
		string[] constantIds = typeof(ComponentCategories)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.IsLiteral && field.FieldType == typeof(string))
			.Select(field => (string)field.GetRawConstantValue()!)
			.ToArray();
		string[] vocabularyIds = ComponentCategories.All.Select(category => category.Id).ToArray();

		// Assert
		constantIds.Should().NotBeEmpty(
			because: "the taxonomy exposes its category ids as public constants for callers to reference");
		constantIds.Should().OnlyContain(id => ComponentCategories.IsKnown(id),
			because: "every public const taxonomy id must have a backing AllCategories row, else IsKnown(thatConst) silently returns false");
		vocabularyIds.Should().BeEquivalentTo(constantIds,
			because: "All and the public constants must stay 1:1 — an AllCategories row with no matching const (or a const with no row) is drift");
	}
}
