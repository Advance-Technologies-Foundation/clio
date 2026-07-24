using System;
using Clio.Command.RelatedPages;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="RelatedPageSchemaTypeParser"/> — the CLI/MCP <c>schema-type</c> argument parser
/// shared by create-related-page-addon and get-related-page-addon. It decides whether the tools write/read
/// the web <c>RelatedPage</c> or the mobile <c>MobileRelatedPage</c> add-on, so a wrong mapping silently
/// targets the wrong add-on.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RelatedPageSchemaTypeParserTests {

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("web")]
	[TestCase("WEB")]
	[TestCase("  Web  ")]
	[Description("Null, blank, or any casing/spacing of 'web' resolves to the web RelatedPage add-on (the back-compatible default).")]
	public void Parse_ReturnsWeb_ForNullBlankOrWeb(string schemaType) =>
		RelatedPageSchemaTypeParser.Parse(schemaType).Should().Be(RelatedPageSchemaType.Web,
			because: "an unspecified or 'web' schema-type targets the web RelatedPage add-on");

	[TestCase("mobile")]
	[TestCase("MOBILE")]
	[TestCase("  Mobile  ")]
	[Description("Any casing/spacing of 'mobile' resolves to the mobile MobileRelatedPage add-on.")]
	public void Parse_ReturnsMobile_ForMobile(string schemaType) =>
		RelatedPageSchemaTypeParser.Parse(schemaType).Should().Be(RelatedPageSchemaType.Mobile,
			because: "a 'mobile' schema-type targets the MobileRelatedPage add-on");

	[TestCase("web-mobile")]
	[TestCase("desktop")]
	[TestCase("mobil")]
	[Description("An unrecognized schema-type is rejected rather than silently defaulting, so a typo cannot write the wrong add-on.")]
	public void Parse_Throws_ForUnrecognizedValue(string schemaType) {
		// Act
		Action act = () => RelatedPageSchemaTypeParser.Parse(schemaType);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*schema-type*",
				because: "an invalid schema-type must fail loudly and name the offending argument");
	}
}
