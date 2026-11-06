using Clio.Command.Theming;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ThemeBackgroundTests {

	[Test]
	[Description("Strips control characters, caps the length and drops a blank field of a catalog background, because the catalog is server data shown to the caller.")]
	public void FromCatalog_Should_Sanitize_Catalog_Values() {
		ThemeBackground background = ThemeBackground.FromCatalog(
			new ThemeBackground { Mode = " Image\u0007 ", ImageId = new string('a', 300), Color = "  " });

		background.Should().Match<ThemeBackground>(value =>
				!value.Mode.Contains('\u0007')
				&& value.ImageId == new string('a', ThemeParameterValidator.MaxIdLength) + "..."
				&& value.Color == null,
			because: "a crafted catalog entry must not inject control characters, flood the result or report an empty field");
	}

	[TestCase("rgba(10, 37, 64, 0.85)")]
	[TestCase("rgba(100, 137, 164, 0.850000)")]
	[Description("Keeps a catalog background color that is longer than a mode, such as an rgba() value, whole.")]
	public void FromCatalog_Should_Keep_A_Long_Color_Whole(string color) {
		ThemeBackground background = ThemeBackground.FromCatalog(new ThemeBackground { Mode = "Color", Color = color });

		background.Color.Should().Be(color, because: "get-theme and list-themes must report the stored color unchanged");
	}

	[Test]
	[Description("Reports no background for a catalog entry that has none.")]
	public void FromCatalog_Should_Return_Null_When_The_Catalog_Has_No_Background() {
		ThemeBackground.FromCatalog(null).Should().BeNull(
			because: "a theme without a background must omit the background key");
	}

	[TestCase(null)]
	[TestCase("  ")]
	[Description("Reports no background for a catalog entry whose mode is missing or blank.")]
	public void FromCatalog_Should_Return_Null_When_The_Mode_Is_Blank(string mode) {
		ThemeBackground.FromCatalog(new ThemeBackground { Mode = mode, Color = "#123" }).Should().BeNull(
			because: "a background without a mode is not one the platform applies");
	}

	[TestCase("Image", "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81", null, "Image 6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81")]
	[TestCase("Color", null, "#1A2B3C", "Color #1A2B3C")]
	[TestCase("Color", null, null, "Color")]
	[Description("Describes a background by its mode followed by its image id or its color.")]
	public void Describe_Should_Name_The_Mode_And_Its_Value(string mode, string imageId, string color, string expected) {
		new ThemeBackground { Mode = mode, ImageId = imageId, Color = color }.Describe().Should().Be(expected,
			because: "update-theme logs this text to say which background it wrote");
	}
}
