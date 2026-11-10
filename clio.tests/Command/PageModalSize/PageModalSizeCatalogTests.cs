namespace Clio.Tests.Command.PageModalSize;

using System;
using Clio.Command.PageModalSize;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageModalSizeCatalogTests {

	[TestCase("small", 1)]
	[TestCase("medium", 2)]
	[TestCase("large", 3)]
	[TestCase("extra-large", 4)]
	[TestCase("S", 1)]
	[TestCase("M", 2)]
	[TestCase("L", 3)]
	[TestCase("XL", 4)]
	[TestCase(" Large ", 3)]
	[TestCase("xl", 4)]
	[Description("Maps every Page Designer size name and caption, case-insensitively, to the value the designer stores.")]
	public void Parse_ShouldReturnDesignerValue_WhenSizeIsADesignerOption(string size, int expected) {
		// Act
		int value = PageModalSizeCatalog.Parse(size);

		// Assert
		value.Should().Be(expected,
			because: "the designer stores S/M/L/XL as SchemaSize 1/2/3/4 in modalSettings.size");
	}

	[TestCase("extra-small")]
	[TestCase("custom")]
	[TestCase("none")]
	[TestCase("wider")]
	[TestCase("3")]
	[TestCase("XXL")]
	[Description("Refuses a size the Page Designer does not offer and names the accepted values.")]
	public void Parse_ShouldThrow_WhenSizeIsNotADesignerOption(string size) {
		// Act
		Action act = () => PageModalSizeCatalog.Parse(size);

		// Assert
		act.Should().Throw<UnsupportedPageModalSizeException>(
				because: "a size the designer cannot show would leave no size selected in the Page Designer")
			.Where(ex => ex.Message.Contains("extra-large (XL)") && !ex.Message.Contains("(Parameter"),
				"the refusal must list the accepted values in a clean message");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("  ")]
	[Description("Refuses an empty size as a size problem, so the caller lists the accepted values.")]
	public void Parse_ShouldThrow_WhenSizeIsEmpty(string size) {
		// Act
		Action act = () => PageModalSizeCatalog.Parse(size);

		// Assert
		act.Should().Throw<UnsupportedPageModalSizeException>(
			because: "an empty size is reported as a size problem, so the caller lists the accepted values");
	}

	[TestCase(1, "small")]
	[TestCase(4, "extra-large")]
	[TestCase(0, null)]
	[TestCase(5, null)]
	[TestCase(6, null)]
	[Description("Names only the stored values the designer offers; None, Custom and ExtraSmall have no name.")]
	public void NameOf_ShouldNameOnlyDesignerValues(int value, string expected) {
		// Act
		string name = PageModalSizeCatalog.NameOf(value);

		// Assert
		name.Should().Be(expected, because: "only S/M/L/XL are sizes the designer can show as selected");
	}
}
