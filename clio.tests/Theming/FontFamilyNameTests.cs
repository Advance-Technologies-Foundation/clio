using System;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Theming;

/// <summary>
/// Calibration anchors for <see cref="FontFamilyName"/> — the family-name contract shared by the
/// availability probe, the web-font URL builder and the build's own input validation.
/// </summary>
[TestFixture]
[Category("Unit")]
public class FontFamilyNameTests {

	[Test]
	[Description("Validate rejects a family longer than 100 characters, bounding the probe URL and the process-lifetime availability cache.")]
	public void Validate_ShouldRejectOversizedFamily() {
		// Act / Assert
		((Action)(() => FontFamilyName.Validate(new string('A', 101))))
			.Should().Throw<ArgumentException>().WithMessage("INVALID_FONT_FAMILY*",
				because: "no real Google Fonts family approaches 100 characters, so anything longer is garbage input");
		((Action)(() => FontFamilyName.Validate(new string('A', 100))))
			.Should().NotThrow(because: "the cap itself is still a valid length");
	}

	[Test]
	[Description("Normalize trims padding and collapses internal whitespace runs, so the probe key, the css2 URL and the CSS font-family token all see one spelling of the family.")]
	public void Normalize_ShouldTrimAndCollapseWhitespace() {
		// Act / Assert
		FontFamilyName.Normalize("  Open   Sans  ").Should().Be("Open Sans",
			because: "a padded or doubly-spaced name must reach the probe in the same spelling the build uses");
		FontFamilyName.Normalize("Inter").Should().Be("Inter",
			because: "an already-canonical name is left alone");
	}

	[Test]
	[Description("Normalize passes null and blank through untouched rather than throwing, because an omitted font is a valid build input that simply keeps the template default.")]
	public void Normalize_ShouldPassThroughBlankInput() {
		// Act / Assert
		FontFamilyName.Normalize(null).Should().BeNull(because: "no font requested is not an error");
		FontFamilyName.Normalize("   ").Should().Be("   ",
			because: "a blank name is left for the caller's own blank check rather than silently becoming empty");
	}

	[Test]
	[Description("IsValid enforces the grammar the probe and the build share: untrimmed padding is rejected, and only letters, digits, spaces and hyphens are accepted after a leading letter or digit.")]
	public void IsValid_ShouldEnforceTheSharedGrammar() {
		// Act / Assert
		FontFamilyName.IsValid("PT Sans").Should().BeTrue(because: "spaces inside a family are legitimate");
		FontFamilyName.IsValid("Noto-Sans").Should().BeTrue(because: "hyphenated families are legitimate");
		FontFamilyName.IsValid(" Inter").Should().BeFalse(
			because: "callers must normalize before validating, or a padded name would reach the outbound URL");
		FontFamilyName.IsValid("-Inter").Should().BeFalse(because: "a family starts with a letter or digit");
		FontFamilyName.IsValid("Evil'; }").Should().BeFalse(because: "stylesheet-breaking characters are rejected");
		FontFamilyName.IsValid(null).Should().BeFalse(because: "a null family is not a valid name");
	}
}
