using System;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Calibration anchors for <see cref="FontImportBuilder"/>: URL/rule construction with sorted +
/// de-duplicated weights, default weights, multi-family joins, and family validation.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Theming")]
public sealed class FontImportBuilderTests {

	[Test]
	[Description("BuildRule builds the @import url(...) rule.")]
	public void BuildRule_ShouldBuildImportRule_ForSingleFamily() {
		// Act / Assert
		FontImportBuilder.BuildRule(new[] { new FontFamilyEntry("Open Sans", new[] { 400, 600, 700 }) })
			.Should().Be("@import url('https://fonts.googleapis.com/css2?family=Open+Sans:wght@400;600;700&display=swap');",
				because: "the family name is +-joined and weights are appended");
	}

	[Test]
	[Description("BuildUrl builds a single-family URL.")]
	public void BuildUrl_ShouldBuildSingleFamilyUrl() {
		// Act / Assert
		FontImportBuilder.BuildUrl(new[] { new FontFamilyEntry("Inter", new[] { 400, 500, 600 }) })
			.Should().Be("https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&display=swap",
				because: "a single family produces one family= param plus display=swap");
	}

	[Test]
	[Description("BuildUrl defaults the weights when omitted.")]
	public void BuildUrl_ShouldDefaultWeights_WhenOmitted() {
		// Act / Assert
		FontImportBuilder.BuildUrl(new[] { new FontFamilyEntry("Inter") })
			.Should().Be("https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&display=swap",
				because: "omitted weights default to 400;500;600");
	}

	[Test]
	[Description("BuildUrl joins multiple families.")]
	public void BuildUrl_ShouldJoinMultipleFamilies() {
		// Act / Assert
		FontImportBuilder.BuildUrl(new[] {
			new FontFamilyEntry("Inter", new[] { 400, 700 }),
			new FontFamilyEntry("Roboto", new[] { 400, 700 }),
		}).Should().Be("https://fonts.googleapis.com/css2?family=Inter:wght@400;700&family=Roboto:wght@400;700&display=swap",
			because: "each family contributes a family= param joined by &");
	}

	[Test]
	[Description("BuildUrl sorts and de-duplicates weights.")]
	public void BuildUrl_ShouldSortAndDedupeWeights() {
		// Act / Assert
		FontImportBuilder.BuildUrl(new[] { new FontFamilyEntry("Inter", new[] { 600, 400, 500 }) })
			.Should().Be("https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&display=swap",
				because: "weights are sorted ascending");
		FontImportBuilder.BuildUrl(new[] { new FontFamilyEntry("Inter", new[] { 400, 400, 500 }) })
			.Should().Be("https://fonts.googleapis.com/css2?family=Inter:wght@400;500&display=swap",
				because: "duplicate weights are removed");
	}

	[Test]
	[Description("BuildUrl/BuildRule reject a family with URL/stylesheet-breaking characters.")]
	public void Build_ShouldRejectInvalidFamily_WithInvalidFontFamily() {
		// Act / Assert
		((Action)(() => FontImportBuilder.BuildUrl(new[] { new FontFamilyEntry("Inter'; }") })))
			.Should().Throw<ArgumentException>().WithMessage("INVALID_FONT_FAMILY*",
				because: "a family with quotes/braces would break the URL or stylesheet");
		((Action)(() => FontImportBuilder.BuildRule(new[] { new FontFamilyEntry("Inter<script>") })))
			.Should().Throw<ArgumentException>().WithMessage("INVALID_FONT_FAMILY*",
				because: "a family with angle brackets is rejected");
	}
}
