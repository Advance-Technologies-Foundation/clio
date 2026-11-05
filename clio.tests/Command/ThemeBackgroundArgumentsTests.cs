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
	[Description("Rejects two background sources and a background-image-id that is not an image id.")]
	public void TryValidate_Should_Reject_An_Invalid_Background(string imageId, string color, string expected) {
		new ThemeBackgroundArguments(imageId, color).TryValidate(out string error);

		error.Should().Contain(expected, because: "the caller must learn what to fix before any write");
	}
}
