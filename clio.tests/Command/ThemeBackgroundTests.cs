using Clio.Command.Theming;
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
				!value.Mode.Contains('\u0007') && value.ImageId.Length < 110 && value.Color == null,
			because: "a crafted catalog entry must not inject control characters, flood the result or report an empty field");
	}
}
