using Clio.Command.Theming;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ThemeBackgroundArgumentsTests {

	[TestCase("6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81", "#123", "background-image-id and background-color cannot be combined")]
	[TestCase("not-a-guid", null, "background-image-id 'not-a-guid' is not a valid id")]
	[TestCase(null, "#12345", "background-color '#12345' is not a hex color")]
	[TestCase(null, "rgba(10, 37, 64, 0.85)", "background-color 'rgba(10, 37, 64, 0.85)' is not a hex color")]
	[TestCase(null, "blue", "background-color 'blue' is not a hex color")]
	[Description("Rejects two background sources, a background-image-id that is not an image id, and a background-color that is not #RGB, #RRGGBB or #RRGGBBAA.")]
	public void TryValidate_Should_Reject_An_Invalid_Background(string imageId, string color, string expected) {
		bool valid = new ThemeBackgroundArguments(imageId, color).TryValidate(out string error);

		valid.Should().BeFalse(because: "an invalid background must stop the command before any write");
		error.Should().Contain(expected, because: "the caller must learn what to fix before any write");
	}

	[TestCase("#123")]
	[TestCase("#1a2b3c")]
	[TestCase(" #1A2B3CDD ")]
	[Description("Accepts a background-color written as #RGB, #RRGGBB or #RRGGBBAA.")]
	public void TryValidate_Should_Accept_A_Hex_Color(string color) {
		new ThemeBackgroundArguments(Color: color).TryValidate(out _).Should().BeTrue(
			because: "the documented hex forms reach the server unchanged");
	}
}
