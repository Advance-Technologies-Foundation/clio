using System;
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

	[TestCase("CRT.datagrid")]
	[TestCase("crt.LIST")]
	[TestCase("crt.filelist")]
	[TestCase("CRT.MultiList")]
	[TestCase("crt.imageinput")]
	[Description("Resolves every curated trigger key case-insensitively so a differently-cased component type still receives the crt.Gallery suggestion — case-insensitivity must hold for all five keys, not just one.")]
	public void GetRelated_ShouldResolveCaseInsensitively_WhenTypeCaseDiffers(string mixedCaseType) {
		// Arrange

		// Act
		IReadOnlyList<RelatedComponentSuggestion>? related = ComponentRelations.GetRelated(mixedCaseType);

		// Assert
		related.Should().NotBeNull(
			because: $"component-type matching elsewhere is case-insensitive, so '{mixedCaseType}' must still resolve");
		related!.Should().ContainSingle(suggestion => suggestion.ComponentType == "crt.Gallery",
			because: $"case differences must not hide the crt.Gallery nudge for '{mixedCaseType}'");
	}

	[TestCase("crt.DataGrid")]
	[TestCase("crt.List")]
	[TestCase("crt.FileList")]
	[TestCase("crt.MultiList")]
	[TestCase("crt.ImageInput")]
	[Description("Every curated suggestion is a well-formed crt.* type carrying a non-empty reason and never points back at the source type. This hermetic invariant does not need the live catalog (which the truncated snapshot cannot supply); the data-driven catalog-existence guard is owned by Solution A / ENG-91571.")]
	public void GetRelated_ShouldReturnWellFormedSuggestionsThatNeverPointAtTheSourceType(string componentType) {
		// Arrange

		// Act
		IReadOnlyList<RelatedComponentSuggestion>? related = ComponentRelations.GetRelated(componentType);

		// Assert
		related.Should().NotBeNull(
			because: $"{componentType} is a curated collection/visual type that must carry a suggestion");
		related!.Should().NotContain(
			suggestion => string.Equals(suggestion.ComponentType, componentType, StringComparison.OrdinalIgnoreCase),
			because: "a see-also must point at an ALTERNATIVE, never back at the type the agent is already viewing");
		related!.Should().OnlyContain(
			suggestion => suggestion.ComponentType.StartsWith("crt.", StringComparison.Ordinal),
			because: "every suggested type must be a well-formed Freedom UI crt.* type so the agent can look it up");
		related!.Should().OnlyContain(
			suggestion => !string.IsNullOrWhiteSpace(suggestion.Reason),
			because: "every suggestion must carry an agent-facing rationale, not a bare type name");
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
}
