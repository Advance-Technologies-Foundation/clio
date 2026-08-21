using System;
using System.Collections.Generic;
using Clio.Project.NuGet;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Package.NuGet;

[TestFixture]
[Property("Module", "Package")]
[Category("Unit")]
public class CreatioSdkOnlineTests {

	[Test]
	[Description("Parses a well-formed NuGet registration response into a descending-sorted version list.")]
	public void ParseVersionsFromNugetJson_Should_ReturnDescendingSortedVersions_ForWellFormedResponse() {
		// Arrange
		const string json = """
			{
			  "items": [
			    {
			      "items": [
			        { "catalogEntry": { "version": "8.1.0.10" } },
			        { "catalogEntry": { "version": "8.1.0.30" } },
			        { "catalogEntry": { "version": "8.1.0.20" } }
			      ]
			    }
			  ]
			}
			""";

		// Act
		List<Version> result = CreatioSdkOnline.ParseVersionsFromNugetJson(json);

		// Assert
		result.Should().Equal(
			[new Version("8.1.0.30"), new Version("8.1.0.20"), new Version("8.1.0.10")],
			because: "the newest published SDK version must sort first for LastVersion/FindLatestSdkVersion to pick it up correctly");
	}

	[Test]
	[Description("Throws InvalidOperationException with a clear message instead of a raw NullReferenceException when the top registration item's 'items' property is absent (deserializes to null), matching the exact condition the removed unguarded chain used to NRE on (sonar csharpsquid:S2259).")]
	public void ParseVersionsFromNugetJson_Should_Throw_WhenTopItemHasNoItemsProperty() {
		// Arrange
		const string json = """{ "items": [ {} ] }""";

		// Act
		Action act = () => CreatioSdkOnline.ParseVersionsFromNugetJson(json);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*did not contain any catalog items*",
				because: "a top item with no 'items' property has nothing to parse and must fail loudly with a diagnosable message, not crash with a bare NullReferenceException");
	}

	[Test]
	[Description("Returns an empty list (not an exception) when the top item's 'items' array is explicitly empty — an empty result is a valid signal the caller's NewestOrThrow already handles, distinct from the null-property case above.")]
	public void ParseVersionsFromNugetJson_Should_ReturnEmptyList_WhenItemsArrayIsExplicitlyEmpty() {
		// Arrange
		const string json = """{ "items": [ { "items": [] } ] }""";

		// Act
		List<Version> result = CreatioSdkOnline.ParseVersionsFromNugetJson(json);

		// Assert
		result.Should().BeEmpty(
			because: "an explicitly empty catalog is a valid (if unhelpful) parse result; the caller's NewestOrThrow decides whether that's an error");
	}

	[Test]
	[Description("Throws InvalidOperationException instead of a raw NullReferenceException when the top-level 'items' array is empty (no registration pages at all).")]
	public void ParseVersionsFromNugetJson_Should_Throw_WhenNoTopItemsExist() {
		// Arrange
		const string json = """{ "items": [] }""";

		// Act
		Action act = () => CreatioSdkOnline.ParseVersionsFromNugetJson(json);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an empty top-level registration index has nothing to parse and must fail loudly");
	}

	[Test]
	[Description("Throws InvalidOperationException instead of a raw NullReferenceException when the response body deserializes to an empty object (e.g. '{}', matching what a malformed/unexpected NuGet response could produce).")]
	public void ParseVersionsFromNugetJson_Should_Throw_WhenResponseHasNoItemsProperty() {
		// Arrange
		const string json = "{}";

		// Act
		Action act = () => CreatioSdkOnline.ParseVersionsFromNugetJson(json);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a response with no 'items' property has nothing to parse and must fail loudly instead of NRE-ing on TopItems");
	}
}
