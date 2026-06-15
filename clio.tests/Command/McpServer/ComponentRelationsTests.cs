using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentRelationsTests {

	[TestCase("crt.DataGrid")]
	[TestCase("crt.List")]
	[TestCase("crt.FileList")]
	[TestCase("crt.MultiList")]
	[TestCase("crt.ImageInput")]
	[Description("Returns a crt.Gallery see-also suggestion for the curated collection/visual component types, so the agent is steered toward the overlooked gallery viewer at the decision point.")]
	public void GetRelated_ShouldSuggestGallery_WhenTypeIsCollectionOrVisual(string componentType) {
		// Arrange

		// Act
		IReadOnlyList<RelatedComponentSuggestion>? related = ComponentRelations.GetRelated(componentType);

		// Assert
		related.Should().NotBeNull(
			because: $"{componentType} is a curated collection/visual type that should carry a see-also nudge");
		related!.Should().ContainSingle(suggestion => suggestion.ComponentType == "crt.Gallery",
			because: "the reopened ENG-91134 bug was the agent never considering crt.Gallery for these types");
		related![0].Reason.Should().NotBeNullOrWhiteSpace(
			because: "the suggestion must carry an agent-facing rationale, not just a bare type name");
	}

	[Test]
	[Description("Resolves the curated map case-insensitively so a differently-cased component type still receives the suggestion.")]
	public void GetRelated_ShouldResolveCaseInsensitively_WhenTypeCaseDiffers() {
		// Arrange
		const string mixedCaseType = "CRT.datagrid";

		// Act
		IReadOnlyList<RelatedComponentSuggestion>? related = ComponentRelations.GetRelated(mixedCaseType);

		// Assert
		related.Should().NotBeNull(
			because: "component-type matching elsewhere is case-insensitive, so the relations map must be too");
		related!.Should().ContainSingle(suggestion => suggestion.ComponentType == "crt.Gallery",
			because: "case differences must not hide the crt.Gallery nudge");
	}

	[TestCase("crt.Button")]
	[TestCase("crt.TabContainer")]
	[TestCase("crt.Gallery")]
	[Description("Returns null for component types with no curated alternatives so the response omits relatedComponents entirely and the signal stays low-noise.")]
	public void GetRelated_ShouldReturnNull_WhenTypeHasNoCuratedAlternatives(string componentType) {
		// Arrange

		// Act
		IReadOnlyList<RelatedComponentSuggestion>? related = ComponentRelations.GetRelated(componentType);

		// Assert
		related.Should().BeNull(
			because: $"{componentType} is not a curated collection/visual type, so it must not carry a suggestion");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[Description("Returns null for a null or whitespace component type instead of throwing, so callers can pass the raw type unguarded.")]
	public void GetRelated_ShouldReturnNull_WhenTypeIsNullOrWhitespace(string? componentType) {
		// Arrange

		// Act
		IReadOnlyList<RelatedComponentSuggestion>? related = ComponentRelations.GetRelated(componentType);

		// Assert
		related.Should().BeNull(
			because: "a missing component type has no curated alternatives and must not throw");
	}

	[Test]
	[Description("The stateless discovery tip names crt.Gallery and steers the agent to list mode, since that is the catalog-discovery step the agent skipped in the reopened bug.")]
	public void DiscoveryTip_ShouldNameGalleryAndSteerToListMode() {
		// Arrange

		// Act
		string tip = ComponentRelations.DiscoveryTip;

		// Assert
		tip.Should().Contain("list mode",
			because: "the tip's whole purpose is to push the agent back to list-mode catalog discovery");
		tip.Should().Contain("crt.Gallery",
			because: "crt.Gallery is the canonical non-obvious component the agent missed");
	}
}
