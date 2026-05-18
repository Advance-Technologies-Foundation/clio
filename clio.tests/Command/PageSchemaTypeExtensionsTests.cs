using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class PageSchemaTypeExtensionsTests {

	#region FromNumericValue

	[TestCase(9, PageSchemaType.Web)]
	[TestCase(10, PageSchemaType.Mobile)]
	[TestCase(0, PageSchemaType.Unknown)]
	[TestCase(11, PageSchemaType.Unknown)]
	[TestCase(-1, PageSchemaType.Unknown)]
	[TestCase(null, PageSchemaType.Unknown)]
	[Description("FromNumericValue maps Creatio ClientUnitSchemaType integers to the correct enum value")]
	public void FromNumericValue_ReturnsExpectedType(int? input, PageSchemaType expected) {
		// Act
		PageSchemaType result = PageSchemaTypeExtensions.FromNumericValue(input);

		// Assert
		result.Should().Be(expected,
			because: $"numeric value {input?.ToString() ?? "null"} must map to {expected}");
	}

	#endregion

	#region ToLabel

	[TestCase(PageSchemaType.Web, "web")]
	[TestCase(PageSchemaType.Mobile, "mobile")]
	[TestCase(PageSchemaType.Unknown, "unknown")]
	[Description("ToLabel returns the human-readable label for external JSON responses")]
	public void ToLabel_ReturnsExpectedLabel(PageSchemaType type, string expected) {
		// Act
		string result = type.ToLabel();

		// Assert
		result.Should().Be(expected,
			because: $"{type} must produce the label '{expected}' in JSON responses");
	}

	#endregion

	#region FromBody

	[TestCase(null, PageSchemaType.Unknown)]
	[TestCase("", PageSchemaType.Unknown)]
	[TestCase("   ", PageSchemaType.Unknown)]
	[Description("FromBody returns Unknown for null, empty, or whitespace-only bodies")]
	public void FromBody_WhenBodyIsNullOrWhitespace_ReturnsUnknown(string body, PageSchemaType expected) {
		// Act
		PageSchemaType result = PageSchemaTypeExtensions.FromBody(body);

		// Assert
		result.Should().Be(expected,
			because: "empty or whitespace bodies cannot be classified");
	}

	[Test]
	[Description("FromBody detects mobile pages from JSON body starting with '{'")]
	public void FromBody_WhenBodyStartsWithBrace_ReturnsMobile() {
		// Arrange
		string body = "{ \"viewConfigDiff\": [] }";

		// Act
		PageSchemaType result = PageSchemaTypeExtensions.FromBody(body);

		// Assert
		result.Should().Be(PageSchemaType.Mobile,
			because: "bodies starting with '{' are mobile JSON pages");
	}

	[Test]
	[Description("FromBody detects mobile pages even with leading whitespace before '{'")]
	public void FromBody_WhenBodyHasLeadingWhitespaceBeforeBrace_ReturnsMobile() {
		// Arrange
		string body = "  \t\n { \"viewConfigDiff\": [] }";

		// Act
		PageSchemaType result = PageSchemaTypeExtensions.FromBody(body);

		// Assert
		result.Should().Be(PageSchemaType.Mobile,
			because: "leading whitespace before '{' should be trimmed during detection");
	}

	[Test]
	[Description("FromBody detects web pages from AMD body starting with 'define('")]
	public void FromBody_WhenBodyStartsWithDefine_ReturnsWeb() {
		// Arrange
		string body = "define(\"MyPage\", [], function() { return {}; });";

		// Act
		PageSchemaType result = PageSchemaTypeExtensions.FromBody(body);

		// Assert
		result.Should().Be(PageSchemaType.Web,
			because: "bodies starting with 'define(' are AMD web pages");
	}

	[Test]
	[Description("FromBody detects web pages for any non-JSON content")]
	public void FromBody_WhenBodyIsNonJsonNonAmd_ReturnsWeb() {
		// Arrange
		string body = "// some comment\ndefine(...)";

		// Act
		PageSchemaType result = PageSchemaTypeExtensions.FromBody(body);

		// Assert
		result.Should().Be(PageSchemaType.Web,
			because: "any body not starting with '{' is treated as a web AMD module");
	}

	#endregion
}
