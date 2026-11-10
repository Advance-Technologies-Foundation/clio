using System.Text.Json;
using Clio.Command.Theming;
using Clio.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ThemeBackgroundTests {

	private const string ImageId = "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81";

	[Test]
	[Description("Strips control characters, caps the length and drops a blank field of a catalog background, because the catalog is server data shown to the caller.")]
	public void SanitizeForDisplay_Should_Sanitize_Catalog_Values() {
		// Act
		ThemeBackground background = new ThemeBackground {
			Mode = " Image\u0007 ", ImageId = new string('a', 300), Color = "  "
		}.SanitizeForDisplay();

		// Assert
		background.Should().Match<ThemeBackground>(value =>
				!value.Mode.Contains('\u0007')
				&& value.ImageId == new string('a', ThemeParameterValidator.MaxIdLength) + "..."
				&& value.Color == null,
			because: "a crafted catalog entry must not inject control characters, flood the result or report an empty field");
	}

	[TestCase("rgba(10, 37, 64, 0.85)")]
	[TestCase("rgba(100, 137, 164, 0.850000)")]
	[Description("Keeps a catalog background color that is longer than a mode, such as an rgba() value, whole.")]
	public void SanitizeForDisplay_Should_Keep_A_Long_Color_Whole(string color) {
		// Act
		ThemeBackground background = new ThemeBackground { Mode = "Color", Color = color }.SanitizeForDisplay();

		// Assert
		background.Color.Should().Be(color, because: "get-theme and list-themes must report the stored color unchanged");
	}

	[TestCase(null)]
	[TestCase("  ")]
	[Description("Reports no background for a catalog entry whose mode is missing or blank.")]
	public void SanitizeForDisplay_Should_Return_Null_When_The_Mode_Is_Blank(string mode) {
		// Act & Assert
		new ThemeBackground { Mode = mode, Color = "#123" }.SanitizeForDisplay().Should().BeNull(
			because: "a background without a mode is not one the platform applies");
	}

	[TestCase("Image", ImageId, null, "Image " + ImageId)]
	[TestCase("Color", null, "#1A2B3C", "Color #1A2B3C")]
	[TestCase("Color", null, null, "Color")]
	[Description("Describes a background by its mode followed by its image id or its color.")]
	public void Describe_Should_Name_The_Mode_And_Its_Value(string mode, string imageId, string color, string expected) {
		// Act & Assert
		new ThemeBackground { Mode = mode, ImageId = imageId, Color = color }.Describe().Should().Be(expected,
			because: "update-theme logs this text to say which background it wrote");
	}

	[TestCase(ImageId, "#123", "background-image-id and background-color cannot be combined")]
	[TestCase("null", "null", "background-image-id and background-color cannot be combined")]
	[TestCase("not-a-guid", null, "background-image-id 'not-a-guid' is not a valid id")]
	[TestCase("00000000-0000-0000-0000-000000000000", null, "is not a valid id. Pass the id printed by upload-image.")]
	[TestCase(null, "#12345", "background-color '#12345' is not a hex color")]
	[TestCase(null, "rgba(10, 37, 64, 0.85)", "background-color 'rgba(10, 37, 64, 0.85)' is not a hex color")]
	[TestCase(null, "blue", "background-color 'blue' is not a hex color")]
	[Description("Rejects two background arguments, null included, a background-image-id that is not an image id, and a background-color that is not #RGB, #RRGGBB or #RRGGBBAA.")]
	public void TryParse_Should_Reject_An_Invalid_Background(string imageId, string color, string expected) {
		// Act
		bool valid = ThemeBackground.TryParse(imageId, color, allowRemoval: true, out _, out string error);

		// Assert
		valid.Should().BeFalse(because: "an invalid background must stop the command before any write");
		error.Should().Contain(expected, because: "the caller must learn what to fix before any write");
	}

	[TestCase("#123")]
	[TestCase("#1a2b3c")]
	[TestCase(" #1A2B3CDD ")]
	[Description("Accepts a background-color written as #RGB, #RRGGBB or #RRGGBBAA and sends it without the surrounding blanks.")]
	public void TryParse_Should_Accept_A_Hex_Color(string color) {
		// Act
		ThemeBackground.TryParse(null, color, allowRemoval: false, out ThemeBackground background, out _);

		// Assert
		background.Should().BeEquivalentTo(new ThemeBackground { Mode = ThemeBackground.ColorMode, Color = color.Trim() },
			because: "the documented hex forms reach the server as a color background");
	}

	[Test]
	[Description("Sends an image background with the canonical form of the given image id, so a differently spelled id names the same image.")]
	public void TryParse_Should_Canonicalize_The_Image_Id() {
		// Act
		ThemeBackground.TryParse(" {6D2E4B1F-93A8-4C57-B0E1-2F7A9C3D5E81} ", null, allowRemoval: false,
			out ThemeBackground background, out _);

		// Assert
		background.Should().BeEquivalentTo(new ThemeBackground { Mode = ThemeBackground.ImageMode, ImageId = ImageId },
			because: "ThemeService and the SysImage check must receive the id the image was uploaded under");
	}

	[TestCase(null, null)]
	[TestCase("  ", "")]
	[Description("Reports no background when neither background argument is given, so the stored background is kept.")]
	public void TryParse_Should_Return_No_Background_When_No_Argument_Is_Given(string imageId, string color) {
		// Act
		ThemeBackground.TryParse(imageId, color, allowRemoval: true, out ThemeBackground background, out _);

		// Assert
		background.Should().BeNull(because: "an absent background argument leaves the theme's background alone");
	}

	[TestCase("null", null)]
	[TestCase(null, "null")]
	[TestCase(" NULL ", null)]
	[Description("Reads null in either background argument as a removal that ThemeService receives as an empty background.")]
	public void TryParse_Should_Read_Null_As_A_Removal(string imageId, string color) {
		// Act
		ThemeBackground.TryParse(imageId, color, allowRemoval: true, out ThemeBackground background, out _);

		// Assert
		JsonSerializer.Serialize(background).Should().Be("{}",
			because: "an empty background is what tells ThemeService to remove the stored one");
	}

	[TestCase(ImageId, null, true)]
	[TestCase(null, "null", true)]
	[TestCase(" ", null, false)]
	[TestCase(null, null, false)]
	[Description("Treats any non-blank background argument, null included, as given, so the background version floor applies to it.")]
	public void IsGiven_Should_Count_Every_Non_Blank_Background_Argument(string imageId, string color, bool expected) {
		// Act & Assert
		ThemeBackground.IsGiven(imageId, color).Should().Be(expected,
			because: "an older ThemeService drops a background, a removal included, while still reporting success");
	}
}
